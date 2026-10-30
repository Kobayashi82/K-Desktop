using System.Collections.Concurrent;
using System.Globalization;
using Queue = System.Threading.Channels;
using KDesktop.Core.Localization;
using TL;

namespace KDesktop.Core.Remote.Telegram;

/// <summary>
/// An account with a phone number over MTProto (telegram, phase 6, WTelegramClient): what arrives comes pushed by the
/// library and waits in a queue for <see cref="ReceiveAsync"/>, in the same shape as the bot's (chats with the Bot API's
/// ids: a person by its id, a group negative, a supergroup «-100…»). It writes first, reads groups and sends big files, but
/// has no buttons (<see cref="TelegramCapabilities.CanButtons"/>). Its own messages are left out, except those written to
/// itself («Mensajes guardados») when the owner is the account itself.
/// </summary>
public sealed class TelegramUserConnection : ITelegramConnection, ITelegramAgenda
{
    /// <summary>A supergroup's or channel's id as the Bot API gives it: -(10^12 + id).</summary>
    private const long ChannelBase = 1_000_000_000_000L;

    private const int KeptFiles = 200;

    private readonly WTelegram.Client _client;
    private readonly string _phone;
    private readonly Queue.Channel<TelegramIncoming> _in = Queue.Channel.CreateUnbounded<TelegramIncoming>(new Queue.UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<long, User> _users = [];
    private readonly Dictionary<long, ChatBase> _chats = [];
    private readonly Lock _peers = new();
    private readonly ConcurrentDictionary<string, object> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _fileOrder = new();

    /// <summary>The messages it sent, by chat (to know a reply to it in a group, and not to take its own as orders).</summary>
    private readonly ConcurrentDictionary<(long Chat, int Id), byte> _sent = new();

    private User? _me;
    private long _updates;

    static TelegramUserConnection() =>
        // The library writes to the console by default: only its warnings and errors, to K-Desktop's log.
        WTelegram.Helpers.Log = (level, text) =>
        {
            if (level >= 4) Log.Warn("Telegram (MTProto): " + text);
        };

    /// <param name="session">Where the session is (<see cref="TelegramSessionStore"/>); the connection owns it.</param>
    public TelegramUserConnection(int apiId, string apiHash, string phone, Stream session)
    {
        _phone = phone;
        _client = NewClient(apiId, apiHash, session);
        _client.OnUpdates += OnUpdates;
    }

    public TelegramCapabilities Capabilities => TelegramCapabilities.Account;

    internal static WTelegram.Client NewClient(int apiId, string apiHash, Stream session)
    {
        string? Config(string what) => what switch
        {
            "api_id" => apiId.ToString(CultureInfo.InvariantCulture),
            "api_hash" => apiHash,
            "device_model" => "K-Desktop",
            "app_version" => AppPaths.ShortVersion,
            _ => null,
        };
        return new WTelegram.Client(Config, session) { FloodRetryThreshold = 30 };
    }

    /// <summary>Connects with the session kept; a session that is no longer valid says so (log in again).</summary>
    public async Task<TelegramUser> GetMeAsync(CancellationToken cancel)
    {
        await Call(async () =>
        {
            // With the session of that number, Login answers null at once; anything else is that it has to log in again.
            if (await _client.Login(_phone).ConfigureAwait(false) is not null) throw new TelegramException(Loc.T("telegram.account.loggedOut"), 401);
            return 0;
        }).WaitAsync(cancel).ConfigureAwait(false);
        _me = _client.User;
        lock (_peers) _users[_me.id] = _me;
        // Who it can write to after a restart: its contacts and the recent chats (their access is in them).
        try
        {
            var contacts = await _client.Contacts_GetContacts().WaitAsync(cancel).ConfigureAwait(false);
            lock (_peers) foreach (var (id, u) in contacts.users) _users[id] = u;
            var dialogs = await _client.Messages_GetDialogs(limit: 100).WaitAsync(cancel).ConfigureAwait(false);
            lock (_peers) dialogs.CollectUsersChats(_users, _chats);
        }
        catch (RpcException ex)
        {
            Log.Warn($"Telegram: no se han podido leer los contactos de la cuenta: {ex.Message}");
        }
        return Of(_me);
    }

    private static TelegramUser Of(User u) => new(u.id, ((u.first_name ?? string.Empty) + " " + (u.last_name ?? string.Empty)).Trim(), u.MainUsername, u.phone is { Length: > 0 } p ? "+" + p : null);

    public async Task<IReadOnlyList<TelegramIncoming>> ReceiveAsync(CancellationToken cancel)
    {
        // The library reconnects by itself; a message every so often keeps the loop knowing it is alive.
        using var wake = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        wake.CancelAfter(TimeSpan.FromSeconds(TelegramBotConnection.PollSeconds));
        try { await _in.Reader.WaitToReadAsync(wake.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { return []; }
        var list = new List<TelegramIncoming>();
        while (_in.Reader.TryRead(out var m)) list.Add(m);
        return list;
    }

    private Task OnUpdates(UpdatesBase updates)
    {
        lock (_peers) updates.CollectUsersChats(_users, _chats);
        foreach (var update in updates.UpdateList)
            if (update is UpdateNewMessage { message: Message m } && Parse(m) is { } incoming)
                _in.Writer.TryWrite(incoming);
        return Task.CompletedTask;
    }

    /// <summary>A message as the bot's are; null when it is not one of a person to it (its own, a bot's, a service message…).</summary>
    private TelegramIncoming? Parse(Message m)
    {
        if (_me is not { } me) return null;
        var chat = ChatId(m.peer_id);
        var kind = TelegramChatKind.Private;
        string? title = null;
        lock (_peers)
        {
            switch (m.peer_id)
            {
                case PeerChat pc:
                    kind = TelegramChatKind.Group;
                    title = _chats.GetValueOrDefault(pc.chat_id)?.Title;
                    break;
                case PeerChannel pch:
                    var channel = _chats.GetValueOrDefault(pch.channel_id);
                    kind = channel is Channel { IsGroup: true } ? TelegramChatKind.Group : TelegramChatKind.Channel;
                    title = channel?.Title;
                    break;
            }
        }
        long fromId;
        if (m.flags.HasFlag(Message.Flags.out_))
        {
            // Its own: only what it writes to itself («Mensajes guardados»), and never what K-Desktop sent there.
            if (chat != me.id || _sent.ContainsKey((chat, m.id))) return null;
            fromId = me.id;
        }
        else if ((m.from_id ?? m.peer_id) is PeerUser pu) fromId = pu.user_id;
        else return null;
        User? from;
        lock (_peers) from = _users.GetValueOrDefault(fromId);
        if (from is { IsBot: true }) return null;
        var user = from is null ? new TelegramUser(fromId, fromId.ToString(CultureInfo.InvariantCulture), null) : Of(from);

        var content = TelegramContent.Text;
        TelegramFile? file = null;
        switch (m.media)
        {
            case MessageMediaPhoto { photo: Photo photo }:
                content = TelegramContent.Photo;
                file = new TelegramFile(Keep("p" + photo.id.ToString(CultureInfo.InvariantCulture), photo), null, "image/jpeg", photo.LargestPhotoSize?.FileSize ?? 0);
                break;
            case MessageMediaDocument { document: Document doc }:
                var voice = doc.attributes?.OfType<DocumentAttributeAudio>().Any(a => a.flags.HasFlag(DocumentAttributeAudio.Flags.voice)) == true;
                content = voice ? TelegramContent.Voice : TelegramContent.Document;
                file = new TelegramFile(Keep("d" + doc.id.ToString(CultureInfo.InvariantCulture), doc), doc.Filename, doc.mime_type, doc.size);
                break;
            case null:
                break;
            default:
                content = TelegramContent.Other;
                break;
        }
        if (content == TelegramContent.Text && string.IsNullOrEmpty(m.message)) return null;
        var replyTo = (m.reply_to as MessageReplyHeader)?.reply_to_msg_id;
        // In a group it is spoken to when mentioned or answered (Telegram marks both): as the bot's «a reply to it».
        long? replyToUser = m.flags.HasFlag(Message.Flags.mentioned) || (replyTo is { } r && _sent.ContainsKey((chat, r))) ? me.id : null;
        return new TelegramIncoming(
            Interlocked.Increment(ref _updates), chat, kind, title, m.id, user, new DateTimeOffset(DateTime.SpecifyKind(m.date, DateTimeKind.Utc)), m.message ?? string.Empty,
            content, file, replyToUser, Forwarded: m.fwd_from is not null, Via: TelegramVia.Account, ReplyToMessageId: replyTo is > 0 ? replyTo : null);
    }

    private string Keep(string id, object what)
    {
        if (_files.TryAdd(id, what))
        {
            _fileOrder.Enqueue(id);
            while (_fileOrder.Count > KeptFiles && _fileOrder.TryDequeue(out var old)) _files.TryRemove(old, out _);
        }
        return id;
    }

    private static long ChatId(Peer peer) => peer switch
    {
        PeerUser u => u.user_id,
        PeerChat c => -c.chat_id,
        PeerChannel ch => -(ChannelBase + ch.channel_id),
        _ => 0,
    };

    private InputPeer PeerOf(long chat)
    {
        lock (_peers)
        {
            if (_me is { } me && chat == me.id) return InputPeer.Self;
            if (chat > 0 && _users.TryGetValue(chat, out var user)) return user.ToInputPeer();
            if (chat < -ChannelBase && _chats.TryGetValue(-chat - ChannelBase, out var channel)) return channel.ToInputPeer();
            if (chat < 0 && _chats.TryGetValue(-chat, out var group)) return group.ToInputPeer();
        }
        throw new TelegramException(Loc.T("telegram.error.unknownChat"), 400);
    }

    public async Task<int> SendTextAsync(long chat, string text, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons, CancellationToken cancel)
    {
        var peer = PeerOf(chat);
        var sent = await Call(() => _client.SendMessageAsync(peer, text, preview: WTelegram.Client.LinkPreview.Disabled)).WaitAsync(cancel).ConfigureAwait(false);
        return Sent(chat, sent);
    }

    private int Sent(long chat, Message? message)
    {
        if (message is null) return 0;
        _sent[(chat, message.id)] = 0;
        if (_sent.Count > 2000)
            foreach (var old in _sent.Keys.OrderBy(k => k.Id).Take(500)) _sent.TryRemove(old, out _);
        return message.id;
    }

    /// <summary>Accounts have no buttons: what they would say goes as text (the service does it).</summary>
    public Task AnswerButtonAsync(string callbackId, string? text, CancellationToken cancel) => Task.CompletedTask;

    public async Task<int> SendFileAsync(long chat, TelegramFileKind kind, string path, string? caption, CancellationToken cancel)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new TelegramException(Loc.T("telegram.error.noFile", path));
        if (info.Length > Capabilities.MaxUpload) throw new TelegramException(Loc.T("telegram.error.tooBig", info.Name));
        var peer = PeerOf(chat);
        var sent = await Call(async () =>
        {
            var uploaded = await _client.UploadFileAsync(path).ConfigureAwait(false);
            if (kind == TelegramFileKind.Photo) return await _client.SendMediaAsync(peer, caption ?? string.Empty, uploaded, "image/" + (info.Extension.TrimStart('.').ToLowerInvariant() is "png" ? "png" : "jpeg")).ConfigureAwait(false);
            InputMedia media = kind == TelegramFileKind.Voice
                ? new InputMediaUploadedDocument
                {
                    file = uploaded,
                    mime_type = "audio/ogg",
                    attributes = [new DocumentAttributeAudio { flags = DocumentAttributeAudio.Flags.voice, duration = OggSeconds(path) }],
                }
                : new InputMediaUploadedDocument
                {
                    file = uploaded,
                    mime_type = "application/octet-stream",
                    attributes = [new DocumentAttributeFilename { file_name = info.Name }],
                    flags = InputMediaUploadedDocument.Flags.force_file,
                };
            return await _client.SendMessageAsync(peer, caption ?? string.Empty, media).ConfigureAwait(false);
        }).WaitAsync(cancel).ConfigureAwait(false);
        return Sent(chat, sent);
    }

    /// <summary>How long an Ogg Opus lasts: the granule of its last page (48 kHz).</summary>
    internal static int OggSeconds(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            for (var i = bytes.Length - 27; i >= 0; i--)
            {
                if (bytes[i] != 'O' || bytes[i + 1] != 'g' || bytes[i + 2] != 'g' || bytes[i + 3] != 'S') continue;
                var granule = BitConverter.ToInt64(bytes, i + 6);
                return granule > 0 ? (int)Math.Ceiling(granule / 48000.0) : 0;
            }
        }
        catch (IOException) { }
        return 0;
    }

    public Task SendChatActionAsync(long chat, string action, CancellationToken cancel)
    {
        var peer = PeerOf(chat);
        SendMessageAction what = action switch
        {
            "record_voice" => new SendMessageRecordAudioAction(),
            "upload_photo" => new SendMessageUploadPhotoAction(),
            "upload_document" => new SendMessageUploadDocumentAction(),
            _ => new SendMessageTypingAction(),
        };
        return Call(() => _client.Messages_SetTyping(peer, what)).WaitAsync(cancel);
    }

    public Task EditTextAsync(long chat, int message, string text, CancellationToken cancel)
    {
        var peer = PeerOf(chat);
        return Call(() => _client.Messages_EditMessage(peer, message, text)).WaitAsync(cancel);
    }

    public Task DeleteAsync(long chat, int message, CancellationToken cancel)
    {
        var peer = PeerOf(chat);
        return Call(() => _client.DeleteMessages(peer, message)).WaitAsync(cancel);
    }

    public async Task DownloadAsync(string fileId, string destination, CancellationToken cancel)
    {
        if (!_files.TryGetValue(fileId, out var what)) throw new TelegramException(Loc.T("telegram.error.download"));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var output = File.Create(destination);
        await using (output.ConfigureAwait(false))
        {
            await Call(async () => what switch
            {
                Photo photo => await _client.DownloadFileAsync(photo, output).ConfigureAwait(false),
                Document doc => (object)await _client.DownloadFileAsync(doc, output).ConfigureAwait(false),
                _ => throw new TelegramException(Loc.T("telegram.error.download")),
            }).WaitAsync(cancel).ConfigureAwait(false);
        }
    }

    // ── the account's contacts (phase 6.4) ───────────────────────────────

    public async Task<IReadOnlyList<TelegramUser>> AgendaAsync(CancellationToken cancel)
    {
        var contacts = await Call(() => _client.Contacts_GetContacts()).WaitAsync(cancel).ConfigureAwait(false);
        lock (_peers) foreach (var (id, u) in contacts.users) _users[id] = u;
        return [.. contacts.users.Values.Where(u => !u.IsBot && u.id != _me?.id).Select(Of).OrderBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public async Task<TelegramUser?> AddToAgendaAsync(string phone, string firstName, string lastName, CancellationToken cancel)
    {
        var number = new string(phone.Where(c => char.IsAsciiDigit(c) || c == '+').ToArray());
        var imported = await Call(() => _client.Contacts_ImportContacts([new InputPhoneContact { client_id = 1, phone = number, first_name = firstName.Trim(), last_name = lastName.Trim() }]))
            .WaitAsync(cancel).ConfigureAwait(false);
        if (imported.imported.Length == 0) return null;
        lock (_peers)
        {
            foreach (var (id, user) in imported.users) _users[id] = user;
            return _users.TryGetValue(imported.imported[0].user_id, out var added) ? Of(added) : null;
        }
    }

    public Task LogOutAsync(CancellationToken cancel) => Call(() => _client.Auth_LogOut()).WaitAsync(cancel);

    /// <summary>
    /// A call to Telegram, with its failures as <see cref="TelegramException"/>: too fast (FLOOD_WAIT) as the bot's 429
    /// with the wait; the session closed as 401 (log in again); the account limited for spam as its own message.
    /// </summary>
    private static async Task<T> Call<T>(Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.Code == 420)
        {
            throw new TelegramException(Loc.T("telegram.error.tooFast"), 429, TimeSpan.FromSeconds(Math.Max(1, ex.X)));
        }
        catch (RpcException ex) when (ex.Code == 401 || ex.Message is "AUTH_KEY_UNREGISTERED" or "SESSION_REVOKED" or "USER_DEACTIVATED" or "USER_DEACTIVATED_BAN")
        {
            throw new TelegramException(Loc.T("telegram.account.loggedOut"), 401);
        }
        catch (RpcException ex) when (ex.Message == "PEER_FLOOD")
        {
            throw new TelegramException(Loc.T("telegram.account.limited"), 400) { Limited = true };
        }
        catch (RpcException ex)
        {
            throw new TelegramException(ex.Message, ex.Code);
        }
        catch (WTelegram.WTException ex)
        {
            throw new IOException(ex.Message, ex);
        }
    }

    public void Dispose()
    {
        _client.OnUpdates -= OnUpdates;
        _client.Dispose();
        _in.Writer.TryComplete();
    }
}

/// <summary>
/// Logging in an account (phase 6.2): the number, then the code Telegram sends (to the app or by SMS), then the password of
/// the two-step verification if it has one. Each step says what it needs next; null when it is in. The session is kept as it
/// goes (<see cref="TelegramSessionStore"/>).
/// </summary>
public sealed class TelegramAccountLogin : IDisposable
{
    private readonly WTelegram.Client _client;

    public TelegramAccountLogin(int apiId, string apiHash, Stream session) => _client = TelegramUserConnection.NewClient(apiId, apiHash, session);

    /// <summary>What it asks for next: <c>code</c>, <c>password</c>, or null when it is in (then <see cref="Me"/>).</summary>
    public async Task<string?> StepAsync(string value)
    {
        string? need;
        try
        {
            need = await _client.Login(value.Trim()).ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            throw new TelegramException(ex.Message switch
            {
                "PHONE_NUMBER_INVALID" => Loc.T("telegram.account.error.phone"),
                "PHONE_CODE_INVALID" or "PHONE_CODE_EMPTY" => Loc.T("telegram.account.error.code"),
                "PHONE_CODE_EXPIRED" => Loc.T("telegram.account.error.expired"),
                "PASSWORD_HASH_INVALID" => Loc.T("telegram.account.error.password"),
                "API_ID_INVALID" or "API_ID_PUBLISHED_FLOOD" => Loc.T("telegram.account.error.api"),
                "PHONE_NUMBER_BANNED" => Loc.T("telegram.account.error.banned"),
                _ when ex.Code == 420 => Loc.T("telegram.account.error.wait", Math.Max(1, ex.X / 60)),
                _ => ex.Message,
            }, ex.Code);
        }
        catch (WTelegram.WTException ex)
        {
            throw new TelegramException(ex.Message);
        }
        return need switch
        {
            null => null,
            "verification_code" => "code",
            "password" => "password",
            "name" => throw new TelegramException(Loc.T("telegram.account.error.noAccount")),
            _ => throw new TelegramException(Loc.T("telegram.account.error.step", need)),
        };
    }

    public TelegramUser? Me => _client.User is { } u ? new TelegramUser(u.id, ((u.first_name ?? string.Empty) + " " + (u.last_name ?? string.Empty)).Trim(), u.MainUsername) : null;

    public void Dispose() => _client.Dispose();
}

/// <summary>
/// The account's session as a stream for the library, kept in a file encrypted with DPAPI (only this user of this PC can
/// read it; a copy elsewhere is useless): each change is written whole.
/// </summary>
public sealed class TelegramSessionStore : MemoryStream
{
    private readonly string _path;
    private readonly Func<string, string> _protect;
    private bool _loading;

    public TelegramSessionStore(string path, Func<string, string> protect, Func<string, string?> unprotect)
    {
        _path = path;
        _protect = protect;
        try
        {
            if (File.Exists(path) && unprotect(File.ReadAllText(path)) is { Length: > 0 } kept)
            {
                _loading = true;
                var bytes = Convert.FromBase64String(kept);
                Write(bytes, 0, bytes.Length);
                Position = 0;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            Log.Warn($"Telegram: no se ha podido leer la sesión de la cuenta: {ex.Message}");
        }
        finally
        {
            _loading = false;
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        base.Write(buffer, offset, count);
        Persist();
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        base.Write(buffer);
        Persist();
    }

    public override void SetLength(long value)
    {
        base.SetLength(value);
        Persist();
    }

    private void Persist()
    {
        if (_loading) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, _protect(Convert.ToBase64String(GetBuffer(), 0, (int)Length)));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Telegram: no se ha podido guardar la sesión de la cuenta: {ex.Message}");
        }
    }
}

using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using KDesktop.Core.Calendar;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Remote.Telegram;

/// <summary>
/// Telegram in K-Desktop's process (telegram, phase 1): the bot connects and listens with long polling on its own task (it
/// comes back after a cut with a growing wait, and every error goes to the log without bringing anything down), the owner
/// is linked with a 6-digit code, and what is sent goes out in order through one queue that respects Telegram's waits.
/// The token never leaves this process. Off and closed until a bot and an owner are set.
/// </summary>
public sealed class TelegramService : IDisposable
{
    public static string DefaultFolder => Path.Combine(AppPaths.DataDir, "Telegram");

    /// <summary>How long a linking code is valid.</summary>
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Wrong codes before the linking is cancelled (a new code has to be asked for).</summary>
    public const int LinkTries = 5;

    /// <summary>Telegram's limit for one text; longer ones go in several messages.</summary>
    public const int MaxText = 4096;

    private const long LogLimit = 512 * 1024;

    /// <summary>Something to send: a text (with buttons), or a file with the text as its caption.</summary>
    private sealed record Outgoing(long Chat, string Text, string To, TaskCompletionSource<bool>? Done, IReadOnlyList<IReadOnlyList<TelegramButton>>? Buttons = null,
        string? File = null, TelegramFileKind Kind = TelegramFileKind.Document, TaskCompletionSource<int>? SentId = null);

    /// <summary>Telegram's limit for a file's caption; a longer text goes after it, as a message.</summary>
    private const int MaxCaption = 1024;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (TaskCompletionSource<bool> Answer, string Question)> _asks = new();

    private readonly string _folder;
    private readonly Func<string, string> _protect;
    private readonly Func<string, string?> _unprotect;
    private readonly Func<string, ITelegramConnection> _connect;
    private readonly Lock _gate = new();
    private readonly Lock _logGate = new();
    private readonly Channel<Outgoing> _queue = Channel.CreateUnbounded<Outgoing>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _life = new();
    private readonly TelegramLimiter _limiter = new();
    private readonly SemaphoreSlim _downloading = new(1, 1);
    private readonly Task _sender;
    private (CancellationTokenSource Stop, Task Loop)? _run;
    private volatile ITelegramConnection? _connection;
    private string? _linkCode;
    private DateTimeOffset _linkExpires;
    private int _linkWrong;
    private bool _disposed;

    /// <param name="connect">
    /// Makes the connection: for the bot, with its token; for the account, with its api_hash (the tests give a fake one).
    /// </param>
    /// <param name="kind">The bot (Telegram\) or the account with a number (Telegram\Account\, phase 6): each with its own owner, contacts and options.</param>
    public TelegramService(string folder, Func<string, string> protect, Func<string, string?> unprotect, Func<string, ITelegramConnection>? connect = null,
        TelegramVia kind = TelegramVia.Bot)
    {
        _folder = folder;
        _protect = protect;
        _unprotect = unprotect;
        Kind = kind;
        _connect = connect ?? (kind == TelegramVia.Bot ? token => new TelegramBotConnection(token) : OpenAccount);
        Settings = Load();
        _sender = Task.Run(SendLoopAsync);
    }

    /// <summary>The folder of the account with a number (its settings and its session).</summary>
    public static string AccountFolder => Path.Combine(DefaultFolder, "Account");

    public TelegramVia Kind { get; }

    public TelegramSettings Settings { get; }

    /// <summary>Whether there is a bot or an account, switched on.</summary>
    public bool On
    {
        get
        {
            lock (_gate) return Kind == TelegramVia.Bot ? Settings.Bot is { Enabled: true } : Settings.Account is { Enabled: true, Id: > 0 };
        }
    }

    /// <summary>Whether a bot or an account is set (on or off).</summary>
    public bool Configured
    {
        get
        {
            lock (_gate) return Kind == TelegramVia.Bot ? Settings.Bot is not null : Settings.Account is { Id: > 0 };
        }
    }

    /// <summary>Who it is in Telegram: the bot's or the account's id.</summary>
    public long MeId
    {
        get
        {
            lock (_gate) return Kind == TelegramVia.Bot ? Settings.Bot?.Id ?? 0 : Settings.Account?.Id ?? 0;
        }
    }

    /// <summary>Its @user (empty when it has none: an account may not have one).</summary>
    public string MeUsername
    {
        get
        {
            lock (_gate) return (Kind == TelegramVia.Bot ? Settings.Bot?.Username : Settings.Account?.Username) ?? string.Empty;
        }
    }

    /// <summary>Something to know that is not a failure: Telegram limits the account for a while (phase 6.5).</summary>
    public string? Warning { get; private set; }

    public TelegramState State { get; private set; } = TelegramState.Off;

    /// <summary>What is wrong now, in words (no network, the token is not valid…); null: fine.</summary>
    public string? Problem { get; private set; }

    /// <summary>The state, the bot or the owner changed (any thread).</summary>
    public event Action? Changed;

    /// <summary>The owner was linked (the connection's task).</summary>
    public event Action<TelegramOwner>? OwnerLinked;

    /// <summary>
    /// A message to be served (the owner's, a contact's or someone new's with a level that is not «Ignorar»), with the
    /// verdict (the connection's task; for the next phases).
    /// </summary>
    public event Action<TelegramIncoming, TelegramVerdict>? Received;

    /// <summary>Someone new wrote for the first time and waits for the owner's decision (the connection's task).</summary>
    public event Action<TelegramPerson>? StrangerArrived;

    /// <summary>Whether a game (or anything) is at full screen now: with «solo yo mientras juego», only the owner is served.</summary>
    public Func<bool> Busy { get; set; } = () => false;

    /// <summary>The first wait after a failure (it doubles up to 5 minutes); shorter in the tests.</summary>
    internal TimeSpan RetryBase { get; set; } = TimeSpan.FromSeconds(5);

    internal Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    public TelegramBotSettings? Bot
    {
        get { lock (_gate) return Settings.Bot; }
    }

    public TelegramOwner? Owner
    {
        get { lock (_gate) return Settings.Owner; }
    }

    public string LogFile => Path.Combine(_folder, "messages.log");

    private string SettingsFile => Path.Combine(_folder, "settings.json");

    // ── the bot ──────────────────────────────────────────────────────────

    /// <summary>
    /// Checks a token with Telegram and keeps it (encrypted). The same bot with a new token keeps its owner; another bot
    /// starts without one. Throws <see cref="TelegramException"/> (or a network error) with what fails in words.
    /// </summary>
    public async Task<TelegramUser> SetBotAsync(string token, CancellationToken cancel = default)
    {
        token = token.Trim();
        if (!TelegramBotConnection.LooksLikeToken(token)) throw new TelegramException(Loc.T("telegram.error.notToken"));
        TelegramUser me;
        using (var probe = _connect(token))
        {
            me = await probe.GetMeAsync(cancel).ConfigureAwait(false);
            if (probe is TelegramBotConnection bot) await bot.DeleteWebhookAsync(cancel).ConfigureAwait(false);
        }
        lock (_gate)
        {
            if (Settings.Bot?.Id != me.Id) Settings.Owner = null;
            Settings.Bot = new TelegramBotSettings { TokenProtected = _protect(token), Id = me.Id, Name = me.Name, Username = me.Username ?? string.Empty, Enabled = true };
            _linkCode = null;
            SaveSettings();
        }
        WriteLog($"bot @{me.Username} guardado");
        Restart();
        return me;
    }

    /// <summary>Forgets the bot, its token and the owner.</summary>
    public void RemoveBot()
    {
        lock (_gate)
        {
            Settings.Bot = null;
            Settings.Owner = null;
            _linkCode = null;
            SaveSettings();
        }
        WriteLog("bot quitado");
        Restart();
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (Kind == TelegramVia.Account)
            {
                if (Settings.Account is not { } account || account.Enabled == enabled) return;
                account.Enabled = enabled;
            }
            else
            {
                if (Settings.Bot is not { } bot || bot.Enabled == enabled) return;
                bot.Enabled = enabled;
            }
            SaveSettings();
        }
        Restart();
    }

    /// <summary>The address of the bot or the account in Telegram («https://t.me/kdesktop_bot»); null without an @user.</summary>
    public string? BotUrl => MeUsername is { Length: > 0 } user ? "https://t.me/" + user : null;

    // ── the account with a number (phase 6) ──────────────────────────────

    private TelegramAccountLogin? _login;
    private (int ApiId, string ApiHash, string Phone) _loginWith;

    public TelegramAccountSettings? Account
    {
        get { lock (_gate) return Settings.Account; }
    }

    private string SessionFile => Path.Combine(_folder, "account.session");

    /// <summary>Whether a login is under way (waiting for the code or the password).</summary>
    public bool LoggingIn => _login is not null;

    /// <summary>
    /// Starts logging in an account (phase 6.2) with the application of my.telegram.org and the number: what it needs next
    /// (<c>code</c>, <c>password</c>) or null when it is in. The account that was on stops meanwhile. Throws
    /// <see cref="TelegramException"/> with what fails in words.
    /// </summary>
    public async Task<string?> LoginAsync(int apiId, string apiHash, string phone)
    {
        CancelLogin(restart: false);
        Stop();
        DeleteSession();
        _loginWith = (apiId, apiHash.Trim(), new string(phone.Where(c => char.IsAsciiDigit(c) || c == '+').ToArray()));
        _login = new TelegramAccountLogin(apiId, _loginWith.ApiHash, new TelegramSessionStore(SessionFile, _protect, _unprotect));
        WriteLog($"iniciando sesión con {_loginWith.Phone}");
        return await StepAsync(_loginWith.Phone).ConfigureAwait(false);
    }

    /// <summary>The code Telegram sent, or the password of the two-step verification: what it needs next, or null when it is in.</summary>
    public Task<string?> LoginStepAsync(string value) =>
        _login is null ? throw new TelegramException(Loc.T("telegram.account.error.step", "login")) : StepAsync(value);

    private async Task<string?> StepAsync(string value)
    {
        var need = await _login!.StepAsync(value).ConfigureAwait(false);
        if (need is not null) return need;
        var me = _login.Me ?? throw new TelegramException(Loc.T("telegram.account.error.step", "user"));
        _login.Dispose();
        _login = null;
        lock (_gate)
        {
            var fresh = Settings.Account is null;
            // Another account: whoever was the owner has to link again (the same one keeps it).
            if (Settings.Account?.Id != me.Id) Settings.Owner = null;
            Settings.Account = new TelegramAccountSettings
            {
                ApiId = _loginWith.ApiId, ApiHashProtected = _protect(_loginWith.ApiHash), Phone = _loginWith.Phone,
                Id = me.Id, Name = me.Name, Username = me.Username ?? string.Empty, Enabled = true,
            };
            if (fresh)
            {
                // An account with a number is more careful than a bot (phase 6.5), and the bot already tells the mails.
                Settings.Limits.NewPerHour = 3;
                Settings.Notices.ImportantMail = false;
            }
            SaveSettings();
        }
        WriteLog($"sesión iniciada: {me.Shown} [{me.Id}]");
        Log.Info($"Telegram: sesión iniciada en la cuenta {me.Id}");
        Restart();
        return null;
    }

    /// <summary>Leaves a login half done; the account that was there (if any) goes on.</summary>
    public void CancelLogin(bool restart = true)
    {
        if (_login is null) return;
        _login.Dispose();
        _login = null;
        if (!restart) return;
        // The session of the account that was there was replaced: it has to log in again.
        if (Configured) SetState(TelegramState.Problem, Loc.T("telegram.account.loggedOut"));
        else Restart();
    }

    /// <summary>Closes the session (in Telegram too, when it can) and forgets the account and its owner; the contacts stay.</summary>
    public async Task LogOutAsync()
    {
        if (_connection is ITelegramAgenda agenda)
        {
            try { await agenda.LogOutAsync(_life.Token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is TelegramException or IOException or OperationCanceledException)
            {
                WriteLog($"no se ha podido cerrar la sesión en Telegram: {ex.Message}");
            }
        }
        lock (_gate)
        {
            Settings.Account = null;
            Settings.Owner = null;
            _linkCode = null;
            SaveSettings();
        }
        Stop();
        DeleteSession();
        WriteLog("sesión cerrada");
        Restart();
    }

    private void DeleteSession()
    {
        try { File.Delete(SessionFile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Telegram: no se ha podido borrar la sesión: {ex.Message}"); }
    }

    private ITelegramConnection OpenAccount(string apiHash)
    {
        var account = Account ?? throw new TelegramException(Loc.T("telegram.account.loggedOut"), 401);
        return new TelegramUserConnection(account.ApiId, apiHash, account.Phone, new TelegramSessionStore(SessionFile, _protect, _unprotect));
    }

    /// <summary>The people in the account's contacts in Telegram (phase 6.4); null when it is not connected.</summary>
    public async Task<IReadOnlyList<TelegramUser>?> AgendaAsync() =>
        _connection is ITelegramAgenda agenda ? await agenda.AgendaAsync(_life.Token).ConfigureAwait(false) : null;

    /// <summary>
    /// Adds someone to the account's contacts in Telegram by their number; who it is, or null when that number has no
    /// Telegram. Throws <see cref="TelegramException"/> when it is not connected.
    /// </summary>
    public async Task<TelegramUser?> AddToAgendaAsync(string phone, string firstName, string lastName)
    {
        if (_connection is not ITelegramAgenda agenda) throw new TelegramException(Loc.T("telegram.error.notReady"));
        var user = await agenda.AddToAgendaAsync(phone, firstName, lastName, _life.Token).ConfigureAwait(false);
        WriteLog(user is null ? $"{phone} no tiene Telegram" : $"añadido a la agenda: {user.Shown} [{user.Id}]");
        return user;
    }

    /// <summary>
    /// Makes someone a contact with <paramref name="level"/> (from the account's contacts, or added by their number); one
    /// already there keeps what it had. False when it is the owner.
    /// </summary>
    public bool AddContact(TelegramUser user, TelegramLevel level)
    {
        lock (_gate)
        {
            if (Settings.Owner?.Id == user.Id) return false;
            Settings.Pending.RemoveAll(p => p.Id == user.Id);
            Settings.Blocked.RemoveAll(p => p.Id == user.Id);
            if (Settings.Contacts.All(c => c.Id != user.Id))
                Settings.Contacts.Add(new TelegramContact { Id = user.Id, Name = user.Name, Username = user.Username, Phone = user.Phone, Level = level, Added = Now() });
            SaveSettings();
        }
        WriteLog($"contacto añadido: {user.Shown} [{user.Id}] ({level})");
        Changed?.Invoke();
        return true;
    }

    // ── the owner ────────────────────────────────────────────────────────

    /// <summary>The code being waited for (sent to the bot, or with the link); null: not linking.</summary>
    public string? LinkCode
    {
        get
        {
            lock (_gate) return _linkCode is not null && Now() < _linkExpires ? _linkCode : null;
        }
    }

    /// <summary>«https://t.me/kdesktop_bot?start=123456»: opening it in Telegram and pressing «Iniciar» links.</summary>
    public string? LinkUrl => LinkCode is { } code && BotUrl is { } url ? Kind == TelegramVia.Bot ? $"{url}?start={code}" : url : null;

    /// <summary>A new 6-digit code, valid for <see cref="LinkLifetime"/>; whoever sends it to the bot becomes the owner.</summary>
    public string StartLinking()
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        lock (_gate)
        {
            _linkCode = code;
            _linkExpires = Now() + LinkLifetime;
            _linkWrong = 0;
        }
        Changed?.Invoke();
        return code;
    }

    public void CancelLinking()
    {
        lock (_gate) _linkCode = null;
        Changed?.Invoke();
    }

    public void Unlink()
    {
        lock (_gate)
        {
            Settings.Owner = null;
            SaveSettings();
        }
        WriteLog("dueño desvinculado");
        Changed?.Invoke();
    }

    // ── sending ──────────────────────────────────────────────────────────

    /// <summary>Puts a text for the owner in the queue; false when there is nobody to send it to (no bot on, no owner).</summary>
    public bool SendToOwner(string text) => Enqueue(text, null);

    /// <summary>As <see cref="SendToOwner"/>, waiting until it is sent: true when Telegram took it.</summary>
    public Task<bool> SendToOwnerAsync(string text)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return Enqueue(text, done) ? done.Task : Task.FromResult(false);
    }

    /// <summary>
    /// Sends a text, or a file with the text as its caption, to a chat (whoever asks has checked <see cref="CanWriteTo"/>);
    /// true when Telegram took it.
    /// </summary>
    public Task<bool> SendAsync(long chat, string text, string? file = null, TelegramFileKind kind = TelegramFileKind.Document)
    {
        if (!On || (string.IsNullOrWhiteSpace(text) && file is null)) return Task.FromResult(false);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _queue.Writer.TryWrite(new Outgoing(chat, text, NameOf(chat), done, null, file, kind)) ? done.Task : Task.FromResult(false);
    }

    /// <summary>As <see cref="SendAsync"/>, with the id of the (last) message sent, to delete it later; null when it was not sent.</summary>
    public async Task<int?> SendForIdAsync(long chat, string text, string? file = null, TelegramFileKind kind = TelegramFileKind.Document)
    {
        if (!On || (string.IsNullOrWhiteSpace(text) && file is null)) return null;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_queue.Writer.TryWrite(new Outgoing(chat, text, NameOf(chat), done, null, file, kind, sent))) return null;
        if (!await done.Task.ConfigureAwait(false)) return null;
        return sent.Task.IsCompletedSuccessfully ? sent.Task.Result : 0;
    }

    /// <summary>Deletes a message the bot sent (undoing telegram_send); false when it could not (gone, too old: 48 h).</summary>
    public async Task<bool> DeleteAsync(long chat, int message)
    {
        if (_connection is not { Capabilities.CanDelete: true } connection || message <= 0) return false;
        try
        {
            await connection.DeleteAsync(chat, message, _life.Token).ConfigureAwait(false);
            WriteLog($"borrado un mensaje a {NameOf(chat)}");
            return true;
        }
        catch (Exception ex) when (ex is TelegramException or HttpRequestException or IOException or OperationCanceledException or JsonException)
        {
            WriteLog($"no borrado ({NameOf(chat)}): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// «Escribiendo…» in a chat (it lasts about 5 s, or until the bot writes); with <paramref name="voice"/>, «Grabando un
    /// audio…».
    /// </summary>
    public void Typing(long chat, bool voice = false)
    {
        if (_connection is { } connection) _ = Quiet(() => connection.SendChatActionAsync(chat, voice ? "record_voice" : "typing", _life.Token));
    }

    // ── what was said lately (telegram_read) ─────────────────────────────

    /// <summary>Messages kept per chat, and for how long (only in memory: telegram_read sees what is recent).</summary>
    public const int KeptPerChat = 50;

    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(2);

    private readonly Dictionary<long, LinkedList<TelegramLine>> _recent = [];

    /// <summary>The file of a message kept last (telegram_send_file «lo último que llegó»).</summary>
    public string? LastDownload { get; private set; }

    private void Keep(long chat, string from, string text, bool sent)
    {
        if (text.Trim().Length == 0) return;
        var now = Now();
        lock (_recent)
        {
            if (!_recent.TryGetValue(chat, out var lines)) _recent[chat] = lines = new LinkedList<TelegramLine>();
            lines.AddLast(new TelegramLine(chat, from, now, text.Length > 2000 ? text[..2000] + "…" : text, sent));
            while (lines.Count > KeptPerChat || (lines.First is { } first && now - first.Value.At > KeptFor)) lines.RemoveFirst();
        }
    }

    /// <summary>The last <paramref name="max"/> messages of a chat (oldest first), the bot's own too.</summary>
    public IReadOnlyList<TelegramLine> Recent(long chat, int max = 20)
    {
        var since = Now() - KeptFor;
        lock (_recent)
            return _recent.TryGetValue(chat, out var lines) ? [.. lines.Where(l => l.At >= since).TakeLast(Math.Clamp(max, 1, KeptPerChat))] : [];
    }

    /// <summary>The chats with something said lately, newest first.</summary>
    public IReadOnlyList<long> RecentChats()
    {
        lock (_recent) return [.. _recent.Where(c => c.Value.Count > 0).OrderByDescending(c => c.Value.Last!.Value.At).Select(c => c.Key)];
    }

    /// <summary>A group's name, or null when the bot does not know it.</summary>
    public string? GroupName(long id)
    {
        lock (_gate) return Settings.Groups.FirstOrDefault(g => g.Id == id)?.Title;
    }

    /// <summary>Puts a text (with buttons) for a chat in the queue, without waiting.</summary>
    public void Post(long chat, string text, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons = null) => Reply(chat, text, NameOf(chat), buttons);

    /// <summary>Who a chat is, for the log: «dueño», a contact's name or its id.</summary>
    public string NameOf(long id)
    {
        lock (_gate)
        {
            if (Settings.Owner?.Id == id) return "dueño";
            return Settings.Contacts.FirstOrDefault(c => c.Id == id)?.Shown ?? id.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The chat a recipient means: empty, the owner; a contact's id, or its name or @user when only one has it. Null:
    /// nobody (or several).
    /// </summary>
    public long? Recipient(string to)
    {
        to = to.Trim().TrimStart('@');
        lock (_gate)
        {
            if (to.Length == 0) return Settings.Owner?.Id;
            // An id; a number that is no id may be a phone number (below).
            if (long.TryParse(to, CultureInfo.InvariantCulture, out var id) && (Settings.Owner?.Id == id || Settings.Contacts.Any(c => c.Id == id))) return id;
            // By name, @user or number, without case or accents; else by a part of the name, when only one has it («Ana» → «Ana García»).
            var key = Folded(to);
            if (key.Length == 0) return null;
            static string Digits(string? s) => new([.. (s ?? string.Empty).Where(char.IsAsciiDigit)]);
            var found = Settings.Contacts.Where(c => Folded(c.Name) == key || Folded(c.Username ?? string.Empty) == key
                                                     || (Digits(to).Length >= 6 && Digits(c.Phone).EndsWith(Digits(to), StringComparison.Ordinal))).ToList();
            if (found.Count == 0)
                found = [.. Settings.Contacts.Where(c => Mail.MailRules.Fold(c.Name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word => Folded(word) == key))];
            return found.Count == 1 ? found[0].Id : null;
        }
    }

    /// <summary>Tells Telegram a button was handled, with a short text over the chat.</summary>
    public void Answer(TelegramIncoming button, string? text)
    {
        if (_connection is { } connection && button.CallbackId is { } id) _ = Quiet(() => connection.AnswerButtonAsync(id, text, _life.Token));
    }

    /// <summary>Changes a message the bot sent (its buttons go).</summary>
    public void Edit(long chat, int message, string text)
    {
        if (_connection is { } connection && message > 0) _ = Quiet(() => connection.EditTextAsync(chat, message, text, _life.Token));
    }

    /// <summary>
    /// Asks the owner by Telegram, with «Sí» and «No» (phase 3.4): true or false, or null when nobody answered in
    /// <paramref name="expire"/> (it is not done) or it could not be asked. The message then says what happened.
    /// </summary>
    public async Task<bool?> AskOwnerAsync(string question, TimeSpan expire, CancellationToken cancel = default)
    {
        if (!On || Owner is not { } owner) return null;
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sent = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        _asks[key] = (answer, question);
        IReadOnlyList<IReadOnlyList<TelegramButton>> buttons = [[new("✓ " + Loc.T("telegram.ask.yes"), "y:" + key), new("✗ " + Loc.T("telegram.ask.no"), "n:" + key)]];
        if (!_queue.Writer.TryWrite(new Outgoing(owner.Id, "❓ " + question, "dueño", null, buttons, SentId: sent)))
        {
            _asks.TryRemove(key, out _);
            return null;
        }
        try
        {
            using var expired = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            expired.CancelAfter(expire);
            return await answer.Task.WaitAsync(expired.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!_asks.TryRemove(key, out _)) return answer.Task.IsCompletedSuccessfully ? answer.Task.Result : null;
            WriteLog($"sin respuesta: {Short(question)}");
            if (sent.Task.IsCompletedSuccessfully) Edit(owner.Id, sent.Task.Result, "⌛ " + Loc.T("telegram.ask.expired", question));
            return null;
        }
    }

    /// <summary>
    /// Downloads the file of a message into <paramref name="folder"/> without overwriting another («foto (2).jpg»); its
    /// path, or null when it has none or it could not be downloaded.
    /// </summary>
    public async Task<string?> DownloadAsync(TelegramIncoming m, string folder, CancellationToken cancel = default)
    {
        if (m.File is not { } file || _connection is not { } connection) return null;
        if (file.Size > connection.Capabilities.MaxDownload)
        {
            WriteLog($"archivo demasiado grande: {file.Name} ({file.Size} bytes)");
            return null;
        }
        var stamp = m.Date.ToLocalTime().ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);
        var name = m.Content switch
        {
            TelegramContent.Photo => $"{Loc.T("telegram.file.photo")} {stamp}.jpg",
            TelegramContent.Voice => $"{Loc.T("telegram.file.voice")} {stamp}.ogg",
            _ => Web.WebServer.SafeName(file.Name) ?? $"{Loc.T("telegram.file.file")} {stamp}",
        };
        // One at a time: two «factura.pdf» at once must not choose the same free name.
        await _downloading.WaitAsync(cancel).ConfigureAwait(false);
        var path = string.Empty;
        try
        {
            Directory.CreateDirectory(folder);
            path = Mail.MailService.FreeName(folder, name);
            await connection.DownloadAsync(file.Id, path, cancel).ConfigureAwait(false);
            WriteLog($"archivo guardado: {path}");
            LastDownload = path;
            return path;
        }
        catch (Exception ex) when (ex is TelegramException or HttpRequestException or IOException or UnauthorizedAccessException or JsonException)
        {
            WriteLog($"archivo no guardado ({name}): {ex.Message}");
            try { if (path.Length > 0) File.Delete(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            return null;
        }
        finally
        {
            _downloading.Release();
        }
    }

    private bool Enqueue(string text, TaskCompletionSource<bool>? done)
    {
        if (!On || Owner is not { } owner || string.IsNullOrWhiteSpace(text)) return false;
        return _queue.Writer.TryWrite(new Outgoing(owner.Id, text, "dueño", done));
    }

    private void Reply(long chat, string text, string to, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons = null) =>
        _queue.Writer.TryWrite(new Outgoing(chat, text, to, null, buttons));

    /// <summary>
    /// Whether K-Desktop may write to <paramref name="id"/> now (phase 2.3): the owner always; the blocked and the contacts
    /// without «puede escribirle» never; a contact who never wrote, within the limit an hour (each yes counts).
    /// </summary>
    public bool CanWriteTo(long id)
    {
        TelegramContact? contact;
        int perHour;
        lock (_gate)
        {
            if (Settings.Owner?.Id == id) return true;
            if (Settings.Blocked.Any(b => b.Id == id)) return false;
            contact = Settings.Contacts.FirstOrDefault(c => c.Id == id);
            perHour = Settings.Limits.NewPerHour;
        }
        if (contact is not { MayWrite: true }) return false;
        return contact.LastWrote is not null || _limiter.AllowNew(Now(), perHour);
    }

    /// <summary>Texts longer than Telegram allows, cut at a line or a space.</summary>
    public static IReadOnlyList<string> Split(string text, int max = MaxText)
    {
        var parts = new List<string>();
        var rest = text.Trim();
        while (rest.Length > max)
        {
            var cut = rest.LastIndexOf('\n', max - 1);
            if (cut < max / 2) cut = rest.LastIndexOf(' ', max - 1);
            if (cut < max / 2) cut = max;
            parts.Add(rest[..cut].TrimEnd());
            rest = rest[cut..].TrimStart();
        }
        if (rest.Length > 0) parts.Add(rest);
        return parts;
    }

    /// <summary>
    /// Sends what is queued, in order. Too fast: waits what Telegram says and tries again; without connection or network:
    /// tries for a while (about two minutes) and then gives it up; refused (blocked, a wrong chat): gives it up at once.
    /// </summary>
    private async Task SendLoopAsync()
    {
        var stop = _life.Token;
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stop).ConfigureAwait(false))
            {
                var ok = true;
                var text = item.Text;
                if (item.File is { } file)
                {
                    var caption = text.Length <= MaxCaption ? text : null;
                    ok = await SendPartAsync(item, $"[{Path.GetFileName(file)}] {caption}", async (c, t) =>
                    {
                        var id = await c.SendFileAsync(item.Chat, item.Kind, file, caption, t).ConfigureAwait(false);
                        if (caption is not null) item.SentId?.TrySetResult(id);
                        return id;
                    }, stop).ConfigureAwait(false);
                    if (ok) Keep(item.Chat, "K-Desktop", $"[{Path.GetFileName(file)}] {caption}", true);
                    text = caption is null ? text : string.Empty;
                }
                // Without buttons (an account), they go as numbered lines, answered with the number or the word.
                var offered = item.Buttons is { Count: > 0 } && !Buttons ? item.Buttons.SelectMany(row => row).ToList() : null;
                if (offered is not null) text = text.TrimEnd() + "\n\n" + ChoiceLines(offered);
                var parts = ok ? Split(text) : [];
                for (var i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    var last = i == parts.Count - 1;
                    var buttons = last && offered is null ? item.Buttons : null;
                    if (!await SendPartAsync(item, part, async (c, t) =>
                        {
                            var id = await c.SendTextAsync(item.Chat, part, buttons, t).ConfigureAwait(false);
                            if (last) item.SentId?.TrySetResult(id);
                            if (last && offered is not null) Offer(item.Chat, id, offered);
                            return id;
                        }, stop).ConfigureAwait(false))
                    {
                        ok = false;
                        break;
                    }
                    Keep(item.Chat, "K-Desktop", part, true);
                }
                item.SentId?.TrySetResult(0);
                item.Done?.TrySetResult(ok);
            }
        }
        catch (OperationCanceledException) { }
        while (_queue.Reader.TryRead(out var left)) left.Done?.TrySetResult(false);
    }

    private async Task<bool> SendPartAsync(Outgoing item, string text, Func<ITelegramConnection, CancellationToken, Task<int>> send, CancellationToken stop)
    {
        var waited = TimeSpan.Zero;
        var wait = RetryBase;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var connection = _connection;
            if (connection is null)
            {
                if (!On || waited > TimeSpan.FromMinutes(2)) break;
                await Task.Delay(TimeSpan.FromSeconds(1), stop).ConfigureAwait(false);
                waited += TimeSpan.FromSeconds(1);
                attempt--;
                continue;
            }
            try
            {
                // An account goes slower than a bot (phase 6.5): never two messages in the same second.
                if (Kind == TelegramVia.Account && Now() - _lastSent is { } since && since < AccountGap && since >= TimeSpan.Zero)
                    await Task.Delay(AccountGap - since, stop).ConfigureAwait(false);
                await send(connection, stop).ConfigureAwait(false);
                _lastSent = Now();
                WriteLog($"→ {item.To}: {Short(text)}");
                if (Warning is not null && _lastSent - _warned > TimeSpan.FromHours(1))
                {
                    Warning = null;
                    Changed?.Invoke();
                }
                return true;
            }
            catch (TelegramException ex) when (ex.RetryAfter is { } after)
            {
                WriteLog($"Telegram pide esperar {after.TotalSeconds:0} s");
                if (after > TimeSpan.FromMinutes(5))
                {
                    // A long wait: it is not kept waiting (it would hold everything else), and the page says so.
                    Warn(Loc.T("telegram.account.waiting", Math.Ceiling(after.TotalMinutes)));
                    return false;
                }
                await Task.Delay(after, stop).ConfigureAwait(false);
            }
            catch (TelegramException ex)
            {
                if (ex.Limited) Warn(ex.Message);
                WriteLog($"no enviado a {item.To}: {ex.Message}");
                Log.Warn($"Telegram: no se ha podido enviar: {ex.Message}");
                return false;
            }
            catch (Exception ex) when (Transient(ex, stop))
            {
                if (attempt >= 5) break;
                await Task.Delay(wait, stop).ConfigureAwait(false);
                waited += wait;
                wait *= 2;
            }
        }
        WriteLog($"no enviado a {item.To}: sin conexión");
        return false;
    }

    /// <summary>The least time between two messages of an account.</summary>
    private static readonly TimeSpan AccountGap = TimeSpan.FromSeconds(1.2);

    private DateTimeOffset _lastSent = DateTimeOffset.MinValue;
    private DateTimeOffset _warned;

    private void Warn(string text)
    {
        _warned = Now();
        Warning = text;
        Log.Warn($"Telegram: {text}");
        Changed?.Invoke();
    }

    // ── answering without buttons (an account) ───────────────────────────

    /// <summary>How long the numbered lines of a message can be answered.</summary>
    public static readonly TimeSpan OfferedFor = TimeSpan.FromMinutes(30);

    private sealed record Offered(long Chat, int Message, IReadOnlyList<TelegramButton> Buttons, DateTimeOffset At);

    private readonly List<Offered> _offered = [];

    /// <summary>Whether the connection has buttons (an account has not; until it connects, by its kind).</summary>
    private bool Buttons => _connection?.Capabilities.CanButtons ?? Kind == TelegramVia.Bot;

    /// <summary>The buttons as lines: «1 · ✓ Sí», «2 · ✗ No» and how to answer.</summary>
    public static string ChoiceLines(IReadOnlyList<TelegramButton> buttons) =>
        string.Join("\n", buttons.Select((b, i) => string.Create(CultureInfo.InvariantCulture, $"{i + 1} · {b.Text}"))) + "\n" + Loc.T("telegram.choice.hint");

    private void Offer(long chat, int message, IReadOnlyList<TelegramButton> buttons)
    {
        lock (_offered)
        {
            _offered.RemoveAll(o => Now() - o.At > OfferedFor);
            _offered.Add(new Offered(chat, message, buttons, Now()));
        }
    }

    private static string Folded(string text) => new([.. Mail.MailRules.Fold(text).Where(char.IsLetterOrDigit)]);

    /// <summary>
    /// An answer to numbered lines (the number, or what the line says: «sí», «permitir»…) as the button it stands for; the
    /// one answered to, else the newest of that chat. Null: it is not one.
    /// </summary>
    private TelegramIncoming? Chosen(TelegramIncoming m)
    {
        var said = Folded(m.Text);
        if (said.Length == 0 || said.Length > 40) return null;
        var number = int.TryParse(m.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
        lock (_offered)
        {
            _offered.RemoveAll(o => Now() - o.At > OfferedFor);
            var candidates = _offered.Where(o => o.Chat == m.ChatId).Reverse().ToList();
            if (m.ReplyToMessageId is { } replied && candidates.FirstOrDefault(o => o.Message == replied) is { } answered) candidates = [answered];
            foreach (var offer in candidates)
            {
                var button = number > 0
                    ? number <= offer.Buttons.Count ? offer.Buttons[number - 1] : null
                    : offer.Buttons.FirstOrDefault(b => Folded(b.Text) == said);
                // A number only counts for the newest lines.
                if (button is null && number > 0) return null;
                if (button is null) continue;
                _offered.Remove(offer);
                return m with { CallbackId = string.Empty, CallbackData = button.Data, MessageId = offer.Message, Text = string.Empty, Content = TelegramContent.Other, File = null };
            }
        }
        return null;
    }

    // ── the connection ───────────────────────────────────────────────────

    public void Start() => Restart();

    /// <summary>Stops the connection (it comes back with <see cref="Restart"/>).</summary>
    private void Stop()
    {
        if (_run is { } old)
        {
            old.Stop.Cancel();
            _ = old.Loop.ContinueWith(_ => old.Stop.Dispose(), TaskScheduler.Default);
            _run = null;
        }
        _connection = null;
    }

    private void Restart()
    {
        Stop();
        if (_disposed) return;
        if (!On)
        {
            SetState(TelegramState.Off, null);
            return;
        }
        var stop = new CancellationTokenSource();
        _run = (stop, Task.Run(() => RunAsync(stop.Token)));
    }

    /// <summary>Connects and listens; a cut comes back with a growing wait (5 s → 5 min). A wrong token stops it.</summary>
    private async Task RunAsync(CancellationToken stop)
    {
        var wait = RetryBase;
        while (!stop.IsCancellationRequested)
        {
            if (!On) return;
            string? kept;
            lock (_gate) kept = Kind == TelegramVia.Bot ? Settings.Bot?.TokenProtected : Settings.Account?.ApiHashProtected;
            if (kept is null || _unprotect(kept) is not { } secret)
            {
                SetState(TelegramState.Problem, Loc.T(Kind == TelegramVia.Bot ? "telegram.error.token" : "telegram.account.loggedOut"));
                return;
            }
            SetState(TelegramState.Connecting, Problem);
            ITelegramConnection connection;
            try { connection = _connect(secret); }
            catch (TelegramException ex)
            {
                SetState(TelegramState.Problem, ex.Message);
                return;
            }
            try
            {
                var me = await connection.GetMeAsync(stop).ConfigureAwait(false);
                Refresh(me);
                _connection = connection;
                SetState(TelegramState.Connected, null);
                WriteLog($"conectado como {me.Shown}");
                // «/comandos» in the bot's menu, in the language of K-Desktop.
                if (connection is TelegramBotConnection botApi)
                    _ = Quiet(() => botApi.SetMenuAsync([(Loc.T("telegram.cmd.word"), Loc.T("telegram.cmd.menu"))], stop));
                wait = RetryBase;
                while (!stop.IsCancellationRequested)
                {
                    var arrived = await connection.ReceiveAsync(stop).ConfigureAwait(false);
                    foreach (var message in arrived) Handle(message);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (TelegramException ex) when (ex.BadToken)
            {
                SetState(TelegramState.Problem, ex.Message);
                Log.Warn($"Telegram: {ex.Message}");
                return;
            }
            catch (Exception ex) when (ex is TelegramException || Transient(ex, stop))
            {
                SetState(TelegramState.Problem, ex.Message);
                WriteLog($"sin conexión: {ex.Message}");
                Log.Warn($"Telegram: sin conexión ({ex.Message}); se vuelve a intentar en {wait.TotalSeconds:0} s");
                try { await Task.Delay((ex as TelegramException)?.RetryAfter ?? wait, stop).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                wait = TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, TimeSpan.FromMinutes(5).Ticks));
            }
            catch (Exception ex)
            {
                // Nothing of Telegram brings K-Desktop down.
                Log.Error("Telegram: error inesperado", ex);
                SetState(TelegramState.Problem, ex.Message);
                try { await Task.Delay(TimeSpan.FromMinutes(1), stop).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
            finally
            {
                if (_connection == connection) _connection = null;
                connection.Dispose();
            }
        }
    }

    private static bool Transient(Exception ex, CancellationToken stop) =>
        ex is HttpRequestException or IOException or SocketException or JsonException
        || (ex is TaskCanceledException && !stop.IsCancellationRequested);

    /// <summary>The bot (in @BotFather) or the account changed its name or @user: kept (the id does not change).</summary>
    private void Refresh(TelegramUser me)
    {
        lock (_gate)
        {
            if (Settings.Account is { } account)
            {
                if (account.Name == me.Name && account.Username == (me.Username ?? string.Empty)) return;
                account.Name = me.Name;
                account.Username = me.Username ?? string.Empty;
            }
            else
            {
                if (Settings.Bot is not { } bot || (bot.Name == me.Name && bot.Username == (me.Username ?? string.Empty))) return;
                bot.Name = me.Name;
                bot.Username = me.Username ?? string.Empty;
            }
            SaveSettings();
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// What arrived: a button of the owner's, the code while linking (in a private chat: «/start 123456» from the link, or
    /// the code alone), and the rest sorted by <see cref="TelegramInbox"/>: dropped, someone new (told to the owner once),
    /// held by a limit («ahora no puedo», once a while) or served with its level.
    /// </summary>
    internal void Handle(TelegramIncoming m)
    {
        if (!m.IsButton && Chosen(m) is { } chosen)
        {
            WriteLog($"← {m.From.Shown} [{m.From.Id}]: «{Short(m.Text)}» como respuesta");
            m = chosen;
        }
        if (m.IsButton)
        {
            HandleButton(m);
            return;
        }
        WriteLog($"← {m.From.Shown} [{m.From.Id}]{(m.ChatKind == TelegramChatKind.Group ? $" en «{m.ChatTitle}»" : string.Empty)}: {Short(m.Text)}");
        if (m.ChatKind == TelegramChatKind.Private && TryLink(m)) return;
        if (m.ChatKind == TelegramChatKind.Group) SeeGroup(m);
        TelegramVerdict verdict;
        lock (_gate) verdict = TelegramInbox.Classify(m, Settings, MeId, MeUsername);
        if (verdict.Why is not ("channel" or "blocked" or "groupIgnored" or "ignored"))
            Keep(m.ChatId, m.From.Shown, m.Text.Length > 0 ? m.Text : $"[{m.Content}]", false);
        if (verdict.Route == TelegramRoute.Drop)
        {
            WriteLog($"  descartado ({verdict.Why})");
            return;
        }
        if (verdict.Route == TelegramRoute.Contact) Remember(m.From);
        if (verdict.Route == TelegramRoute.Stranger)
        {
            NewStranger(m);
            if (verdict.Level == TelegramLevel.Ignore)
            {
                WriteLog("  descartado (desconocido)");
                return;
            }
        }
        if (verdict.Route == TelegramRoute.Owner && m.ChatKind == TelegramChatKind.Private && m.Text.Trim().Equals("/start", StringComparison.OrdinalIgnoreCase))
        {
            Reply(m.ChatId, Loc.T("telegram.owner.hello", Environment.MachineName), "dueño");
            return;
        }
        if (verdict.Route != TelegramRoute.Owner && Held(m)) return;
        Received?.Invoke(m, verdict);
    }

    /// <summary>The limits of whoever is not the owner: only the owner while gaming, and so many messages a minute.</summary>
    private bool Held(TelegramIncoming m)
    {
        TelegramLimits limits;
        lock (_gate) limits = Settings.Limits;
        var now = Now();
        var why = limits.OnlyOwnerWhileGaming && Busy() ? "gaming" : !_limiter.AllowIn(m.From.Id, now, limits.PerMinute) ? "rate" : null;
        if (why is null) return false;
        WriteLog($"  en espera ({why})");
        if (_limiter.ShouldTell(m.From.Id, now, TimeSpan.FromMinutes(10)))
            Reply(m.ChatId, Loc.T(why == "gaming" ? "telegram.busy.gaming" : "telegram.busy.rate"), m.From.Shown);
        return true;
    }

    /// <summary>The code while linking; true when the message was about it.</summary>
    private bool TryLink(TelegramIncoming m)
    {
        var text = m.Text.Trim();
        var start = text.StartsWith("/start", StringComparison.OrdinalIgnoreCase);
        var said = start ? text[6..].Trim() : text;
        string? code;
        lock (_gate) code = _linkCode is not null && Now() < _linkExpires ? _linkCode : null;
        if (code is null || Owner?.Id == m.From.Id || !(start || (said.Length == 6 && said.All(char.IsAsciiDigit)))) return false;
        if (said == code)
        {
            var linked = new TelegramOwner { Id = m.From.Id, Name = m.From.Name, Username = m.From.Username, Linked = Now() };
            lock (_gate)
            {
                Settings.Owner = linked;
                Settings.Pending.RemoveAll(p => p.Id == linked.Id);
                Settings.Blocked.RemoveAll(p => p.Id == linked.Id);
                Settings.Contacts.RemoveAll(c => c.Id == linked.Id);
                _linkCode = null;
                SaveSettings();
            }
            WriteLog($"dueño vinculado: {linked.Shown} [{linked.Id}]");
            Log.Info($"Telegram: dueño vinculado ({linked.Id})");
            Reply(m.ChatId, Loc.T("telegram.linked.reply", Environment.MachineName), "dueño");
            Changed?.Invoke();
            OwnerLinked?.Invoke(linked);
            return true;
        }
        bool give;
        lock (_gate)
        {
            give = ++_linkWrong >= LinkTries;
            if (give) _linkCode = null;
        }
        WriteLog($"código incorrecto de {m.From.Id}");
        Reply(m.ChatId, Loc.T(give ? "telegram.link.gaveUp" : "telegram.link.wrong"), m.From.Shown);
        if (give) Changed?.Invoke();
        return true;
    }

    /// <summary>A group seen: kept (and its name refreshed) so it can be ignored from Options.</summary>
    private void SeeGroup(TelegramIncoming m)
    {
        var changed = false;
        lock (_gate)
        {
            var group = Settings.Groups.FirstOrDefault(g => g.Id == m.ChatId);
            if (group is null)
            {
                Settings.Groups.Add(new TelegramGroup { Id = m.ChatId, Title = m.ChatTitle ?? string.Empty, Seen = Now() });
                changed = true;
            }
            else if (m.ChatTitle is { Length: > 0 } title && group.Title != title)
            {
                group.Title = title;
                changed = true;
            }
            if (changed) SaveSettings();
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>A contact's name and @user follow what Telegram says (the id never changes), and when they last wrote.</summary>
    private void Remember(TelegramUser from)
    {
        bool renamed;
        lock (_gate)
        {
            if (Settings.Contacts.FirstOrDefault(c => c.Id == from.Id) is not { } contact) return;
            renamed = contact.Name != from.Name || contact.Username != from.Username;
            contact.Name = from.Name;
            contact.Username = from.Username;
            contact.LastWrote = Now();
            SaveSettings();
        }
        if (renamed) Changed?.Invoke();
    }

    /// <summary>Someone new: waits for the owner's decision; told once (PC and Telegram, with buttons).</summary>
    private void NewStranger(TelegramIncoming m)
    {
        TelegramPerson person;
        bool notify;
        lock (_gate)
        {
            if (Settings.Pending.Any(p => p.Id == m.From.Id)) return;
            person = new TelegramPerson { Id = m.From.Id, Name = m.From.Name, Username = m.From.Username, When = Now(), Text = Short(m.Text) };
            Settings.Pending.Add(person);
            notify = Settings.Others.Notify;
            SaveSettings();
        }
        WriteLog($"  nuevo: {person.Shown} espera tu decisión");
        Changed?.Invoke();
        if (!notify) return;
        StrangerArrived?.Invoke(person);
        if (Owner is not { } owner) return;
        var text = Loc.T("telegram.stranger.text", person.Shown) + (person.Text is { Length: > 0 } said ? $"\n\n«{said}»" : string.Empty);
        Reply(owner.Id, text, "dueño",
        [
            [new(Loc.T("telegram.stranger.allow"), $"p:allow:{person.Id}"), new(Loc.T("telegram.stranger.ignore"), $"p:ignore:{person.Id}"), new(Loc.T("telegram.stranger.block"), $"p:block:{person.Id}")],
        ]);
    }

    /// <summary>
    /// A button pressed: the owner's decisions about someone new and answers to a question (only the owner's count); any
    /// other (a command…) goes on as a message, with the level of whoever pressed it.
    /// </summary>
    private void HandleButton(TelegramIncoming m)
    {
        var data = m.CallbackData ?? string.Empty;
        if (data.StartsWith("y:", StringComparison.Ordinal) || data.StartsWith("n:", StringComparison.Ordinal))
        {
            AnswerQuestion(m, data);
            return;
        }
        if (!data.StartsWith("p:", StringComparison.Ordinal))
        {
            TelegramVerdict verdict;
            lock (_gate) verdict = TelegramInbox.Classify(m, Settings, MeId, MeUsername);
            WriteLog($"botón de {m.From.Shown} [{m.From.Id}]: {data} ({verdict.Why})");
            if (!verdict.Served || (verdict.Route != TelegramRoute.Owner && Held(m)))
            {
                Answer(m, Loc.T("telegram.button.notYours"));
                return;
            }
            Received?.Invoke(m, verdict);
            return;
        }
        var connection = _connection;
        string answer;
        if (Owner?.Id != m.From.Id) answer = Loc.T("telegram.button.notYours");
        else
        {
            var parts = (m.CallbackData ?? string.Empty).Split(':');
            answer = parts.Length == 3 && parts[0] == "p" && long.TryParse(parts[2], CultureInfo.InvariantCulture, out var id) && Decide(id, parts[1]) is { } done
                ? done
                : Loc.T("telegram.button.old");
            WriteLog($"botón del dueño: {m.CallbackData} → {answer}");
            if (connection is not null) _ = Quiet(() => connection.EditTextAsync(m.ChatId, m.MessageId, answer, _life.Token));
        }
        if (connection is not null && m.CallbackId is { } callback) _ = Quiet(() => connection.AnswerButtonAsync(callback, answer, _life.Token));
    }

    /// <summary>«Sí» or «No» of a question to the owner: the one waiting gets it and the message says what was answered.</summary>
    private void AnswerQuestion(TelegramIncoming m, string data)
    {
        if (Owner?.Id != m.From.Id)
        {
            Answer(m, Loc.T("telegram.button.notYours"));
            return;
        }
        var yes = data[0] == 'y';
        if (!_asks.TryRemove(data[2..], out var ask))
        {
            Answer(m, Loc.T("telegram.button.old"));
            Edit(m.ChatId, m.MessageId, Loc.T("telegram.button.old"));
            return;
        }
        WriteLog($"respuesta del dueño: {(yes ? "sí" : "no")} a «{Short(ask.Question)}»");
        ask.Answer.TrySetResult(yes);
        Answer(m, null);
        Edit(m.ChatId, m.MessageId, (yes ? "✓ " : "✗ ") + ask.Question);
    }

    private string? Decide(long id, string what)
    {
        if (Person(id) is not { } person) return null;
        switch (what)
        {
            case "allow":
                Allow(id, TelegramLevel.ChatOnly);
                return Loc.T("telegram.decided.allow", person.Shown);
            case "ignore":
                Allow(id, TelegramLevel.Ignore);
                return Loc.T("telegram.decided.ignore", person.Shown);
            case "block":
                Block(id);
                return Loc.T("telegram.decided.block", person.Shown);
            default:
                return null;
        }
    }

    private static async Task Quiet(Func<Task> call)
    {
        try { await call().ConfigureAwait(false); }
        catch (Exception ex) when (ex is TelegramException or HttpRequestException or IOException or OperationCanceledException or JsonException) { }
    }

    // ── contacts and decisions ───────────────────────────────────────────

    /// <summary>Someone known by its id: waiting, blocked or a contact.</summary>
    public TelegramPerson? Person(long id)
    {
        lock (_gate)
        {
            if (Settings.Pending.FirstOrDefault(p => p.Id == id) is { } pending) return pending;
            if (Settings.Blocked.FirstOrDefault(p => p.Id == id) is { } blocked) return blocked;
            return Settings.Contacts.FirstOrDefault(c => c.Id == id) is { } c ? new TelegramPerson { Id = c.Id, Name = c.Name, Username = c.Username, When = c.Added } : null;
        }
    }

    /// <summary>Makes <paramref name="id"/> a contact with <paramref name="level"/> (from waiting or blocked; a contact keeps its lists).</summary>
    public void Allow(long id, TelegramLevel level)
    {
        lock (_gate)
        {
            if (Settings.Owner?.Id == id) return;
            var person = Settings.Pending.Concat(Settings.Blocked).FirstOrDefault(p => p.Id == id);
            Settings.Pending.RemoveAll(p => p.Id == id);
            Settings.Blocked.RemoveAll(p => p.Id == id);
            var contact = Settings.Contacts.FirstOrDefault(c => c.Id == id);
            if (contact is null)
            {
                if (person is null) return;
                contact = new TelegramContact { Id = id, Name = person.Name, Username = person.Username, Added = Now(), LastWrote = person.When };
                Settings.Contacts.Add(contact);
            }
            contact.Level = level;
            SaveSettings();
        }
        WriteLog($"contacto {id}: {level}");
        Changed?.Invoke();
    }

    /// <summary>Blocks <paramref name="id"/>: its messages are dropped without telling, and nothing is sent to it.</summary>
    public void Block(long id)
    {
        lock (_gate)
        {
            if (Settings.Owner?.Id == id || Person(id) is not { } person || Settings.Blocked.Any(b => b.Id == id)) return;
            var was = Settings.Contacts.FirstOrDefault(c => c.Id == id);
            Settings.Pending.RemoveAll(p => p.Id == id);
            Settings.Contacts.RemoveAll(c => c.Id == id);
            Settings.Blocked.Add(new TelegramPerson { Id = id, Name = person.Name, Username = person.Username, When = Now(), Text = person.Text, Was = was });
            SaveSettings();
        }
        WriteLog($"bloqueado {id}");
        Changed?.Invoke();
    }

    /// <summary>Unblocks <paramref name="id"/>: a contact comes back as it was (level and lists); anybody else waits for a decision again.</summary>
    public void Unblock(long id)
    {
        lock (_gate)
        {
            if (Settings.Blocked.FirstOrDefault(b => b.Id == id) is not { } blocked) return;
            Settings.Blocked.Remove(blocked);
            if (blocked.Was is { } contact) Settings.Contacts.Add(contact);
            else Settings.Pending.Add(new TelegramPerson { Id = id, Name = blocked.Name, Username = blocked.Username, When = Now(), Text = blocked.Text });
            SaveSettings();
        }
        WriteLog($"desbloqueado {id}");
        Changed?.Invoke();
    }

    /// <summary>Forgets <paramref name="id"/> (a contact, someone waiting or blocked): next time it writes, it is someone new.</summary>
    public void Forget(long id)
    {
        lock (_gate)
        {
            Settings.Pending.RemoveAll(p => p.Id == id);
            Settings.Blocked.RemoveAll(p => p.Id == id);
            Settings.Contacts.RemoveAll(c => c.Id == id);
            SaveSettings();
        }
        Changed?.Invoke();
    }

    /// <summary>Reads the options while the connection may be changing them (a copy of the lists, a value…).</summary>
    public T Read<T>(Func<TelegramSettings, T> read)
    {
        lock (_gate) return read(Settings);
    }

    /// <summary>Changes the options (a contact's level or lists, a group, «todos los demás», the limits) and saves them.</summary>
    public void Update(Action<TelegramSettings> change)
    {
        lock (_gate)
        {
            change(Settings);
            SaveSettings();
        }
        Changed?.Invoke();
    }

    private void SetState(TelegramState state, string? problem)
    {
        if (State == state && Problem == problem) return;
        State = state;
        Problem = problem;
        Changed?.Invoke();
    }

    // ── files ────────────────────────────────────────────────────────────

    private TelegramSettings Load()
    {
        try { return CalendarJson.Load<TelegramSettings>(SettingsFile) ?? new TelegramSettings(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Telegram: no se han podido leer las opciones: {ex.Message}");
            return new TelegramSettings();
        }
    }

    public void SaveSettings()
    {
        try
        {
            lock (_gate) CalendarJson.Save(SettingsFile, Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Telegram: no se han podido guardar las opciones: {ex.Message}");
        }
    }

    private static string Short(string text)
    {
        var one = text.ReplaceLineEndings(" ");
        return one.Length > 200 ? one[..200] + "…" : one;
    }

    /// <summary>The record of what comes and goes (Telegram\messages.log; the previous one kept as messages.old.log).</summary>
    private void WriteLog(string line)
    {
        try
        {
            lock (_logGate)
            {
                Directory.CreateDirectory(_folder);
                var file = LogFile;
                if (File.Exists(file) && new FileInfo(file).Length > LogLimit) File.Move(file, Path.Combine(_folder, "messages.old.log"), overwrite: true);
                File.AppendAllText(file, $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture)}  {line}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Restart();
        _queue.Writer.TryComplete();
        _life.Cancel();
        try { _sender.Wait(TimeSpan.FromSeconds(2)); }
        catch (AggregateException) { }
        _life.Dispose();
    }
}

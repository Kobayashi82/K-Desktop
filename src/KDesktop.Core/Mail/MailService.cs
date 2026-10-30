using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text.Json;
using KDesktop.Core.Accounts;
using KDesktop.Core.Calendar;
using KDesktop.Core.Localization;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;

namespace KDesktop.Core.Mail;

/// <summary>A mail just arrived (mail, phase 2): its header, whether it is important and its account.</summary>
public sealed record MailArrived(MailHeader Mail, bool Important, MailAccountSettings Account);

/// <summary>What a move did, to undo it: the mail as it was (folder, flags) and where it is now.</summary>
public sealed record MailMoved(string Op, string NowId, string BeforeId, bool WasRead);

/// <summary>
/// The mail of K-Desktop (mail, phases 1–3): the accounts, a watcher per account (IMAP IDLE on the inbox; without IDLE,
/// a look every few minutes; it comes back after a sleep or without network), the headers of the last days in a cache
/// (the body and the attachments only when asked), the notices of what arrives and the events, and reading, looking
/// for, moving, leaving drafts and sending. It lives in K-Desktop's process: the watch does not fall with the assistant,
/// and the passwords and tokens stay in one place. Nothing is deleted for good: «delete» is the trash, and it can be undone.
/// </summary>
public sealed class MailService : IDisposable
{
    public static string DefaultFolder => Path.Combine(AppPaths.DataDir, "Mail");

    private static readonly TimeSpan Poll = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan IdleFor = TimeSpan.FromMinutes(9);

    private sealed class Cache
    {
        public List<MailHeader> Mails { get; set; } = [];
        public uint UidValidity { get; set; }
        public uint LastUid { get; set; }
    }

    private readonly string _folder;
    private readonly Func<string, string> _protect;
    private readonly Func<string, string?> _unprotect;
    private readonly AccountService? _accounts;
    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<string, Cache> _caches = new();
    private readonly ConcurrentDictionary<string, (CancellationTokenSource Stop, Task Loop)> _watchers = new();
    private readonly ConcurrentDictionary<string, string> _problems = new();
    private bool _disposed;

    public MailService(string folder, Func<string, string> protect, Func<string, string?> unprotect, AccountService? accounts)
    {
        _folder = folder;
        _protect = protect;
        _unprotect = unprotect;
        _accounts = accounts;
        Settings = Load();
    }

    public MailSettings Settings { get; }

    /// <summary>A mail arrived (a watcher's thread).</summary>
    public event Action<MailArrived>? Arrived;

    /// <summary>The list, the accounts or their state changed (any thread).</summary>
    public event Action? Changed;

    /// <summary>A mail was sent (any thread).</summary>
    public event Action? Sent;

    public IReadOnlyList<MailAccountSettings> Accounts
    {
        get { lock (_gate) return [.. Settings.Accounts]; }
    }

    public MailAccountSettings? Account(string id)
    {
        lock (_gate) return Settings.Accounts.FirstOrDefault(a => a.Id == id);
    }

    /// <summary>An account by what the user calls it («trabajo», «gmail») or its address; null: none or several.</summary>
    public MailAccountSettings? FindAccount(string? name)
    {
        var all = Accounts;
        if (string.IsNullOrWhiteSpace(name)) return all.FirstOrDefault(a => a.Use == MailUse.All && a.Enabled) ?? (all.Count > 0 ? all[0] : null);
        var wanted = MailRules.Fold(name);
        var found = all.Where(a => MailRules.Fold(a.Name) == wanted || MailRules.Fold(a.Address) == wanted).ToList();
        if (found.Count == 1) return found[0];
        found = all.Where(a => MailRules.Fold(a.Name + " " + a.Address).Contains(wanted, StringComparison.Ordinal)).ToList();
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>What is wrong with an account now (it could not connect…); null: fine.</summary>
    public string? Problem(string accountId) => _problems.GetValueOrDefault(accountId);

    // ── accounts ─────────────────────────────────────────────────────────

    /// <summary>Adds or replaces an account (with a new password, if given) and starts watching it.</summary>
    public void Save(MailAccountSettings account, string? password = null)
    {
        if (password is { Length: > 0 }) account.PasswordProtected = _protect(password);
        lock (_gate)
        {
            Settings.Accounts.RemoveAll(a => a.Id == account.Id);
            Settings.Accounts.Add(account);
            SaveSettings();
        }
        Restart(account.Id);
        Changed?.Invoke();
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            Settings.Accounts.RemoveAll(a => a.Id == id);
            SaveSettings();
        }
        StopWatching(id);
        _caches.TryRemove(id, out _);
        _problems.TryRemove(id, out _);
        try { File.Delete(CacheFile(id)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        Changed?.Invoke();
    }

    /// <summary>A connected account of Accounts\ is gone: the mail accounts that used it go too.</summary>
    public void ForgetAccount(string oauthAccountId)
    {
        foreach (var a in Accounts.Where(a => a.OAuthAccountId == oauthAccountId)) Remove(a.Id);
    }

    /// <summary>
    /// «Probar»: signs in to IMAP and SMTP and says what fails in words. <paramref name="password"/>: a new one typed (not
    /// saved yet); null: the one saved.
    /// </summary>
    public async Task<string?> TestAsync(MailAccountSettings account, string? password, CancellationToken cancel = default)
    {
        var probe = Copy(account);
        if (password is { Length: > 0 }) probe.PasswordProtected = _protect(password);
        await using var connection = new MailConnection(probe, _unprotect, _accounts);
        try
        {
            var imap = await connection.OpenImapAsync(cancel).ConfigureAwait(false);
            await imap.Inbox.OpenAsync(FolderAccess.ReadOnly, cancel).ConfigureAwait(false);
            await connection.TestSmtpAsync(cancel).ConfigureAwait(false);
            return null;
        }
        catch (MailException ex)
        {
            return ex.Message;
        }
        catch (Exception ex) when (ex is ImapCommandException or IOException or SocketException or AccountException)
        {
            return ex.Message;
        }
    }

    private static MailAccountSettings Copy(MailAccountSettings a) => JsonSerializer.Deserialize<MailAccountSettings>(JsonSerializer.Serialize(a, CalendarJson.Options), CalendarJson.Options)!;

    // ── watching ─────────────────────────────────────────────────────────

    public void Start()
    {
        foreach (var account in Accounts) Restart(account.Id);
    }

    private void Restart(string id)
    {
        StopWatching(id);
        if (_disposed || Account(id) is not { Enabled: true, Use: MailUse.All } account) return;
        var stop = new CancellationTokenSource();
        _watchers[id] = (stop, Task.Run(() => WatchAsync(account, stop.Token)));
    }

    private void StopWatching(string id)
    {
        if (!_watchers.TryRemove(id, out var w)) return;
        w.Stop.Cancel();
        _ = w.Loop.ContinueWith(_ => w.Stop.Dispose(), TaskScheduler.Default);
    }

    /// <summary>
    /// Watches an account's inbox: what arrives is told at once (IDLE) or within a few minutes (without IDLE). A dropped
    /// connection comes back with a growing wait (5 s → 5 min); the errors go to the log and the account's problem.
    /// </summary>
    private async Task WatchAsync(MailAccountSettings account, CancellationToken stop)
    {
        var wait = TimeSpan.FromSeconds(5);
        var first = true;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var connection = new MailConnection(account, _unprotect, _accounts);
                var imap = await connection.OpenImapAsync(stop).ConfigureAwait(false);
                var inbox = imap.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadOnly, stop).ConfigureAwait(false);
                await CatchUpAsync(account, inbox, announce: !first, stop).ConfigureAwait(false);
                first = false;
                SetProblem(account.Id, null);
                wait = TimeSpan.FromSeconds(5);
                while (!stop.IsCancellationRequested && imap.IsConnected)
                {
                    if (imap.Capabilities.HasFlag(ImapCapabilities.Idle))
                    {
                        using var done = new CancellationTokenSource(IdleFor);
                        void Arrive(object? sender, EventArgs e) => done.Cancel();
                        inbox.CountChanged += Arrive;
                        try { await imap.IdleAsync(done.Token, stop).ConfigureAwait(false); }
                        finally { inbox.CountChanged -= Arrive; }
                    }
                    else
                    {
                        await Task.Delay(Poll, stop).ConfigureAwait(false);
                        await imap.NoOpAsync(stop).ConfigureAwait(false);
                    }
                    await CatchUpAsync(account, inbox, announce: true, stop).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is MailException or AccountException or ImapCommandException or ImapProtocolException or IOException or SocketException or ServiceNotConnectedException or OperationCanceledException)
            {
                SetProblem(account.Id, ex.Message);
                Log.Warn($"Correo: {account.Address}: {ex.Message}; se vuelve a probar en {wait.TotalSeconds:0} s");
            }
            try { await Task.Delay(wait, stop).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            wait = TimeSpan.FromSeconds(Math.Min(300, wait.TotalSeconds * 2));
            // An account given a new token or password meanwhile: its newest settings.
            if (Account(account.Id) is { } fresh) account = fresh;
        }
    }

    private void SetProblem(string id, string? problem)
    {
        var before = _problems.GetValueOrDefault(id);
        if (problem is null) _problems.TryRemove(id, out _);
        else _problems[id] = problem;
        if (before != problem) Changed?.Invoke();
    }

    /// <summary>Brings the cache up to date: the new mails (told when <paramref name="announce"/>), flags of the last ones, and the old ones go.</summary>
    private async Task CatchUpAsync(MailAccountSettings account, IMailFolder inbox, bool announce, CancellationToken cancel)
    {
        var cache = CacheOf(account.Id);
        var since = DateTime.Now.AddDays(-Math.Max(1, Settings.KeepDays));
        IList<UniqueId> uids;
        var reset = cache.UidValidity != inbox.UidValidity;
        if (reset || cache.LastUid == 0)
        {
            uids = await inbox.SearchAsync(SearchQuery.DeliveredAfter(since), cancel).ConfigureAwait(false);
            announce = false;
        }
        else uids = await inbox.SearchAsync(SearchQuery.Uids(new UniqueIdRange(new UniqueId(inbox.UidValidity, cache.LastUid + 1), UniqueId.MaxValue)), cancel).ConfigureAwait(false);
        var fresh = uids.Count == 0 ? [] : await FetchHeadersAsync(account, inbox, uids.TakeLast(500).ToList(), cancel).ConfigureAwait(false);

        // The read / unread of the ones kept (they change in the phone or the client).
        var known = new Dictionary<uint, bool>();
        if (!reset && cache.Mails.Count > 0)
        {
            var keptUids = cache.Mails.Where(m => m.Folder == "INBOX").Select(m => new UniqueId(inbox.UidValidity, m.Uid)).ToList();
            foreach (var s in await inbox.FetchAsync(keptUids, MessageSummaryItems.UniqueId | MessageSummaryItems.Flags, cancel).ConfigureAwait(false))
                known[s.UniqueId.Id] = s.Flags?.HasFlag(MessageFlags.Seen) == true;
        }
        List<MailHeader> added;
        lock (_gate)
        {
            if (reset) cache.Mails.Clear();
            cache.UidValidity = inbox.UidValidity;
            if (!reset && known.Count > 0)
                cache.Mails = [.. cache.Mails.Where(m => m.Folder != "INBOX" || known.ContainsKey(m.Uid)).Select(m => m.Folder == "INBOX" ? m with { Read = known[m.Uid] } : m)];
            var ids = cache.Mails.Select(m => m.Id).ToHashSet();
            added = [.. fresh.Where(m => !ids.Contains(m.Id))];
            cache.Mails.AddRange(added);
            cache.Mails = [.. cache.Mails.Where(m => m.Date >= since).OrderByDescending(m => m.Date)];
            if (fresh.Count > 0) cache.LastUid = Math.Max(cache.LastUid, fresh.Max(m => m.Uid));
        }
        SaveCache(account.Id);
        if (added.Count > 0 || known.Count > 0) Changed?.Invoke();
        if (!announce) return;
        foreach (var mail in added.OrderBy(m => m.Date))
        {
            var important = MailRules.IsImportant(mail, Settings.Important);
            Log.Info($"Correo: nuevo de {mail.From.Address} en {account.Address}{(important ? " (importante)" : string.Empty)}");
            try { Arrived?.Invoke(new MailArrived(mail, important, account)); }
            catch (Exception ex) { Log.Error("Correo: error al avisar", ex); }
        }
    }

    private static async Task<List<MailHeader>> FetchHeadersAsync(MailAccountSettings account, IMailFolder folder, IList<UniqueId> uids, CancellationToken cancel)
    {
        var items = MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.BodyStructure
                    | MessageSummaryItems.InternalDate | MessageSummaryItems.PreviewText;
        var list = new List<MailHeader>();
        foreach (var s in await folder.FetchAsync(uids, items, cancel).ConfigureAwait(false)) list.Add(HeaderOf(account.Id, folder, s));
        return list;
    }

    private static MailPerson Person(InternetAddress? address) => address switch
    {
        MailboxAddress m => new MailPerson(m.Name ?? string.Empty, m.Address ?? string.Empty),
        { } other => new MailPerson(other.Name ?? string.Empty, other.ToString()),
        _ => new MailPerson(string.Empty, string.Empty),
    };

    private static MailHeader HeaderOf(string accountId, IMailFolder folder, IMessageSummary s)
    {
        var attachments = (s.Attachments ?? []).Select((a, i) => new MailAttachment(MailText.SafeFileName(a.FileName ?? a.ContentType?.Name ?? "adjunto"), a.Octets, a.ContentType?.MimeType ?? "application/octet-stream", i)).ToList();
        var envelope = s.Envelope;
        return new MailHeader
        {
            Id = MailHeader.MakeId(accountId, folder.FullName, folder.UidValidity, s.UniqueId.Id),
            AccountId = accountId,
            Folder = folder.FullName,
            Uid = s.UniqueId.Id,
            UidValidity = folder.UidValidity,
            MessageId = envelope?.MessageId,
            From = Person(envelope?.From?.FirstOrDefault()),
            To = [.. (envelope?.To ?? []).Select(Person)],
            Cc = [.. (envelope?.Cc ?? []).Select(Person)],
            Subject = envelope?.Subject ?? string.Empty,
            Date = envelope?.Date ?? s.InternalDate ?? DateTimeOffset.Now,
            Snippet = MailText.Snippet(s.PreviewText ?? string.Empty),
            Read = s.Flags?.HasFlag(MessageFlags.Seen) == true,
            Flagged = s.Flags?.HasFlag(MessageFlags.Flagged) == true,
            Attachments = attachments,
        };
    }

    // ── reading ──────────────────────────────────────────────────────────

    /// <summary>
    /// The mails kept of the last days, newest first: of an account or all, unread only, important only, with words, from
    /// someone, in a span. <paramref name="server"/>: when the cache has none, ask the server too (words anywhere in the inbox).
    /// </summary>
    public async Task<IReadOnlyList<MailHeader>> ListAsync(string? accountId = null, bool unread = false, bool important = false, string? text = null,
        string? from = null, DateTimeOffset? since = null, DateTimeOffset? until = null, int max = 20, bool server = true, CancellationToken cancel = default)
    {
        var accounts = Accounts.Where(a => a.Use == MailUse.All && a.Enabled && (accountId is null || a.Id == accountId)).ToList();
        var all = new List<MailHeader>();
        foreach (var a in accounts) lock (_gate) all.AddRange(CacheOf(a.Id).Mails);
        IEnumerable<MailHeader> found = all;
        if (unread) found = found.Where(m => !m.Read);
        if (important) found = found.Where(m => MailRules.IsImportant(m, Settings.Important));
        if (from is { Length: > 0 }) found = found.Where(m => MailRules.Fold($"{m.From.Name} {m.From.Address}").Contains(MailRules.Fold(from), StringComparison.Ordinal));
        if (since is { } s) found = found.Where(m => m.Date >= s);
        if (until is { } u) found = found.Where(m => m.Date < u);
        found = found.Where(m => MailRules.Matches(m, text));
        var list = found.OrderByDescending(m => m.Date).Take(max).ToList();
        if (list.Count > 0 || !server || (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(from))) return list;

        // Older than the cache: the server looks.
        foreach (var a in accounts)
        {
            await using var connection = new MailConnection(a, _unprotect, _accounts);
            var imap = await connection.OpenImapAsync(cancel).ConfigureAwait(false);
            await imap.Inbox.OpenAsync(FolderAccess.ReadOnly, cancel).ConfigureAwait(false);
            SearchQuery query = SearchQuery.All;
            if (text is { Length: > 0 }) query = query.And(SearchQuery.SubjectContains(text).Or(SearchQuery.BodyContains(text)));
            if (from is { Length: > 0 }) query = query.And(SearchQuery.FromContains(from));
            if (since is { } s2) query = query.And(SearchQuery.DeliveredAfter(s2.LocalDateTime));
            if (until is { } u2) query = query.And(SearchQuery.DeliveredBefore(u2.LocalDateTime));
            var uids = await imap.Inbox.SearchAsync(query, cancel).ConfigureAwait(false);
            list.AddRange(await FetchHeadersAsync(a, imap.Inbox, uids.TakeLast(max).ToList(), cancel).ConfigureAwait(false));
        }
        return [.. list.OrderByDescending(m => m.Date).Take(max)];
    }

    public int Unread
    {
        get
        {
            var count = 0;
            foreach (var a in Accounts.Where(a => a.Use == MailUse.All && a.Enabled)) lock (_gate) count += CacheOf(a.Id).Mails.Count(m => !m.Read && m.Folder == "INBOX");
            return count;
        }
    }

    public MailHeader? Header(string id)
    {
        if (MailHeader.ParseId(id) is not { } parts) return null;
        lock (_gate) return CacheOf(parts.Account).Mails.FirstOrDefault(m => m.Id == id);
    }

    /// <summary>Opens the folder of a mail and gives its UID (null: that folder changed and the mail is not there any more).</summary>
    private static async Task<(IMailFolder Folder, UniqueId Uid)?> LocateAsync(ImapClient imap, string id, FolderAccess access, CancellationToken cancel)
    {
        if (MailHeader.ParseId(id) is not { } parts) return null;
        var folder = parts.Folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase) ? imap.Inbox : await imap.GetFolderAsync(parts.Folder, cancel).ConfigureAwait(false);
        await folder.OpenAsync(access, cancel).ConfigureAwait(false);
        if (folder.UidValidity != parts.Validity) return null;
        return (folder, new UniqueId(parts.Validity, parts.Uid));
    }

    /// <summary>Reads a mail whole (its text clean, without quotes; HTML without pictures). <paramref name="markRead"/>: the account's choice.</summary>
    public async Task<MailContent> ReadAsync(string id, bool? markRead = null, CancellationToken cancel = default)
    {
        var account = AccountOfId(id);
        await using var connection = new MailConnection(account, _unprotect, _accounts);
        var imap = await connection.OpenImapAsync(cancel).ConfigureAwait(false);
        var mark = markRead ?? account.MarkReadByAssistant;
        var located = await LocateAsync(imap, id, mark ? FolderAccess.ReadWrite : FolderAccess.ReadOnly, cancel).ConfigureAwait(false)
                      ?? throw new MailException(Loc.T("mail.error.gone"));
        var (folder, uid) = located;
        var message = await folder.GetMessageAsync(uid, cancel).ConfigureAwait(false);
        if (mark) await folder.AddFlagsAsync(uid, MessageFlags.Seen, true, cancel).ConfigureAwait(false);
        var summary = (await folder.FetchAsync([uid], MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.BodyStructure | MessageSummaryItems.InternalDate | MessageSummaryItems.PreviewText, cancel).ConfigureAwait(false)).FirstOrDefault();
        var header = summary is null ? Header(id) ?? new MailHeader { Id = id } : HeaderOf(account.Id, folder, summary);
        var text = MailText.ForAssistant(message.TextBody, message.HtmlBody);
        return new MailContent(header with { Read = header.Read || mark }, text, message.HtmlBody, [.. message.ReplyTo.Mailboxes.Select(m => new MailPerson(m.Name ?? string.Empty, m.Address))]);
    }

    /// <summary>
    /// Saves a mail's attachments in a folder (all, or those whose name fits <paramref name="filter"/>, «*.pdf»), never
    /// over another file («factura (2).pdf»). Returns their paths.
    /// </summary>
    public async Task<IReadOnlyList<string>> SaveAttachmentsAsync(string id, string folderPath, string? filter = null, CancellationToken cancel = default)
    {
        var account = AccountOfId(id);
        await using var connection = new MailConnection(account, _unprotect, _accounts);
        var imap = await connection.OpenImapAsync(cancel).ConfigureAwait(false);
        var (folder, uid) = await LocateAsync(imap, id, FolderAccess.ReadOnly, cancel).ConfigureAwait(false) ?? throw new MailException(Loc.T("mail.error.gone"));
        var message = await folder.GetMessageAsync(uid, cancel).ConfigureAwait(false);
        Directory.CreateDirectory(folderPath);
        var saved = new List<string>();
        foreach (var part in message.Attachments.OfType<MimePart>())
        {
            var name = MailText.SafeFileName(part.FileName ?? part.ContentType.Name ?? "adjunto");
            if (!string.IsNullOrWhiteSpace(filter) && filter.Trim() != "*" && !System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(filter.Trim(), name)) continue;
            var path = FreeName(folderPath, name);
            if (part.Content is null) continue;
            await using (var stream = File.Create(path)) await part.Content.DecodeToAsync(stream, cancel).ConfigureAwait(false);
            saved.Add(path);
        }
        Log.Info($"Correo: {saved.Count} adjuntos guardados en {folderPath}");
        return saved;
    }

    /// <summary>«factura.pdf», or «factura (2).pdf» when that one exists.</summary>
    public static string FreeName(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{stem} ({n}){ext}");
        return path;
    }

    private MailAccountSettings AccountOfId(string id) =>
        (MailHeader.ParseId(id) is { } parts ? Account(parts.Account) : null) ?? throw new MailException(Loc.T("mail.error.noAccount"));

    // ── moving ───────────────────────────────────────────────────────────

    /// <summary>
    /// <c>read</c>, <c>unread</c>, <c>archive</c>, <c>trash</c> (never deleted for good) or <c>inbox</c> (back). Returns how
    /// to undo it.
    /// </summary>
    public async Task<MailMoved> MoveAsync(string id, string op, CancellationToken cancel = default)
    {
        var account = AccountOfId(id);
        await using var connection = new MailConnection(account, _unprotect, _accounts);
        var imap = await connection.OpenImapAsync(cancel).ConfigureAwait(false);
        var (folder, uid) = await LocateAsync(imap, id, FolderAccess.ReadWrite, cancel).ConfigureAwait(false) ?? throw new MailException(Loc.T("mail.error.gone"));
        var flags = (await folder.FetchAsync([uid], MessageSummaryItems.Flags, cancel).ConfigureAwait(false)).FirstOrDefault()?.Flags ?? MessageFlags.None;
        var wasRead = flags.HasFlag(MessageFlags.Seen);
        string nowId = id;
        switch (op)
        {
            case "read":
                await folder.AddFlagsAsync(uid, MessageFlags.Seen, true, cancel).ConfigureAwait(false);
                break;
            case "unread":
                await folder.RemoveFlagsAsync(uid, MessageFlags.Seen, true, cancel).ConfigureAwait(false);
                break;
            case "archive" or "trash" or "inbox":
                var target = op == "inbox" ? imap.Inbox : await SpecialAsync(imap, op == "trash" ? SpecialFolder.Trash : SpecialFolder.Archive, cancel).ConfigureAwait(false)
                             ?? throw new MailException(Loc.T(op == "trash" ? "mail.error.noTrash" : "mail.error.noArchive"));
                var moved = await folder.MoveToAsync(uid, target, cancel).ConfigureAwait(false);
                if (moved is { } newUid)
                {
                    await target.OpenAsync(FolderAccess.ReadOnly, cancel).ConfigureAwait(false);
                    nowId = MailHeader.MakeId(account.Id, target.FullName, target.UidValidity, newUid.Id);
                }
                else nowId = string.Empty;
                break;
            default:
                throw new MailException(Loc.T("mail.error.op", op));
        }
        UpdateCache(account.Id, id, op);
        return new MailMoved(op, nowId, id, wasRead);
    }

    /// <summary>Puts a mail back as it was (an undo of <see cref="MoveAsync"/>).</summary>
    public async Task<bool> UndoMoveAsync(MailMoved moved, CancellationToken cancel = default)
    {
        switch (moved.Op)
        {
            case "read" or "unread":
                await MoveAsync(moved.BeforeId, moved.WasRead ? "read" : "unread", cancel).ConfigureAwait(false);
                return true;
            case "archive" or "trash" when moved.NowId.Length > 0:
                if (MailHeader.ParseId(moved.BeforeId)?.Folder is not { } before) return false;
                var account = AccountOfId(moved.NowId);
                await using (var connection = new MailConnection(account, _unprotect, _accounts))
                {
                    var imap = await connection.OpenImapAsync(cancel).ConfigureAwait(false);
                    var (folder, uid) = await LocateAsync(imap, moved.NowId, FolderAccess.ReadWrite, cancel).ConfigureAwait(false) ?? throw new MailException(Loc.T("mail.error.gone"));
                    var target = before.Equals("INBOX", StringComparison.OrdinalIgnoreCase) ? imap.Inbox : await imap.GetFolderAsync(before, cancel).ConfigureAwait(false);
                    await folder.MoveToAsync(uid, target, cancel).ConfigureAwait(false);
                }
                return true;
            default:
                return false;
        }
    }

    private static async Task<IMailFolder?> SpecialAsync(ImapClient imap, SpecialFolder which, CancellationToken cancel)
    {
        if ((imap.Capabilities & (ImapCapabilities.SpecialUse | ImapCapabilities.XList)) != 0 && imap.GetFolder(which) is { } special) return special;
        // Without SPECIAL-USE: by the usual names.
        string[] names = which switch
        {
            SpecialFolder.Trash => ["Trash", "Deleted Items", "Deleted Messages", "Papelera", "Elementos eliminados", "INBOX.Trash"],
            SpecialFolder.Archive => ["Archive", "Archivo", "Archived", "INBOX.Archive"],
            SpecialFolder.Drafts => ["Drafts", "Borradores", "INBOX.Drafts"],
            SpecialFolder.Sent => ["Sent", "Sent Items", "Sent Messages", "Enviados", "Elementos enviados", "INBOX.Sent"],
            _ => [],
        };
        if (imap.PersonalNamespaces.Count == 0 || imap.GetFolder(imap.PersonalNamespaces[0]) is not { } personal) return null;
        var all = await personal.GetSubfoldersAsync(true, cancel).ConfigureAwait(false);
        return all.FirstOrDefault(f => names.Contains(f.Name, StringComparer.OrdinalIgnoreCase) || names.Contains(f.FullName, StringComparer.OrdinalIgnoreCase));
    }

    private void UpdateCache(string accountId, string id, string op)
    {
        lock (_gate)
        {
            var cache = CacheOf(accountId);
            var index = cache.Mails.FindIndex(m => m.Id == id);
            if (index < 0) return;
            if (op is "read" or "unread") cache.Mails[index] = cache.Mails[index] with { Read = op == "read" };
            else cache.Mails.RemoveAt(index);
        }
        SaveCache(accountId);
        Changed?.Invoke();
    }

    // ── writing ──────────────────────────────────────────────────────────

    /// <summary>The message of a draft or a mail to send (a reply keeps the thread: In-Reply-To and References).</summary>
    public MimeMessage Compose(MailDraft draft)
    {
        var account = Account(draft.AccountId) ?? throw new MailException(Loc.T("mail.error.noAccount"));
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(account.DisplayName.Length > 0 ? account.DisplayName : account.Name, account.Address));
        foreach (var to in draft.To) message.To.Add(Parse(to));
        foreach (var cc in draft.Cc ?? []) message.Cc.Add(Parse(cc));
        message.Subject = draft.Subject;
        if (draft.InReplyTo is { Length: > 0 } reply)
        {
            message.InReplyTo = reply;
            message.References.Add(reply);
        }
        var builder = new BodyBuilder { TextBody = draft.Body };
        foreach (var file in draft.Attachments ?? [])
        {
            if (!File.Exists(file)) throw new MailException(Loc.T("mail.error.noFile", file));
            builder.Attachments.Add(file);
        }
        message.Body = builder.ToMessageBody();
        return message;
    }

    private static MailboxAddress Parse(string address)
    {
        if (MailboxAddress.TryParse(address.Trim(), out var parsed)) return parsed;
        throw new MailException(Loc.T("mail.error.badAddress", address));
    }

    /// <summary>Leaves a draft in the account's Drafts folder, to finish in the usual client. Returns its id (for an undo).</summary>
    public async Task<string> SaveDraftAsync(MailDraft draft, CancellationToken cancel = default)
    {
        var account = Account(draft.AccountId) ?? throw new MailException(Loc.T("mail.error.noAccount"));
        var message = Compose(draft);
        await using var connection = new MailConnection(account, _unprotect, _accounts);
        var imap = await connection.OpenImapAsync(cancel).ConfigureAwait(false);
        var drafts = await SpecialAsync(imap, SpecialFolder.Drafts, cancel).ConfigureAwait(false) ?? throw new MailException(Loc.T("mail.error.noDrafts"));
        await drafts.OpenAsync(FolderAccess.ReadWrite, cancel).ConfigureAwait(false);
        var uid = await drafts.AppendAsync(new AppendRequest(message, MessageFlags.Draft | MessageFlags.Seen), cancel).ConfigureAwait(false);
        Log.Info($"Correo: borrador en {account.Address} para {string.Join(", ", draft.To)}");
        return uid is { } u ? MailHeader.MakeId(account.Id, drafts.FullName, drafts.UidValidity, u.Id) : string.Empty;
    }

    /// <summary>Deletes a draft K-Desktop left (an undo): only from the Drafts folder.</summary>
    public async Task<bool> DeleteDraftAsync(string id, CancellationToken cancel = default)
    {
        if (id.Length == 0) return false;
        var account = AccountOfId(id);
        await using var connection = new MailConnection(account, _unprotect, _accounts);
        var imap = await connection.OpenImapAsync(cancel).ConfigureAwait(false);
        var drafts = await SpecialAsync(imap, SpecialFolder.Drafts, cancel).ConfigureAwait(false);
        if (drafts is null || MailHeader.ParseId(id)?.Folder != drafts.FullName) return false;
        var (folder, uid) = await LocateAsync(imap, id, FolderAccess.ReadWrite, cancel).ConfigureAwait(false) ?? (drafts, UniqueId.Invalid);
        if (!uid.IsValid) return false;
        await folder.AddFlagsAsync(uid, MessageFlags.Deleted, true, cancel).ConfigureAwait(false);
        await folder.ExpungeAsync([uid], cancel).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Sends a mail. With «solo enviar», only to the addresses allowed. A copy goes to Sent when the server does not keep
    /// one by itself (Gmail and Outlook do).
    /// </summary>
    public async Task SendAsync(MailDraft draft, CancellationToken cancel = default)
    {
        var account = Account(draft.AccountId) ?? throw new MailException(Loc.T("mail.error.noAccount"));
        if (account.Use == MailUse.SendOnly)
        {
            var allowed = account.AllowedRecipients.Count > 0 ? account.AllowedRecipients : [account.Address];
            var wrong = draft.To.Concat(draft.Cc ?? []).Where(a => !allowed.Any(x => a.Contains(x.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
            if (wrong.Count > 0) throw new MailException(Loc.T("mail.error.notAllowed", string.Join(", ", wrong)));
        }
        var message = Compose(draft);
        await using var connection = new MailConnection(account, _unprotect, _accounts);
        await connection.SendAsync(message, cancel).ConfigureAwait(false);
        Log.Info($"Correo: enviado desde {account.Address} a {string.Join(", ", draft.To)}");
        Sent?.Invoke();
        var keepsCopy = account.SmtpHost.Contains("gmail", StringComparison.OrdinalIgnoreCase) || account.SmtpHost.Contains("office365", StringComparison.OrdinalIgnoreCase)
                        || account.SmtpHost.Contains("outlook", StringComparison.OrdinalIgnoreCase);
        if (keepsCopy || account.Use == MailUse.SendOnly) return;
        try
        {
            var imap = await connection.OpenImapAsync(cancel).ConfigureAwait(false);
            if (await SpecialAsync(imap, SpecialFolder.Sent, cancel).ConfigureAwait(false) is { } sent)
            {
                await sent.OpenAsync(FolderAccess.ReadWrite, cancel).ConfigureAwait(false);
                await sent.AppendAsync(new AppendRequest(message, MessageFlags.Seen), cancel).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is MailException or ImapCommandException or IOException)
        {
            Log.Warn($"Correo: enviado, pero no se ha podido guardar en Enviados: {ex.Message}");
        }
    }

    /// <summary>Whether the user has ever written to (or received from) this address: a new one is said before sending.</summary>
    public bool Knows(string address)
    {
        var a = address.Trim().ToLowerInvariant();
        foreach (var account in Accounts)
            lock (_gate)
                if (CacheOf(account.Id).Mails.Any(m => m.From.Address.Equals(a, StringComparison.OrdinalIgnoreCase)
                                                      || m.To.Any(t => t.Address.Equals(a, StringComparison.OrdinalIgnoreCase)))) return true;
        return false;
    }

    // ── files ────────────────────────────────────────────────────────────

    private string SettingsFile => Path.Combine(_folder, "settings.json");

    private string CacheFile(string id) => Path.Combine(_folder, "Cache", id + ".json");

    private MailSettings Load()
    {
        try { return CalendarJson.Load<MailSettings>(SettingsFile) ?? new MailSettings(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Correo: no se han podido leer las opciones: {ex.Message}");
            return new MailSettings();
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
            Log.Warn($"Correo: no se han podido guardar las opciones: {ex.Message}");
        }
    }

    private Cache CacheOf(string id) => _caches.GetOrAdd(id, key =>
    {
        try { return CalendarJson.Load<Cache>(CacheFile(key)) ?? new Cache(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return new Cache(); }
    });

    private void SaveCache(string id)
    {
        try
        {
            lock (_gate) CalendarJson.Save(CacheFile(id), CacheOf(id));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Correo: no se ha podido guardar la copia: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var id in _watchers.Keys.ToList()) StopWatching(id);
    }
}

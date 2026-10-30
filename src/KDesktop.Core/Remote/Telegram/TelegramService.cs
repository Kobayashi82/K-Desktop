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

    private sealed record Outgoing(long Chat, string Text, string To, TaskCompletionSource<bool>? Done, IReadOnlyList<IReadOnlyList<TelegramButton>>? Buttons = null);

    private readonly string _folder;
    private readonly Func<string, string> _protect;
    private readonly Func<string, string?> _unprotect;
    private readonly Func<string, ITelegramConnection> _connect;
    private readonly Lock _gate = new();
    private readonly Lock _logGate = new();
    private readonly Channel<Outgoing> _queue = Channel.CreateUnbounded<Outgoing>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _life = new();
    private readonly TelegramLimiter _limiter = new();
    private readonly Task _sender;
    private (CancellationTokenSource Stop, Task Loop)? _run;
    private volatile ITelegramConnection? _connection;
    private string? _linkCode;
    private DateTimeOffset _linkExpires;
    private int _linkWrong;
    private bool _disposed;

    /// <param name="connect">Makes the connection for a token (the tests give a fake one).</param>
    public TelegramService(string folder, Func<string, string> protect, Func<string, string?> unprotect, Func<string, ITelegramConnection>? connect = null)
    {
        _folder = folder;
        _protect = protect;
        _unprotect = unprotect;
        _connect = connect ?? (token => new TelegramBotConnection(token));
        Settings = Load();
        _sender = Task.Run(SendLoopAsync);
    }

    public TelegramSettings Settings { get; }

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
            if (Settings.Bot is not { } bot || bot.Enabled == enabled) return;
            bot.Enabled = enabled;
            SaveSettings();
        }
        Restart();
    }

    /// <summary>The address of the bot in Telegram («https://t.me/kdesktop_bot»); null without a bot.</summary>
    public string? BotUrl => Bot is { Username.Length: > 0 } b ? "https://t.me/" + b.Username : null;

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
    public string? LinkUrl => LinkCode is { } code && BotUrl is { } url ? $"{url}?start={code}" : null;

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

    private bool Enqueue(string text, TaskCompletionSource<bool>? done)
    {
        if (Bot is not { Enabled: true } || Owner is not { } owner || string.IsNullOrWhiteSpace(text)) return false;
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
                var parts = Split(item.Text);
                for (var i = 0; i < parts.Count; i++)
                {
                    if (!await SendPartAsync(item, parts[i], i == parts.Count - 1 ? item.Buttons : null, stop).ConfigureAwait(false))
                    {
                        ok = false;
                        break;
                    }
                }
                item.Done?.TrySetResult(ok);
            }
        }
        catch (OperationCanceledException) { }
        while (_queue.Reader.TryRead(out var left)) left.Done?.TrySetResult(false);
    }

    private async Task<bool> SendPartAsync(Outgoing item, string text, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons, CancellationToken stop)
    {
        var waited = TimeSpan.Zero;
        var wait = RetryBase;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var connection = _connection;
            if (connection is null)
            {
                if (Bot is not { Enabled: true } || waited > TimeSpan.FromMinutes(2)) break;
                await Task.Delay(TimeSpan.FromSeconds(1), stop).ConfigureAwait(false);
                waited += TimeSpan.FromSeconds(1);
                attempt--;
                continue;
            }
            try
            {
                await connection.SendTextAsync(item.Chat, text, buttons, stop).ConfigureAwait(false);
                WriteLog($"→ {item.To}: {Short(text)}");
                return true;
            }
            catch (TelegramException ex) when (ex.RetryAfter is { } after)
            {
                WriteLog($"Telegram pide esperar {after.TotalSeconds:0} s");
                await Task.Delay(after, stop).ConfigureAwait(false);
            }
            catch (TelegramException ex)
            {
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

    // ── the connection ───────────────────────────────────────────────────

    public void Start() => Restart();

    private void Restart()
    {
        if (_run is { } old)
        {
            old.Stop.Cancel();
            _ = old.Loop.ContinueWith(_ => old.Stop.Dispose(), TaskScheduler.Default);
            _run = null;
        }
        _connection = null;
        if (_disposed) return;
        if (Bot is not { Enabled: true })
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
            if (Bot is not { Enabled: true } bot) return;
            if (_unprotect(bot.TokenProtected) is not { } token)
            {
                SetState(TelegramState.Problem, Loc.T("telegram.error.token"));
                return;
            }
            SetState(TelegramState.Connecting, Problem);
            var connection = _connect(token);
            try
            {
                var me = await connection.GetMeAsync(stop).ConfigureAwait(false);
                Refresh(me);
                _connection = connection;
                SetState(TelegramState.Connected, null);
                WriteLog($"conectado como @{me.Username}");
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

    /// <summary>The bot changed its name or @user in @BotFather: kept (the id does not change).</summary>
    private void Refresh(TelegramUser me)
    {
        lock (_gate)
        {
            if (Settings.Bot is not { } bot || (bot.Name == me.Name && bot.Username == (me.Username ?? string.Empty))) return;
            bot.Name = me.Name;
            bot.Username = me.Username ?? string.Empty;
            SaveSettings();
        }
    }

    /// <summary>
    /// What arrived: a button of the owner's, the code while linking (in a private chat: «/start 123456» from the link, or
    /// the code alone), and the rest sorted by <see cref="TelegramInbox"/>: dropped, someone new (told to the owner once),
    /// held by a limit («ahora no puedo», once a while) or served with its level.
    /// </summary>
    internal void Handle(TelegramIncoming m)
    {
        if (m.IsButton)
        {
            HandleButton(m);
            return;
        }
        WriteLog($"← {m.From.Shown} [{m.From.Id}]{(m.ChatKind == TelegramChatKind.Group ? $" en «{m.ChatTitle}»" : string.Empty)}: {Short(m.Text)}");
        if (m.ChatKind == TelegramChatKind.Private && TryLink(m)) return;
        if (m.ChatKind == TelegramChatKind.Group) SeeGroup(m);
        var bot = Bot;
        TelegramVerdict verdict;
        lock (_gate) verdict = TelegramInbox.Classify(m, Settings, bot?.Id ?? 0, bot?.Username ?? string.Empty);
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

    /// <summary>A button pressed: only the owner's count («Permitir», «Ignorar», «Bloquear» of someone new).</summary>
    private void HandleButton(TelegramIncoming m)
    {
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
            if (Settings.Owner?.Id == id || Person(id) is not { } person) return;
            Settings.Pending.RemoveAll(p => p.Id == id);
            Settings.Contacts.RemoveAll(c => c.Id == id);
            if (Settings.Blocked.All(b => b.Id != id)) Settings.Blocked.Add(new TelegramPerson { Id = id, Name = person.Name, Username = person.Username, When = Now() });
            SaveSettings();
        }
        WriteLog($"bloqueado {id}");
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

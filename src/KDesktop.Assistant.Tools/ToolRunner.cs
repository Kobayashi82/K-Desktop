using System.Text.Json;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Assistant.Sharing;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools;

/// <summary>How a tool call went.</summary>
/// <param name="Result">Its result (null when it was not run: not allowed, or no such tool).</param>
public sealed record ToolOutcome(ToolResult? Result, ActivityEntry Entry)
{
    public bool Ran => Result is not null;
}

/// <summary>A tool that was run (<see cref="ToolRunner.Ran"/>): who for, which, how it got there and whether it failed.</summary>
public sealed record ToolRan(string? Assistant, string Tool, bool ReadsOnly, string Route, bool Failed);

/// <summary>Where a tool call is (<see cref="ToolRunner.Using"/>, stage 13.4).</summary>
public enum ToolPhase
{
    /// <summary>Waiting for the user to allow it.</summary>
    Asking,
    Running,
    /// <summary>Over (run, refused or unanswered).</summary>
    Over,
}

/// <summary>A tool call that asks, starts or is over: what the assistant is busy with meanwhile.</summary>
public sealed record ToolUse(string? Assistant, string Tool, AssistantActivity Activity, ToolPhase Phase);

/// <summary>
/// Runs the tools (stage 3.3, plan §8): asks when its risk says so (🟡 the first time, unless trusted; 🔴 every time; no
/// answer in 10 s is a no), writes each call in the activity, and keeps what can be undone (also across restarts) until
/// it is undone or forgotten. One per process; thread-safe.
/// </summary>
public sealed class ToolRunner
{
    /// <summary>How long a question waits for an answer.</summary>
    public static readonly TimeSpan AskTime = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The route of «Usar una herramienta» (G1): the user's own command, so it counts as allowed; only a script not approved
    /// yet (new or changed) and what the user set to «Preguntar siempre» ask.
    /// </summary>
    public const string ActionRoute = "action";

    /// <summary>The same, run by itself (an event): nobody is asked, and what would ask is not done.</summary>
    public const string UnattendedActionRoute = "actionUnattended";

    /// <summary>Undoable lines kept (the oldest are forgotten).</summary>
    private const int KeptUndos = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly ToolRegistry _registry;
    private readonly Func<IReadOnlySet<string>> _trusted;
    private readonly Func<string> _askBefore;
    private readonly Func<AskMessage, CancellationToken, Task<string>> _ask;
    private readonly Action<LogMessage> _log;
    private readonly string _undoFile;
    private readonly string? _approvedFile;
    private List<StoredUndo>? _undos;
    private HashSet<string>? _approved;

    /// <param name="trusted">The tools the user said not to ask about again.</param>
    /// <param name="ask">Asks the user (K-Desktop) and gives the choice: <c>yes</c>, <c>no</c>, <c>always</c> or <c>timeout</c>.</param>
    /// <param name="log">Sends a line of the activity.</param>
    /// <param name="undoFile">Where what can be undone is kept.</param>
    /// <param name="askBefore">The user's <see cref="AskPolicy"/> (null: <see cref="AskPolicy.First"/>).</param>
    /// <param name="approvedFile">Where the approved <see cref="ITool.Fingerprint"/>s are kept (null: nowhere, they always ask).</param>
    public ToolRunner(ToolRegistry registry, Func<IReadOnlySet<string>> trusted, Func<AskMessage, CancellationToken, Task<string>> ask,
        Action<LogMessage> log, string undoFile, Func<string>? askBefore = null, string? approvedFile = null)
    {
        _registry = registry;
        _trusted = trusted;
        _askBefore = askBefore ?? (() => AskPolicy.First);
        _ask = ask;
        _log = log;
        _undoFile = undoFile;
        _approvedFile = approvedFile;
    }

    public ToolRegistry Registry => _registry;

    /// <summary>
    /// A tool ran (not when it was not allowed or does not exist), on the thread that ran it: the character reacts to how
    /// the order went (stage 13.3).
    /// </summary>
    public event Action<ToolRan>? Ran;

    /// <summary>
    /// A tool call asks the user, starts or is over (stage 13.4), on the thread that runs it: the character shows what it is
    /// busy with. Every call that asks or starts ends with <see cref="ToolPhase.Over"/>.
    /// </summary>
    public event Action<ToolUse>? Using;

    /// <summary>
    /// A tool asks its character for a gesture (stage 13.5, <see cref="ToolContext.Show"/>), on the thread that runs it:
    /// whoever listens must not hold it up.
    /// </summary>
    public event Action<ToolShown>? Showing;

    private void Show(ToolContext context, ITool tool, ToolGesture gesture)
    {
        try { Showing?.Invoke(new ToolShown(context.Assistant, tool.Name, gesture)); }
        catch (Exception ex) { Log.Warn($"Asistente: no se ha podido enseñar el gesto de {tool.Name}: {ex.Message}"); }
    }

    private void Use(ToolContext context, ITool tool, ToolPhase phase)
    {
        try { Using?.Invoke(new ToolUse(context.Assistant, tool.Name, ToolActivities.Of(tool), phase)); }
        catch (Exception ex) { Log.Warn($"Asistente: no se ha podido avisar de {tool.Name}: {ex.Message}"); }
    }

    /// <summary>Runs <paramref name="name"/> for <paramref name="heard"/> (what was asked), asking first when it must.</summary>
    /// <param name="route">How it got here: <c>rule</c>, <c>ai</c>…</param>
    public async Task<ToolOutcome> RunAsync(string name, JsonElement arguments, ToolContext context, string heard, string route, CancellationToken cancellationToken)
    {
        if (_registry.Find(name) is not { } tool)
            return new ToolOutcome(null, Record(context.Assistant, heard, route,
                Loc.T(_registry.FindAny(name) is null ? "tools.unknown" : "tools.off", name), ok: false));

        string intent;
        try { intent = tool.Intent(arguments); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { intent = name; }

        string? fingerprint;
        try { fingerprint = tool.Fingerprint; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido leer lo que ejecutaría {name}: {ex.Message}");
            return new ToolOutcome(ToolResult.Fail($"Cannot read what {name} would run."), Record(context.Assistant, heard, route, Loc.T("tools.failed", intent), ok: false));
        }
        var unseen = fingerprint is not null && !IsApproved(fingerprint);
        var shared = route == McpShare.Route;
        try
        {
            return await RunCoreAsync(tool, intent, fingerprint, unseen, shared, arguments, context, heard, route, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Use(context, tool, ToolPhase.Over);
        }
    }

    private async Task<ToolOutcome> RunCoreAsync(ITool tool, string intent, string? fingerprint, bool unseen, bool shared, JsonElement arguments, ToolContext context,
        string heard, string route, CancellationToken cancellationToken)
    {
        var name = tool.Name;
        var risk = _registry.RiskOf(tool, arguments);
        if (route is ActionRoute or UnattendedActionRoute)
        {
            if (unseen || _registry.ChosenRisk(tool) == ToolRisk.AskAlways)
            {
                if (route == UnattendedActionRoute)
                {
                    Log.Info($"Asistente: {name} desde un evento tendría que preguntar: no se ejecuta");
                    return new ToolOutcome(null, Record(context.Assistant, heard, ActionRoute, Loc.T("tools.notAskedAlone", intent), ok: false));
                }
                Use(context, tool, ToolPhase.Asking);
                var choice = await AskAsync(tool, ToolRisk.AskAlways, Loc.T(unseen ? "tools.askNew" : "tools.ask", intent), fingerprint is not null, context, cancellationToken).ConfigureAwait(false);
                if (choice is not ("yes" or "always"))
                    return new ToolOutcome(null, Record(context.Assistant, heard, route, Loc.T(choice == "no" ? "tools.denied" : "tools.unanswered", intent), ok: false));
                if (choice == "always" && fingerprint is not null) Approve(fingerprint);
            }
            route = ActionRoute;
        }
        else if (!shared && ScheduledOrder.Current is { } scheduled)
        {
            // An order programmed earlier, now due: nobody is asked. What was approved then runs, and what only reads or
            // is safe; whatever else would ask is not done (the answer says so).
            if (!scheduled.Allowed.Contains(name) && !tool.ReadsOnly && (unseen || tool.AlwaysAsks || risk != ToolRisk.Safe))
            {
                Log.Info($"Asistente: {name} no se aprobó al programar la orden: no se ejecuta");
                return new ToolOutcome(null, Record(context.Assistant, heard, route, Loc.T("tools.notApproved", intent), ok: false));
            }
        }
        else if (unseen || MustAsk(tool, risk, shared, context.SharedFree))
        {
            Use(context, tool, ToolPhase.Asking);
            var text = shared ? Loc.T("tools.askShared", context.Caller ?? "?", intent) : Loc.T(unseen ? "tools.askNew" : "tools.ask", intent);
            var choice = await AskAsync(tool, risk, text, fingerprint is not null || (shared && risk != ToolRisk.AskAlways), context, cancellationToken).ConfigureAwait(false);
            if (choice is not ("yes" or "always"))
            {
                var why = Loc.T(choice == "no" ? "tools.denied" : "tools.unanswered", intent);
                return new ToolOutcome(null, Record(context.Assistant, heard, route, why, ok: false));
            }
            if (choice == "always" && fingerprint is not null) Approve(fingerprint);
        }

        Use(context, tool, ToolPhase.Running);
        var running = context with { Show = gesture => Show(context, tool, gesture) };
        ToolResult result;
        try
        {
            result = await tool.RunAsync(arguments, running, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Asistente: la herramienta {name} ha fallado: {ex.Message}");
            result = ToolResult.Fail(Loc.T("tools.failed", intent));
        }

        string? undoId = null;
        var did = result.Done ?? Capitalize(intent);
        if (result.Undo is { } undo && !result.Failed) undoId = Keep(undo, did, context.Assistant);
        try { Ran?.Invoke(new ToolRan(context.Assistant, name, tool.ReadsOnly, route, result.Failed)); }
        catch (Exception ex) { Log.Warn($"Asistente: no se ha podido avisar de {name}: {ex.Message}"); }
        return new ToolOutcome(result, Record(context.Assistant, heard, route, did, !result.Failed, undoId));
    }

    /// <summary>Undoes a line of the activity (Panel → Assistant → Activity). False when it cannot (already undone, gone…).</summary>
    public async Task<bool> UndoAsync(string undoId, CancellationToken cancellationToken)
    {
        StoredUndo? stored;
        lock (_gate)
        {
            stored = Undos().FirstOrDefault(u => u.Id == undoId);
            if (stored is not null) Undos().Remove(stored);
        }
        if (stored is null || _registry.Find(stored.Tool) is not { } tool)
        {
            Record(stored?.Assistant, string.Empty, "undo", Loc.T("tools.undoGone"), ok: false, undid: undoId);
            return false;
        }

        bool ok;
        try { ok = await tool.UndoAsync(new ToolUndo(stored.Tool, stored.Data), cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Asistente: no se ha podido deshacer ({stored.Tool}): {ex.Message}");
            ok = false;
        }
        Save();
        Record(stored.Assistant, string.Empty, "undo", Loc.T(ok ? "tools.undone" : "tools.undoFailed", stored.Did), ok, undid: undoId);
        return ok;
    }

    /// <summary>
    /// «Deshaz»: undoes the newest thing <paramref name="assistant"/> did that can be undone, if it was less than
    /// <paramref name="within"/> ago. Null when there is nothing; otherwise whether it worked and what it was (for the activity).
    /// </summary>
    public async Task<(bool Ok, string Did)?> UndoLastAsync(string? assistant, TimeSpan within, CancellationToken cancellationToken)
    {
        StoredUndo? last;
        lock (_gate)
        {
            var since = DateTimeOffset.Now - within;
            last = Undos().LastOrDefault(u => u.Assistant == assistant && u.At >= since);
        }
        if (last is null) return null;
        return (await UndoAsync(last.Id, cancellationToken).ConfigureAwait(false), last.Did);
    }

    /// <summary>Writes a line of the activity that is not a tool call (a timer that rang…).</summary>
    public ActivityEntry Record(string? assistant, string heard, string route, string did, bool ok, string? undo = null, string? undid = null)
    {
        var entry = new ActivityEntry(NewId(), DateTimeOffset.Now, heard, route, did, ok, undo, undid);
        _log(new LogMessage(entry, assistant));
        return entry;
    }

    /// <summary>
    /// Whether to ask first. Another program (<paramref name="shared"/>): whatever does more than read asks, whatever the
    /// user's policy, until «yes, and don't ask again»; what always asks, always.
    /// </summary>
    private bool MustAsk(ITool tool, ToolRisk risk, bool shared, bool free) =>
        shared && free && !tool.AlwaysAsks ? risk == ToolRisk.AskAlways
        : tool.AlwaysAsks || shared
        ? risk == ToolRisk.AskAlways || (!tool.ReadsOnly && !_trusted().Contains(tool.Name))
        : AskPolicy.Asks(risk, _askBefore(), _trusted().Contains(tool.Name));

    private bool IsApproved(string fingerprint)
    {
        lock (_gate) return Approved().Contains(fingerprint);
    }

    private void Approve(string fingerprint)
    {
        if (_approvedFile is null) return;
        string json;
        lock (_gate)
        {
            if (!Approved().Add(fingerprint)) return;
            json = JsonSerializer.Serialize(Approved().Order(StringComparer.Ordinal), Json);
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_approvedFile)!);
            File.WriteAllText(_approvedFile, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido guardar {_approvedFile}: {ex.Message}");
        }
    }

    private HashSet<string> Approved()
    {
        if (_approved is not null) return _approved;
        _approved = new HashSet<string>(StringComparer.Ordinal);
        if (_approvedFile is null || !File.Exists(_approvedFile)) return _approved;
        try
        {
            foreach (var item in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_approvedFile), Json) ?? []) _approved.Add(item);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido leer {_approvedFile}: {ex.Message}");
        }
        return _approved;
    }

    private async Task<string> AskAsync(ITool tool, ToolRisk risk, string text, bool approvable, ToolContext context, CancellationToken cancellationToken)
    {
        var question = new AskMessage(NewId(), tool.Name, text, risk == ToolRisk.AskOnce || approvable, (int)AskTime.TotalSeconds, context.Assistant);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // A little more than the user's time: K-Desktop says «timeout» itself when it runs out.
        timeout.CancelAfter(AskTime + TimeSpan.FromSeconds(3));
        try
        {
            return await _ask(question, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "timeout";
        }
    }

    private string Keep(ToolUndo undo, string did, string? assistant)
    {
        var id = NewId();
        lock (_gate)
        {
            var undos = Undos();
            undos.Add(new StoredUndo(id, undo.Tool, undo.Data, did, assistant, DateTimeOffset.Now));
            if (undos.Count > KeptUndos) undos.RemoveRange(0, undos.Count - KeptUndos);
        }
        Save();
        return id;
    }

    private List<StoredUndo> Undos()
    {
        if (_undos is not null) return _undos;
        try
        {
            _undos = File.Exists(_undoFile) ? JsonSerializer.Deserialize<List<StoredUndo>>(File.ReadAllText(_undoFile), Json) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido leer {_undoFile}: {ex.Message}");
            _undos = [];
        }
        return _undos;
    }

    private void Save()
    {
        string json;
        lock (_gate) json = JsonSerializer.Serialize(Undos(), Json);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_undoFile)!);
            File.WriteAllText(_undoFile, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido guardar {_undoFile}: {ex.Message}");
        }
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    private static string Capitalize(string text) => text.Length > 0 ? char.ToUpper(text[0], Loc.Instance.Culture) + text[1..] : text;

    private sealed record StoredUndo(string Id, string Tool, string Data, string Did, string? Assistant, DateTimeOffset At);
}

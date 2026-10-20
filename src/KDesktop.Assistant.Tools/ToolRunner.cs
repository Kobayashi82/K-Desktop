using System.Text.Json;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools;

/// <summary>How a tool call went.</summary>
/// <param name="Result">Its result (null when it was not run: not allowed, or no such tool).</param>
public sealed record ToolOutcome(ToolResult? Result, ActivityEntry Entry)
{
    public bool Ran => Result is not null;
}

/// <summary>
/// Runs the tools (stage 3.3, plan §8): asks when its risk says so (🟡 the first time, unless trusted; 🔴 every time; no
/// answer in 10 s is a no), writes each call in the activity, and keeps what can be undone (also across restarts) until
/// it is undone or forgotten. One per process; thread-safe.
/// </summary>
public sealed class ToolRunner
{
    /// <summary>How long a question waits for an answer.</summary>
    public static readonly TimeSpan AskTime = TimeSpan.FromSeconds(10);

    /// <summary>Undoable lines kept (the oldest are forgotten).</summary>
    private const int KeptUndos = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly ToolRegistry _registry;
    private readonly Func<IReadOnlySet<string>> _trusted;
    private readonly Func<AskMessage, CancellationToken, Task<string>> _ask;
    private readonly Action<LogMessage> _log;
    private readonly string _undoFile;
    private List<StoredUndo>? _undos;

    /// <param name="trusted">The tools the user said not to ask about again.</param>
    /// <param name="ask">Asks the user (K-Desktop) and gives the choice: <c>yes</c>, <c>no</c>, <c>always</c> or <c>timeout</c>.</param>
    /// <param name="log">Sends a line of the activity.</param>
    /// <param name="undoFile">Where what can be undone is kept.</param>
    public ToolRunner(ToolRegistry registry, Func<IReadOnlySet<string>> trusted, Func<AskMessage, CancellationToken, Task<string>> ask,
        Action<LogMessage> log, string undoFile)
    {
        _registry = registry;
        _trusted = trusted;
        _ask = ask;
        _log = log;
        _undoFile = undoFile;
    }

    public ToolRegistry Registry => _registry;

    /// <summary>Runs <paramref name="name"/> for <paramref name="heard"/> (what was asked), asking first when it must.</summary>
    /// <param name="route">How it got here: <c>rule</c>, <c>ai</c>…</param>
    public async Task<ToolOutcome> RunAsync(string name, JsonElement arguments, ToolContext context, string heard, string route, CancellationToken cancellationToken)
    {
        if (_registry.Find(name) is not { } tool)
            return new ToolOutcome(null, Record(context.Assistant, heard, route, Loc.T("tools.unknown", name), ok: false));

        string intent;
        try { intent = tool.Intent(arguments); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { intent = name; }

        if (MustAsk(tool))
        {
            var choice = await AskAsync(tool, intent, context, cancellationToken).ConfigureAwait(false);
            if (choice is not ("yes" or "always"))
            {
                var why = Loc.T(choice == "no" ? "tools.denied" : "tools.unanswered", intent);
                return new ToolOutcome(null, Record(context.Assistant, heard, route, why, ok: false));
            }
        }

        ToolResult result;
        try
        {
            result = await tool.RunAsync(arguments, context, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Asistente: la herramienta {name} ha fallado: {ex.Message}");
            result = ToolResult.Fail(Loc.T("tools.failed", intent));
        }

        string? undoId = null;
        var did = result.Done ?? Capitalize(intent);
        if (result.Undo is { } undo && !result.Failed) undoId = Keep(undo, did, context.Assistant);
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

    /// <summary>Writes a line of the activity that is not a tool call (a timer that rang…).</summary>
    public ActivityEntry Record(string? assistant, string heard, string route, string did, bool ok, string? undo = null, string? undid = null)
    {
        var entry = new ActivityEntry(NewId(), DateTimeOffset.Now, heard, route, did, ok, undo, undid);
        _log(new LogMessage(entry, assistant));
        return entry;
    }

    private bool MustAsk(ITool tool) => tool.Risk switch
    {
        ToolRisk.Safe => false,
        ToolRisk.AskOnce => !_trusted().Contains(tool.Name),
        _ => true,
    };

    private async Task<string> AskAsync(ITool tool, string intent, ToolContext context, CancellationToken cancellationToken)
    {
        var question = new AskMessage(NewId(), tool.Name, Loc.T("tools.ask", intent), tool.Risk == ToolRisk.AskOnce, (int)AskTime.TotalSeconds, context.Assistant);
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

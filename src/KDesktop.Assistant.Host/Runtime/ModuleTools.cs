using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Assistant.Tools.Declarative;
using KDesktop.Assistant.Tools.Mcp;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The tools of the process (stage 3.3), for all the assistants and the module without a body: the registry with the
/// built-in ones, the runner (permissions, activity, undo), the questions K-Desktop asks for it (<c>ask</c> →
/// <c>answer</c>), the tools the user trusts (<c>config</c> <c>tools</c>); <see cref="ModuleTalk"/> says the timers that ring. Also
/// what is remembered about the user (stage 3.7): Options → Memory asks (<c>memory</c>) and sees it (<c>memoryState</c>);
/// the notes, the same with Options → Notes (<c>notes</c>, <c>notesState</c>). And the tools added without compiling (stage
/// 9): the JSON ones, the MCP servers', the user's choices for each one (<c>config</c> <c>tools</c> <c>settings</c>) and
/// Options → Tools (<c>tools</c> → <c>toolsState</c>, <c>toolTest</c>).
/// </summary>
internal sealed class ModuleTools : IDisposable
{
    private readonly KDesktopLink _link;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _questions = new(StringComparer.Ordinal);
    private volatile IReadOnlySet<string> _trusted = new HashSet<string>(StringComparer.Ordinal);
    private volatile string _askBefore = AskPolicy.Risky;
    private readonly ModuleBrowser _browser;
    private readonly Timer _stateTimer;
    private ToolRegistry Registry => Runner.Registry;

    /// <param name="commands">The user's K-Desktop commands (stage 4.4).</param>
    /// <param name="screen">Pictures of the desktop and the page in the browser (stage 8.3).</param>
    /// <param name="vision">The model looking at them.</param>
    /// <param name="browser">The assistants' own browser (stage 8.4), closed with the tools.</param>
    /// <param name="writer">The model rewriting texts (transform_text).</param>
    public ModuleTools(KDesktopLink link, ICommandBridge commands, IScreen screen, IVision vision, IWriter writer, ModuleBrowser browser)
    {
        _browser = browser;
        _link = link;
        var folder = Path.Combine(AppPaths.DataDir, "Assistant");
        Builtin = new BuiltinTools(folder);
        var registry = new ToolRegistry();
        Builtin.AddTo(registry, screen, vision, browser);
        CommandTools.AddTo(registry, commands, writer);
        Secrets = new ToolSecrets(Path.Combine(folder, "secrets.json"));
        Json = new JsonTools(Path.Combine(folder, "Tools"), registry, Secrets, commands);
        Runner = new ToolRunner(registry, () => _trusted, AskAsync, message => link.Send(message), Path.Combine(folder, "undo.json"), () => _askBefore,
            Path.Combine(folder, "approved.json"));
        Json.Runner = Runner;
        Json.Broken += file => link.Send(new NotifyMessage(Loc.T("tools.json.broken", Path.GetFileName(file.File)), file.Errors[0]));
        Mcp = new McpServers(Path.Combine(folder, "mcp.json"), Path.Combine(folder, "mcp-cache.json"), AppPaths.LogDir, registry, Secrets);
        // Options → Tools sees every change, a moment later and once for many.
        _stateTimer = new Timer(_ => SendToolsState());
        registry.Changed += StateChanged;
        Mcp.Changed += StateChanged;
        Json.Loaded += StateChanged;
        Builtin.Memory.Changed += SendMemory;
        Builtin.Notes.Changed += SendNotes;
    }

    /// <summary>The assistants learn from what is talked about (<c>config</c> <c>tools</c> <c>learn</c>).</summary>
    public bool Learn { get; private set; } = true;

    public BuiltinTools Builtin { get; }

    /// <summary>The JSON tools of <c>Assistant\Tools</c> (stage 9.1).</summary>
    public JsonTools Json { get; }

    /// <summary>The secrets of the JSON tools and the MCP servers.</summary>
    public ToolSecrets Secrets { get; }

    public ToolRunner Runner { get; }

    /// <summary>The MCP servers of <c>Assistant\mcp.json</c> (stage 9.2).</summary>
    public McpServers Mcp { get; }

    /// <summary>Connected: the timers that came due while the process was off ring now; the watches kept start looking; the JSON tools are loaded and their folder watched.</summary>
    public void Start()
    {
        Builtin.Timers.Start();
        Builtin.Watches.Start();
        Json.Start();
        Mcp.Start();
    }

    /// <summary>The <c>tools</c> section of <c>config</c>: <c>trusted</c>, the tools that no longer ask, and <c>askBefore</c> (<see cref="AskPolicy"/>).</summary>
    public void Configure(JsonObject? tools)
    {
        var trusted = new HashSet<string>(StringComparer.Ordinal);
        if (tools?["trusted"] is JsonArray list)
            foreach (var item in list)
                if (item is JsonValue v && v.TryGetValue<string>(out var name)) trusted.Add(name);
        _trusted = trusted;
        _askBefore = tools?["askBefore"] is JsonValue ask && ask.TryGetValue<string>(out var policy) ? policy : AskPolicy.Risky;
        Learn = tools?["learn"] is not JsonValue learn || !learn.TryGetValue<bool>(out var on) || on;
        var preferences = new Dictionary<string, ToolPreference>(StringComparer.Ordinal);
        if (tools?["settings"] is JsonObject settings)
            foreach (var (name, value) in settings)
                if (value is JsonObject choice)
                    preferences[name] = new ToolPreference(choice["enabled"] is not JsonValue e || !e.TryGetValue<bool>(out var enabled) || enabled,
                        choice["risk"] is JsonValue r && r.TryGetValue<string>(out var risk) ? risk : null);
        Registry.SetPreferences(preferences);
    }

    /// <summary>Options → Tools: the list, testing a tool, starting or stopping a server, loading the files again.</summary>
    public async Task OnToolsAsync(ToolsMessage request)
    {
        switch (request.Op)
        {
            case "test" when request.Name is { } name:
                _link.Send(await TestAsync(name, request.Arguments).ConfigureAwait(false));
                break;
            case "start" when request.Name is { } server:
                var watch = Stopwatch.StartNew();
                var error = await Mcp.TestAsync(server, CancellationToken.None).ConfigureAwait(false);
                _link.Send(new ToolTestMessage(server, error is null,
                    error ?? Loc.T("tools.mcp.started", Mcp.ToolsOf(server).Count), (int)watch.ElapsedMilliseconds));
                break;
            case "stop" when request.Name is { } server:
                await Mcp.StopAsync(server).ConfigureAwait(false);
                break;
            case "reload":
                Json.Reload();
                Mcp.Reload();
                break;
        }
        SendToolsState();
    }

    /// <summary>Runs a tool as an assistant would (asking, in the activity), and what it gave back.</summary>
    private async Task<ToolTestMessage> TestAsync(string name, string? arguments)
    {
        JsonElement parsed;
        try { parsed = ToolArguments.Parse(string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments); }
        catch (JsonException ex) { return new ToolTestMessage(name, false, ex.Message, 0); }
        var watch = Stopwatch.StartNew();
        var outcome = await Runner.RunAsync(name, parsed, new ToolContext(null), Loc.T("tools.testHeard"), "test", CancellationToken.None).ConfigureAwait(false);
        var ok = outcome.Result is { Failed: false };
        var text = outcome.Result is { } result ? result.Reply is { } reply && reply != result.Text ? result.Text + "\n" + reply : result.Text : outcome.Entry.Did;
        return new ToolTestMessage(name, ok, text, (int)watch.ElapsedMilliseconds);
    }

    private void StateChanged() => _stateTimer.Change(TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan);

    /// <summary>All the tools, the JSON files and the MCP servers, for Options → Tools.</summary>
    public void SendToolsState()
    {
        var registry = Registry;
        var tools = registry.Everything.Select(t =>
        {
            var group = registry.GroupOf(t.Name) ?? ToolRegistry.Builtin;
            var source = t switch { JsonTool json => json.File, McpTool mcp => mcp.Server, _ => null };
            return new ToolInfo(t.Name, group, t.Description, ToolPreference.Name(registry.RiskOf(t)), ToolPreference.Name(t.Risk), t.ReadsOnly,
                registry.IsEnabled(t.Name), source, t.Examples.Count > 0 ? t.Examples[0] : null, t.ParametersSchemaJson);
        }).ToList();
        var files = Json.Files.Select(f => new ToolFileInfo(f.File, f.Enabled, f.Errors, f.Secrets ?? [], f.Name, f.Type)).ToList();
        var servers = Mcp.Servers.Select(s => new McpServerInfo(s.Id, s.Entry.Transport, s.Entry.Summary, s.Entry.Disabled,
            s.State.ToString().ToLowerInvariant(), Mcp.ToolsOf(s.Id).Count, s.Entry.SecretNames, s.Error, s.Since)).ToList();
        _link.Send(new ToolsStateMessage(tools, files, servers, Mcp.Errors));
    }

    public void OnAnswer(AnswerMessage answer)
    {
        if (_questions.TryRemove(answer.Id, out var waiting)) waiting.TrySetResult(answer.Choice);
    }

    public Task UndoAsync(UndoMessage undo) => Runner.UndoAsync(undo.Undo, CancellationToken.None);

    /// <summary>Options → Memory: list, add, edit, delete or clear (each change sends the whole memory back).</summary>
    public void OnMemory(MemoryMessage request)
    {
        var memory = Builtin.Memory;
        switch (request.Op)
        {
            case "add" when request.Text is { } text:
                memory.Remember(text, "fact", 0.7, "added");
                break;
            case "edit" when request is { Id: { } id, Text: { } text }:
                memory.Edit(id, text);
                break;
            case "delete" when request.Id is { } id:
                memory.Delete(id);
                break;
            case "clear":
                memory.Clear();
                Log.Info("Asistente: memoria borrada desde Opciones");
                break;
            default:
                SendMemory();
                break;
        }
    }

    private void SendMemory() => _link.Send(new MemoryStateMessage(Builtin.Memory.All));

    /// <summary>Options → Notes: list, add, edit, delete or clear (each change sends all the notes back).</summary>
    public void OnNotes(NotesMessage request)
    {
        var notes = Builtin.Notes;
        switch (request.Op)
        {
            case "add" when request.Text is { Length: > 0 } text:
                notes.Add(text, DateTimeOffset.Now);
                break;
            case "edit" when request is { Id: { } id, Text: { } text }:
                notes.Edit(id, text);
                break;
            case "delete" when request.Id is { } id:
                notes.Remove(id);
                break;
            case "clear":
                notes.Clear();
                Log.Info("Asistente: notas borradas desde Opciones");
                break;
            default:
                SendNotes();
                break;
        }
    }

    private void SendNotes() =>
        _link.Send(new NotesStateMessage([.. Builtin.Notes.All.OrderByDescending(n => n.At).Select(n => new NoteEntry(n.Id, n.Text, n.At))]));

    private async Task<string> AskAsync(AskMessage question, CancellationToken cancellationToken)
    {
        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _questions[question.Id] = answer;
        try
        {
            if (!_link.Send(question)) return "timeout";
            using (cancellationToken.Register(() => answer.TrySetCanceled(cancellationToken)))
                return await answer.Task.ConfigureAwait(false);
        }
        finally
        {
            _questions.TryRemove(question.Id, out _);
        }
    }

    public void Dispose()
    {
        Builtin.Memory.Changed -= SendMemory;
        Builtin.Notes.Changed -= SendNotes;
        Builtin.Dispose();
        Json.Dispose();
        Mcp.Dispose();
        _stateTimer.Dispose();
        _browser.Dispose();
        foreach (var waiting in _questions.Values) waiting.TrySetResult("timeout");
    }
}

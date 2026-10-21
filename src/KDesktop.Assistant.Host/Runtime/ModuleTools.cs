using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The tools of the process (stage 3.3), for all the assistants and the module without a body: the registry with the
/// built-in ones, the runner (permissions, activity, undo), the questions K-Desktop asks for it (<c>ask</c> →
/// <c>answer</c>), the tools the user trusts (<c>config</c> <c>tools</c>) and the timers ringing (<c>notify</c>). Also
/// what is remembered about the user (stage 3.7): Options → Memory asks (<c>memory</c>) and sees it (<c>memoryState</c>).
/// </summary>
internal sealed class ModuleTools : IDisposable
{
    private readonly KDesktopLink _link;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _questions = new(StringComparer.Ordinal);
    private volatile IReadOnlySet<string> _trusted = new HashSet<string>(StringComparer.Ordinal);
    private volatile string _askBefore = AskPolicy.Risky;

    /// <param name="commands">The user's K-Desktop commands (stage 4.4).</param>
    public ModuleTools(KDesktopLink link, ICommandBridge commands)
    {
        _link = link;
        var folder = Path.Combine(AppPaths.DataDir, "Assistant");
        Builtin = new BuiltinTools(folder);
        var registry = new ToolRegistry();
        Builtin.AddTo(registry);
        CommandTools.AddTo(registry, commands);
        Runner = new ToolRunner(registry, () => _trusted, AskAsync, message => link.Send(message), Path.Combine(folder, "undo.json"), () => _askBefore);
        Builtin.Timers.Rang += OnTimerRang;
        Builtin.Memory.Changed += SendMemory;
    }

    /// <summary>The assistants learn from what is talked about (<c>config</c> <c>tools</c> <c>learn</c>).</summary>
    public bool Learn { get; private set; } = true;

    public BuiltinTools Builtin { get; }

    public ToolRunner Runner { get; }

    /// <summary>Connected: the timers that came due while the process was off ring now.</summary>
    public void Start() => Builtin.Timers.Start();

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

    /// <summary>A timer rang: K-Desktop tells the user (and the activity says so).</summary>
    private void OnTimerRang(TimerItem timer)
    {
        var clock = timer.Due.ToLocalTime().ToString("t", Loc.Instance.Culture);
        var text = timer.Text.Length > 0 ? Loc.T("tool.set_timer.rangFor", clock, timer.Text) : Loc.T("tool.set_timer.rang", clock);
        Log.Info($"Asistente: temporizador de las {clock}");
        var title = timer.Assistant is { } id && AssistantPackages.Find(id) is { } installed ? installed.Manifest.Name : Loc.T("tool.set_timer.title");
        _link.Send(new NotifyMessage(title, text, timer.Assistant));
        Runner.Record(timer.Assistant, string.Empty, "timer", text, ok: true);
    }

    public void Dispose()
    {
        Builtin.Timers.Rang -= OnTimerRang;
        Builtin.Memory.Changed -= SendMemory;
        Builtin.Dispose();
        foreach (var waiting in _questions.Values) waiting.TrySetResult("timeout");
    }
}

using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The tools of the process (stage 3.3), for all the assistants and the module without a body: the registry with the
/// built-in ones, the runner (permissions, activity, undo), the questions K-Desktop asks for it (<c>ask</c> →
/// <c>answer</c>), the tools the user trusts (<c>config</c> <c>tools</c>) and the timers ringing (<c>notify</c>).
/// </summary>
internal sealed class ModuleTools : IDisposable
{
    private readonly KDesktopLink _link;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _questions = new(StringComparer.Ordinal);
    private volatile IReadOnlySet<string> _trusted = new HashSet<string>(StringComparer.Ordinal);

    public ModuleTools(KDesktopLink link)
    {
        _link = link;
        var folder = Path.Combine(AppPaths.DataDir, "Assistant");
        Builtin = new BuiltinTools(folder);
        var registry = new ToolRegistry();
        Builtin.AddTo(registry);
        Runner = new ToolRunner(registry, () => _trusted, AskAsync, message => link.Send(message), Path.Combine(folder, "undo.json"));
        Builtin.Timers.Rang += OnTimerRang;
    }

    public BuiltinTools Builtin { get; }

    public ToolRunner Runner { get; }

    /// <summary>Connected: the timers that came due while the process was off ring now.</summary>
    public void Start() => Builtin.Timers.Start();

    /// <summary>The <c>tools</c> section of <c>config</c>: <c>trusted</c>, the tools that no longer ask.</summary>
    public void Configure(JsonObject? tools)
    {
        var trusted = new HashSet<string>(StringComparer.Ordinal);
        if (tools?["trusted"] is JsonArray list)
            foreach (var item in list)
                if (item is JsonValue v && v.TryGetValue<string>(out var name)) trusted.Add(name);
        _trusted = trusted;
    }

    public void OnAnswer(AnswerMessage answer)
    {
        if (_questions.TryRemove(answer.Id, out var waiting)) waiting.TrySetResult(answer.Choice);
    }

    public Task UndoAsync(UndoMessage undo) => Runner.UndoAsync(undo.Undo, CancellationToken.None);

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
        Builtin.Dispose();
        foreach (var waiting in _questions.Values) waiting.TrySetResult("timeout");
    }
}

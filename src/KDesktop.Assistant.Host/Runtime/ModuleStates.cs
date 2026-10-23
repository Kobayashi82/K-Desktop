using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Sdk;
using KDesktop.Assistant.Tools;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// What each assistant shows it is doing (stage 13.4), put together from what tells it: the conversation (listening,
/// thinking, thinking hard, no brain, speaking, rest), its tools (asking for permission, running: busy with what), its
/// model loading, and a dictation K-Desktop is doing for it. Its character gets the state with its detail (on the engine
/// thread) and K-Desktop its <c>status</c>, only when they change. Any thread.
/// </summary>
/// <remarks>
/// Which wins: a tool waiting for permission, then speaking, then a tool running, then the model loading while it
/// thinks, then what the conversation says. A dictation is listening, with that activity.
/// </remarks>
internal sealed class ModuleStates
{
    private readonly KDesktopLink _link;
    private readonly EngineLoop _engine;
    private readonly AssistantRuntime _runtime;
    private readonly Func<string?, string> _languageOf;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Of> _all = new(StringComparer.Ordinal);
    private bool _brainLoading;

    /// <param name="languageOf">The language an assistant speaks in (the words of its detail).</param>
    public ModuleStates(KDesktopLink link, EngineLoop engine, AssistantRuntime runtime, Func<string?, string> languageOf)
    {
        _link = link;
        _engine = engine;
        _runtime = runtime;
        _languageOf = languageOf;
    }

    /// <summary>What the conversation says it is doing (<c>thinking</c>, <c>speaking</c>…, see <see cref="KDesktop.Assistant.Brain.Talk.ITalkOutput.State"/>).</summary>
    public void Talk(string? assistant, string state) => Change(assistant, of => of.Talk = state);

    /// <summary>One of its tools asks, starts or is over.</summary>
    public void Tool(ToolUse use) => Change(use.Assistant, of =>
    {
        of.Tools.RemoveAll(t => t.Tool == use.Tool);
        if (use.Phase != ToolPhase.Over) of.Tools.Add(use);
    });

    /// <summary>K-Desktop is writing what is said for it, or has stopped.</summary>
    public void Dictation(string? assistant, bool active) => Change(assistant, of => of.Dictating = active);

    /// <summary>A model of this PC started or finished loading: whoever is thinking now waits for it.</summary>
    public void BrainLoading(bool loading)
    {
        lock (_gate)
        {
            if (_brainLoading == loading) return;
            _brainLoading = loading;
            foreach (var (key, of) in _all) Publish(key, of);
        }
    }

    /// <summary>An assistant left the desktop: what it was doing is forgotten (it comes back at rest).</summary>
    public void Forget(string assistant)
    {
        lock (_gate) _all.Remove(assistant);
    }

    /// <summary>What it shows now, and its detail (for the tests and a newly loaded one).</summary>
    public (AssistantState State, AssistantActivity Activity, string? Tool) Now(string? assistant)
    {
        lock (_gate) return _all.TryGetValue(assistant ?? string.Empty, out var of) ? Compose(of, _brainLoading) : (AssistantState.Idle, AssistantActivity.None, null);
    }

    private void Change(string? assistant, Action<Of> change)
    {
        var key = assistant ?? string.Empty;
        lock (_gate)
        {
            if (!_all.TryGetValue(key, out var of)) _all[key] = of = new Of();
            change(of);
            Publish(key, of);
        }
    }

    /// <summary>Tells it (and K-Desktop) when what it shows changed. Inside the lock: the engine gets them in order.</summary>
    private void Publish(string key, Of of)
    {
        var now = Compose(of, _brainLoading);
        if (now == of.Shown) return;
        of.Shown = now;
        var (state, activity, tool) = now;
        var assistant = key.Length == 0 ? null : key;
        _link.Send(new StatusMessage(NameOf(state), Assistant: assistant,
            Activity: activity == AssistantActivity.None ? null : ToolActivities.Id(activity), Tool: tool));
        if (assistant is null) return;
        string? text;
        using (Loc.Instance.Use(_languageOf(assistant))) text = ToolActivities.Text(activity, tool);
        var detail = activity == AssistantActivity.None ? StateDetail.None : new StateDetail(activity, tool, text);
        _engine.Post(() => _runtime.SetState(assistant, state, detail));
    }

    internal static (AssistantState, AssistantActivity, string?) Compose(Of of, bool brainLoading)
    {
        if (of.Tools.LastOrDefault(t => t.Phase == ToolPhase.Asking) is { } asking) return (AssistantState.AwaitingConfirmation, asking.Activity, asking.Tool);
        var talk = of.Talk switch
        {
            "listening" => AssistantState.Listening,
            "thinking" => AssistantState.Thinking,
            "thinkingHard" => AssistantState.ThinkingHard,
            "noBrain" => AssistantState.NoBrain,
            "speaking" => AssistantState.Speaking,
            _ => AssistantState.Idle,
        };
        if (talk == AssistantState.Speaking) return (talk, AssistantActivity.None, null);
        if (of.Tools.LastOrDefault() is { } running) return (AssistantState.Busy, running.Activity, running.Tool);
        if (brainLoading && talk is AssistantState.Thinking or AssistantState.ThinkingHard) return (AssistantState.LoadingBrain, AssistantActivity.None, null);
        if (of.Dictating && talk is AssistantState.Idle or AssistantState.Listening) return (AssistantState.Listening, AssistantActivity.Dictation, null);
        return (talk, AssistantActivity.None, null);
    }

    /// <summary>Its name in <c>status</c>: the state in camelCase.</summary>
    internal static string NameOf(AssistantState state) => char.ToLowerInvariant(state.ToString()[0]) + state.ToString()[1..];

    internal sealed class Of
    {
        public string Talk { get; set; } = "idle";

        public List<ToolUse> Tools { get; } = [];

        public bool Dictating { get; set; }

        public (AssistantState, AssistantActivity, string?)? Shown { get; set; }
    }
}

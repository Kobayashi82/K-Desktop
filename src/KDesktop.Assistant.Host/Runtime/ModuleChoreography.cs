using System.Collections.Concurrent;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The general choreographer (stages 6.3–6.4): every <c>step</c> of a run an assistant asked for is shown with its
/// gestures (the table of <see cref="ActionGestures"/>), and the action happens at the instant of the gesture's
/// <c>Commit</c> (<c>go</c>), or the assistant did it itself (<c>done</c>). What it cannot show it says while K-Desktop does
/// it; if it fails, does not answer or takes longer than the step's time, K-Desktop does it and it reacts with a warning.
/// Every step gets exactly one answer, always before K-Desktop stops waiting: an assistant never holds an order back.
/// An order cancelled while its command runs skips all that is left (<c>skip</c> with <c>all</c>).
/// </summary>
internal sealed class ModuleChoreography(KDesktopLink link, EngineLoop engine, AssistantRuntime runtime)
{
    /// <summary>The step is answered this long before K-Desktop would stop waiting.</summary>
    private static readonly TimeSpan AnswerMargin = TimeSpan.FromMilliseconds(600);

    /// <summary>A gesture that ignores the end of its time gets this much more, then it is left behind.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(400);

    /// <summary>How long a step is said in the bubble while it happens (Communicate, or a gesture it cannot do).</summary>
    private static readonly TimeSpan SayFor = TimeSpan.FromSeconds(1.6);

    /// <summary>Its reactions (a warning when the module had to do a step), with their context (stage 13.3).</summary>
    public ModuleReactions? Reactions { get; set; }

    /// <summary>
    /// In «Fast» no manoeuvre takes over half a second (stage 6.7): past this the action happens anyway and the gesture is
    /// left behind.
    /// </summary>
    private static readonly TimeSpan FastBudget = TimeSpan.FromMilliseconds(500);

    /// <summary>What an adornment may take per gesture (it holds nothing back, but it should not drag on either).</summary>
    private static readonly TimeSpan AdornBudget = TimeSpan.FromSeconds(1);

    /// <summary>The runs with gestures going on: run → whose.</summary>
    private readonly ConcurrentDictionary<string, string> _runs = new(StringComparer.Ordinal);

    /// <summary>The results waited for after a Commit: (run, step) → ok.</summary>
    private readonly ConcurrentDictionary<(string, int), TaskCompletionSource<bool>> _results = new();

    /// <summary>The step each assistant is showing (engine thread): a new one, or the end of the run, ends it.</summary>
    private readonly Dictionary<string, CancellationTokenSource> _playing = new(StringComparer.Ordinal);

    /// <summary>A run with gestures starts; <paramref name="cancellationToken"/> is the order's (cancelling it skips the rest).</summary>
    public void Begin(string run, string assistant, CancellationToken cancellationToken)
    {
        _runs[run] = assistant;
        cancellationToken.Register(() =>
        {
            if (!_runs.TryRemove(run, out _)) return;
            Log.Info($"Asistente: «{assistant}» cancela el run {run}: se salta lo que queda");
            link.Send(new SkipMessage(run, 0, All: true));
            engine.Post(() => StopPlaying(assistant));
        });
    }

    /// <summary>The run ended (<c>end</c>): what its last step was still showing stops.</summary>
    public void End(string run)
    {
        if (_runs.TryRemove(run, out var assistant)) engine.Post(() => StopPlaying(assistant));
    }

    /// <summary>A step of one of its runs (pipe thread).</summary>
    public void OnStep(StepMessage step)
    {
        if (!_runs.TryGetValue(step.Run, out var assistant))
        {
            // Not one of ours (any more): let it run.
            link.Send(new GoMessage(step.Run, step.Index));
            return;
        }
        Log.Info($"Asistente: «{assistant}» paso {step.Index} ({step.Kind} {step.Op}, prof. {step.Depth}): «{step.Summary}»" +
                 (step.Target is { } t ? $" en «{t.Title}» ({t.State}{(t.Result is { } r ? $" → {r.X},{r.Y} {r.W}×{r.H}" : "")})" : ""));
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _results[(step.Run, step.Index)] = result;
        engine.Post(() => _ = PlayAsync(assistant, step, result));
    }

    public void OnResult(StepResultMessage result)
    {
        if (_results.TryRemove((result.Run, result.Index), out var waiting)) waiting.TrySetResult(result.Status == "ok");
        if (result.Status == "ok") return;
        var whose = _runs.TryGetValue(result.Run, out var assistant) ? $"«{assistant}» " : "";
        Log.Info($"Asistente: {whose}paso {result.Index} del run {result.Run}: {result.Status}{(result.Message is { } m ? $" ({m})" : "")}");
    }

    /// <summary>How long scanning what an assistant reads may take (it flies there first, at the speed of its last step).</summary>
    private static readonly TimeSpan ScanBudget = TimeSpan.FromSeconds(4);

    /// <summary>The speed of manoeuvres the last step asked for (Options → Assistant module → General).</summary>
    private volatile string? _lastSpeed;

    /// <summary>
    /// An assistant read the screen (look_at_screen, any thread): its figure scans that area (stage 10.1). Only decoration:
    /// the picture is already taken, nothing waits for it, and never over a step of an order.
    /// </summary>
    public void Scan(string assistant, KDesktop.Core.Platform.ScreenRect area)
    {
        if (!runtime.HasBody(assistant)) return;
        engine.Post(() => _ = ScanAsync(assistant, new PixelRect(area.Left, area.Top, area.Left + area.Width, area.Top + area.Height)));
    }

    private async Task ScanAsync(string assistant, PixelRect area)
    {
        if (_playing.ContainsKey(assistant)) return;
        var speed = StepGestures.SpeedOf(_lastSpeed);
        var budget = speed == ManoeuvreSpeed.Fast ? FastBudget : ScanBudget;
        var step = new StepMessage("", 0, 1, 0, "lookAtScreen", "", "", (int)budget.TotalMilliseconds, null, _lastSpeed ?? "normal");
        using var limit = new CancellationTokenSource(budget);
        var context = new StepGestureContext(engine, engine.Now + budget.TotalSeconds, step, () => { }, Task.FromResult(true), null, speed);
        if (runtime.TryPerform(assistant, new Gesture.Scan(area), context, limit.Token) is not { } running) return;
        try
        {
            await running.WaitAsync(budget + Grace).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            // Only decoration: whatever it was doing is left there.
        }
        catch (Exception ex)
        {
            Log.Error($"Asistente: «{assistant}» ha fallado al escanear lo que lee", ex);
        }
    }

    private void StopPlaying(string assistant)
    {
        if (!_playing.Remove(assistant, out var playing)) return;
        try { playing.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>One step, gesture after gesture (engine thread). Whatever happens, it is answered once.</summary>
    private async Task PlayAsync(string assistant, StepMessage step, TaskCompletionSource<bool> result)
    {
        StopPlaying(assistant);
        var speed = StepGestures.SpeedOf(step.Speed);
        _lastSpeed = step.Speed;
        var budget = TimeSpan.FromMilliseconds(Math.Max(300, step.TimeoutMs - AnswerMargin.TotalMilliseconds));
        if (speed == ManoeuvreSpeed.Fast && budget > FastBudget) budget = FastBudget;
        using var limit = new CancellationTokenSource(budget);
        _playing[assistant] = limit;
        var end = engine.Now + budget.TotalSeconds;
        var answered = 0;
        void Answer(PipeMessage message)
        {
            if (Interlocked.Exchange(ref answered, 1) == 0) link.Send(message);
        }
        void Go() => Answer(new GoMessage(step.Run, step.Index));
        // The result after the action, or false if it never comes in the step's time.
        var committed = result.Task.WaitAsync(budget + TimeSpan.FromSeconds(10)).ContinueWith(t => t.IsCompletedSuccessfully && t.Result, TaskScheduler.Default);

        var window = StepGestures.WindowOf(step.Target, runtime);
        var plan = ActionGestures.ForStep(step.Kind, step.Op, step.Target?.State == "maximized");
        try
        {
            foreach (var part in plan)
            {
                if (limit.IsCancellationRequested) break;
                // Said while it happens: the action does not wait for the bubble.
                if (part.Kind == GestureKind.Communicate)
                {
                    if (part.Commits) Go();
                    await SayAsync(assistant, step, limit.Token);
                    continue;
                }
                var gesture = StepGestures.Build(part, step, window, runtime);
                var context = new StepGestureContext(engine, end, step, part.Commits ? Go : () => Prepare(part, window), committed,
                    gesture is Gesture.WaitForWindow wait ? AppearedAsync(wait.Process, limit.Token) : null, speed);
                var running = gesture is null ? null : runtime.TryPerform(assistant, gesture, context, limit.Token);
                if (running is null)
                {
                    // It cannot show this part: a preparation is left to the action; the action itself, said while it happens.
                    if (!part.Commits) continue;
                    Go();
                    await SayAsync(assistant, step, limit.Token);
                    continue;
                }
                var outcome = await OutcomeAsync(assistant, part.Kind, running, budget);
                switch (outcome)
                {
                    case GestureOutcome.PerformedSelf when part.Commits:
                        Answer(new DoneMessage(step.Run, step.Index));
                        break;
                    case GestureOutcome.Committed when part.Commits:
                        Go();
                        break;
                    case GestureOutcome.Committed:
                        if (!context.WasCommitted) Prepare(part, window);
                        break;
                    case GestureOutcome.NotPerformed when part.Commits:
                        Go();
                        if (Reactions is { } reactions) await reactions.ReactAsync(assistant, Reaction.Warning, ReactionSource.Order, step.Kind);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Asistente: no se ha podido representar el paso {step.Index} de «{assistant}»", ex);
        }
        finally
        {
            // Never left without an answer.
            Go();
            _results.TryRemove((step.Run, step.Index), out _);
            if (_playing.TryGetValue(assistant, out var current) && current == limit) _playing.Remove(assistant);
        }
    }

    /// <summary>How a gesture ended: what it said, or not performed when it failed, did not answer in time or threw.</summary>
    private static async Task<GestureOutcome> OutcomeAsync(string assistant, GestureKind kind, Task<GestureOutcome> running, TimeSpan budget)
    {
        try
        {
            return await running.WaitAsync(budget + Grace).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            Log.Warn($"Asistente: «{assistant}» no termina el gesto {kind} a tiempo; lo hace K-Desktop");
        }
        catch (OperationCanceledException)
        {
            Log.Info($"Asistente: «{assistant}» deja el gesto {kind} a medias (se acabó su tiempo)");
        }
        catch (Exception ex)
        {
            Log.Error($"Asistente: «{assistant}» ha fallado en el gesto {kind}", ex);
        }
        return GestureOutcome.NotPerformed;
    }

    /// <summary>
    /// An action of a command launched another way (stage 6.7, <c>observed</c>): shown quickly, without the bubble and
    /// without anything waiting for it (the action happens on its own). Never over a step of an order being shown.
    /// </summary>
    public void OnObserved(ObservedMessage observed)
    {
        if (runtime.HasBody(observed.Assistant)) engine.Post(() => _ = AdornAsync(observed));
    }

    private async Task AdornAsync(ObservedMessage observed)
    {
        var assistant = observed.Assistant;
        foreach (var item in observed.Steps)
        {
            if (_playing.ContainsKey(assistant)) return;
            var step = new StepMessage("", 0, 1, 0, item.Kind, item.Op, item.Summary, (int)AdornBudget.TotalMilliseconds, item.Target, "fast");
            var window = StepGestures.WindowOf(item.Target, runtime);
            foreach (var part in ActionGestures.ForStep(item.Kind, item.Op, item.Target?.State == "maximized"))
            {
                if (part.Kind == GestureKind.Communicate || StepGestures.Build(part, step, window, runtime) is not { } gesture) continue;
                using var limit = new CancellationTokenSource(AdornBudget);
                var context = new StepGestureContext(engine, engine.Now + AdornBudget.TotalSeconds, step, () => { }, Task.FromResult(true), null,
                    ManoeuvreSpeed.Fast);
                if (runtime.TryPerform(assistant, gesture, context, limit.Token) is not { } running) continue;
                try
                {
                    await running.WaitAsync(AdornBudget + Grace).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
                {
                    // Only decoration: whatever it was doing is left there.
                }
                catch (Exception ex)
                {
                    Log.Error($"Asistente: «{assistant}» ha fallado al adornar {item.Kind}", ex);
                }
            }
        }
    }

    /// <summary>The step in its bubble, with its Communicate gesture, for a moment.</summary>
    private Task SayAsync(string assistant, StepMessage step, CancellationToken token) =>
        runtime.CommunicateAsync(assistant, step.Summary, SayFor, token);

    /// <summary>
    /// What a part that only leads to the action does at its Commit: a maximized window is restored before it is moved
    /// (through K-Desktop when it is an administrator's window).
    /// </summary>
    private void Prepare(GestureStep part, WindowInfo? window)
    {
        if (part is { Kind: GestureKind.PressButton, Button: CaptionButton.Restore } && window is not null)
            runtime.WindowServices.TryCommand(window.Handle, WindowCommand.Restore);
    }

    /// <summary>For WaitForWindow: the first new window of that program, or null when none shows up in the step's time.</summary>
    private async Task<WindowInfo?> AppearedAsync(string? process, CancellationToken token)
    {
        var before = runtime.World.Current.Windows.Select(w => w.Handle).ToHashSet();
        try
        {
            while (true)
            {
                await Task.Delay(150, token).ConfigureAwait(true);
                var found = runtime.World.Current.Windows.FirstOrDefault(w => !before.Contains(w.Handle) &&
                    (process is null || string.Equals(w.AppId, process, StringComparison.OrdinalIgnoreCase)));
                if (found is not null) return found;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}

using System.Collections.Concurrent;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using static KDesktop.Assistant.Host.Interop.Native;

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
        var budget = TimeSpan.FromMilliseconds(Math.Max(300, step.TimeoutMs - AnswerMargin.TotalMilliseconds));
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
                    gesture is Gesture.WaitForWindow wait ? AppearedAsync(wait.Process, limit.Token) : null);
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
                        await ReactAsync(assistant, Reaction.Warning);
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

    /// <summary>The step in its bubble, with its Communicate gesture, for a moment.</summary>
    private Task SayAsync(string assistant, StepMessage step, CancellationToken token) =>
        runtime.CommunicateAsync(assistant, step.Summary, SayFor, token);

    /// <summary>A short reaction (a warning when the module had to do it), never long.</summary>
    private async Task ReactAsync(string assistant, Reaction reaction)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        var context = new StepGestureContext(engine, engine.Now + 1.5, new StepMessage("", 0, 0, 0, "", "", "", 1500), () => { }, Task.FromResult(true), null);
        if (runtime.TryPerform(assistant, new Gesture.React(reaction), context, limit.Token) is { } running)
            await OutcomeAsync(assistant, GestureKind.React, running, TimeSpan.FromSeconds(1.5));
    }

    /// <summary>
    /// What a part that only leads to the action does at its Commit: a maximized window is restored before it is moved
    /// (one of an administrator's process cannot be touched from here: K-Desktop's move restores it anyway).
    /// </summary>
    private static void Prepare(GestureStep part, WindowInfo? window)
    {
        if (part is { Kind: GestureKind.PressButton, Button: CaptionButton.Restore } && window is { Elevated: false } w)
            ShowWindow((nint)w.Handle, 9 /* SW_RESTORE */);
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

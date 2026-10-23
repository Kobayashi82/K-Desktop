using System.Collections.Concurrent;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Sdk;
using KDesktop.Assistant.Tools;
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

    /// <summary>How long the gesture of a tool may take (it flies or walks there first, at the speed of the last step).</summary>
    private static readonly TimeSpan ToolBudget = TimeSpan.FromSeconds(4);

    /// <summary>Waiting for the window of what a tool opened: a browser or a program can take a while to show it.</summary>
    private static readonly TimeSpan OpenBudget = TimeSpan.FromSeconds(6);

    /// <summary>How long a tool's mark stays (a beacon by the clock for a timer).</summary>
    private static readonly TimeSpan MarkFor = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The speed of manoeuvres (Options → Assistant module → General): K-Desktop sends it in <c>config</c> <c>general</c>, and
    /// each step brings it too.
    /// </summary>
    private volatile string? _lastSpeed;

    /// <summary>The user's speed of manoeuvres (<c>showy</c>, <c>normal</c>, <c>fast</c>), from <c>config</c> <c>general</c>.</summary>
    public string? Speed
    {
        get => _lastSpeed;
        set => _lastSpeed = value;
    }

    /// <summary>How long catching the user's eye may take before speaking (stage 13.6), in «Showy» and in «Normal».</summary>
    private static readonly TimeSpan AnnounceShowy = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan AnnounceNormal = TimeSpan.FromSeconds(2);

    /// <summary>
    /// It is about to say something on its own (stage 13.6: a timer, «avísame cuando», «Asistente: decir»): first it catches
    /// the user's eye (<see cref="Gesture.Announce"/>). Finished when the gesture is, or at most its time (2 s, 3 in «Showy»,
    /// half a second in «Fast»); at once when it has no body, does not know the gesture or is showing a step of an order.
    /// </summary>
    public Task AnnounceAsync(string assistant, AnnounceReason reason)
    {
        if (!runtime.HasBody(assistant)) return Task.CompletedTask;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.Post(async () =>
        {
            try { await CatchEyeAsync(assistant, reason).ConfigureAwait(true); }
            finally { done.TrySetResult(); }
        });
        return done.Task;
    }

    private async Task CatchEyeAsync(string assistant, AnnounceReason reason)
    {
        if (_playing.ContainsKey(assistant)) return;
        StopDecorating(assistant);
        var speed = StepGestures.SpeedOf(_lastSpeed);
        var budget = speed switch
        {
            ManoeuvreSpeed.Fast => FastBudget,
            ManoeuvreSpeed.Showy => AnnounceShowy,
            _ => AnnounceNormal,
        };
        var system = runtime.World.System;
        var gesture = new Gesture.Announce(reason, system.Cursor, system.UserIdleSeconds);
        var step = new StepMessage("", 0, 1, 0, "announce", reason.ToString(), "", (int)budget.TotalMilliseconds, null, _lastSpeed ?? "normal");
        using var limit = new CancellationTokenSource(budget);
        // A step of an order that comes meanwhile takes its place, like a tool's gesture.
        _decorating[assistant] = limit;
        var context = new StepGestureContext(engine, engine.Now + budget.TotalSeconds, step, () => { }, Task.FromResult(true), null, speed);
        try
        {
            if (runtime.TryPerform(assistant, gesture, context, limit.Token) is not { } running) return;
            Log.Info($"Asistente: «{assistant}» llama la atención ({reason}) antes de hablar");
            await running.WaitAsync(budget + Grace).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            // Its time is up: it speaks anyway.
        }
        catch (Exception ex)
        {
            Log.Error($"Asistente: «{assistant}» ha fallado al llamar la atención", ex);
        }
        finally
        {
            if (_decorating.TryGetValue(assistant, out var current) && current == limit) _decorating.Remove(assistant);
        }
    }

    /// <summary>The gesture of a tool each assistant is showing (engine thread): a newer one, or a step of an order, ends it.</summary>
    private readonly Dictionary<string, CancellationTokenSource> _decorating = new(StringComparer.Ordinal);

    /// <summary>The user's window in front (never K-Desktop's), for a gesture on it; null when there is none.</summary>
    public Func<WindowInfo?>? Front { get; set; }

    /// <summary>
    /// A tool asked its character for a gesture (stage 13.5, any thread; look_at_screen's scan was the first, 10.1): a beacon
    /// by the clock, waiting for the window of what it opens, the tray… Only decoration: the tool goes on without waiting, and
    /// it is never shown over a step of an order. An assistant without a body, or that does not know it, does nothing.
    /// </summary>
    public void Show(ToolShown shown)
    {
        if (shown.Assistant is not { Length: > 0 } assistant || !runtime.HasBody(assistant)) return;
        // What was there before it opens anything: by the time the engine gets to it, its window may be up already.
        var before = shown.Gesture.Kind == GestureKind.WaitForWindow ? runtime.World.Current.Windows.Select(w => w.Handle).ToHashSet() : null;
        engine.Post(() => _ = ShowAsync(assistant, shown, before));
    }

    private async Task ShowAsync(string assistant, ToolShown shown, HashSet<long>? before)
    {
        if (_playing.ContainsKey(assistant)) return;
        if (ToolGestureOf(shown.Gesture) is not { } gesture)
        {
            Log.Info($"Asistente: el gesto {shown.Gesture.Kind} de {shown.Tool} no tiene dónde hacerse");
            return;
        }
        StopDecorating(assistant);
        var speed = StepGestures.SpeedOf(_lastSpeed);
        var budget = speed == ManoeuvreSpeed.Fast ? FastBudget : gesture is Gesture.WaitForWindow ? OpenBudget : ToolBudget;
        var step = new StepMessage("", 0, 1, 0, shown.Tool, "", "", (int)budget.TotalMilliseconds, null, _lastSpeed ?? "normal");
        using var limit = new CancellationTokenSource(budget);
        _decorating[assistant] = limit;
        var appeared = gesture is Gesture.WaitForWindow wait ? AppearedAsync(wait.Process, limit.Token, before) : null;
        var context = new StepGestureContext(engine, engine.Now + budget.TotalSeconds, step, () => { }, Task.FromResult(true), appeared, speed);
        try
        {
            if (runtime.TryPerform(assistant, gesture, context, limit.Token) is not { } running) return;
            Log.Info($"Asistente: «{assistant}» enseña {gesture.Kind} por {shown.Tool}");
            await running.WaitAsync(budget + Grace).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            // Only decoration: whatever it was doing is left there.
        }
        catch (Exception ex)
        {
            Log.Error($"Asistente: «{assistant}» ha fallado en el gesto {gesture.Kind} de {shown.Tool}", ex);
        }
        finally
        {
            if (_decorating.TryGetValue(assistant, out var current) && current == limit) _decorating.Remove(assistant);
        }
    }

    /// <summary>The SDK gesture for what a tool asked, or null when its place is not there (the window is gone…).</summary>
    private Gesture? ToolGestureOf(ToolGesture asked)
    {
        var window = asked.Where switch
        {
            ToolSpot.Window when asked.Window != 0 => runtime.World.Current.Window(asked.Window),
            ToolSpot.Front => Front?.Invoke(),
            _ => null,
        };
        Place? place = asked.Where switch
        {
            ToolSpot.Clock => new Place.Clock(),
            ToolSpot.Tray => new Place.Tray(),
            ToolSpot.Window or ToolSpot.Front => window is null ? null : new Place.OfWindow(window),
            ToolSpot.Area when asked.Area is { Width: > 0, Height: > 0 } a => new Place.Area(Pixel(a)),
            _ => null,
        };
        return asked.Kind switch
        {
            GestureKind.Mark when place is not null => new Gesture.Mark(place, MarkFor, asked.Text),
            GestureKind.PointAt when place is not null => new Gesture.PointAt(place),
            GestureKind.GoTo when place is not null => new Gesture.GoTo(place),
            GestureKind.WaitForWindow => new Gesture.WaitForWindow(asked.Process, null),
            GestureKind.Scan when asked.Area is { Width: > 0, Height: > 0 } area => new Gesture.Scan(Pixel(area)),
            GestureKind.Typing when window is not null => new Gesture.Typing(window),
            _ => null,
        };
    }

    private static PixelRect Pixel(KDesktop.Core.Platform.ScreenRect area) => new(area.Left, area.Top, area.Left + area.Width, area.Top + area.Height);

    private void StopDecorating(string assistant)
    {
        if (!_decorating.Remove(assistant, out var decorating)) return;
        try { decorating.Cancel(); }
        catch (ObjectDisposedException) { }
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
        // A step of an order goes before the decoration of a tool.
        StopDecorating(assistant);
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

    /// <summary>
    /// For WaitForWindow: the first new window of that program (any, without one), or null when none shows up in the step's
    /// time. <paramref name="before"/>: the windows there were before it opened (null: now).
    /// </summary>
    private async Task<WindowInfo?> AppearedAsync(string? process, CancellationToken token, HashSet<long>? before = null)
    {
        before ??= runtime.World.Current.Windows.Select(w => w.Handle).ToHashSet();
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

using System.Windows;
using KDesktop.Assistant.Host.Stage;
using KDesktop.Assistant.Sdk;
using KDesktop.Assistant.Tools;
using KDesktop.Core;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The showcase (stages 13.9 and 16.1): what each assistant can show, and «do this one now», on the stage (a desktop of its
/// own, never the user's). The host's own items (entrances, exits, states, reactions, gestures and, with the stage's
/// windows, what it does to a window) are launched by the host, the same way K-Desktop asks for them in use; its own
/// animations (<see cref="IAssistant.Showcase"/>, SDK 1.18) it plays itself. One at a time per assistant: a new one, or
/// <c>stop</c>, stops the one before. Everything on the engine thread.
/// </summary>
/// <param name="send">To K-Desktop (null when recording, where nobody asks).</param>
/// <param name="replay">
/// Takes the assistant off the stage the way it says and, a moment later, loads it again (for entrances and exits); false when it
/// cannot.
/// </param>
/// <param name="settled">Ends when the stage's loads and unloads are done (after an exit, it is back on the stage).</param>
internal sealed class ModuleShowcase(Action<PipeMessage>? send, AssistantRuntime runtime, StageScene stage, ModuleChoreography choreography,
    Func<string, ExitKind, EntranceKind, TimeSpan, Task<bool>> replay, Func<Task> settled)
{
    /// <summary>How long a state is shown.</summary>
    private static readonly TimeSpan StateFor = TimeSpan.FromSeconds(3);

    /// <summary>What a reaction or a gesture may take.</summary>
    private static readonly TimeSpan GestureFor = TimeSpan.FromSeconds(4);

    /// <summary>What a gesture on a window may take (as a step of an order: it may fly or walk there first).</summary>
    private static readonly TimeSpan WindowFor = TimeSpan.FromSeconds(6);

    /// <summary>Moving a window may take longer (as K-Desktop allows it for a step).</summary>
    private static readonly TimeSpan MoveFor = TimeSpan.FromSeconds(10);

    /// <summary>How long the bubble says what happens when it cannot do it itself.</summary>
    private static readonly TimeSpan SayFor = TimeSpan.FromSeconds(1.6);

    /// <summary>What one of its own may take before K-Desktop stops waiting.</summary>
    private static readonly TimeSpan OwnFor = TimeSpan.FromSeconds(30);

    /// <summary>A moment between leaving and coming back, so the exit is seen whole.</summary>
    private static readonly TimeSpan Away = TimeSpan.FromSeconds(0.6);

    /// <summary>How long a window stays as the gesture left it before the stage puts it back.</summary>
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(1);

    /// <summary>The host's own items, in the order K-Desktop shows them: (id, group).</summary>
    internal static IReadOnlyList<(string Id, string Group)> Standard => Core.Assistant.ShowcaseItems.Standard;

    /// <summary>What each assistant is showing now.</summary>
    private readonly Dictionary<string, CancellationTokenSource> _playing = new(StringComparer.Ordinal);

    public void OnShowcase(ShowcaseMessage message)
    {
        switch (message.Op)
        {
            case ShowcaseMessage.List:
                send?.Invoke(new ShowcaseListMessage(message.Assistant, List(message.Assistant)));
                break;
            case ShowcaseMessage.Play:
                _ = PlayAsync(message.Assistant, message.Item, message.Own);
                break;
            case ShowcaseMessage.Stop:
                Stop(message.Assistant);
                break;
        }
    }

    /// <summary>The host's items and then its own (none if it is not on the stage).</summary>
    internal IReadOnlyList<ShowcaseEntry> List(string assistant)
    {
        if (!runtime.IsLoaded(assistant)) return [];
        var own = runtime.ShowcaseOf(assistant)
            .Where(i => !string.IsNullOrWhiteSpace(i.Id))
            .DistinctBy(i => i.Id, StringComparer.Ordinal)
            .Select(i => new ShowcaseEntry(i.Id, GroupOf(i.Group), true, i.Name is { Count: > 0 } names ? names : null));
        return [.. Standard.Select(s => new ShowcaseEntry(s.Id, s.Group)), .. own];
    }

    /// <summary>The group's name on the pipe (one it does not know: <c>other</c>).</summary>
    internal static string GroupOf(ShowcaseGroup group) => group switch
    {
        ShowcaseGroup.Idle => "idle",
        ShowcaseGroup.Moving => "moving",
        ShowcaseGroup.Playing => "playing",
        ShowcaseGroup.Feelings => "feelings",
        ShowcaseGroup.Tricks => "tricks",
        _ => "other",
    };

    public void Stop(string assistant)
    {
        if (!_playing.Remove(assistant, out var playing)) return;
        try { playing.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private async Task PlayAsync(string assistant, string item, bool own)
    {
        var result = await RunAsync(assistant, item, own).ConfigureAwait(true);
        send?.Invoke(new ShowcaseDoneMessage(assistant, item, own, result));
    }

    /// <summary>Plays one item (the one before stops) and says how it went (<see cref="ShowcaseDoneMessage"/>'s results).</summary>
    public async Task<string> RunAsync(string assistant, string item, bool own)
    {
        Stop(assistant);
        using var stop = new CancellationTokenSource();
        _playing[assistant] = stop;
        string result;
        try
        {
            await settled().WaitAsync(stop.Token).ConfigureAwait(true);
            // An entrance may start from an empty stage (taken away before it is recorded).
            var entrance = !own && item.StartsWith("enter.", StringComparison.Ordinal);
            result = !runtime.IsLoaded(assistant) && !entrance ? ShowcaseDoneMessage.Unknown
                : own ? await OwnAsync(assistant, item, stop.Token).ConfigureAwait(true)
                : await StandardAsync(assistant, item, stop.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            result = ShowcaseDoneMessage.Stopped;
        }
        catch (OperationCanceledException)
        {
            // Its time was up: it has been shown.
            result = ShowcaseDoneMessage.Done;
        }
        catch (Exception ex)
        {
            Log.Error($"Asistente: escaparate de «{assistant}»: {item} ha fallado", ex);
            result = ShowcaseDoneMessage.Failed;
        }
        finally
        {
            if (_playing.TryGetValue(assistant, out var current) && current == stop) _playing.Remove(assistant);
        }
        if (stop.IsCancellationRequested && result == ShowcaseDoneMessage.Done) result = ShowcaseDoneMessage.Stopped;
        Log.Info($"Asistente: escaparate de «{assistant}»: {(own ? "suyo " : "")}{item} → {result}");
        return result;
    }

    private async Task<string> OwnAsync(string assistant, string item, CancellationToken stop)
    {
        if (!runtime.ShowcaseOf(assistant).Any(i => i.Id == item)) return ShowcaseDoneMessage.Unknown;
        if (runtime.PlayShowcase(assistant, item, stop) is not { } running) return ShowcaseDoneMessage.Failed;
        return await running.WaitAsync(OwnFor, stop).ConfigureAwait(true) ? ShowcaseDoneMessage.Done : ShowcaseDoneMessage.NotPerformed;
    }

    private async Task<string> StandardAsync(string assistant, string item, CancellationToken stop)
    {
        switch (item)
        {
            case "enter.startup":
                return await ReplayAsync(assistant, ExitKind.Reloading, EntranceKind.Startup, TimeSpan.Zero).ConfigureAwait(true);
            case "enter.summoned":
                return await ReplayAsync(assistant, ExitKind.Reloading, EntranceKind.Summoned, TimeSpan.Zero).ConfigureAwait(true);
            case "exit.dismissed":
                return await ReplayAsync(assistant, ExitKind.Dismissed, EntranceKind.Reloaded, Away).ConfigureAwait(true);
            case "exit.uninstalling":
                return await ReplayAsync(assistant, ExitKind.Uninstalling, EntranceKind.Reloaded, Away).ConfigureAwait(true);
        }
        if (item.StartsWith("state.", StringComparison.Ordinal))
            return Enum.TryParse<AssistantState>(item["state.".Length..], ignoreCase: true, out var state) && state != AssistantState.Idle
                ? await StateAsync(assistant, state, stop).ConfigureAwait(true)
                : ShowcaseDoneMessage.Unknown;
        if (item.StartsWith("react.", StringComparison.Ordinal))
            return Enum.TryParse<Reaction>(item["react.".Length..], ignoreCase: true, out var reaction)
                ? await GestureAsync(assistant, new Gesture.React(reaction) { Context = new ReactionContext(ReactionSource.User, What: "showcase") }, stop).ConfigureAwait(true)
                : ShowcaseDoneMessage.Unknown;
        if (item.StartsWith("window.", StringComparison.Ordinal)) return await WindowAsync(assistant, item, stop).ConfigureAwait(true);
        switch (item)
        {
            case "gesture.communicate":
                await runtime.CommunicateAsync(assistant, Loc.T("showcase.sample"), StateFor, stop).ConfigureAwait(true);
                stop.ThrowIfCancellationRequested();
                return ShowcaseDoneMessage.Done;
            case "gesture.typing":
                return await TypingAsync(assistant, stop).ConfigureAwait(true);
        }
        return GestureOf(item) is { } gesture ? await GestureAsync(assistant, gesture, stop).ConfigureAwait(true) : ShowcaseDoneMessage.Unknown;
    }

    /// <summary>The gestures that need no window: by the cursor, the clock, the tray, the main window of the stage.</summary>
    private Gesture? GestureOf(string item)
    {
        var system = runtime.World.System;
        var work = stage.WorkArea;
        var cursor = stage.Cursor ?? new Point(work.Left + work.Width * 0.2, work.Top + work.Height * 0.7);
        return item switch
        {
            "gesture.announce" => new Gesture.Announce(AnnounceReason.Other, stage.Cursor, system.UserIdleSeconds),
            "gesture.mark" => new Gesture.Mark(new Place.Clock(), StateFor, DateTime.Now.AddMinutes(10).ToString("t", Loc.Instance.Culture)),
            "gesture.pointAt" => new Gesture.PointAt(new Place.Tray()),
            "gesture.goTo" => new Gesture.GoTo(new Place.AtPoint(cursor)),
            "gesture.scan" => new Gesture.Scan(stage.InfoOf(stage.Main)?.Bounds ?? Middle(work)),
            _ => null,
        };
    }

    /// <summary>The middle half of an area.</summary>
    private static PixelRect Middle(PixelRect area) =>
        new(area.Left + area.Width / 4, area.Top + area.Height / 4, area.Right - area.Width / 4, area.Bottom - area.Height / 4);

    private async Task<string> GestureAsync(string assistant, Gesture gesture, CancellationToken stop)
    {
        var outcome = await choreography.ShowcaseAsync(assistant, gesture, GestureFor, stop).ConfigureAwait(true);
        return outcome is null or GestureOutcome.NotPerformed ? ShowcaseDoneMessage.NotPerformed : ShowcaseDoneMessage.Done;
    }

    /// <summary>Typing into the stage's note pad, which fills up letter by letter meanwhile.</summary>
    private async Task<string> TypingAsync(string assistant, CancellationToken stop)
    {
        var main = await ReadyAsync(stage.Main, stop).ConfigureAwait(true);
        main.Typed = "";
        stage.Type(main, Loc.T("showcase.stage.typed"));
        try
        {
            var outcome = await choreography.ShowcaseAsync(assistant, new Gesture.Typing(stage.InfoOf(main)), GestureFor, stop).ConfigureAwait(true);
            return outcome is null or GestureOutcome.NotPerformed ? ShowcaseDoneMessage.NotPerformed : ShowcaseDoneMessage.Done;
        }
        finally { stage.Type(null, null); }
    }

    /// <summary>A window shown, restored and in front, ready for a gesture (it waits for it to get there).</summary>
    private async Task<StageWindow> ReadyAsync(StageWindow window, CancellationToken stop)
    {
        var wait = window.State != StageWindowState.Normal || stage.Front != window;
        if (window.State != StageWindowState.Normal) stage.Restore(window);
        stage.BringToFront(window);
        if (wait) await Task.Delay(400, stop).ConfigureAwait(true);
        return window;
    }

    /// <summary>
    /// A gesture on one of the stage's windows, as a step of an order: the window changes at its Commit (or, when it does
    /// not commit, when it ends: K-Desktop would do it then); when it cannot do it, the stage does it and the bubble says it,
    /// as in use. Then, a moment later, the window goes back the way it was (<paramref name="after"/>).
    /// </summary>
    private async Task<string> WindowAsync(string assistant, string item, CancellationToken stop)
    {
        var main = stage.Main;
        switch (item)
        {
            case "window.minimize":
                await ReadyAsync(main, stop).ConfigureAwait(true);
                return await PressAsync(assistant, item, main, CaptionButton.Minimize, () => stage.Minimize(main), () => stage.Restore(main), stop).ConfigureAwait(true);
            case "window.maximize":
                await ReadyAsync(main, stop).ConfigureAwait(true);
                return await PressAsync(assistant, item, main, CaptionButton.Maximize, () => stage.Maximize(main), () => stage.Restore(main), stop).ConfigureAwait(true);
            case "window.close":
                await ReadyAsync(main, stop).ConfigureAwait(true);
                return await PressAsync(assistant, item, main, CaptionButton.Close, () => stage.Close(main), () => stage.Open(main), stop).ConfigureAwait(true);
            case "window.move":
            {
                await ReadyAsync(main, stop).ConfigureAwait(true);
                var destination = stage.MoveDestination(main);
                var gesture = new Gesture.MoveWindow(stage.InfoOf(main)!, destination);
                return await PerformAsync(assistant, item, gesture, () => stage.Move(main, destination, animate: true), null, MoveFor, stop,
                    self: () => stage.Move(main, destination, animate: false)).ConfigureAwait(true);
            }
            case "window.activate":
            {
                var behind = stage.Second;
                if (behind.State != StageWindowState.Normal) stage.Restore(behind);
                await ReadyAsync(main, stop).ConfigureAwait(true);
                var gesture = new Gesture.ActivateWindow(stage.InfoOf(behind)!);
                return await PerformAsync(assistant, item, gesture, () => stage.BringToFront(behind), () => stage.BringToFront(main), WindowFor, stop).ConfigureAwait(true);
            }
            case "window.open":
            {
                var opened = stage.Opened;
                if (opened.State != StageWindowState.Closed)
                {
                    stage.Close(opened);
                    await Task.Delay(300, stop).ConfigureAwait(true);
                }
                // What was opened takes a moment to show its window, as a program would.
                var appeared = OpenLaterAsync(stop);
                var gesture = new Gesture.WaitForWindow(opened.AppId, null);
                return await PerformAsync(assistant, item, gesture, null, () => stage.Close(opened), WindowFor, stop, appeared,
                    show: () => { if (opened.State == StageWindowState.Closed) stage.OpenNew(); }).ConfigureAwait(true);
            }
        }
        return ShowcaseDoneMessage.Unknown;
    }

    private async Task<WindowInfo?> OpenLaterAsync(CancellationToken stop)
    {
        try
        {
            await Task.Delay(700, stop).ConfigureAwait(true);
            return stage.InfoOf(stage.OpenNew());
        }
        catch (OperationCanceledException) { return null; }
    }

    private Task<string> PressAsync(string assistant, string item, StageWindow window, CaptionButton button, Action effect, Action after, CancellationToken stop)
    {
        var info = stage.InfoOf(window)!;
        var gesture = new Gesture.PressButton(info, button, info.Buttons![button]);
        return PerformAsync(assistant, item, gesture, effect, after, WindowFor, stop);
    }

    /// <param name="effect">What happens at its Commit (null: nothing to commit, as waiting for a window).</param>
    /// <param name="after">What puts the stage back, a moment after it ended (null: it stays).</param>
    /// <param name="self">When it did it itself (it moved the window): what puts it exactly in place.</param>
    /// <param name="show">When it could not wait for it, the window comes anyway.</param>
    private async Task<string> PerformAsync(string assistant, string item, Gesture gesture, Action? effect, Action? after, TimeSpan budget, CancellationToken stop,
        Task<WindowInfo?>? appeared = null, Action? self = null, Action? show = null)
    {
        var committed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Commit()
        {
            if (committed.Task.IsCompleted) return;
            effect?.Invoke();
            committed.TrySetResult(true);
        }
        string result;
        try
        {
            var outcome = await choreography.ShowcaseAsync(assistant, gesture, budget, stop, Commit, committed.Task, appeared).ConfigureAwait(true);
            if (outcome is null or GestureOutcome.NotPerformed)
            {
                // As in use: the action happens anyway and the bubble says it while it does.
                Commit();
                show?.Invoke();
                await runtime.CommunicateAsync(assistant, Loc.T("showcase.item." + item), SayFor, stop).ConfigureAwait(true);
                result = ShowcaseDoneMessage.NotPerformed;
            }
            else
            {
                if (outcome == GestureOutcome.PerformedSelf && self is not null)
                {
                    committed.TrySetResult(true);
                    self();
                }
                else Commit();
                show?.Invoke();
                result = ShowcaseDoneMessage.Done;
            }
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested)
        {
            // Its time was up: K-Desktop would have done it.
            Commit();
            show?.Invoke();
            result = ShowcaseDoneMessage.Done;
        }
        if (after is not null)
        {
            await Task.Delay(Linger, stop).ConfigureAwait(true);
            after();
            await Task.Delay(300, stop).ConfigureAwait(true);
        }
        return result;
    }

    /// <summary>A state for a few seconds (speaking, with a voice level that comes and goes), then idle again.</summary>
    private async Task<string> StateAsync(string assistant, AssistantState state, CancellationToken stop)
    {
        var detail = state == AssistantState.Busy
            ? new StateDetail(AssistantActivity.SearchingWeb, "look_up_web", ToolActivities.Text(AssistantActivity.SearchingWeb, "look_up_web"))
            : StateDetail.None;
        runtime.SetState(assistant, state, detail);
        try
        {
            if (state != AssistantState.Speaking)
            {
                await Task.Delay(StateFor, stop).ConfigureAwait(true);
                return ShowcaseDoneMessage.Done;
            }
            var start = Environment.TickCount64;
            for (var elapsed = 0.0; elapsed < StateFor.TotalSeconds; elapsed = (Environment.TickCount64 - start) / 1000.0)
            {
                // Syllables: about five a second, louder and softer, with short pauses.
                var syllable = Math.Abs(Math.Sin(elapsed * Math.PI * 5));
                var phrase = 0.55 + 0.45 * Math.Sin(elapsed * 1.7);
                runtime.VoiceLevel(assistant, elapsed % 1.4 > 1.15 ? 0 : syllable * phrase);
                await Task.Delay(33, stop).ConfigureAwait(true);
            }
            return ShowcaseDoneMessage.Done;
        }
        finally
        {
            if (state == AssistantState.Speaking) runtime.VoiceLevel(assistant, 0);
            runtime.SetState(assistant, AssistantState.Idle);
        }
    }

    /// <summary>Leaves as <paramref name="why"/> says and, <paramref name="away"/> later, comes back as <paramref name="how"/> says.</summary>
    private async Task<string> ReplayAsync(string assistant, ExitKind why, EntranceKind how, TimeSpan away) =>
        await replay(assistant, why, how, away).ConfigureAwait(true) ? ShowcaseDoneMessage.Done : ShowcaseDoneMessage.NotPerformed;
}

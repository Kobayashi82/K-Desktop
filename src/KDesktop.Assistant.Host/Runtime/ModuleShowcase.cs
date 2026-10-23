using System.Windows;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Assistant.Sdk;
using KDesktop.Assistant.Tools;
using KDesktop.Core;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The showcase (stage 13.9): what each assistant can show, and «do this one now». The host's own items (entrances, exits,
/// states, reactions and the gestures that need no window) are launched by the host, the same way K-Desktop asks for them
/// in use; its own animations (<see cref="IAssistant.Showcase"/>, SDK 1.18) it plays itself. One at a time per assistant:
/// a new one, or <c>stop</c>, stops the one before. Everything on the engine thread.
/// </summary>
/// <param name="replay">
/// Takes the assistant off the desktop the way it says and, a moment later, loads it again (for entrances and exits); false when it cannot
/// (it was not loaded from a package).
/// </param>
internal sealed class ModuleShowcase(KDesktopLink link, AssistantRuntime runtime, ModuleChoreography? choreography,
    Func<string, ExitKind, EntranceKind, TimeSpan, Task<bool>> replay)
{
    /// <summary>How long a state is shown.</summary>
    private static readonly TimeSpan StateFor = TimeSpan.FromSeconds(3);

    /// <summary>What a reaction or a gesture may take.</summary>
    private static readonly TimeSpan GestureFor = TimeSpan.FromSeconds(4);

    /// <summary>What one of its own may take before K-Desktop stops waiting.</summary>
    private static readonly TimeSpan OwnFor = TimeSpan.FromSeconds(30);

    /// <summary>A moment between leaving and coming back, so the exit is seen whole.</summary>
    private static readonly TimeSpan Away = TimeSpan.FromSeconds(0.6);

    /// <summary>The host's own items, in the order K-Desktop shows them: (id, group).</summary>
    internal static IReadOnlyList<(string Id, string Group)> Standard { get; } =
    [
        ("enter.startup", "entrances"), ("enter.summoned", "entrances"),
        ("exit.dismissed", "exits"), ("exit.uninstalling", "exits"),
        ("state.listening", "states"), ("state.thinking", "states"), ("state.thinkingHard", "states"),
        ("state.loadingBrain", "states"), ("state.busy", "states"), ("state.awaitingConfirmation", "states"),
        ("state.speaking", "states"), ("state.noBrain", "states"), ("state.sleeping", "states"),
        ("react.success", "reactions"), ("react.celebration", "reactions"), ("react.warning", "reactions"), ("react.failure", "reactions"),
        ("gesture.announce", "gestures"), ("gesture.communicate", "gestures"), ("gesture.mark", "gestures"),
        ("gesture.pointAt", "gestures"), ("gesture.goTo", "gestures"), ("gesture.scan", "gestures"), ("gesture.typing", "gestures"),
    ];

    /// <summary>What each assistant is showing now.</summary>
    private readonly Dictionary<string, CancellationTokenSource> _playing = new(StringComparer.Ordinal);

    public void OnShowcase(ShowcaseMessage message)
    {
        switch (message.Op)
        {
            case ShowcaseMessage.List:
                link.Send(new ShowcaseListMessage(message.Assistant, List(message.Assistant)));
                break;
            case ShowcaseMessage.Play:
                _ = PlayAsync(message.Assistant, message.Item, message.Own);
                break;
            case ShowcaseMessage.Stop:
                Stop(message.Assistant);
                break;
        }
    }

    /// <summary>The host's items and then its own (none if it is not on the desktop).</summary>
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

    private void Stop(string assistant)
    {
        if (!_playing.Remove(assistant, out var playing)) return;
        try { playing.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private async Task PlayAsync(string assistant, string item, bool own)
    {
        Stop(assistant);
        using var stop = new CancellationTokenSource();
        _playing[assistant] = stop;
        string result;
        try
        {
            result = !runtime.IsLoaded(assistant) ? ShowcaseDoneMessage.Unknown
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
        link.Send(new ShowcaseDoneMessage(assistant, item, own, result));
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
        if (item == "gesture.communicate")
        {
            await runtime.CommunicateAsync(assistant, Loc.T("showcase.sample"), StateFor, stop).ConfigureAwait(true);
            stop.ThrowIfCancellationRequested();
            return ShowcaseDoneMessage.Done;
        }
        return GestureOf(assistant, item) is { } gesture ? await GestureAsync(assistant, gesture, stop).ConfigureAwait(true) : ShowcaseDoneMessage.Unknown;
    }

    /// <summary>The gestures that need no window, done where the user is: by the cursor, the clock, the tray, its monitor.</summary>
    private Gesture? GestureOf(string assistant, string item)
    {
        var system = runtime.World.System;
        var world = runtime.World.Current;
        var cursor = system.Cursor ?? world.PrimaryMonitor?.WorkArea.Center ?? default;
        var monitor = (runtime.BodyOf(assistant) is { } body ? world.MonitorAt(body.Center) : null) ?? world.MonitorAt(cursor) ?? world.PrimaryMonitor;
        return item switch
        {
            "gesture.announce" => new Gesture.Announce(AnnounceReason.Other, system.Cursor, system.UserIdleSeconds),
            "gesture.mark" => new Gesture.Mark(new Place.Clock(), StateFor, DateTime.Now.AddMinutes(10).ToString("t", Loc.Instance.Culture)),
            "gesture.pointAt" => new Gesture.PointAt(new Place.Tray()),
            "gesture.goTo" => new Gesture.GoTo(new Place.AtPoint(cursor)),
            "gesture.scan" when monitor is { } m => new Gesture.Scan(Middle(m.WorkArea)),
            "gesture.typing" => new Gesture.Typing(null),
            _ => null,
        };
    }

    /// <summary>The middle half of an area (what it scans: something in the middle of its screen).</summary>
    private static PixelRect Middle(PixelRect area) =>
        new(area.Left + area.Width / 4, area.Top + area.Height / 4, area.Right - area.Width / 4, area.Bottom - area.Height / 4);

    private async Task<string> GestureAsync(string assistant, Gesture gesture, CancellationToken stop)
    {
        if (choreography is null) return ShowcaseDoneMessage.NotPerformed;
        var outcome = await choreography.ShowcaseAsync(assistant, gesture, GestureFor, stop).ConfigureAwait(true);
        return outcome is null or GestureOutcome.NotPerformed ? ShowcaseDoneMessage.NotPerformed : ShowcaseDoneMessage.Done;
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

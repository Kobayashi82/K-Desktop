using System.Text.Json;

namespace KDesktop.Assistant.Sdk;

/// <summary>
/// An assistant: a character that lives on the desktop. The host (K-Desktop's assistant process) loads it from its
/// package, gives it the world and its services, and asks it for gestures; the assistant never sees the AI, the
/// tools, the pipe or K-Desktop.
/// </summary>
/// <remarks>
/// <para>
/// <b>One thread.</b> Every member is called on the host's engine thread, and so are the continuations of awaits
/// inside them (the thread has its own synchronization context). No locking is needed; never block it.
/// </para>
/// <para>
/// <b>The host only wakes it when needed.</b> <see cref="Tick"/> returns how long it can sleep: an assistant at rest
/// asks for a few ticks per second (or fewer) and draws nothing, which keeps the cost close to zero; while moving it
/// asks for one tick per frame and the host paces it with the compositor.
/// </para>
/// <para>
/// <b>Failures are contained.</b> An exception in any member is logged; several in a row unload the assistant.
/// </para>
/// </remarks>
public interface IAssistant : IDisposable
{
    /// <summary>Called once, right after loading, before anything else.</summary>
    void Attach(IAssistantHost host);

    /// <summary>Appear (fly in, fade in, pop out of a hole…), according to why it comes (<see cref="EntranceKind"/>).</summary>
    void Enter(EntranceKind how);

    /// <summary>
    /// Leave, according to why (<see cref="ExitKind"/>). The host waits for it (3 s at most; the token fires then) before
    /// unloading it.
    /// </summary>
    Task ExitAsync(ExitKind why, CancellationToken ct);

    /// <summary>
    /// Advances physics and behaviour, and presents a frame on its surfaces if something changed.
    /// Returns the seconds the host may wait before the next tick (it may call earlier: an event, a gesture,
    /// <see cref="IAssistantHost.RequestTick"/>). Values at or under <see cref="IRendering.FrameInterval"/> mean
    /// «every frame».
    /// </summary>
    double Tick(TickInfo tick);

    /// <summary>Whether it can do this gesture now (not asleep in a game, has a way to reach the window…).</summary>
    bool CanPerform(Gesture gesture);

    /// <summary>
    /// Performs a gesture (§11.3). The token fires when the time is up or the order is cancelled ("stop", Escape):
    /// stop quickly and return. Never throw to say no: return <see cref="GestureOutcome.NotPerformed"/>.
    /// </summary>
    Task<GestureOutcome> PerformAsync(Gesture gesture, IGestureContext ctx, CancellationToken ct);

    /// <summary>What the module is doing (listening, thinking, speaking…), for the character to show it.</summary>
    void SetState(AssistantState state);

    /// <summary>Loudness of its voice while speaking (0…1, about 30 times per second): lights or a mouth that follow it.</summary>
    void OnVoiceLevel(double level);

    /// <summary>
    /// Where speech bubbles of the module point to (usually the top of the head), in screen pixels. Null when hidden.
    /// </summary>
    System.Windows.Point? BubbleAnchor { get; }

    void OnPointer(PointerEvent e);

    void OnSystemEvent(SystemEvent e);

    /// <summary>One of the extras of its manifest («lander.manual»…), launched from a command, the panel or the menu.</summary>
    Task OnExtraAsync(string extraId, CancellationToken ct);

    /// <summary>
    /// The user changed its settings in K-Desktop's Options (values keyed as in its settings.json), or one of the common
    /// ones (<see cref="IAssistantHost.Common"/>, already up to date).
    /// </summary>
    void OnSettingsChanged(JsonElement settings);
}

/// <param name="Now">Seconds on the host's monotonic clock.</param>
/// <param name="Elapsed">Seconds since the previous tick, capped at 0.25 (after a pause nothing jumps).</param>
public readonly record struct TickInfo(double Now, double Elapsed);

/// <summary>Why an assistant comes onto the desktop (<see cref="IAssistant.Enter"/>).</summary>
/// <remarks>The values keep the numbers of SDK 1.8 and earlier with the closest meaning (packages already built).</remarks>
public enum EntranceKind
{
    /// <summary>K-Desktop started, or the user switched the assistant module on: its full entrance.</summary>
    Startup,

    /// <summary>
    /// It was already there a moment ago and comes back at once: a new version, another language, its data started
    /// afresh, its process restarted or recovering from a fall, a rebuild with <c>--assistant-dev</c>. Reappear where
    /// it was, with no show (what it saved on <see cref="ExitKind.Reloading"/>, in its data folder).
    /// </summary>
    Reloaded,

    /// <summary>The user put it on the desktop (Options, the tray, the panel): an entrance that says «here I am».</summary>
    Summoned,
}

/// <summary>Why an assistant leaves the desktop (<see cref="IAssistant.ExitAsync"/>).</summary>
/// <remarks>The values keep the numbers of SDK 1.8 and earlier with the closest meaning (packages already built).</remarks>
public enum ExitKind
{
    /// <summary>K-Desktop is closing, or the user switched the assistant module off: be quick.</summary>
    Shutdown,

    /// <summary>
    /// It comes back at once (<see cref="EntranceKind.Reloaded"/>): no show; save in its data folder what it needs to
    /// reappear where it was (the new one may run in a new process).
    /// </summary>
    Reloading,

    /// <summary>The user took it off the desktop (Options, the tray, its menu): its goodbye.</summary>
    Dismissed,

    /// <summary>It is being removed: the last goodbye (its data folder may be deleted right after).</summary>
    Uninstalling,
}

/// <summary>States the module tells the assistant about; each assistant shows them its own way.</summary>
public enum AssistantState
{
    Idle,
    /// <summary>Its wake word was heard: it is listening.</summary>
    Listening,
    /// <summary>The AI is working on an answer.</summary>
    Thinking,
    Speaking,
    /// <summary>A yes/no question is waiting for the user.</summary>
    AwaitingConfirmation,
    /// <summary>Carrying out an order.</summary>
    Busy,
    Sleeping,
}

/// <summary>How one of its extras shows in its menu and its panel (<see cref="IAssistantHost.SetExtraState"/>).</summary>
public enum ExtraState
{
    Shown,

    /// <summary>Visible but greyed out: it exists, just not now.</summary>
    Disabled,

    /// <summary>Not there at all (it would make no sense yet).</summary>
    Hidden,
}

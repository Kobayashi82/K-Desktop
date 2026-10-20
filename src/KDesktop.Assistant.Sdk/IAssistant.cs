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

    /// <summary>Appear (fly in, fade in, pop out of a hole…).</summary>
    void Enter(EntranceKind how);

    /// <summary>Leave. The host waits for it (with a time limit) before unloading or switching assistant.</summary>
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

    /// <summary>The user changed its settings in K-Desktop's Options (values keyed as in its settings.json).</summary>
    void OnSettingsChanged(JsonElement settings);
}

/// <param name="Now">Seconds on the host's monotonic clock.</param>
/// <param name="Elapsed">Seconds since the previous tick, capped at 0.25 (after a pause nothing jumps).</param>
public readonly record struct TickInfo(double Now, double Elapsed);

public enum EntranceKind
{
    /// <summary>The module was just enabled or K-Desktop started.</summary>
    Startup,

    /// <summary>It replaces another assistant.</summary>
    Switched,

    /// <summary>Back after «Hide for a while» or a full-screen app.</summary>
    Restored,

    /// <summary>Reloaded by <c>--assistant-dev</c> after a rebuild.</summary>
    Reloaded,
}

public enum ExitKind { Shutdown, Switching, Hiding, Uninstalling, Reloading }

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

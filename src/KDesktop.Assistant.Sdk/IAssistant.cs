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
    /// <remarks>
    /// Only the states of SDK 1.12 and earlier come here (<see cref="AssistantStates.Basic"/>) unless the assistant
    /// implements <see cref="SetState(AssistantState, StateDetail)"/>, which the host calls instead.
    /// </remarks>
    void SetState(AssistantState state);

    /// <summary>
    /// What the module is doing, with what it is doing it with (SDK 1.13): <see cref="StateDetail.Activity"/> says it is
    /// searching the internet, reading a page, looking at the screen… The host always calls this one; by default it
    /// passes the closest state of SDK 1.12 to <see cref="SetState(AssistantState)"/>, so an assistant built before keeps
    /// seeing only the states it knows.
    /// </summary>
    void SetState(AssistantState state, StateDetail detail) => SetState(AssistantStates.Basic(state));

    /// <summary>Loudness of its voice while speaking (0…1, about 30 times per second): lights or a mouth that follow it.</summary>
    void OnVoiceLevel(double level);

    /// <summary>
    /// Where speech bubbles of the module point to (usually the top of the head), in screen pixels. Null when hidden.
    /// </summary>
    System.Windows.Point? BubbleAnchor { get; }

    /// <summary>
    /// Shows what it says its own way (SDK 1.15), for an assistant whose manifest says <c>"speech": "own"</c>: a balloon
    /// of its own, a sign it holds up, words in its cloud… The host calls it each time the answer grows while it is being
    /// written (<see cref="SpeechText.Finished"/> false), with all of it when it is ready, and again with a later
    /// <see cref="SpeechText.Until"/> while its voice is still saying it. Each call replaces the text of the one before.
    /// The host still keeps the timing, the voice and the chat: show it until <see cref="HideSpeech"/>. Return false when
    /// it cannot now (hidden, in the middle of a manoeuvre, it does not fit): the host shows its own bubble instead, for
    /// the rest of that answer. Not called while the user chose K-Desktop's bubble for it.
    /// </summary>
    bool ShowSpeech(SpeechText speech) => false;

    /// <summary>
    /// Hides what <see cref="ShowSpeech"/> showed: it has been said and read, the answer was cancelled, or the host's bubble
    /// takes over (SDK 1.15).
    /// </summary>
    void HideSpeech()
    {
    }

    /// <summary>
    /// Where its body is now, in screen pixels (SDK 1.10): what the other assistants avoid stopping on
    /// (<see cref="IWorld.Others"/>). Just the body, not its particles, bubbles or effects. Null while it is not on screen.
    /// Read after every tick. Without it the host takes the bounds of its «body» hit area.
    /// </summary>
    PixelRect? BodyBounds => null;

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

/// <summary>What it says, for <see cref="IAssistant.ShowSpeech"/> (SDK 1.15).</summary>
/// <param name="Text">
/// The answer so far, as plain text (no Markdown), in its language. A long answer comes whole: cut it to what fits (the
/// whole of it is in the chat).
/// </param>
/// <param name="Finished">The answer is complete; false while more is being written.</param>
/// <param name="Until">
/// When the host expects to hide it, on its clock (<see cref="TickInfo.Now"/>): enough to read it, pushed back while its
/// voice speaks. Useful to start fading; hide it when <see cref="IAssistant.HideSpeech"/> comes.
/// </param>
public sealed record SpeechText(string Text, bool Finished, double Until);

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
/// <remarks>The ones after <see cref="Sleeping"/> are from SDK 1.13 (see <see cref="AssistantStates.Basic"/>).</remarks>
public enum AssistantState
{
    Idle,
    /// <summary>Its wake word was heard, or its talk key or a long click: it is listening (also while dictating).</summary>
    Listening,
    /// <summary>The AI is working on an answer.</summary>
    Thinking,
    Speaking,
    /// <summary>A yes/no question is waiting for the user (the little window that asks for permission).</summary>
    AwaitingConfirmation,
    /// <summary>Carrying out an order: one of its tools or a command is running (<see cref="StateDetail.Activity"/> says which).</summary>
    Busy,
    Sleeping,
    /// <summary>The AI thinks it over, slowly (it asked to, or the user said «piénsalo bien»): it may take half a minute.</summary>
    ThinkingHard,
    /// <summary>Its model is being loaded into memory before it can think (from a few seconds to a minute).</summary>
    LoadingBrain,
    /// <summary>
    /// The order needed the AI and there is none (no model, or its server does not answer). It comes right before
    /// <see cref="Speaking"/>, which says so: a shrug while it says it.
    /// </summary>
    NoBrain,
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

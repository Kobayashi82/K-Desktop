using System.Windows;

namespace KDesktop.Assistant.Sdk;

/// <summary>
/// The gesture names, as listed in <see cref="AssistantTraits.Gestures"/> and in assistant.json
/// (camelCase there: "pointAt", "pressButton"…).
/// </summary>
public enum GestureKind
{
    PointAt, GoTo, PressButton, MoveWindow, ActivateWindow, WaitForWindow, Mark, Scan, Typing, Communicate, React,

    /// <summary>SDK 1.14: see <see cref="Gesture.Announce"/>.</summary>
    Announce,
}

/// <summary>
/// What the module wants to achieve; the assistant decides how (§11.2 of the plan). An assistant that only knows
/// <see cref="Communicate"/> and <see cref="React"/> already works: for the rest the module falls back to
/// Communicate and performs the action itself.
/// </summary>
public abstract record Gesture
{
    public abstract GestureKind Kind { get; }

    /// <summary>Point at a window, a point or an area (a spotlight, an arm…).</summary>
    public sealed record PointAt(Place Target) : Gesture { public override GestureKind Kind => GestureKind.PointAt; }

    /// <summary>Go somewhere (fly, walk, teleport).</summary>
    public sealed record GoTo(Place Target) : Gesture { public override GestureKind Kind => GestureKind.GoTo; }

    /// <summary>
    /// Press a caption button. Call <see cref="IGestureContext.Commit"/> at the moment of the effect (when the shot
    /// hits): the window minimizes right then.
    /// </summary>
    public sealed record PressButton(WindowInfo Window, CaptionButton Button, PixelRect ButtonBounds) : Gesture
    {
        public override GestureKind Kind => GestureKind.PressButton;
    }

    /// <summary>
    /// Move a window to <paramref name="Destination"/>. Either move it frame by frame with
    /// <see cref="IWindowServices.TryMove"/> and return <see cref="GestureOutcome.PerformedSelf"/> (K-Desktop then puts
    /// it exactly in place), or call Commit and let the module move it. If <paramref name="Window"/> is maximized it
    /// arrives already restored by a previous PressButton(Restore).
    /// </summary>
    public sealed record MoveWindow(WindowInfo Window, PixelRect Destination) : Gesture
    {
        public override GestureKind Kind => GestureKind.MoveWindow;
    }

    /// <summary>Bring a window to the front (perch on it, knock on the door…). Commit when it should happen.</summary>
    public sealed record ActivateWindow(WindowInfo Window) : Gesture { public override GestureKind Kind => GestureKind.ActivateWindow; }

    /// <summary>
    /// Something was just opened: wait for its window. <see cref="IGestureContext.Appeared"/> completes with the new
    /// window (or null when it does not show up in time).
    /// </summary>
    public sealed record WaitForWindow(string? Process, string? Title) : Gesture { public override GestureKind Kind => GestureKind.WaitForWindow; }

    /// <summary>Leave a mark somewhere for a while (beacon, flag, alarm clock), with an optional label.</summary>
    public sealed record Mark(Place Target, TimeSpan Duration, string? Text) : Gesture { public override GestureKind Kind => GestureKind.Mark; }

    /// <summary>Sweep an area (screenshot, reading the screen).</summary>
    public sealed record Scan(PixelRect Area) : Gesture { public override GestureKind Kind => GestureKind.Scan; }

    /// <summary>Pretend to type into a window (keys or text are being sent to it).</summary>
    public sealed record Typing(WindowInfo? Window) : Gesture { public override GestureKind Kind => GestureKind.Typing; }

    /// <summary>The generic one: stay visible while the host shows <paramref name="Text"/> in the bubble.</summary>
    /// <param name="Icon">Optional icon id from the module (volume, power, clock, web…).</param>
    public sealed record Communicate(string Text, string? Icon) : Gesture { public override GestureKind Kind => GestureKind.Communicate; }

    /// <summary>
    /// A short reaction to how something went. <see cref="Context"/> (SDK 1.12) says where it comes from, how many in a
    /// row and whether it is the first of the day, so the same reaction need not look the same every time.
    /// </summary>
    public sealed record React(Reaction Reaction) : Gesture
    {
        public override GestureKind Kind => GestureKind.React;

        /// <summary>Filled by the host (SDK 1.12); <see cref="ReactionContext.Unknown"/> when it cannot tell.</summary>
        public ReactionContext Context { get; init; } = ReactionContext.Unknown;
    }

    /// <summary>
    /// SDK 1.14: it is about to say something on its own (a timer that rang, «avísame cuando» that happened, «Asistente:
    /// decir»), so it catches the user's eye first: it comes closer to the cursor, waves, blinks… The host waits for it
    /// (at most <see cref="IGestureContext.TimeLeft"/>, about 2 s; half a second in «Fast») and then speaks with
    /// <see cref="Communicate"/>: end as soon as it is done. Nothing to Commit.
    /// </summary>
    /// <param name="Reason">Why it is going to speak.</param>
    /// <param name="Cursor">Where the cursor was when it was asked (screen pixels), or null when it could not be read.</param>
    /// <param name="UserAway">Seconds without keyboard or mouse: the longer, the bigger it may be (the user may be away).</param>
    public sealed record Announce(AnnounceReason Reason, Point? Cursor, double UserAway) : Gesture
    {
        public override GestureKind Kind => GestureKind.Announce;
    }
}

/// <summary>Why an assistant is about to speak on its own (SDK 1.14, <see cref="Gesture.Announce"/>).</summary>
public enum AnnounceReason
{
    Other,

    /// <summary>A timer it set rang (with a text, it is a reminder).</summary>
    Timer,

    /// <summary>Something it was told to watch happened («avísame cuando se cierre…»).</summary>
    Watch,

    /// <summary>One of the user's commands or events asked it to say something («Asistente: decir»).</summary>
    Message,
}

public enum Reaction { Success, Warning, Failure, Celebration }

/// <summary>Where a reaction comes from (SDK 1.12).</summary>
public enum ReactionSource
{
    Unknown,

    /// <summary>One of the user's K-Desktop commands, or an action of K-Desktop (a window, a program, the volume).</summary>
    Order,

    /// <summary>A tool it used (a timer, a note, the web, a tool of an MCP server…).</summary>
    Tool,

    /// <summary>Something it says on its own: a timer that rang, «avísame cuando» that happened.</summary>
    Notice,

    /// <summary>What the user said about it: thanks or praise (Celebration), «that's wrong» (Failure).</summary>
    User,
}

/// <summary>
/// The context of a <see cref="Gesture.React"/> (SDK 1.12), filled by the host.
/// </summary>
/// <param name="Source">Where it comes from.</param>
/// <param name="Streak">
/// How many reactions of this same kind in a row, this one included (1 = the first; the third failure in a row is 3). Starts
/// again after another kind, or after half an hour without reactions.
/// </param>
/// <param name="FirstToday">It is its first reaction of the day (local date): a good moment for something bigger.</param>
/// <param name="What">
/// What it was, when known: the tool (<c>set_timer</c>, <c>run_command</c>…), the notice (<c>timer</c>, <c>watch</c>), the
/// action of a step (<c>window</c>…) or what the user said (<c>praise</c>, <c>wrong</c>).
/// </param>
public sealed record ReactionContext(ReactionSource Source, int Streak = 1, bool FirstToday = false, string? What = null)
{
    public static ReactionContext Unknown { get; } = new(ReactionSource.Unknown);
}

/// <summary>A place on the desktop a gesture refers to.</summary>
public abstract record Place
{
    public sealed record OfWindow(WindowInfo Window) : Place;

    public sealed record AtPoint(Point Point) : Place;

    public sealed record Area(PixelRect Rect) : Place;

    public sealed record Monitor(int Index) : Place;

    /// <summary>The notification area of the taskbar, near the clock (volume, network, power…).</summary>
    public sealed record Tray : Place;

    public sealed record Clock : Place;
}

public enum GestureOutcome
{
    /// <summary>
    /// The gesture is over and the effect was left to the module: either <see cref="IGestureContext.Commit"/> was
    /// called at the right moment, or the module commits now.
    /// </summary>
    Committed,

    /// <summary>The assistant did it itself (moved the window). K-Desktop checks it and fixes the last pixels.</summary>
    PerformedSelf,

    /// <summary>It could not be done (no room, the window would not move…): the module does it.</summary>
    NotPerformed,
}

public enum ManoeuvreSpeed { Showy, Normal, Fast }

/// <summary>What <see cref="IWindowServices.TryCommand"/> does to a window.</summary>
public enum WindowCommand { Minimize, Maximize, Restore, Activate }

/// <summary>What the module gives an assistant while it performs a gesture.</summary>
public interface IGestureContext
{
    /// <summary>
    /// «Do it now»: the module performs the action at this very moment (the impact of the shot). Calling it more
    /// than once does nothing.
    /// </summary>
    void Commit();

    /// <summary>Completes when the action really happened after <see cref="Commit"/> (true) or failed (false).</summary>
    Task<bool> Committed { get; }

    /// <summary>For <see cref="Gesture.WaitForWindow"/>: the window that appeared, or null. Null task otherwise.</summary>
    Task<WindowInfo?>? Appeared { get; }

    /// <summary>
    /// Time left before the module gives up on the gesture and does the action itself. The cancellation token passed
    /// to <see cref="IAssistant.PerformAsync"/> fires then.
    /// </summary>
    TimeSpan TimeLeft { get; }

    /// <summary>How long the user wants manoeuvres to take (Options → Assistant). In Fast, under half a second.</summary>
    ManoeuvreSpeed Speed { get; }

    /// <summary>Position in the order being carried out (step 2 of 3) for assistants that want to show it.</summary>
    int Step { get; }

    int StepCount { get; }
}

/// <summary>Moving and changing windows from the assistant process.</summary>
public interface IWindowServices
{
    /// <summary>
    /// Moves (and resizes) a window at once, for frame-by-frame dragging (its visible part: no shadows nor invisible
    /// borders). A window of an administrator's process is out of this process's reach: K-Desktop moves it (SDK 1.3), a
    /// little behind, at most about 30 times a second. False when it cannot be moved (it is gone, or maximized).
    /// </summary>
    bool TryMove(long handle, PixelRect bounds);

    /// <summary>
    /// Minimizes, maximizes, restores or brings a window to the front (SDK 1.3). Bringing to the front, and anything on a
    /// window of an administrator's process, goes through K-Desktop (Windows only lets it do that): true means it was
    /// asked. False when the window is gone.
    /// </summary>
    bool TryCommand(long handle, WindowCommand command);

    /// <summary>Caption buttons of a window (asks DWM; null when they cannot be found).</summary>
    CaptionButtons? GetCaptionButtons(long handle);

    /// <summary>A small picture of the window, for ghosts and effects (null if it cannot be captured).</summary>
    System.Windows.Media.ImageSource? Thumbnail(long handle, int maxSize);

    /// <summary>The icon of the window's application.</summary>
    System.Windows.Media.ImageSource? AppIcon(long handle);
}

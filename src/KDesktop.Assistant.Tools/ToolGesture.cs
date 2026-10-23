using KDesktop.Assistant.Sdk;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tools;

/// <summary>Where a tool's gesture goes (stage 13.5).</summary>
public enum ToolSpot
{
    None,

    /// <summary>The clock of the taskbar (a timer).</summary>
    Clock,

    /// <summary>The notification area of the taskbar (music, volume).</summary>
    Tray,

    /// <summary>The window <see cref="ToolGesture.Window"/>.</summary>
    Window,

    /// <summary>The user's window in front (never K-Desktop's).</summary>
    Front,

    /// <summary>The area <see cref="ToolGesture.Area"/> of the screen.</summary>
    Area,
}

/// <summary>
/// What a tool would like its character to show while it works (stage 13.5): a beacon by the clock for a timer, waiting for
/// the window of what it opens, going to the tray for the music, typing into the window in front, scanning what it reads.
/// The tool asks with <see cref="ToolContext.Show"/>; the host passes it to the assistant as a gesture of the SDK. Only
/// decoration: nothing waits for it (a tool never takes longer for it), and an assistant that does not know the gesture, has
/// no body or is showing a step of an order does nothing.
/// </summary>
/// <param name="Window">For <see cref="ToolSpot.Window"/>: its handle.</param>
/// <param name="Area">For <see cref="ToolSpot.Area"/> and <see cref="GestureKind.Scan"/>: where on the screen.</param>
/// <param name="Text">For <see cref="GestureKind.Mark"/>: its label («17:42»).</param>
/// <param name="Process">For <see cref="GestureKind.WaitForWindow"/>: the program whose window comes («explorer»), or null for any.</param>
public sealed record ToolGesture(GestureKind Kind, ToolSpot Where = ToolSpot.None, long Window = 0, ScreenRect? Area = null, string? Text = null,
    string? Process = null)
{
    public static ToolGesture Mark(ToolSpot where, string? text = null, long window = 0) => new(GestureKind.Mark, where, window, Text: text);

    public static ToolGesture GoTo(ToolSpot where) => new(GestureKind.GoTo, where);

    public static ToolGesture PointAt(ToolSpot where, long window = 0) => new(GestureKind.PointAt, where, window);

    /// <summary>Something is about to open: asked just before opening it (a quick window would already be there after).</summary>
    public static ToolGesture WaitForWindow(string? process = null) => new(GestureKind.WaitForWindow, Process: process);

    public static ToolGesture Scan(ScreenRect area) => new(GestureKind.Scan, ToolSpot.Area, Area: area);

    /// <summary>Typing into a window: 0 = the user's window in front.</summary>
    public static ToolGesture Typing(long window = 0) => new(GestureKind.Typing, window == 0 ? ToolSpot.Front : ToolSpot.Window, window);

    /// <summary>The names a JSON tool can give in <c>"gesture"</c>.</summary>
    public static IReadOnlyList<string> Names { get; } = ["waitForWindow", "tray", "clock", "typing"];

    /// <summary>The gesture a JSON tool declares (<c>"gesture"</c>), or null (none, or a name it does not know).</summary>
    public static ToolGesture? Named(string? name) => name switch
    {
        "waitForWindow" => WaitForWindow(),
        "tray" => GoTo(ToolSpot.Tray),
        "clock" => Mark(ToolSpot.Clock),
        "typing" => Typing(),
        _ => null,
    };
}

/// <summary>A tool asked for a gesture (<see cref="ToolRunner.Showing"/>): for whom, which tool and what.</summary>
public sealed record ToolShown(string? Assistant, string Tool, ToolGesture Gesture);

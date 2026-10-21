using System.Windows;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Sdk;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The general choreographer's gestures for a step (stage 6.4): the table of <see cref="ActionGestures"/> turned into the
/// SDK's gestures, with the window, its caption buttons and where it ends up from the step's target (and the world).
/// </summary>
internal static class StepGestures
{
    /// <summary>The window of a step as the SDK sees it (from the world if it is there, with its caption buttons), or null.</summary>
    public static WindowInfo? WindowOf(StepTarget? target, AssistantRuntime runtime)
    {
        if (target is not { Hwnd: not 0 } t) return null;
        var known = runtime.World.Current.Window(t.Hwnd);
        var bounds = known?.Bounds ?? (t.Rect is { } r ? Pixel(r) : default);
        var buttons = known?.Buttons ?? runtime.WindowServices.GetCaptionButtons(t.Hwnd) ?? ApproximateButtons(bounds, runtime);
        return new WindowInfo(t.Hwnd, bounds, t.Title, t.Process, t.State == "maximized", t.Elevated, buttons);
    }

    /// <summary>
    /// The caption buttons where Windows usually puts them (46 × 32 pixels at 100 %, top right) when DWM does not say:
    /// close enough for a gesture to aim at.
    /// </summary>
    public static CaptionButtons? ApproximateButtons(PixelRect bounds, AssistantRuntime runtime)
    {
        if (bounds.IsEmpty) return null;
        var scale = runtime.World.Current.MonitorAt(new Point(bounds.Left + bounds.Width / 2.0, bounds.Top + 1))?.DpiScale ?? 1;
        int width = (int)Math.Round(46 * scale), height = (int)Math.Round(32 * scale), right = bounds.Right, top = bounds.Top;
        return new CaptionButtons(
            new PixelRect(right - 3 * width, top, right - 2 * width, top + height),
            new PixelRect(right - 2 * width, top, right - width, top + height),
            new PixelRect(right - width, top, right, top + height));
    }

    /// <summary>The SDK gesture for one part of the plan, or null when the step lacks what it needs (a window, a destination).</summary>
    public static Gesture? Build(GestureStep part, StepMessage step, WindowInfo? window, AssistantRuntime runtime) => part.Kind switch
    {
        GestureKind.PressButton when window is not null && part.Button is { } button && window.Buttons is { } buttons =>
            new Gesture.PressButton(window, button, buttons[button]),
        GestureKind.MoveWindow when window is not null && step.Target?.Result is { } result => new Gesture.MoveWindow(window, Pixel(result)),
        GestureKind.ActivateWindow when window is not null => new Gesture.ActivateWindow(window),
        GestureKind.WaitForWindow => new Gesture.WaitForWindow(step.Target?.Process is { Length: > 0 } p ? p : null, null),
        GestureKind.PointAt => new Gesture.PointAt(PlaceOf(part.Place, window)),
        GestureKind.GoTo => new Gesture.GoTo(PlaceOf(part.Place, window)),
        GestureKind.Mark => new Gesture.Mark(PlaceOf(part.Place, window), TimeSpan.FromSeconds(3), step.Summary),
        GestureKind.Scan => new Gesture.Scan(window?.Bounds is { IsEmpty: false } area ? area : MonitorUnderCursor(runtime)),
        GestureKind.Typing => new Gesture.Typing(window),
        GestureKind.Communicate => new Gesture.Communicate(step.Summary, part.Icon),
        _ => null,
    };

    private static Place PlaceOf(GesturePlace place, WindowInfo? window) => place switch
    {
        GesturePlace.Tray => new Place.Tray(),
        GesturePlace.Clock => new Place.Clock(),
        GesturePlace.Window when window is not null => new Place.OfWindow(window),
        _ => new Place.AtPoint(Cursor()),
    };

    private static Point Cursor() => GetCursorPos(out var p) ? new Point(p.X, p.Y) : new Point(0, 0);

    private static PixelRect MonitorUnderCursor(AssistantRuntime runtime) =>
        (runtime.World.Current.MonitorAt(Cursor()) ?? runtime.World.Current.PrimaryMonitor)?.Bounds ?? default;

    public static PixelRect Pixel(StepRect r) => new(r.X, r.Y, r.X + r.W, r.Y + r.H);

    /// <summary>The speed a step asks for (stage 6.7; none or unknown: normal).</summary>
    public static ManoeuvreSpeed SpeedOf(string? speed) => speed?.ToLowerInvariant() switch
    {
        "showy" => ManoeuvreSpeed.Showy,
        "fast" => ManoeuvreSpeed.Fast,
        _ => ManoeuvreSpeed.Normal,
    };
}

/// <summary>
/// What the assistant gets while it performs a step's gesture: <see cref="Commit"/> sends <c>go</c> (or, for a part that
/// only prepares the action, does that part), <see cref="Committed"/> is K-Desktop's <c>result</c>, and the time left is
/// the step's.
/// </summary>
internal sealed class StepGestureContext(EngineLoop engine, double end, StepMessage step, Action commit, Task<bool> committed,
    Task<WindowInfo?>? appeared, ManoeuvreSpeed speed = ManoeuvreSpeed.Normal) : IGestureContext
{
    private int _committed;

    public void Commit()
    {
        if (Interlocked.Exchange(ref _committed, 1) == 0) commit();
    }

    public bool WasCommitted => Volatile.Read(ref _committed) == 1;

    public Task<bool> Committed => committed;

    public Task<WindowInfo?>? Appeared => appeared;

    public TimeSpan TimeLeft => TimeSpan.FromSeconds(Math.Max(0, end - engine.Now));

    public ManoeuvreSpeed Speed => speed;

    public int Step => step.Index;

    public int StepCount => step.Count;
}

using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace KDesktop.Assistant.Sdk;

/// <summary>
/// The mouse. The overlay lets every click through; to be clickable an assistant places hit areas over its body
/// (invisible windows with that shape). Their events arrive at <see cref="IAssistant.OnPointer"/>.
/// The right button is the host's: it opens the assistant menu (talk, write, extras, settings…).
/// </summary>
public interface IPointer
{
    /// <summary>A new hit area, hidden until it is placed.</summary>
    /// <param name="id">Its name, which comes in <see cref="PointerEvent.Area"/>: give each area its own (a toy, a
    /// button), or their events cannot be told apart.</param>
    IHitArea CreateHitArea(string id = "body");

    /// <summary>
    /// Whether every turn of the mouse wheel reaches it (<see cref="SystemEvent.WheelTurned"/>), anywhere on the
    /// screen. It costs a message per notch: turn it on only while it matters (the cursor near the character, standing
    /// on a window that scrolls). Hard scrolls (<see cref="SystemEvent.FastScroll"/>) arrive anyway.
    /// </summary>
    bool WatchWheel { get; set; }
}

public interface IHitArea : IDisposable
{
    string Id { get; }

    /// <summary>Moves the area (call it when the body moves; nothing happens if the shape is the same).</summary>
    void Place(HitShape shape);

    void Hide();

    /// <summary>Cursor shown over the area.</summary>
    PointerCursor Cursor { get; set; }

    /// <summary>
    /// A cursor of its own over the area (SDK 1.8): a brush, a feather… drawn by the assistant, in screen pixels (draw it
    /// at the DPI of the monitor; Windows' own cursors measure 32 pixels at 100 %). <paramref name="hotspot"/> is the
    /// pixel of the picture that points. Null goes back to <see cref="Cursor"/>.
    /// </summary>
    void SetCursorImage(BitmapSource? image, Point hotspot = default);
}

public enum PointerCursor { Arrow, Hand, Grab, Grabbing }

/// <summary>The shape of a hit area, in screen pixels.</summary>
public abstract record HitShape(PixelRect Bounds)
{
    public sealed record Rectangle(PixelRect Rect) : HitShape(Rect);

    public sealed record Ellipse(PixelRect Rect) : HitShape(Rect);

    /// <summary>Any polygon (a rotated body, for example).</summary>
    public sealed record Polygon(IReadOnlyList<Point> Points) : HitShape(BoundsOf(Points));

    private static PixelRect BoundsOf(IReadOnlyList<Point> points) => points.Count == 0
        ? default
        : new((int)Math.Floor(points.Min(p => p.X)), (int)Math.Floor(points.Min(p => p.Y)),
              (int)Math.Ceiling(points.Max(p => p.X)), (int)Math.Ceiling(points.Max(p => p.Y)));
}

public enum PointerEventKind
{
    Enter, Leave,
    Down, Up,
    /// <summary>Down and up without moving: a plain click.</summary>
    Click,
    DoubleClick,
    /// <summary>The button went down and the cursor moved: the assistant is being dragged.</summary>
    DragStart, DragMove, DragEnd,
}

/// <param name="Area">Id of the hit area (<see cref="IPointer.CreateHitArea"/>).</param>
/// <param name="Position">Cursor position in screen pixels.</param>
public sealed record PointerEvent(PointerEventKind Kind, string Area, Point Position);

/// <summary>
/// The keyboard, for assistants with a manual mode (the Lander's game). Reading the state is always allowed;
/// capturing keys makes K-Desktop swallow them so the application in front does not get them as well.
/// </summary>
public interface IKeyboard
{
    bool IsDown(Key key);

    /// <summary>Starts swallowing these keys (replaces the previous set). Their changes arrive at <see cref="KeyChanged"/>.</summary>
    void Capture(IReadOnlyCollection<Key> keys);

    /// <summary>Stops swallowing keys. Also happens on its own when the assistant is unloaded.</summary>
    void Release();

    /// <summary>A captured key went down (true) or up (false). Raised on the engine thread.</summary>
    event Action<Key, bool>? KeyChanged;
}

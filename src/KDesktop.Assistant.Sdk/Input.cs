using System.Windows;
using System.Windows.Input;

namespace KDesktop.Assistant.Sdk;

/// <summary>
/// The mouse. The overlay lets every click through; to be clickable an assistant places hit areas over its body
/// (invisible windows with that shape). Their events arrive at <see cref="IAssistant.OnPointer"/>.
/// The right button is the host's: it opens the assistant menu (talk, write, extras, settings…).
/// </summary>
public interface IPointer
{
    IHitArea CreateHitArea(string id = "body");
}

public interface IHitArea : IDisposable
{
    string Id { get; }

    /// <summary>Moves the area (call it when the body moves; nothing happens if the shape is the same).</summary>
    void Place(HitShape shape);

    void Hide();

    /// <summary>Cursor shown over the area.</summary>
    PointerCursor Cursor { get; set; }
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

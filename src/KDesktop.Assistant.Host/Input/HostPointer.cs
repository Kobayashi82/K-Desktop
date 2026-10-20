using System.Runtime.InteropServices;
using System.Windows;
using KDesktop.Assistant.Host.Rendering;
using KDesktop.Assistant.Sdk;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.Input;

/// <summary>
/// The mouse for the assistant (<see cref="IPointer"/>): hit areas are invisible windows (alpha 1/255) with the shape
/// of the body, as in Kobii; everywhere else the mouse reaches the windows below as if nothing were there. Their
/// events go to the assistant on the engine thread; the right button opens the host's menu.
/// </summary>
internal sealed class HostPointer(Action<PointerEvent> deliver, Action<string, Point> menu) : IPointer, IDisposable
{
    private readonly List<HitArea> _areas = [];

    /// <summary>Handles of the hit windows (left out of the world).</summary>
    public IEnumerable<nint> Handles => _areas.Select(a => a.Handle);

    public IHitArea CreateHitArea(string id = "body")
    {
        var area = new HitArea(this, id);
        _areas.Add(area);
        return area;
    }

    internal void Forget(HitArea area) => _areas.Remove(area);

    internal void Deliver(PointerEvent e) => deliver(e);

    internal void Menu(string area, Point at) => menu(area, at);

    /// <summary>Keeps the hit windows on top of other «always on top» windows.</summary>
    public void KeepOnTop()
    {
        foreach (var area in _areas) area.KeepOnTop();
    }

    /// <summary>Removes every hit area (an assistant unloaded without disposing its own).</summary>
    public void Dispose()
    {
        foreach (var area in _areas.ToList()) area.Dispose();
    }
}

internal sealed unsafe class HitArea : IHitArea
{
    public const string ClassName = "KDesktopAssistantHit";

    private readonly HostPointer _owner;
    private readonly NativeWindow _window;
    private HitShape? _shape;
    private bool _hover;
    private bool _pressed;
    private bool _dragging;
    private POINT _pressPoint;

    public HitArea(HostPointer owner, string id)
    {
        _owner = owner;
        Id = id;
        _window = new NativeWindow(ClassName, "K-Desktop assistant", WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE)
        {
            MessageHandler = OnMessage,
        };
        SetLayeredWindowAttributes(_window.Handle, 0, 1, LWA_ALPHA);
    }

    public string Id { get; }

    public nint Handle => _window.Handle;

    public PointerCursor Cursor { get; set; } = PointerCursor.Hand;

    public void Place(HitShape shape)
    {
        var bounds = shape.Bounds;
        if (bounds.IsEmpty)
        {
            Hide();
            return;
        }
        if (shape != _shape)
        {
            var sameRegion = _shape is { } old && SameRegion(old, shape);
            _shape = shape;
            if (!sameRegion) SetWindowRgn(_window.Handle, Region(shape), true); // the system keeps the region: no freeing
            _window.Move(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        }
        _window.Show();
    }

    public void Hide()
    {
        _window.Hide();
        if (_hover) Leave();
    }

    public void KeepOnTop()
    {
        if (_window.IsVisible) _window.BringToTop();
    }

    public void Dispose()
    {
        _window.Dispose();
        _owner.Forget(this);
    }

    /// <summary>Same shape and size, moved: the region (relative to the window) does not change.</summary>
    private static bool SameRegion(HitShape a, HitShape b) => (a, b) switch
    {
        (HitShape.Rectangle x, HitShape.Rectangle y) => x.Rect.Width == y.Rect.Width && x.Rect.Height == y.Rect.Height,
        (HitShape.Ellipse x, HitShape.Ellipse y) => x.Rect.Width == y.Rect.Width && x.Rect.Height == y.Rect.Height,
        _ => false,
    };

    private static nint Region(HitShape shape)
    {
        var b = shape.Bounds;
        switch (shape)
        {
            case HitShape.Ellipse:
                return CreateEllipticRgn(0, 0, b.Width, b.Height);
            case HitShape.Polygon polygon when polygon.Points.Count >= 3:
                var points = stackalloc POINT[polygon.Points.Count];
                for (var i = 0; i < polygon.Points.Count; i++)
                    points[i] = new POINT { X = (int)Math.Round(polygon.Points[i].X - b.Left), Y = (int)Math.Round(polygon.Points[i].Y - b.Top) };
                return CreatePolygonRgn(points, polygon.Points.Count, 2);
            default:
                return CreateRectRgn(0, 0, b.Width, b.Height);
        }
    }

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WM_MOUSEMOVE:
                if (!_hover)
                {
                    _hover = true;
                    var track = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = TME_LEAVE, hwndTrack = _window.Handle };
                    TrackMouseEvent(ref track);
                    Raise(PointerEventKind.Enter);
                }
                if (_pressed && GetCursorPos(out var now))
                {
                    if (!_dragging && (Math.Abs(now.X - _pressPoint.X) > GetSystemMetrics(SM_CXDRAG) ||
                                       Math.Abs(now.Y - _pressPoint.Y) > GetSystemMetrics(SM_CYDRAG)))
                    {
                        _dragging = true;
                        Raise(PointerEventKind.DragStart);
                    }
                    else if (_dragging) Raise(PointerEventKind.DragMove);
                }
                return 0;

            case WM_MOUSELEAVE:
                // While dragging the capture keeps the messages coming; the leave counts when it ends.
                if (!_pressed) Leave();
                return 0;

            case WM_LBUTTONDOWN:
                if (GetCursorPos(out var down)) _pressPoint = down;
                _pressed = true;
                _dragging = false;
                SetCapture(_window.Handle);
                Raise(PointerEventKind.Down);
                return 0;

            case WM_LBUTTONUP:
            {
                var wasPressed = _pressed;
                var wasDragging = _dragging;
                _pressed = _dragging = false;
                if (GetCapture() == _window.Handle) ReleaseCapture();
                if (!wasPressed) return 0;
                Raise(PointerEventKind.Up);
                Raise(wasDragging ? PointerEventKind.DragEnd : PointerEventKind.Click);
                return 0;
            }

            case WM_LBUTTONDBLCLK:
                Raise(PointerEventKind.DoubleClick);
                return 0;

            case WM_CAPTURECHANGED:
                if (_pressed)
                {
                    _pressed = false;
                    if (_dragging) Raise(PointerEventKind.DragEnd);
                    _dragging = false;
                }
                return 0;

            case WM_RBUTTONUP:
                _owner.Menu(Id, CursorPoint());
                return 0;

            case WM_SETCURSOR:
                SetCursor(LoadCursor(0, Cursor switch
                {
                    PointerCursor.Arrow => IDC_ARROW,
                    PointerCursor.Grab or PointerCursor.Grabbing => IDC_SIZEALL,
                    _ => IDC_HAND,
                }));
                return 1;
        }
        return null;
    }

    private void Leave()
    {
        if (!_hover) return;
        _hover = false;
        Raise(PointerEventKind.Leave);
    }

    private void Raise(PointerEventKind kind) => _owner.Deliver(new PointerEvent(kind, Id, CursorPoint()));

    private static Point CursorPoint() => GetCursorPos(out var p) ? new Point(p.X, p.Y) : default;
}

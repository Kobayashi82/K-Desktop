using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using KDesktop.Assistant.Host.Rendering;
using KDesktop.Assistant.Sdk;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.Input;

/// <summary>
/// The mouse for the assistant (<see cref="IPointer"/>): hit areas are invisible windows (alpha 1/255) with the shape
/// of the body, as in Kobii; everywhere else the mouse reaches the windows below as if nothing were there. Their
/// events go to the assistant on the engine thread; the right button opens the host's menu.
/// </summary>
/// <param name="write">A middle click: the box to write to it (stage 3.6).</param>
/// <param name="talk">The left button held still on it: talking to it starts, and letting go ends it (stage 5.2).</param>
/// <param name="dropped">Files dropped on one of its areas (stage 13.8): K-Desktop takes it from there.</param>
/// <param name="dropLabel">The cursor while files are dragged over one of its areas, and null when they go (SDK 1.17).</param>
internal sealed class HostPointer(Action<PointerEvent> deliver, Action<string, Point> menu, Action wheelChanged, Action<string>? write = null,
    Action<string, bool>? talk = null, Action<string, IReadOnlyList<string>, Point>? dropped = null, Action<Point?>? dropLabel = null)
    : IPointer, IDisposable
{
    internal void DropLabel(Point? at) => dropLabel?.Invoke(at);

    /// <summary>How long the button must stay pressed, without dragging, to talk.</summary>
    public const int TalkHoldMs = 450;

    internal bool CanTalk => talk is not null;

    internal void Talk(string area, bool active) => talk?.Invoke(area, active);

    /// <summary>Its name, for the label under files dragged over it («Give it to Lander»).</summary>
    public string Name { get; set; } = "";

    internal void Dropped(string area, IReadOnlyList<string> files, Point at) => dropped?.Invoke(area, files, at);

    private readonly List<HitArea> _areas = [];
    private bool _watchWheel;
    private bool _suspended;

    /// <summary>
    /// Its menu is open: the hit windows step aside (hidden, remembering where they go) so that they neither cover the menu
    /// nor take its clicks; when it closes they come back where the assistant last placed them.
    /// </summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value) return;
            _suspended = value;
            foreach (var area in _areas.ToList()) area.Suspend(value);
        }
    }

    /// <summary>Every turn of the wheel reaches it (K-Desktop sends them while some assistant asks).</summary>
    public bool WatchWheel
    {
        get => _watchWheel;
        set
        {
            if (_watchWheel == value) return;
            _watchWheel = value;
            wheelChanged();
        }
    }

    /// <summary>Handles of the hit windows (left out of the world).</summary>
    public IEnumerable<nint> Handles => _areas.Select(a => a.Handle);

    /// <summary>The id of the hit area made without one: the body (the one «picking it up» is about, stage 13.13).</summary>
    public const string BodyArea = "body";

    public IHitArea CreateHitArea(string id = BodyArea)
    {
        var area = new HitArea(this, id);
        _areas.Add(area);
        if (_suspended) area.Suspend(true);
        return area;
    }

    /// <summary>Where its body hit area is now (also while it steps aside for the menu), or null.</summary>
    public PixelRect? BodyBounds => _areas.FirstOrDefault(a => a.Id == BodyArea)?.Placed;

    internal void Forget(HitArea area) => _areas.Remove(area);

    internal void Deliver(PointerEvent e) => deliver(e);

    internal void Menu(string area, Point at) => menu(area, at);

    internal void Write(string area) => write?.Invoke(area);

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
    /// <summary>Stepped aside while the menu is open (<see cref="HostPointer.Suspended"/>).</summary>
    private bool _suspended;
    /// <summary>Where it goes when it comes back (null = hidden).</summary>
    private HitShape? _parked;
    private bool _pressed;
    private bool _dragging;
    /// <summary>The button was held long enough: the user is talking to it (no click, no drag).</summary>
    private bool _talking;
    private POINT _pressPoint;
    private const nuint TalkTimer = 1;
    private readonly FileDropTarget _drop;

    public HitArea(HostPointer owner, string id)
    {
        _owner = owner;
        Id = id;
        _window = new NativeWindow(ClassName, "K-Desktop assistant", WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE)
        {
            MessageHandler = OnMessage,
        };
        SetLayeredWindowAttributes(_window.Handle, 0, 1, LWA_ALPHA);
        _drop = new FileDropTarget(_window.Handle, OnFiles, _owner.DropLabel);
        _drop.Register();
    }

    /// <summary>Files dragged over the area, taken away or dropped on it (stage 13.8), with their picture (1.17).</summary>
    private void OnFiles(PointerEventKind kind, IReadOnlyList<string> files, Point at, BitmapSource? preview)
    {
        _owner.Deliver(new PointerEvent(kind, Id, at) { Files = files, Preview = preview });
        if (kind == PointerEventKind.FilesDropped) _owner.Dropped(Id, files, at);
    }

    public string Id { get; }

    public nint Handle => _window.Handle;

    /// <summary>Its bounds where the assistant placed it (also while stepped aside), or null if hidden.</summary>
    public PixelRect? Placed => _suspended ? _parked?.Bounds : _window.IsVisible ? _shape?.Bounds : null;

    public PointerCursor Cursor { get; set; } = PointerCursor.Hand;

    /// <summary>The assistant's own cursor (<see cref="SetCursorImage"/>), or 0.</summary>
    private nint _customCursor;

    public void SetCursorImage(BitmapSource? image, Point hotspot = default)
    {
        var previous = _customCursor;
        _customCursor = image is null ? 0 : CursorImages.Create(image, hotspot);
        // Over the area right now: the new one shows at once (not at the next mouse move).
        if (_hover) SetCursor(_customCursor != 0 ? _customCursor : LoadCursor(0, IDC_HAND));
        if (previous != 0) DestroyIcon(previous);
    }

    public void Place(HitShape shape)
    {
        if (_suspended)
        {
            _parked = shape;
            return;
        }
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
        _parked = null;
        _window.Hide();
        if (_hover) Leave();
    }

    internal void Suspend(bool on)
    {
        if (on)
        {
            _parked = _window.IsVisible ? _shape : null;
            _window.Hide();
            if (_hover) Leave();
            _suspended = true;
            return;
        }
        _suspended = false;
        if (_parked is { } shape)
        {
            _parked = null;
            Place(shape);
        }
    }

    public void KeepOnTop()
    {
        if (_window.IsVisible) _window.BringToTop();
    }

    public void Dispose()
    {
        _drop.Revoke();
        _window.Dispose();
        _owner.Forget(this);
        if (_customCursor != 0) DestroyIcon(_customCursor);
        _customCursor = 0;
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
                if (_pressed && !_talking && GetCursorPos(out var now))
                {
                    if (!_dragging && (Math.Abs(now.X - _pressPoint.X) > GetSystemMetrics(SM_CXDRAG) ||
                                       Math.Abs(now.Y - _pressPoint.Y) > GetSystemMetrics(SM_CYDRAG)))
                    {
                        _dragging = true;
                        KillTimer(_window.Handle, TalkTimer);
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
                _dragging = _talking = false;
                SetCapture(_window.Handle);
                if (_owner.CanTalk) SetTimer(_window.Handle, TalkTimer, HostPointer.TalkHoldMs, 0);
                Raise(PointerEventKind.Down);
                return 0;

            case WM_TIMER when (nuint)wParam == TalkTimer:
                // Held still long enough: it is not a click, it is talking to it.
                KillTimer(_window.Handle, TalkTimer);
                if (_pressed && !_dragging && !_talking)
                {
                    _talking = true;
                    _owner.Talk(Id, true);
                }
                return 0;

            case WM_LBUTTONUP:
            {
                var wasPressed = _pressed;
                var wasDragging = _dragging;
                var wasTalking = _talking;
                _pressed = _dragging = _talking = false;
                KillTimer(_window.Handle, TalkTimer);
                if (GetCapture() == _window.Handle) ReleaseCapture();
                if (!wasPressed) return 0;
                Raise(PointerEventKind.Up);
                if (wasTalking) _owner.Talk(Id, false);
                else Raise(wasDragging ? PointerEventKind.DragEnd : PointerEventKind.Click);
                return 0;
            }

            case WM_LBUTTONDBLCLK:
                Raise(PointerEventKind.DoubleClick);
                return 0;

            case WM_CAPTURECHANGED:
                KillTimer(_window.Handle, TalkTimer);
                if (_pressed)
                {
                    _pressed = false;
                    if (_dragging) Raise(PointerEventKind.DragEnd);
                    if (_talking) _owner.Talk(Id, false);
                    _dragging = _talking = false;
                }
                return 0;

            case WM_RBUTTONUP:
                _owner.Menu(Id, CursorPoint());
                return 0;

            case WM_MBUTTONUP:
                _owner.Write(Id);
                return 0;

            case WM_SETCURSOR when _customCursor != 0:
                SetCursor(_customCursor);
                return 1;

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

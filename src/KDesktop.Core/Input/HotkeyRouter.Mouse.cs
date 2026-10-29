using KDesktop.Core.Native;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Input;

/// <summary>
/// Esquinas activas y gestos del ratón (E4). Corners: while some command has one, the mouse movements are looked at; the
/// cursor still in a free corner (<see cref="HotCorners"/>) for <see cref="HotkeyIndex.HotCornerDelay"/> runs it, once
/// until it goes away; never with a button held nor over a full-screen game or film. Gestures: the gesture button is held
/// back when pressed; let go without moving, it is given back as a click; held still, as a press (dragging with it still
/// works); moved, it is a drawing and letting go runs its command (<see cref="MouseGestures"/>). Never in K-Desktop's own
/// windows, in the programs of «Sin gestos» or in full screen.
/// </summary>
public sealed partial class HotkeyRouter
{
    /// <summary>Moved this far (pixels) from where the button went down, it is a drawing.</summary>
    private const int DrawThreshold = 12;

    /// <summary>Held this long (milliseconds) without moving, the button goes to the program as always.</summary>
    private const int HoldStill = 500;

    // ── corners ──────────────────────────────────────────────────────────

    private ScreenRect[] _monitors = [];
    private long _monitorsAt = long.MinValue;
    private ScreenCorner? _corner;
    private (int X, int Y) _cornerAt;
    private volatile bool _cornerFired;
    private int _cornerTurn;
    private Timer? _cornerTimer;

    private void UpdateMoveForwarding() =>
        _hook.ForwardMouseMove = _recorder is not null || _index.HasCorners || _gesture is not null;

    /// <summary>The monitors, asked again every few seconds (hook thread: cheap).</summary>
    private ScreenRect[] Monitors()
    {
        var now = Environment.TickCount64;
        if (now - _monitorsAt > 2000)
        {
            _monitors = [.. ScreenCapture.Monitors()];
            _monitorsAt = now;
        }
        return _monitors;
    }

    private void OnMove(MouseInput e)
    {
        if (_gesture is { } stroke) GestureMove(stroke, e.X, e.Y);
        if (_index.HasCorners) CornerMove(e.X, e.Y);
    }

    private void CornerMove(int x, int y)
    {
        if (_cornerFired)
        {
            if (Math.Abs(x - _cornerAt.X) <= HotCorners.Rearm && Math.Abs(y - _cornerAt.Y) <= HotCorners.Rearm) return;
            _cornerFired = false;
        }
        var corner = _chord.Count == 0 && _capture is null && _gesture is null ? HotCorners.At(x, y, Monitors()) : null;
        if (corner == _corner) return;
        _corner = corner;
        var turn = Interlocked.Increment(ref _cornerTurn);
        _cornerTimer?.Dispose();
        _cornerTimer = null;
        if (corner is not { } c) return;
        _cornerAt = (x, y);
        _cornerTimer = new Timer(_ => CornerReached(c, turn), null, _index.HotCornerDelay, Timeout.Infinite);
    }

    /// <summary>The cursor stayed in the corner: its command, if it is still there and nothing is in the way (a worker thread).</summary>
    private void CornerReached(ScreenCorner corner, int turn)
    {
        try
        {
            if (turn != Volatile.Read(ref _cornerTurn) || _capture is not null || _recorder is not null) return;
            if (!Win32.GetCursorPos(out var p) || HotCorners.At(p.X, p.Y, _monitors) != corner) return;
            if (Win32.IsKeyDown(KeyNames.VK_LBUTTON) || Win32.IsKeyDown(KeyNames.VK_RBUTTON) || Win32.IsKeyDown(KeyNames.VK_MBUTTON)) return;
            _cornerFired = true;
            var front = ForegroundWindow.Capture();
            if (front.ProcessId != Environment.ProcessId && FullScreenAt(front.Handle, p.X, p.Y)) return;
            var hotkey = HotCorners.Hotkey(corner);
            var commands = _index.Resolve(hotkey, front.ProcessName, out _);
            if (commands.Count > 0) Triggered?.Invoke(new HotkeyTrigger(hotkey, commands, false, front));
        }
        catch (Exception ex)
        {
            Log.Error("Error en una esquina activa", ex);
        }
    }

    /// <summary>A full-screen game or presentation, or <paramref name="window"/> covering the monitor of the point.</summary>
    private bool FullScreenAt(IntPtr window, int x, int y)
    {
        if (SHQueryUserNotificationState(out var state) == 0 && state is 3 /* D3D full screen */ or 4 /* presentation */) return true;
        if (window == IntPtr.Zero || !GetWindowRect(Win32.GetAncestor(window, Win32.GA_ROOT), out var r)) return false;
        foreach (var m in Monitors())
        {
            if (x < m.Left || x >= m.Right || y < m.Top || y >= m.Bottom) continue;
            return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom && !IsDesktop(window);
        }
        return false;
    }

    // ── gestures ─────────────────────────────────────────────────────────

    /// <summary>A press of the gesture button being watched. Its fields are guarded by <see cref="_gestureLock"/>.</summary>
    private sealed class GestureStroke(MouseButton button, int x, int y, ForegroundWindow front)
    {
        public MouseButton Button { get; } = button;
        public int StartX { get; } = x;
        public int StartY { get; } = y;
        public ForegroundWindow Front { get; } = front;
        public List<GesturePoint> Points { get; } = [new(x, y)];

        /// <summary>Moved past <see cref="DrawThreshold"/>: a drawing.</summary>
        public bool Drawing { get; set; }

        /// <summary>Given back to the program (held still): the rest of the press goes as always.</summary>
        public bool GivenBack { get; set; }

        /// <summary>Given back because another button was pressed: its release counts for the combinations («Mouse 2 + Mouse 1»).</summary>
        public bool InChord { get; set; }

        /// <summary>Cancelled while drawing (Esc, another button): its release does nothing.</summary>
        public bool Cancelled { get; set; }

        public Timer? HoldTimer { get; set; }
    }

    private readonly Lock _gestureLock = new();
    private volatile GestureStroke? _gesture;

    /// <summary>A press became a drawing: the stroke can be shown (<see cref="GestureTrail"/>).</summary>
    public event Action? GestureDrawing;

    /// <summary>The drawing ended: the gesture that ran (<c>Gesture C</c>), or null when none (not one, cancelled).</summary>
    public event Action<string?>? GestureFinished;

    /// <summary>The stroke being drawn and the window in front when it began; null when none is being drawn.</summary>
    public (GesturePoint[] Points, ForegroundWindow Front)? GestureTrail()
    {
        lock (_gestureLock)
            return _gesture is { Drawing: true, Cancelled: false } g ? ([.. g.Points], g.Front) : null;
    }

    /// <summary>The gesture hotkey a stroke is (<c>Gesture C</c>) and the commands it would run in <paramref name="front"/>.</summary>
    public (string? Hotkey, IReadOnlyList<Model.Command> Commands) GestureOf(IReadOnlyList<GesturePoint> points, ForegroundWindow front)
    {
        var index = _index;
        if (MouseGestures.Recognize(points, index.CustomGestures, out _) is not { } id) return (null, []);
        var hotkey = Hotkey.Gesture(id);
        return (hotkey, index.Resolve(hotkey, front.ProcessName, out _));
    }

    /// <summary>The gesture button went down: held back when a gesture may start here (hook thread).</summary>
    private bool GestureDown(MouseInput e)
    {
        var index = _index;
        if (!index.HasGestures || e.Button != index.GestureButton || _gesture is not null || _capture is not null || _windowPicker is not null) return false;
        if (_chord.Count > 0 || CurrentModifiers() != Modifiers.None || index.TakesAlone(ButtonName(e.Button))) return false;
        var hwnd = Win32.WindowFromPoint(new Win32.POINT { X = e.X, Y = e.Y });
        var under = ForegroundWindow.FromHandle(hwnd == IntPtr.Zero ? IntPtr.Zero : Win32.GetAncestor(hwnd, Win32.GA_ROOT));
        if (under.ProcessId == Environment.ProcessId || index.GesturesOffIn(under.ProcessName) || FullScreenAt(under.Handle, e.X, e.Y)) return false;
        var stroke = new GestureStroke(e.Button, e.X, e.Y, ForegroundWindow.Capture());
        stroke.HoldTimer = new Timer(_ => HeldStill(stroke), null, HoldStill, Timeout.Infinite);
        _gesture = stroke;
        UpdateMoveForwarding();
        return true;
    }

    private void GestureMove(GestureStroke stroke, int x, int y)
    {
        var started = false;
        lock (_gestureLock)
        {
            if (stroke.GivenBack || stroke.Cancelled) return;
            stroke.Points.Add(new GesturePoint(x, y));
            if (!stroke.Drawing && Math.Abs(x - stroke.StartX) + Math.Abs(y - stroke.StartY) > DrawThreshold)
                started = stroke.Drawing = true;
        }
        if (started) Post(() => GestureDrawing?.Invoke());
    }

    /// <summary>Held without moving: the press goes to the program, so dragging with the button still works (a worker thread).</summary>
    private void HeldStill(GestureStroke stroke)
    {
        lock (_gestureLock)
        {
            if (!ReferenceEquals(_gesture, stroke) || stroke.Drawing || stroke.GivenBack || stroke.Cancelled) return;
            stroke.GivenBack = true;
        }
        InputSimulator.MouseDown(stroke.Button);
    }

    /// <summary>
    /// Another button went down while the gesture button is held (hook thread). Before a drawing, the gesture button is
    /// given back first, so the program and the combinations («Mouse 2 + Mouse 1») see both, in order; while drawing, the
    /// drawing is cancelled. Null: the event goes on as always.
    /// </summary>
    private bool? GestureOtherDown(GestureStroke stroke, MouseInput e)
    {
        lock (_gestureLock)
        {
            if (stroke.GivenBack || stroke.Cancelled) return null;
            if (stroke.Drawing)
            {
                stroke.Cancelled = true;
                Post(() => GestureFinished?.Invoke(null));
                return null;
            }
            stroke.GivenBack = stroke.InChord = true;
        }
        var held = ButtonName(stroke.Button);
        _chord.Remove(held);
        _chord.Add(held);
        var block = OnMouseDown(e.Button);
        MouseButton first = stroke.Button, second = e.Button;
        Post(() =>
        {
            InputSimulator.MouseDown(first);
            if (!block) InputSimulator.MouseDown(second);
        });
        return true;
    }

    /// <summary>The gesture button went up (hook thread). Null: the event goes on as always.</summary>
    private bool? GestureUp(GestureStroke stroke)
    {
        GesturePoint[]? points = null;
        bool click;
        lock (_gestureLock)
        {
            _gesture = null;
            stroke.HoldTimer?.Dispose();
            UpdateMoveForwarding();
            if (stroke.InChord) return null;
            if (stroke.GivenBack) return false;
            if (stroke.Cancelled) return true;
            click = !stroke.Drawing;
            if (!click) points = [.. stroke.Points];
        }
        var button = stroke.Button;
        if (click) Post(() => InputSimulator.MouseClick(button));
        else Post(() => FinishGesture(points!, stroke.Front));
        return true;
    }

    /// <summary>Esc while drawing cancels the drawing (hook thread).</summary>
    private bool CancelGesture()
    {
        lock (_gestureLock)
        {
            if (_gesture is not { Drawing: true, Cancelled: false } stroke) return false;
            stroke.Cancelled = true;
        }
        Post(() => GestureFinished?.Invoke(null));
        return true;
    }

    private void FinishGesture(GesturePoint[] points, ForegroundWindow front)
    {
        var (hotkey, commands) = GestureOf(points, front);
        GestureFinished?.Invoke(commands.Count > 0 ? hotkey : null);
        if (hotkey is null) return;
        Log.Info($"Gesto: {hotkey}" + (commands.Count == 0 ? " (sin comando aquí)" : string.Empty));
        if (commands.Count > 0) Triggered?.Invoke(new HotkeyTrigger(hotkey, commands, false, front));
    }
}

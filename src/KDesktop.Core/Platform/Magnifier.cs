using System.Diagnostics;
using System.Runtime.InteropServices;
using KDesktop.Core.Model;

namespace KDesktop.Core.Platform;

/// <summary>
/// «Lupa»: a lens that follows the mouse showing the area around it enlarged (or, in the other mode, the whole
/// monitor it is on enlarged around it). Either way the zoom is anchored at the pointer: what is under it stays under it, so a
/// click lands on what is seen there. The lens lets clicks through. On a thread of its own (the magnification API and
/// the lens window belong to the thread that creates them).
/// </summary>
public sealed class Magnifier : IDisposable
{
    public const double MinZoom = 1.25, MaxZoom = 8;

    private readonly Lock _lock = new();
    private Thread? _thread;
    private volatile bool _stop;
    private int _frames;
    private long _startedAt;
    private double _zoom = 2;
    private ZoomMode _mode = ZoomMode.Lens;
    private ZoomLensSize _size = ZoomLensSize.Medium;

    /// <summary>The zoom was turned on or off, or changed (the web app shows it).</summary>
    public event Action? Changed;

    public bool IsOn => _thread is not null;

    public double Zoom => _zoom;

    /// <summary>How it looks (Settings › Capturas y lupa); applies from the next time it turns on.</summary>
    public void Configure(ZoomMode mode, ZoomLensSize size)
    {
        var restart = IsOn && (mode != _mode || size != _size);
        _mode = mode;
        _size = size;
        if (!restart) return;
        var zoom = _zoom;
        Off(notify: false);
        On(zoom);
    }

    /// <summary>Turns it on at <paramref name="zoom"/> (or changes the zoom if it is on).</summary>
    public void On(double zoom)
    {
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        lock (_lock)
        {
            if (_thread is null)
            {
                _stop = false;
                var mode = _mode;
                _thread = new Thread(() => Run(mode)) { IsBackground = true, Name = "Lupa" };
                _frames = 0;
                _startedAt = Environment.TickCount64;
                _thread.Start();
                Log.Info($"Lupa: ×{_zoom:0.##} ({mode})");
            }
        }
        Changed?.Invoke();
    }

    public void Off() => Off(notify: true);

    private void Off(bool notify)
    {
        Thread? thread;
        lock (_lock)
        {
            thread = _thread;
            _thread = null;
            _stop = true;
        }
        if (thread is null) return;
        thread.Join(TimeSpan.FromSeconds(2));
        var seconds = (Environment.TickCount64 - _startedAt) / 1000.0;
        Log.Info($"Lupa: apagada ({(seconds > 0 ? _frames / seconds : 0):0} fotogramas/s)");
        if (notify) Changed?.Invoke();
    }

    /// <summary>On or off; returns whether it is on now.</summary>
    public bool Toggle(double zoom)
    {
        if (IsOn) Off();
        else On(zoom);
        return IsOn;
    }

    /// <summary>One step more or less zoom (×1.25 each), turning it on if needed.</summary>
    public void Step(int direction)
    {
        var next = direction > 0 ? _zoom * 1.25 : _zoom / 1.25;
        if (!IsOn && direction > 0) On(Math.Max(next, MinZoom));
        else if (next < MinZoom - 0.01) Off();
        else On(next);
    }

    /// <summary>
    /// Full screen: where the magnified view starts so that <paramref name="cursorX"/>, <paramref name="cursorY"/>
    /// stays where it is (a point p shows at (p − offset) × zoom).
    /// </summary>
    public static (double X, double Y) Offset(int cursorX, int cursorY, double zoom) =>
        (cursorX * (1 - 1 / zoom), cursorY * (1 - 1 / zoom));

    /// <summary>The lens's size in physical pixels (the round one is a circle).</summary>
    public static (int Width, int Height) LensSize(ZoomLensSize size, bool round = false) => (size, round) switch
    {
        (ZoomLensSize.Small, false) => (480, 300),
        (ZoomLensSize.Large, false) => (1120, 700),
        (_, false) => (800, 500),
        (ZoomLensSize.Small, true) => (360, 360),
        (ZoomLensSize.Large, true) => (800, 800),
        _ => (560, 560),
    };

    /// <summary>
    /// What the lens shows: the area around the cursor, <paramref name="zoom"/> times smaller than the lens, so the
    /// cursor (at the lens's centre) is over the same point it is over without the lens.
    /// </summary>
    public static ScreenRect LensSource(int cursorX, int cursorY, int lensWidth, int lensHeight, double zoom)
    {
        var w = (int)Math.Round(lensWidth / zoom);
        var h = (int)Math.Round(lensHeight / zoom);
        return new ScreenRect(cursorX - w / 2, cursorY - h / 2, w, h);
    }

    private void Run(ZoomMode mode)
    {
        if (!MagInitialize())
        {
            Log.Warn("Lupa: Windows no permitió la ampliación");
            lock (_lock) _thread = null;
            Changed?.Invoke();
            return;
        }
        try
        {
            if (mode == ZoomMode.FullScreen) RunFullScreen();
            else RunLens(round: mode == ZoomMode.Circle);
        }
        catch (Exception ex)
        {
            Log.Error("Lupa", ex);
            lock (_lock) _thread = null;
            Changed?.Invoke();
        }
        finally
        {
            if (mode == ZoomMode.FullScreen) MagSetFullscreenTransform(1f, 0, 0);
            MagUninitialize();
        }
    }

    /// <summary>
    /// Full screen: on the main monitor Windows' own full-screen zoom; on the others (it can't do them) a lens as big
    /// as the monitor, anchored at the pointer the same way.
    /// </summary>
    private void RunFullScreen()
    {
        (int X, int Y) last = (int.MinValue, int.MinValue);
        var lastZoom = 0.0;
        var transformed = false;
        var cover = new Lens();
        var coverOn = default(ScreenRect);
        try
        {
            while (!_stop)
            {
                var started = Stopwatch.GetTimestamp();
                Pump();
                GetCursorPos(out var p);
                var zoom = _zoom;
                var monitor = ScreenCapture.MonitorAt(p.X, p.Y);
                if (monitor.Left == 0 && monitor.Top == 0)
                {
                    if (cover.Visible) cover.Hide();
                    if ((p.X, p.Y) != last || Math.Abs(zoom - lastZoom) > 0.001 || !transformed)
                    {
                        var (x, y) = Offset(p.X, p.Y, zoom);
                        MagSetFullscreenTransform((float)zoom, (int)Math.Round(x), (int)Math.Round(y));
                        transformed = true;
                    }
                }
                else
                {
                    if (transformed)
                    {
                        MagSetFullscreenTransform(1f, 0, 0);
                        transformed = false;
                    }
                    if (!cover.Created || coverOn != monitor)
                    {
                        cover.Destroy();
                        cover.Create(monitor.Width, monitor.Height, round: false, border: 0);
                        coverOn = monitor;
                    }
                    var (x, y) = Offset(p.X - monitor.Left, p.Y - monitor.Top, zoom);
                    var source = new ScreenRect(monitor.Left + (int)Math.Round(x), monitor.Top + (int)Math.Round(y),
                        (int)Math.Round(monitor.Width / zoom), (int)Math.Round(monitor.Height / zoom));
                    cover.Show(monitor.Left, monitor.Top, source, zoom);
                }
                last = (p.X, p.Y);
                lastZoom = zoom;
                NextFrame(started);
            }
        }
        finally
        {
            cover.Destroy();
        }
    }

    private void RunLens(bool round)
    {
        var (width, height) = LensSize(_size, round);
        var lens = new Lens();
        lens.Create(width, height, round, Lens.Border);
        try
        {
            while (!_stop)
            {
                var started = Stopwatch.GetTimestamp();
                Pump();
                GetCursorPos(out var p);
                var source = LensSource(p.X, p.Y, width - 2 * Lens.Border, height - 2 * Lens.Border, _zoom);
                lens.Show(p.X - width / 2, p.Y - height / 2, source, _zoom);
                NextFrame(started);
            }
        }
        finally
        {
            lens.Destroy();
        }
    }

    private static void Pump()
    {
        while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1 /* PM_REMOVE */))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    /// <summary>
    /// Waits for the screen's next frame, so the lens moves in step with it (a fixed sleep drifts against the refresh
    /// and stutters). Setting the magnifier's source already waits for one (measured: 10 ms at 99 Hz), so only a turn
    /// that didn't (the main monitor's full-screen zoom, the pointer still) waits here. Without composition, about
    /// 100 times a second.
    /// </summary>
    private void NextFrame(long turnStarted)
    {
        _frames++;
        if (Stopwatch.GetElapsedTime(turnStarted).TotalMilliseconds > 3) return;
        if (DwmFlush() != 0) Thread.Sleep(10);
    }

    // ── the lens window ──────────────────────────────────────────────────

    /// <summary>
    /// A click-through window with the magnifier control inside: the lens around the mouse (with a gold border), or a
    /// whole monitor for full screen on the others.
    /// </summary>
    private sealed class Lens
    {
        public const int Border = 3;
        private static readonly WndProc HostProc = (h, m, w, l) => DefWindowProc(h, m, w, l); // kept alive
        private static bool _classRegistered;

        private IntPtr _host, _control;
        private (int X, int Y) _at = (int.MinValue, int.MinValue);
        private double _zoom;

        public bool Created => _host != IntPtr.Zero;

        public bool Visible { get; private set; }

        public void Create(int width, int height, bool round, int border)
        {
            var instance = GetModuleHandle(null);
            if (!_classRegistered)
            {
                var wc = new WNDCLASSEX
                {
                    cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(HostProc),
                    hInstance = instance,
                    // The border: K-Desktop's gold.
                    hbrBackground = CreateSolidBrush(0x15A8E0),
                    lpszClassName = "KDesktopLens",
                };
                if (RegisterClassEx(ref wc) == 0) throw new InvalidOperationException("RegisterClassEx " + Marshal.GetLastWin32Error());
                _classRegistered = true;
            }

            // Layered + transparent: the mouse goes through it to what is below.
            const uint exStyle = WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            _host = CreateWindowEx(exStyle, "KDesktopLens", "K-Desktop Lupa", WS_POPUP | WS_CLIPCHILDREN, 0, 0, width, height,
                IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (_host == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx " + Marshal.GetLastWin32Error());
            SetLayeredWindowAttributes(_host, 0, 255, LWA_ALPHA);
            var (w, h) = (width - 2 * border, height - 2 * border);
            _control = CreateWindowEx(0, "Magnifier", "K-Desktop", WS_CHILD | WS_VISIBLE, border, border, w, h, _host, IntPtr.Zero, instance, IntPtr.Zero);
            if (_control == IntPtr.Zero) throw new InvalidOperationException("Magnifier " + Marshal.GetLastWin32Error());
            if (border > 0)
            {
                SetWindowRgn(_host, round ? CreateEllipticRgn(0, 0, width + 1, height + 1) : CreateRoundRectRgn(0, 0, width + 1, height + 1, 28, 28), true);
                SetWindowRgn(_control, round ? CreateEllipticRgn(0, 0, w + 1, h + 1) : CreateRoundRectRgn(0, 0, w + 1, h + 1, 22, 22), true);
            }
            // The lens must not show itself (a tunnel of lenses).
            var self = _host;
            MagSetWindowFilterList(_control, 0 /* MW_FILTERMODE_EXCLUDE */, 1, ref self);
            _at = (int.MinValue, int.MinValue);
            _zoom = 0;
        }

        /// <summary>Puts the window's corner at <paramref name="x"/>, <paramref name="y"/> showing <paramref name="source"/>.</summary>
        public void Show(int x, int y, ScreenRect source, double zoom)
        {
            if (Math.Abs(zoom - _zoom) > 0.001)
            {
                var transform = new MAGTRANSFORM { m00 = (float)zoom, m11 = (float)zoom, m22 = 1 };
                MagSetWindowTransform(_control, ref transform);
                _zoom = zoom;
            }
            if ((x, y) != _at || !Visible)
            {
                SetWindowPos(_host, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                _at = (x, y);
                Visible = true;
            }
            // Moved first, so both land in the same frame. Setting the source redraws it (and waits for the frame):
            // invalidating it too made it wait for two more, 33 fps instead of the monitor's.
            MagSetWindowSource(_control, new RECT { Left = source.Left, Top = source.Top, Right = source.Right, Bottom = source.Bottom });
        }

        public void Hide()
        {
            ShowWindow(_host, 0 /* SW_HIDE */);
            Visible = false;
        }

        public void Destroy()
        {
            if (_host != IntPtr.Zero) DestroyWindow(_host);
            _host = _control = IntPtr.Zero;
            Visible = false;
        }
    }

    public void Dispose() => Off();

    // ── P/Invoke ─────────────────────────────────────────────────────────

    private const uint WS_POPUP = 0x80000000, WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000;
    private const uint WS_EX_TOPMOST = 0x8, WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const uint LWA_ALPHA = 0x2;
    private const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MAGTRANSFORM
    {
        public float m00, m01, m02, m10, m11, m12, m20, m21, m22;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("magnification.dll")] private static extern bool MagInitialize();
    [DllImport("magnification.dll")] private static extern bool MagUninitialize();
    [DllImport("magnification.dll")] private static extern bool MagSetFullscreenTransform(float magLevel, int xOffset, int yOffset);
    [DllImport("magnification.dll")] private static extern bool MagSetWindowTransform(IntPtr hwnd, ref MAGTRANSFORM transform);
    [DllImport("magnification.dll")] private static extern bool MagSetWindowSource(IntPtr hwnd, RECT rect);
    [DllImport("magnification.dll")] private static extern bool MagSetWindowFilterList(IntPtr hwnd, int mode, int count, ref IntPtr list);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateEllipticRgn(int x1, int y1, int x2, int y2);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
}

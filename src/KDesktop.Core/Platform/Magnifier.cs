using System.Runtime.InteropServices;
using KDesktop.Core.Model;

namespace KDesktop.Core.Platform;

/// <summary>
/// «Lupa»: a lens that follows the mouse showing the area around it enlarged (or, in the other mode, the whole main
/// monitor enlarged around it). Either way the zoom is anchored at the pointer: what is under it stays under it, so a
/// click lands on what is seen there. The lens lets clicks through. On a thread of its own (the magnification API and
/// the lens window belong to the thread that creates them).
/// </summary>
public sealed class Magnifier : IDisposable
{
    public const double MinZoom = 1.25, MaxZoom = 8;

    private readonly Lock _lock = new();
    private Thread? _thread;
    private volatile bool _stop;
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
        Log.Info("Lupa: apagada");
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

    private void RunFullScreen()
    {
        var width = GetSystemMetrics(0);
        var height = GetSystemMetrics(1);
        (int X, int Y) last = (int.MinValue, int.MinValue);
        var lastZoom = 0.0;
        while (!_stop)
        {
            GetCursorPos(out var p);
            var zoom = _zoom;
            if ((p.X, p.Y) != last || Math.Abs(zoom - lastZoom) > 0.001)
            {
                // The full-screen zoom is only the main monitor's.
                if (p.X >= 0 && p.Y >= 0 && p.X < width && p.Y < height)
                {
                    var (x, y) = Offset(p.X, p.Y, zoom);
                    MagSetFullscreenTransform((float)zoom, (int)Math.Round(x), (int)Math.Round(y));
                }
                else MagSetFullscreenTransform(1f, 0, 0);
                last = (p.X, p.Y);
                lastZoom = zoom;
            }
            Thread.Sleep(10);
        }
    }

    // ── the lens ─────────────────────────────────────────────────────────

    private const int Border = 3;
    private static readonly WndProc HostProc = (h, m, w, l) => DefWindowProc(h, m, w, l); // kept alive
    private static bool _classRegistered;

    private void RunLens(bool round)
    {
        var (width, height) = LensSize(_size, round);
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
        var host = CreateWindowEx(exStyle, "KDesktopLens", "K-Desktop Lupa", WS_POPUP | WS_CLIPCHILDREN, 0, 0, width, height,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (host == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx " + Marshal.GetLastWin32Error());
        try
        {
            SetLayeredWindowAttributes(host, 0, 255, LWA_ALPHA);
            SetWindowRgn(host, round ? CreateEllipticRgn(0, 0, width + 1, height + 1) : CreateRoundRectRgn(0, 0, width + 1, height + 1, 28, 28), true);
            var lens = CreateWindowEx(0, "Magnifier", "K-Desktop", WS_CHILD | WS_VISIBLE, Border, Border, width - 2 * Border, height - 2 * Border,
                host, IntPtr.Zero, instance, IntPtr.Zero);
            if (lens == IntPtr.Zero) throw new InvalidOperationException("Magnifier " + Marshal.GetLastWin32Error());
            SetWindowRgn(lens, round
                ? CreateEllipticRgn(0, 0, width - 2 * Border + 1, height - 2 * Border + 1)
                : CreateRoundRectRgn(0, 0, width - 2 * Border + 1, height - 2 * Border + 1, 22, 22), true);
            // The lens must not show itself (a tunnel of lenses).
            var self = host;
            MagSetWindowFilterList(lens, 0 /* MW_FILTERMODE_EXCLUDE */, 1, ref self);

            (int X, int Y) last = (int.MinValue, int.MinValue);
            var shown = false;
            while (!_stop)
            {
                while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1 /* PM_REMOVE */))
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
                GetCursorPos(out var p);
                var zoom = _zoom;
                var transform = new MAGTRANSFORM { m00 = (float)zoom, m11 = (float)zoom, m22 = 1 };
                MagSetWindowTransform(lens, ref transform);
                var inner = (W: width - 2 * Border, H: height - 2 * Border);
                var source = LensSource(p.X, p.Y, inner.W, inner.H, zoom);
                MagSetWindowSource(lens, new RECT { Left = source.Left, Top = source.Top, Right = source.Right, Bottom = source.Bottom });
                if ((p.X, p.Y) != last || !shown)
                {
                    SetWindowPos(host, HWND_TOPMOST, p.X - width / 2, p.Y - height / 2, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    last = (p.X, p.Y);
                    shown = true;
                }
                InvalidateRect(lens, IntPtr.Zero, true);
                Thread.Sleep(12);
            }
        }
        finally
        {
            DestroyWindow(host);
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
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateEllipticRgn(int x1, int y1, int x2, int y2);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
}

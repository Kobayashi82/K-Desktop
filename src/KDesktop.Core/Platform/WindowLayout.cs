using System.Runtime.InteropServices;
using KDesktop.Core.Model;
using KDesktop.Core.Native;

namespace KDesktop.Core.Platform;

/// <summary>A rectangle in screen pixels.</summary>
public readonly record struct ScreenRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public int CenterX => Left + Width / 2;
    public int CenterY => Top + Height / 2;
}

/// <summary>«Ventana»: maximise, place, centre, move to another monitor, transparency.</summary>
public static class WindowLayout
{
    /// <summary>Where a window goes in a work area for <paramref name="snap"/> (custom: x, y, w, h in %).</summary>
    public static ScreenRect SnapRect(ScreenRect area, WindowSnap snap, int x = 0, int y = 0, int width = 50, int height = 100)
    {
        int W(double f) => (int)Math.Round(area.Width * f);
        int H(double f) => (int)Math.Round(area.Height * f);
        ScreenRect Part(double left, double top, double w, double h) =>
            new(area.Left + W(left), area.Top + H(top), W(left + w) - W(left), H(top + h) - H(top));
        return snap switch
        {
            WindowSnap.LeftHalf => Part(0, 0, 0.5, 1),
            WindowSnap.RightHalf => Part(0.5, 0, 0.5, 1),
            WindowSnap.TopHalf => Part(0, 0, 1, 0.5),
            WindowSnap.BottomHalf => Part(0, 0.5, 1, 0.5),
            WindowSnap.TopLeft => Part(0, 0, 0.5, 0.5),
            WindowSnap.TopRight => Part(0.5, 0, 0.5, 0.5),
            WindowSnap.BottomLeft => Part(0, 0.5, 0.5, 0.5),
            WindowSnap.BottomRight => Part(0.5, 0.5, 0.5, 0.5),
            WindowSnap.LeftThird => Part(0, 0, 1 / 3.0, 1),
            WindowSnap.CenterThird => Part(1 / 3.0, 0, 1 / 3.0, 1),
            WindowSnap.RightThird => Part(2 / 3.0, 0, 1 / 3.0, 1),
            WindowSnap.LeftTwoThirds => Part(0, 0, 2 / 3.0, 1),
            WindowSnap.RightTwoThirds => Part(1 / 3.0, 0, 2 / 3.0, 1),
            _ => Custom(),
        };

        ScreenRect Custom()
        {
            double Clamp(int v) => Math.Clamp(v, 0, 100) / 100.0;
            var left = Clamp(x);
            var top = Clamp(y);
            var w = Math.Max(0.05, Math.Min(Clamp(width), 1 - left));
            var h = Math.Max(0.05, Math.Min(Clamp(height), 1 - top));
            return Part(left, top, w, h);
        }
    }

    /// <summary>The same place and size, in proportion, in another work area (moving to another monitor).</summary>
    public static ScreenRect MapRect(ScreenRect window, ScreenRect from, ScreenRect to)
    {
        double sx = (double)to.Width / Math.Max(1, from.Width), sy = (double)to.Height / Math.Max(1, from.Height);
        var width = Math.Min(to.Width, (int)Math.Round(window.Width * sx));
        var height = Math.Min(to.Height, (int)Math.Round(window.Height * sy));
        var left = to.Left + (int)Math.Round((window.Left - from.Left) * sx);
        var top = to.Top + (int)Math.Round((window.Top - from.Top) * sy);
        left = Math.Clamp(left, to.Left, to.Right - width);
        top = Math.Clamp(top, to.Top, to.Bottom - height);
        return new ScreenRect(left, top, width, height);
    }

    /// <summary>A window of the given size centred in an area.</summary>
    public static ScreenRect Centered(ScreenRect window, ScreenRect area)
    {
        var width = Math.Min(window.Width, area.Width);
        var height = Math.Min(window.Height, area.Height);
        return new ScreenRect(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
    }

    // ── on real windows ──────────────────────────────────────────────────

    /// <summary>The window to act on: the one in front (its root) or the main window of a program.</summary>
    public static IntPtr Find(WindowTarget target, string processName, IntPtr foreground)
    {
        if (target == WindowTarget.Active) return foreground;
        var processes = WindowTools.FindProcesses(processName);
        try
        {
            var windows = WindowTools.TopLevelWindows(processes.Select(p => (uint)p.Id).ToHashSet());
            return windows.FirstOrDefault(h => !Win32.IsIconic(h)) is var visible && visible != IntPtr.Zero ? visible : windows.FirstOrDefault();
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    public static bool IsMaximized(IntPtr hwnd) => IsZoomed(hwnd);

    public static void Show(IntPtr hwnd, int command) => Win32.ShowWindow(hwnd, command);

    /// <summary>The work areas of the monitors (without the taskbar), left to right and top to bottom.</summary>
    public static IReadOnlyList<ScreenRect> WorkAreas()
    {
        var areas = new List<(ScreenRect Area, bool Primary)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref info)) areas.Add((ToRect(info.rcWork), (info.dwFlags & 1) != 0));
            return true;
        }, IntPtr.Zero);
        return areas.OrderBy(a => a.Area.Left).ThenBy(a => a.Area.Top).Select(a => a.Area).ToList();
    }

    /// <summary>The work area of the monitor the window is on.</summary>
    public static ScreenRect WorkAreaOf(IntPtr hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        return GetMonitorInfo(monitor, ref info) ? ToRect(info.rcWork) : default;
    }

    /// <summary>The visible bounds of a window (without the invisible resize borders of Windows 10/11).</summary>
    public static ScreenRect VisibleBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out var frame, Marshal.SizeOf<RECT>()) == 0) return ToRect(frame);
        GetWindowRect(hwnd, out var rect);
        return ToRect(rect);
    }

    /// <summary>Puts the visible part of the window exactly on <paramref name="target"/> (restoring it if maximised).</summary>
    public static void MoveTo(IntPtr hwnd, ScreenRect target)
    {
        if (IsZoomed(hwnd) || Win32.IsIconic(hwnd)) Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
        GetWindowRect(hwnd, out var outer);
        var visible = VisibleBounds(hwnd);
        // The invisible borders around the visible frame.
        var left = visible.Left - outer.Left;
        var top = visible.Top - outer.Top;
        var right = outer.Right - visible.Right;
        var bottom = outer.Bottom - visible.Bottom;
        Win32.SetWindowPos(hwnd, IntPtr.Zero, target.Left - left, target.Top - top, target.Width + left + right, target.Height + top + bottom,
            Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    /// <summary>Moves the window to another monitor keeping its place and size in proportion (maximised stays maximised).</summary>
    public static bool MoveToMonitor(IntPtr hwnd, MonitorChoice choice, int number)
    {
        var areas = WorkAreas();
        if (areas.Count < 2 && choice != MonitorChoice.Number) return false;
        var from = WorkAreaOf(hwnd);
        var index = areas.ToList().FindIndex(a => a == from);
        if (index < 0) index = 0;
        var target = choice switch
        {
            MonitorChoice.Next => (index + 1) % areas.Count,
            MonitorChoice.Previous => (index - 1 + areas.Count) % areas.Count,
            _ => Math.Clamp(number, 1, areas.Count) - 1,
        };
        if (target == index) return false;
        var maximized = IsZoomed(hwnd);
        if (maximized) Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
        MoveTo(hwnd, MapRect(VisibleBounds(hwnd), from, areas[target]));
        if (maximized) Win32.ShowWindow(hwnd, Win32.SW_SHOWMAXIMIZED);
        return true;
    }

    /// <summary>
    /// Makes the window <paramref name="percent"/> % opaque; if it already is, opaque again. Returns the opacity it has
    /// afterwards.
    /// </summary>
    public static int ToggleOpacity(IntPtr hwnd, int percent)
    {
        percent = Math.Clamp(percent, 10, 100);
        var style = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        var current = 100;
        if ((style & Win32.WS_EX_LAYERED) != 0 && GetLayeredWindowAttributes(hwnd, out _, out var alpha, out var flags) && (flags & LWA_ALPHA) != 0)
            current = (int)Math.Round(alpha * 100 / 255.0);
        var next = current == percent || current < 100 ? 100 : percent;
        if (next == 100)
        {
            if ((style & Win32.WS_EX_LAYERED) != 0)
            {
                SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
                Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, (IntPtr)(style & ~Win32.WS_EX_LAYERED));
            }
            return 100;
        }
        if ((style & Win32.WS_EX_LAYERED) == 0) Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, (IntPtr)(style | Win32.WS_EX_LAYERED));
        SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(next * 255 / 100.0), LWA_ALPHA);
        return next;
    }

    private static ScreenRect ToRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    // ── P/Invoke ─────────────────────────────────────────────────────────

    private const uint LWA_ALPHA = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

    [DllImport("user32.dll")]
    private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint key, out byte alpha, out uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace KDesktop.Native;

internal static class UiNative
{
    // ── DWM ──────────────────────────────────────────────────────────────
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public enum Backdrop { Auto = 0, None = 1, Mica = 2, Acrylic = 3, Tabbed = 4 }
    public enum Corner { Default = 0, DoNotRound = 1, Round = 2, RoundSmall = 3 }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    public static bool SupportsBackdrop { get; } = Environment.OSVersion.Version.Build >= 22621;

    public static void SetDarkMode(IntPtr hwnd, bool dark)
    {
        var v = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
    }

    public static void SetCorners(IntPtr hwnd, Corner corner)
    {
        var v = (int)corner;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref v, sizeof(int));
    }

    /// <summary>Applies a system backdrop (acrylic/mica). Returns false when unsupported.</summary>
    public static bool SetBackdrop(IntPtr hwnd, Backdrop backdrop)
    {
        if (!SupportsBackdrop) return false;
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var v = (int)backdrop;
        return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref v, sizeof(int)) == 0;
    }

    // ── Window styles ────────────────────────────────────────────────────
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x20, WS_EX_APPWINDOW = 0x40000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

    public static void AddExStyle(IntPtr hwnd, bool toolWindow, bool noActivate, bool clickThrough = false)
    {
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        style &= ~WS_EX_APPWINDOW;
        if (toolWindow) style |= WS_EX_TOOLWINDOW;
        if (noActivate) style |= WS_EX_NOACTIVATE;
        if (clickThrough) style |= WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
    }

    // ── Positioning (physical pixels) ────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT p);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    public const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    public static RECT WorkAreaAt(POINT p)
    {
        var mon = MonitorFromPoint(p, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(mon, ref info);
        return info.rcWork;
    }

    public static POINT CursorPosition() => GetCursorPos(out var p) ? p : default;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    /// <summary>The whole area and the work area (without the taskbar) of the monitor chosen in Options.</summary>
    public static (RECT Monitor, RECT Work) MonitorFor(Core.Model.ScreenChoice choice)
    {
        var mon = choice switch
        {
            Core.Model.ScreenChoice.Primary => MonitorFromPoint(default, 1 /* MONITOR_DEFAULTTOPRIMARY */),
            Core.Model.ScreenChoice.ActiveWindow when GetForegroundWindow() is var fg && fg != IntPtr.Zero => MonitorFromWindow(fg, 2),
            _ => MonitorFromPoint(CursorPosition(), 2 /* MONITOR_DEFAULTTONEAREST */),
        };
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(mon, ref info);
        return (info.rcMonitor, info.rcWork);
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, [Out] char[] name, int max);

    private static string ClassName(IntPtr hwnd)
    {
        var name = new char[64];
        var length = GetClassName(hwnd, name, name.Length);
        return length > 0 ? new string(name, 0, length) : string.Empty;
    }

    /// <summary>
    /// Whether something covers <paramref name="monitor"/> whole: an exclusive full-screen game or a presentation
    /// (anywhere), or the window in front filling that monitor (a film, a borderless game). The desktop and K-Desktop's
    /// own windows never count.
    /// </summary>
    public static bool FullscreenOn(RECT monitor)
    {
        if (SHQueryUserNotificationState(out var state) == 0 && state is 3 /* D3D full screen */ or 4 /* presentation */) return true;
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        GetWindowThreadProcessId(fg, out var pid);
        if (pid == Environment.ProcessId) return false;
        if (ClassName(fg) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        return GetWindowRect(fg, out var r)
            && r.Left <= monitor.Left && r.Top <= monitor.Top && r.Right >= monitor.Right && r.Bottom >= monitor.Bottom;
    }

    public static void MoveWindow(IntPtr hwnd, int x, int y) =>
        SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

    public static IntPtr Handle(Window window) => new WindowInteropHelper(window).EnsureHandle();
}

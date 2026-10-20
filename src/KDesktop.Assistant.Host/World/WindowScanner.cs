using System.Runtime.InteropServices;
using KDesktop.Assistant.Sdk;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.World;

/// <summary>
/// Goes through the top-level windows in Z order and keeps the ones an assistant can use (from Kobii): visible,
/// not minimized, not hidden on another virtual desktop and not part of the shell. Bounds are DWM's (without
/// invisible borders or shadows). Windows of elevated processes are read too, and marked.
/// </summary>
internal sealed unsafe class WindowScanner(ISet<nint> ownWindows)
{
    private const int MinWidth = 80;
    private const int MinHeight = 40;

    /// <summary>Classes of the shell and pop-ups that are never terrain.</summary>
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
        "Xaml_WindowedPopupClass", "ForegroundStaging", "MultitaskingViewFrame", "TaskListThumbnailWnd",
        "Windows.Internal.Shell.TabProxyWindow", "Shell_InputSwitchTopLevelWindow", "EdgeUiInputTopWndClass",
        "ApplicationManager_ImmersiveShellWindow", "Internet Explorer_Hidden", "tooltips_class32", "#32768",
        "SysShadow", "IME", "MSCTFIME UI", "Chrome_WidgetWin_2",
    };

    [ThreadStatic]
    private static List<nint>? _enumerated;

    private readonly Dictionary<uint, (string? Name, bool Elevated)> _processes = [];

    /// <summary>The usable windows, topmost first.</summary>
    public List<WindowInfo> Scan()
    {
        var result = new List<WindowInfo>(32);
        foreach (var hwnd in EnumerateTopLevel())
            if (TryDescribe(hwnd, out var info)) result.Add(info);
        if (_processes.Count > 512) _processes.Clear();
        return result;
    }

    /// <summary>Describes a window if it counts.</summary>
    public bool TryDescribe(nint hwnd, out WindowInfo info)
    {
        info = null!;
        if (ownWindows.Contains(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;

        var exStyle = (uint)(long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        var style = (uint)(long)GetWindowLongPtr(hwnd, GWL_STYLE);
        // Layers that let the mouse through (other apps' overlays) and tool windows.
        if ((exStyle & WS_EX_TRANSPARENT) != 0 && (exStyle & WS_EX_LAYERED) != 0) return false;
        if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0) return false;
        if (IsCloaked(hwnd)) return false;

        var className = GetClassName(hwnd);
        if (className is null || IgnoredClasses.Contains(className)) return false;

        var hasCaption = (style & WS_CAPTION) == WS_CAPTION;
        var owner = GetWindow(hwnd, GW_OWNER);
        if (owner != 0 && !hasCaption && (exStyle & WS_EX_APPWINDOW) == 0) return false;
        var title = GetTitle(hwnd);
        if (!hasCaption && title.Length == 0) return false;
        if (!TryGetBounds(hwnd, out var bounds) || bounds.Right - bounds.Left < MinWidth || bounds.Bottom - bounds.Top < MinHeight)
            return false;

        var (name, elevated) = ProcessOf(hwnd);
        info = new WindowInfo(hwnd, bounds.ToPixelRect(), title, name, IsZoomed(hwnd), elevated);
        return true;
    }

    /// <summary>
    /// How full-screen detection sees a window (see <see cref="SystemStatus.CoveredMonitors"/>): one that can cover a
    /// monitor (not a maximized window with its title bar, and not a layered overlay unless it is the one in front) and
    /// whether it hides what is below it on its monitor (an ordinary window that is not «always on top»; pop-ups, tool
    /// windows and see-through overlays never do).
    /// </summary>
    public static bool TryLayer(nint hwnd, ISet<nint> ownWindows, nint foreground, out ScreenLayer layer)
    {
        layer = default;
        if (ownWindows.Contains(hwnd) || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
        var exStyle = (uint)(long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        var style = (uint)(long)GetWindowLongPtr(hwnd, GWL_STYLE);
        if ((exStyle & WS_EX_TRANSPARENT) != 0 && (exStyle & WS_EX_LAYERED) != 0) return false;
        if ((exStyle & WS_EX_TOOLWINDOW) != 0 && (exStyle & WS_EX_APPWINDOW) == 0) return false;
        if (IsCloaked(hwnd)) return false;
        var className = GetClassName(hwnd);
        if (className is null || IgnoredClasses.Contains(className)) return false;
        if (!TryGetBounds(hwnd, out var bounds) || bounds.Right - bounds.Left < MinWidth || bounds.Bottom - bounds.Top < MinHeight)
            return false;

        var hasCaption = (style & WS_CAPTION) == WS_CAPTION;
        var popup = GetWindow(hwnd, GW_OWNER) != 0 && !hasCaption && (exStyle & WS_EX_APPWINDOW) == 0;
        var canCover = !(hasCaption && IsZoomed(hwnd)) && (hwnd == foreground || (exStyle & WS_EX_LAYERED) == 0);
        var blocks = !popup && (exStyle & WS_EX_TOPMOST) == 0 && (hasCaption || GetTitle(hwnd).Length > 0);
        layer = new ScreenLayer(bounds.ToPixelRect(), canCover, blocks);
        return true;
    }

    /// <summary>The top-level windows, topmost first.</summary>
    public static List<nint> TopLevel() => EnumerateTopLevel();

    public static bool IsCloaked(nint hwnd)
    {
        var cloaked = 0;
        return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    /// <summary>Real bounds according to DWM (without invisible borders or shadows).</summary>
    public static bool TryGetBounds(nint hwnd, out RECT bounds)
    {
        RECT rect;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, &rect, (uint)sizeof(RECT)) == 0)
        {
            bounds = rect;
            return true;
        }
        return GetWindowRect(hwnd, out bounds);
    }

    public static string? GetClassName(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var length = Interop.Native.GetClassName(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    public static string GetTitle(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var length = GetWindowText(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    /// <summary>Owner process name (lower case, without extension) and whether it runs elevated (cached per process).</summary>
    public (string? Name, bool Elevated) ProcessOf(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return (null, false);
        if (_processes.TryGetValue(pid, out var cached)) return cached;

        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        string? name = null;
        // A process we cannot even query is a protected or elevated one: either way, out of our reach.
        var elevated = process == 0;
        if (process != 0)
        {
            try
            {
                var buffer = stackalloc char[1024];
                uint size = 1024;
                if (QueryFullProcessImageName(process, 0, buffer, ref size))
                    name = Path.GetFileNameWithoutExtension(new string(buffer, 0, (int)size)).ToLowerInvariant();
                elevated = IsElevated(process);
            }
            finally { CloseHandle(process); }
        }
        return _processes[pid] = (name, elevated);
    }

    /// <summary>Full path of the executable that owns a window (null if it cannot be read).</summary>
    public static string? PathOf(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        var process = pid == 0 ? 0 : OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0) return null;
        try
        {
            var buffer = stackalloc char[1024];
            uint size = 1024;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally { CloseHandle(process); }
    }

    private static bool IsElevated(nint process)
    {
        // Our own token is medium integrity: opening an elevated process's token is denied, which says as much.
        if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return Marshal.GetLastPInvokeError() == 5;
        try
        {
            var elevation = 0;
            return GetTokenInformation(token, TokenElevation, &elevation, sizeof(int), out _) && elevation != 0;
        }
        finally { CloseHandle(token); }
    }

    private static List<nint> EnumerateTopLevel()
    {
        _enumerated = new List<nint>(256);
        EnumWindows(&OnWindow, 0);
        var list = _enumerated;
        _enumerated = null;
        return list;
    }

    [UnmanagedCallersOnly]
    private static int OnWindow(nint hwnd, nint lParam)
    {
        _enumerated?.Add(hwnd);
        return 1;
    }
}

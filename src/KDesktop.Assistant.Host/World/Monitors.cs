using System.Runtime.InteropServices;
using KDesktop.Assistant.Sdk;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.World;

/// <summary>The monitors with their work area and scaling (from Kobii), and the taskbars that go with them.</summary>
internal static unsafe class Monitors
{
    [ThreadStatic]
    private static List<nint>? _handles;

    /// <summary>The primary first, then left to right (stable indexes).</summary>
    public static List<MonitorInfo> Read()
    {
        _handles = [];
        EnumDisplayMonitors(0, 0, &OnMonitor, 0);
        var monitors = new List<MonitorInfo>(_handles.Count);
        foreach (var handle in _handles)
        {
            var info = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
            if (!GetMonitorInfo(handle, ref info)) continue;
            var dpi = GetDpiForMonitor(handle, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? dpiX : 96u;
            monitors.Add(new MonitorInfo(monitors.Count, info.rcMonitor.ToPixelRect(), info.rcWork.ToPixelRect(), dpi / 96.0,
                (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
        }
        _handles = null;
        return monitors.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top)
            .Select((m, i) => m with { Index = i }).ToList();
    }

    /// <summary>
    /// The taskbar of each monitor, from the gap between its bounds and its work area (it works centred or on the
    /// left, on any edge and on secondary monitors, as in Lander); on the primary, the notification area and the
    /// clock. Windows 11 draws them inside one XAML island, so they are estimated from its right end. With
    /// auto-hide there is no gap: the bar is the strip along the bottom edge.
    /// </summary>
    public static List<TaskbarInfo> Taskbars(IReadOnlyList<MonitorInfo> monitors)
    {
        var bars = new List<TaskbarInfo>(monitors.Count);
        foreach (var m in monitors)
        {
            PixelRect? bar = null;
            var b = m.Bounds;
            var w = m.WorkArea;
            if (w.Bottom < b.Bottom) bar = new PixelRect(b.Left, w.Bottom, b.Right, b.Bottom);
            else if (w.Top > b.Top) bar = new PixelRect(b.Left, b.Top, b.Right, w.Top);
            else if (w.Left > b.Left) bar = new PixelRect(b.Left, b.Top, w.Left, b.Bottom);
            else if (w.Right < b.Right) bar = new PixelRect(w.Right, b.Top, b.Right, b.Bottom);
            var autoHide = bar is null;
            var bounds = bar ?? new PixelRect(b.Left, b.Bottom - (int)(48 * m.DpiScale), b.Right, b.Bottom);
            PixelRect? tray = null, clock = null;
            if (m.IsPrimary && bounds.Width > bounds.Height)
            {
                var s = m.DpiScale;
                clock = new PixelRect(bounds.Right - (int)(112 * s), bounds.Top, bounds.Right - (int)(12 * s), bounds.Bottom);
                tray = new PixelRect(bounds.Right - (int)(300 * s), bounds.Top, clock.Value.Left, bounds.Bottom);
            }
            bars.Add(new TaskbarInfo(m.Index, bounds, tray, clock, autoHide));
        }
        return bars;
    }

    [UnmanagedCallersOnly]
    private static int OnMonitor(nint monitor, nint hdc, RECT* rect, nint data)
    {
        _handles?.Add(monitor);
        return 1;
    }
}

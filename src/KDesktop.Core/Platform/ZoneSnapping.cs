using KDesktop.Core.Model;
using KDesktop.Core.Native;

namespace KDesktop.Core.Platform;

/// <summary>A zone windows snap into now: its monitor, the zone, its number (as in the drawing) and its place on the screen.</summary>
public sealed record SnapZone(ScreenMonitor Monitor, ArrangedZone Zone, int Number, ScreenRect Rect);

/// <summary>
/// Snapping windows into zones (E1b, second batch): each monitor uses the zones of the disposición put last on it
/// (<see cref="AppSettings.ActiveArrangements"/>); one where none was ever put does not snap. Shift + drag drops a window
/// into the zone under the cursor; «A la zona siguiente / anterior» moves it zone by zone, and past the last one of a
/// monitor to the next monitor with zones.
/// </summary>
public static class ZoneSnapping
{
    /// <summary>A window this close to a zone (in pixels, each edge) is in it.</summary>
    private const int Tolerance = 16;

    /// <summary>The zones to snap into, monitor by monitor (left to right) and in their order.</summary>
    public static IReadOnlyList<SnapZone> ActiveZones(AppConfig config, IReadOnlyList<ScreenMonitor> monitors)
    {
        var result = new List<SnapZone>();
        var active = config.Settings.ActiveArrangements.ToList();
        foreach (var monitor in monitors)
        {
            var entry = active.FirstOrDefault(a => ReferenceEquals(WindowArrangements.MonitorExactly(a.MonitorName, a.MonitorDevice, monitors), monitor));
            if (entry is null || config.FindArrangement(entry.ArrangementId) is not { } arrangement) continue;
            var zones = arrangement.Zones.Where(z => z.IsOn(entry.MonitorName, entry.MonitorDevice)).ToList();
            for (var i = 0; i < zones.Count; i++) result.Add(new SnapZone(monitor, zones[i], i + 1, WindowArrangements.RectIn(zones[i], monitor.WorkArea)));
        }
        return result;
    }

    /// <summary>The zone under a point of the screen (null: none there).</summary>
    public static SnapZone? At(IReadOnlyList<SnapZone> zones, int x, int y) =>
        zones.FirstOrDefault(z => x >= z.Rect.Left && x < z.Rect.Right && y >= z.Rect.Top && y < z.Rect.Bottom);

    /// <summary>
    /// The zone a window goes to with «siguiente» (<paramref name="forward"/>) or «anterior»: the next one after the zone it
    /// is in (round all the monitors); a window in no zone goes first to the one under its middle.
    /// </summary>
    public static SnapZone? Step(IReadOnlyList<SnapZone> zones, ScreenRect window, bool forward)
    {
        if (zones.Count == 0) return null;
        var current = -1;
        for (var i = 0; i < zones.Count && current < 0; i++)
            if (Near(zones[i].Rect, window)) current = i;
        if (current < 0) return At(zones, window.CenterX, window.CenterY) ?? (forward ? zones[0] : zones[^1]);
        return zones[(current + (forward ? 1 : -1) + zones.Count) % zones.Count];
    }

    private static bool Near(ScreenRect a, ScreenRect b) =>
        Math.Abs(a.Left - b.Left) <= Tolerance && Math.Abs(a.Top - b.Top) <= Tolerance && Math.Abs(a.Right - b.Right) <= Tolerance && Math.Abs(a.Bottom - b.Bottom) <= Tolerance;

    /// <summary>Where a window is for <see cref="Step"/>: a maximised one, its whole work area.</summary>
    public static ScreenRect PlaceOf(IntPtr hwnd) => WindowLayout.IsMaximized(hwnd) ? WindowLayout.WorkAreaOf(hwnd) : WindowLayout.VisibleBounds(hwnd);

    /// <summary>A window into a zone: moved there (restored if it was maximised); maximised when the zone is the whole monitor.</summary>
    public static void Put(IntPtr hwnd, SnapZone zone)
    {
        WindowLayout.MoveTo(hwnd, zone.Rect);
        if (zone.Zone.IsFull) Win32.ShowWindow(hwnd, Win32.SW_SHOWMAXIMIZED);
    }
}

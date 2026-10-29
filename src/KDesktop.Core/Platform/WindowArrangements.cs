using System.Runtime.InteropServices;
using KDesktop.Core.Model;
using KDesktop.Core.Native;

namespace KDesktop.Core.Platform;

/// <summary>A monitor that is on: its GDI name, its own name, its work area (without the taskbar), whether it is the main one and its whole area.</summary>
public sealed record ScreenMonitor(string Device, string Name, ScreenRect WorkArea, bool Primary, ScreenRect Bounds = default)
{
    /// <summary>Its whole area (the work area when it was not given).</summary>
    public ScreenRect Area => Bounds.Width > 0 ? Bounds : WorkArea;
}

/// <summary>How putting a disposición went.</summary>
/// <param name="Placed">Windows put in their zone (also the ones opened).</param>
/// <param name="Opened">Programs that were not open and were opened.</param>
/// <param name="Missing">Programs that were not open (and were not opened, or their window did not appear in time).</param>
public sealed record ArrangeResult(int Placed, int Opened, IReadOnlyList<string> Missing);

/// <summary>
/// «Disposiciones» (E1, 5.0.375; zones since E1b): the windows open now as zones, and a disposición put: each program to
/// its zone. A monitor is found by its own name (and its GDI name when there are two the same); one that is not there now:
/// the main one, in proportion.
/// </summary>
public static class WindowArrangements
{
    /// <summary>How long the windows of the programs opened are waited for.</summary>
    public static readonly TimeSpan OpenWait = TimeSpan.FromSeconds(20);

    /// <summary>The monitors on now, left to right (like <see cref="WindowLayout.WorkAreas"/>).</summary>
    public static IReadOnlyList<ScreenMonitor> Monitors()
    {
        var names = DisplayTools.Monitors().ToDictionary(m => m.DeviceName, m => OwnName(m.Name), StringComparer.OrdinalIgnoreCase);
        var result = new List<ScreenMonitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(monitor, ref info))
                result.Add(new ScreenMonitor(info.szDevice, names.GetValueOrDefault(info.szDevice) ?? info.szDevice, ToRect(info.rcWork), (info.dwFlags & 1) != 0,
                    ToRect(info.rcMonitor)));
            return true;
        }, IntPtr.Zero);
        return [.. result.OrderBy(m => m.WorkArea.Left).ThenBy(m => m.WorkArea.Top)];
    }

    /// <summary>«DELL U2723QE» out of DisplayTools' «2. DELL U2723QE».</summary>
    private static string OwnName(string name)
    {
        var dot = name.IndexOf(". ", StringComparison.Ordinal);
        return dot > 0 && name[..dot].All(char.IsDigit) ? name[(dot + 2)..] : name;
    }

    /// <summary>
    /// K-Desktop remembers every monitor it sees (its name, GDI name, resolution and work area), so a disposición can still
    /// be cut for one that is not connected. Returns true when something changed.
    /// </summary>
    public static bool Remember(ICollection<KnownMonitor> known, IReadOnlyList<ScreenMonitor> monitors)
    {
        var changed = false;
        foreach (var m in monitors)
        {
            var entry = known.FirstOrDefault(k => k.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase) && k.Device.Equals(m.Device, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                known.Add(new KnownMonitor { Name = m.Name, Device = m.Device, Width = m.Area.Width, Height = m.Area.Height, WorkWidth = m.WorkArea.Width, WorkHeight = m.WorkArea.Height });
                changed = true;
            }
            else if (entry.Width != m.Area.Width || entry.Height != m.Area.Height || entry.WorkWidth != m.WorkArea.Width || entry.WorkHeight != m.WorkArea.Height)
            {
                // A changed resolution: the item is replaced, so the list notices (and saves).
                known.Remove(entry);
                known.Add(new KnownMonitor { Name = m.Name, Device = m.Device, Width = m.Area.Width, Height = m.Area.Height, WorkWidth = m.WorkArea.Width, WorkHeight = m.WorkArea.Height });
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>
    /// Where a zone goes: the monitor with its name and GDI name; else the only one with its name; else the one with its
    /// GDI name; else the main one (the user's choice, 29/10/2026).
    /// </summary>
    public static ScreenMonitor? MonitorFor(string name, string device, IReadOnlyList<ScreenMonitor> monitors) =>
        MonitorExactly(name, device, monitors) ?? monitors.FirstOrDefault(m => device.Length > 0 && m.Device.Equals(device, StringComparison.OrdinalIgnoreCase))
        ?? monitors.FirstOrDefault(m => m.Primary) ?? (monitors.Count > 0 ? monitors[0] : null);

    public static ScreenMonitor? MonitorFor(ArrangedZone zone, IReadOnlyList<ScreenMonitor> monitors) => MonitorFor(zone.MonitorName, zone.MonitorDevice, monitors);

    /// <summary>That very monitor (its name and GDI name, or the only one with its name), or none.</summary>
    public static ScreenMonitor? MonitorExactly(string name, string device, IReadOnlyList<ScreenMonitor> monitors)
    {
        bool SameName(ScreenMonitor m) => name.Length > 0 && m.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
        var named = monitors.Where(SameName).ToList();
        return named.FirstOrDefault(m => device.Length > 0 && m.Device.Equals(device, StringComparison.OrdinalIgnoreCase)) ?? (named.Count == 1 ? named[0] : null);
    }

    /// <summary>Its place in that work area (the fractions, kept inside it).</summary>
    public static ScreenRect RectIn(ArrangedZone zone, ScreenRect area) => RectIn(zone.Left, zone.Top, zone.Width, zone.Height, area);

    public static ScreenRect RectIn(double left, double top, double width, double height, ScreenRect area)
    {
        width = Math.Clamp(width, 0.05, 1);
        height = Math.Clamp(height, 0.05, 1);
        left = Math.Clamp(left, 0, 1 - width);
        top = Math.Clamp(top, 0, 1 - height);
        return new ScreenRect(area.Left + (int)Math.Round(area.Width * left), area.Top + (int)Math.Round(area.Height * top),
            (int)Math.Round(area.Width * width), (int)Math.Round(area.Height * height));
    }

    /// <summary>The fractions of a rectangle in a work area.</summary>
    public static (double Left, double Top, double Width, double Height) FractionsOf(ScreenRect rect, ScreenRect area)
    {
        double W(int v) => Math.Round(v / (double)Math.Max(1, area.Width), 4);
        double H(int v) => Math.Round(v / (double)Math.Max(1, area.Height), 4);
        return (W(rect.Left - area.Left), H(rect.Top - area.Top), W(rect.Width), H(rect.Height));
    }

    /// <summary>
    /// Which open window each program takes (null: none of its program is free): among the windows of its program, the one
    /// with its title, else the next one in order. One window is never taken twice.
    /// </summary>
    public static IReadOnlyList<T?> Match<T>(IReadOnlyList<ZoneProgram> entries, IReadOnlyList<T> windows, Func<T, string> process, Func<T, string> title)
        where T : class
    {
        var result = new T?[entries.Count];
        var used = new HashSet<T>(ReferenceEqualityComparer.Instance);
        // First the ones with their exact title, so «the next one» never takes a window another entry names.
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            result[i] = windows.FirstOrDefault(w => !used.Contains(w) && process(w).Equals(entry.Process, StringComparison.OrdinalIgnoreCase)
                                                    && entry.Title.Length > 0 && title(w).Equals(entry.Title, StringComparison.Ordinal));
            if (result[i] is { } taken) used.Add(taken);
        }
        for (var i = 0; i < entries.Count; i++)
        {
            if (result[i] is not null) continue;
            result[i] = windows.FirstOrDefault(w => !used.Contains(w) && process(w).Equals(entries[i].Process, StringComparison.OrdinalIgnoreCase));
            if (result[i] is { } taken) used.Add(taken);
        }
        return result;
    }

    /// <summary>A window open now as it is, to make zones of it.</summary>
    public sealed record OpenPlace(string Process, string Name, string Title, string Path, ScreenMonitor Monitor, ScreenRect Rect, bool Maximized);

    /// <summary>
    /// «Guardar cómo están ahora»: each monitor cut by its windows (not minimised; a maximised one, the whole screen), with
    /// <see cref="ZoneCuts.Tile"/>: never a zone over another; windows one over another share the zone.
    /// </summary>
    public static List<ArrangedZone> ZonesOf(IEnumerable<OpenPlace> windows)
    {
        var zones = new List<ArrangedZone>();
        foreach (var monitor in PiecesOf(windows).GroupBy(z => (z.MonitorName, z.MonitorDevice)))
            zones.AddRange(ZoneCuts.Tile([.. monitor], monitor.Key.MonitorName, monitor.Key.MonitorDevice));
        return zones;
    }

    /// <summary>Each window as it is: a rectangle of its monitor with its program (two in the same place, one).</summary>
    private static List<ArrangedZone> PiecesOf(IEnumerable<OpenPlace> windows)
    {
        var zones = new List<ArrangedZone>();
        foreach (var w in windows)
        {
            var (left, top, width, height) = w.Maximized ? (0d, 0d, 1d, 1d) : FractionsOf(w.Rect, w.Monitor.WorkArea);
            // Kept inside the monitor (a window half out of it).
            width = Math.Clamp(width, 0.05, 1);
            height = Math.Clamp(height, 0.05, 1);
            left = Math.Clamp(left, 0, 1 - width);
            top = Math.Clamp(top, 0, 1 - height);
            var zone = zones.FirstOrDefault(z => z.IsOn(w.Monitor.Name, w.Monitor.Device) && z.SameRect(left, top, width, height));
            if (zone is null)
            {
                zone = new ArrangedZone { MonitorName = w.Monitor.Name, MonitorDevice = w.Monitor.Device };
                zone.SetRect(left, top, width, height);
                zones.Add(zone);
            }
            zone.Programs.Add(new ZoneProgram { Process = w.Process, Name = w.Name, Title = w.Title, Path = w.Path });
        }
        return zones;
    }

    /// <summary>The windows open now (never K-Desktop's own, nor the minimised ones) as zones of their monitors.</summary>
    public static List<ArrangedZone> Capture()
    {
        var monitors = Monitors();
        var own = Path.GetFileNameWithoutExtension(AppPaths.ExecutablePath);
        var places = new List<OpenPlace>();
        foreach (var window in AppFinder.OpenWindows())
        {
            if (window.Process.Equals(own, StringComparison.OrdinalIgnoreCase) || Win32.IsIconic(window.Hwnd)) continue;
            var rect = WindowLayout.RestoredBounds(window.Hwnd);
            if (MonitorOf(rect, monitors) is not { } monitor) continue;
            var path = WindowTools.ProcessPath((int)window.Pid) ?? string.Empty;
            // A Store app lives in its frame host: there is nothing to open again.
            if (window.Process.Equals("applicationframehost", StringComparison.OrdinalIgnoreCase)) path = string.Empty;
            places.Add(new OpenPlace(window.Process.ToLowerInvariant(), window.DisplayName, window.Title, path, monitor, rect, WindowLayout.IsMaximized(window.Hwnd)));
        }
        return ZonesOf(places);
    }

    /// <summary>The monitor a rectangle is on (by its middle; else the nearest work area).</summary>
    public static ScreenMonitor? MonitorOf(ScreenRect rect, IReadOnlyList<ScreenMonitor> monitors) =>
        monitors.FirstOrDefault(m => rect.CenterX >= m.Area.Left && rect.CenterX < m.Area.Right && rect.CenterY >= m.Area.Top && rect.CenterY < m.Area.Bottom)
        ?? monitors.OrderBy(m => Math.Abs(m.WorkArea.CenterX - rect.CenterX) + Math.Abs(m.WorkArea.CenterY - rect.CenterY)).FirstOrDefault();

    /// <summary>
    /// It stays as the disposición of each monitor with zones in it (the one its windows snap into, E1b): the last one put
    /// there wins. Returns true when something changed.
    /// </summary>
    public static bool SetActive(ICollection<ActiveArrangement> active, WindowArrangement arrangement)
    {
        var changed = false;
        foreach (var (name, device) in arrangement.Zones.Select(z => (z.MonitorName, z.MonitorDevice)).Distinct())
        {
            var entry = active.FirstOrDefault(a => a.MonitorName.Equals(name, StringComparison.OrdinalIgnoreCase) && a.MonitorDevice.Equals(device, StringComparison.OrdinalIgnoreCase));
            if (entry?.ArrangementId == arrangement.Id) continue;
            if (entry is not null) active.Remove(entry);
            active.Add(new ActiveArrangement { MonitorName = name, MonitorDevice = device, ArrangementId = arrangement.Id });
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Puts <paramref name="arrangement"/>: each program of each zone to it (several windows of the same one: by title, else
    /// in order); with <see cref="WindowArrangement.OpenMissing"/>, the programs that are not open are opened
    /// (<paramref name="launch"/>) and placed when their window appears.
    /// </summary>
    public static async Task<ArrangeResult> ApplyAsync(WindowArrangement arrangement, Func<string, bool> launch, CancellationToken token)
    {
        var entries = new List<ZoneProgram>();
        var zoneOf = new List<ArrangedZone>();
        foreach (var zone in arrangement.Zones)
            foreach (var program in zone.Programs.Where(p => p.Process.Length > 0))
            {
                entries.Add(program);
                zoneOf.Add(zone);
            }
        var monitors = Monitors();
        var own = Path.GetFileNameWithoutExtension(AppPaths.ExecutablePath);
        var open = AppFinder.OpenWindows().Where(w => !w.Process.Equals(own, StringComparison.OrdinalIgnoreCase)).ToList();
        var matched = Match(entries, open, w => w.Process, w => w.Title);
        var placed = 0;
        var waiting = new List<int>();
        var missing = new List<string>();
        for (var i = 0; i < entries.Count; i++)
        {
            if (matched[i] is { } window)
            {
                if (Place(window.Hwnd, zoneOf[i], monitors)) placed++;
            }
            else if (arrangement.OpenMissing && entries[i].Path.Length > 0 && File.Exists(entries[i].Path) && launch(entries[i].Path)) waiting.Add(i);
            else missing.Add(entries[i].Display);
        }
        var opened = waiting.Count;
        if (waiting.Count > 0)
        {
            // The new windows: of their program and not there before.
            var before = open.Select(w => w.Hwnd).ToHashSet();
            var until = DateTime.UtcNow + OpenWait;
            while (waiting.Count > 0 && DateTime.UtcNow < until)
            {
                await Task.Delay(300, token).ConfigureAwait(false);
                var fresh = AppFinder.OpenWindows().Where(w => !before.Contains(w.Hwnd)).ToList();
                var now = Match([.. waiting.Select(i => entries[i])], fresh, w => w.Process, w => w.Title);
                for (var j = waiting.Count - 1; j >= 0; j--)
                {
                    if (now[j] is not { } window) continue;
                    // A window just shown may move itself once more: a moment, and it goes where it should.
                    await Task.Delay(400, token).ConfigureAwait(false);
                    if (Place(window.Hwnd, zoneOf[waiting[j]], monitors)) placed++;
                    before.Add(window.Hwnd);
                    waiting.RemoveAt(j);
                }
            }
            missing.AddRange(waiting.Select(i => entries[i].Display));
        }
        Log.Info($"Disposición «{arrangement.Name}»: {placed} colocada(s), {opened} abierta(s), {missing.Count} sin abrir");
        return new ArrangeResult(placed, opened, missing);
    }

    /// <summary>One window to its zone: restored and moved there; maximised when the zone is the whole monitor.</summary>
    private static bool Place(IntPtr hwnd, ArrangedZone zone, IReadOnlyList<ScreenMonitor> monitors)
    {
        if (!Win32.IsWindow(hwnd) || MonitorFor(zone, monitors) is not { } monitor) return false;
        WindowLayout.MoveTo(hwnd, RectIn(zone, monitor.WorkArea));
        if (zone.IsFull) Win32.ShowWindow(hwnd, Win32.SW_SHOWMAXIMIZED);
        return true;
    }

    private static ScreenRect ToRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    // ── P/Invoke ─────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
}

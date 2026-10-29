using System.Runtime.InteropServices;
using KDesktop.Core.Model;
using KDesktop.Core.Native;

namespace KDesktop.Core.Platform;

/// <summary>A monitor that is on: its GDI name, its own name, its work area (without the taskbar) and whether it is the main one.</summary>
public sealed record ScreenMonitor(string Device, string Name, ScreenRect WorkArea, bool Primary);

/// <summary>How putting a disposición went.</summary>
/// <param name="Placed">Windows put in their place (also the ones opened).</param>
/// <param name="Opened">Programs that were not open and were opened.</param>
/// <param name="Missing">Programs that were not open (and were not opened, or their window did not appear in time).</param>
public sealed record ArrangeResult(int Placed, int Opened, IReadOnlyList<string> Missing);

/// <summary>
/// «Disposiciones» (E1, 5.0.375): the windows open now as a disposición, and a disposición put back. A monitor is found by
/// its own name (and its GDI name when there are two the same); one that is not there now: the main one, in proportion.
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
                result.Add(new ScreenMonitor(info.szDevice, names.GetValueOrDefault(info.szDevice) ?? info.szDevice, ToRect(info.rcWork), (info.dwFlags & 1) != 0));
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
    /// Where a window of a disposición goes: the monitor with its name and GDI name; else the only one with its name; else
    /// the one with its GDI name; else the main one (the user's choice, 29/10/2026).
    /// </summary>
    public static ScreenMonitor? MonitorFor(ArrangedWindow window, IReadOnlyList<ScreenMonitor> monitors)
    {
        if (monitors.Count == 0) return null;
        bool SameName(ScreenMonitor m) => window.MonitorName.Length > 0 && m.Name.Equals(window.MonitorName, StringComparison.OrdinalIgnoreCase);
        bool SameDevice(ScreenMonitor m) => window.MonitorDevice.Length > 0 && m.Device.Equals(window.MonitorDevice, StringComparison.OrdinalIgnoreCase);
        var named = monitors.Where(SameName).ToList();
        return monitors.FirstOrDefault(m => SameName(m) && SameDevice(m))
               ?? (named.Count == 1 ? named[0] : null)
               ?? monitors.FirstOrDefault(SameDevice)
               ?? monitors.FirstOrDefault(m => m.Primary)
               ?? monitors[0];
    }

    /// <summary>Its place in that work area (the fractions, kept inside it).</summary>
    public static ScreenRect RectIn(ArrangedWindow window, ScreenRect area)
    {
        var width = Math.Clamp(window.Width, 0.05, 1);
        var height = Math.Clamp(window.Height, 0.05, 1);
        var left = Math.Clamp(window.Left, 0, 1 - width);
        var top = Math.Clamp(window.Top, 0, 1 - height);
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
    /// Which open window each entry takes (null: none of its program is free): among the windows of its program, the one
    /// with its title, else the next one in order. One window is never taken twice.
    /// </summary>
    public static IReadOnlyList<T?> Match<T>(IReadOnlyList<ArrangedWindow> entries, IReadOnlyList<T> windows, Func<T, string> process, Func<T, string> title)
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

    /// <summary>The windows open now (never K-Desktop's own), as they are: where, how and on which monitor.</summary>
    public static List<ArrangedWindow> Capture()
    {
        var monitors = Monitors();
        var own = Path.GetFileNameWithoutExtension(AppPaths.ExecutablePath);
        var result = new List<ArrangedWindow>();
        foreach (var window in AppFinder.OpenWindows())
        {
            if (window.Process.Equals(own, StringComparison.OrdinalIgnoreCase)) continue;
            var state = Win32.IsIconic(window.Hwnd) ? ArrangedState.Minimized : WindowLayout.IsMaximized(window.Hwnd) ? ArrangedState.Maximized : ArrangedState.Normal;
            var rect = WindowLayout.RestoredBounds(window.Hwnd);
            var monitor = MonitorOf(rect, monitors);
            if (monitor is null) continue;
            var (left, top, width, height) = FractionsOf(rect, monitor.WorkArea);
            var path = WindowTools.ProcessPath((int)window.Pid) ?? string.Empty;
            // A Store app lives in its frame host: there is nothing to open again.
            if (window.Process.Equals("applicationframehost", StringComparison.OrdinalIgnoreCase)) path = string.Empty;
            result.Add(new ArrangedWindow
            {
                Process = window.Process.ToLowerInvariant(),
                Name = window.DisplayName,
                Title = window.Title,
                Path = path,
                MonitorName = monitor.Name,
                MonitorDevice = monitor.Device,
                Left = left, Top = top, Width = width, Height = height,
                State = state,
            });
        }
        return result;
    }

    /// <summary>The monitor a rectangle is on (by its middle; else the nearest work area).</summary>
    private static ScreenMonitor? MonitorOf(ScreenRect rect, IReadOnlyList<ScreenMonitor> monitors) =>
        monitors.FirstOrDefault(m => rect.CenterX >= m.WorkArea.Left && rect.CenterX < m.WorkArea.Right && rect.CenterY >= m.WorkArea.Top && rect.CenterY < m.WorkArea.Bottom)
        ?? monitors.OrderBy(m => Math.Abs(m.WorkArea.CenterX - rect.CenterX) + Math.Abs(m.WorkArea.CenterY - rect.CenterY)).FirstOrDefault();

    /// <summary>
    /// Puts <paramref name="arrangement"/> back: each window found in its place and state; with <see cref="WindowArrangement.OpenMissing"/>,
    /// the programs that are not open are opened (<paramref name="launch"/>) and placed when their window appears.
    /// </summary>
    public static async Task<ArrangeResult> ApplyAsync(WindowArrangement arrangement, Func<string, bool> launch, CancellationToken token)
    {
        var entries = arrangement.Windows.Where(w => w.Enabled && w.Process.Length > 0).ToList();
        var monitors = Monitors();
        var own = Path.GetFileNameWithoutExtension(AppPaths.ExecutablePath);
        var open = AppFinder.OpenWindows().Where(w => !w.Process.Equals(own, StringComparison.OrdinalIgnoreCase)).ToList();
        var matched = Match(entries, open, w => w.Process, w => w.Title);
        var placed = 0;
        var waiting = new List<ArrangedWindow>();
        var missing = new List<string>();
        for (var i = 0; i < entries.Count; i++)
        {
            if (matched[i] is { } window)
            {
                if (Place(window.Hwnd, entries[i], monitors)) placed++;
            }
            else if (arrangement.OpenMissing && entries[i].Path.Length > 0 && File.Exists(entries[i].Path) && launch(entries[i].Path)) waiting.Add(entries[i]);
            else missing.Add(entries[i].Name.Length > 0 ? entries[i].Name : entries[i].Process);
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
                var now = Match(waiting, fresh, w => w.Process, w => w.Title);
                for (var i = waiting.Count - 1; i >= 0; i--)
                {
                    if (now[i] is not { } window) continue;
                    // A window just shown may move itself once more: a moment, and it goes where it should.
                    await Task.Delay(400, token).ConfigureAwait(false);
                    if (Place(window.Hwnd, waiting[i], monitors)) placed++;
                    before.Add(window.Hwnd);
                    waiting.RemoveAt(i);
                }
            }
            missing.AddRange(waiting.Select(w => w.Name.Length > 0 ? w.Name : w.Process));
        }
        Log.Info($"Disposición «{arrangement.Name}»: {placed} colocada(s), {opened} abierta(s), {missing.Count} sin abrir");
        return new ArrangeResult(placed, opened, missing);
    }

    /// <summary>One window to its place: restored, moved, then maximised or minimised as it was saved.</summary>
    private static bool Place(IntPtr hwnd, ArrangedWindow entry, IReadOnlyList<ScreenMonitor> monitors)
    {
        if (!Win32.IsWindow(hwnd) || MonitorFor(entry, monitors) is not { } monitor) return false;
        WindowLayout.MoveTo(hwnd, RectIn(entry, monitor.WorkArea));
        if (entry.State == ArrangedState.Maximized) Win32.ShowWindow(hwnd, Win32.SW_SHOWMAXIMIZED);
        else if (entry.State == ArrangedState.Minimized) Win32.ShowWindow(hwnd, Win32.SW_MINIMIZE);
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

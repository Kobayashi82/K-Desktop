using KDesktop.Assistant.Sdk;
using Microsoft.Win32;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.World;

/// <summary>A visible window for full-screen detection (<see cref="WindowScanner.TryLayer"/>).</summary>
internal readonly record struct ScreenLayer(PixelRect Bounds, bool CanCover, bool Blocks);

/// <summary>
/// The state of the PC that matters to an assistant (from Kobii): idleness, battery, power saving, dark mode, «do not
/// disturb» and full-screen applications. The expensive readings are spaced out.
/// </summary>
internal sealed class SystemStatus
{
    private const double PowerInterval = 30;
    private const double NotificationStateInterval = 2;

    private double _nextPowerCheck;
    private double _nextNotificationCheck;
    private bool _themeDirty = true;

    public bool OnBattery { get; private set; }

    public bool BatteryLow { get; private set; }

    public bool PowerSaver { get; private set; }

    public bool DarkMode { get; private set; }

    /// <summary>Notifications silenced (do not disturb, presentation).</summary>
    public bool QuietMode { get; private set; }

    /// <summary>An exclusive DirectX game or presentation mode: nothing should show, and the GPU is freed.</summary>
    public bool ExclusiveFullscreen { get; private set; }

    public HashSet<int> FullscreenMonitors { get; } = [];

    /// <summary>Seconds since the last key press or mouse move anywhere.</summary>
    public static double UserIdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = 8 };
        return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.dwTime) / 1000.0 : 0;
    }

    /// <summary>Reads the theme again (WM_SETTINGCHANGE).</summary>
    public void InvalidateTheme() => _themeDirty = true;

    public void InvalidatePower() => _nextPowerCheck = 0;

    /// <summary>Checks full screen again (another window came to the front).</summary>
    public void InvalidateFullscreen() => _nextNotificationCheck = 0;

    public void Update(double now, IReadOnlyList<MonitorInfo> monitors, ISet<nint> ownWindows)
    {
        if (_themeDirty)
        {
            _themeDirty = false;
            DarkMode = ReadDarkMode();
        }
        if (now >= _nextPowerCheck)
        {
            _nextPowerCheck = now + PowerInterval;
            ReadPower();
        }
        if (now >= _nextNotificationCheck)
        {
            _nextNotificationCheck = now + NotificationStateInterval;
            ReadNotificationState();
            DetectFullscreen(monitors, ownWindows);
        }
    }

    private void ReadPower()
    {
        if (!GetSystemPowerStatus(out var status)) return;
        // BatteryFlag 128: no battery; 255 (or the percentage at 255): unknown.
        var present = status.BatteryFlag is not (128 or 255) && status.BatteryLifePercent != 255;
        OnBattery = present && status.ACLineStatus == 0;
        BatteryLow = present && status.BatteryLifePercent <= 20 && status.ACLineStatus != 1;
        PowerSaver = status.SystemStatusFlag == 1;
    }

    private void ReadNotificationState()
    {
        var quiet = false;
        var exclusive = false;
        if (SHQueryUserNotificationState(out var state) == 0)
        {
            quiet = state is QUNS_QUIET_TIME or QUNS_PRESENTATION_MODE;
            exclusive = state is QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;
        }
        QuietMode = quiet || NotificationsDisabled();
        ExclusiveFullscreen = exclusive;
    }

    /// <summary>
    /// Monitors covered by a full-screen window (video, borderless game, presentation), whether it has the focus or not:
    /// a film on one monitor while the user works on the other counts too (5.0.61; before, only the window in front did,
    /// and the Lander came out over the film until the user clicked on it). With an exclusive game or a presentation every
    /// monitor counts: nothing shows anyway (the overlay is suspended), so no assistant should go on flying unseen — as
    /// the standalone Lander, which hid and stopped.
    /// </summary>
    private void DetectFullscreen(IReadOnlyList<MonitorInfo> monitors, ISet<nint> ownWindows)
    {
        FullscreenMonitors.Clear();
        if (ExclusiveFullscreen)
        {
            foreach (var monitor in monitors) FullscreenMonitors.Add(monitor.Index);
            return;
        }
        FullscreenMonitors.UnionWith(CoveredMonitors(Layers(ownWindows), monitors));
    }

    /// <summary>The windows as full-screen detection sees them, topmost first (read lazily: it stops when it can).</summary>
    private static IEnumerable<ScreenLayer> Layers(ISet<nint> ownWindows)
    {
        var foreground = GetForegroundWindow();
        foreach (var hwnd in WindowScanner.TopLevel())
            if (WindowScanner.TryLayer(hwnd, ownWindows, foreground, out var layer)) yield return layer;
    }

    /// <summary>
    /// Goes down the windows of each monitor from the top: the first that covers it whole makes it full screen; an
    /// ordinary window over part of it first means it is not. Overlays, pop-ups and «always on top» windows (a
    /// picture-in-picture, sticky notes) neither decide nor hide what is below them.
    /// </summary>
    public static HashSet<int> CoveredMonitors(IEnumerable<ScreenLayer> topFirst, IReadOnlyList<MonitorInfo> monitors)
    {
        var covered = new HashSet<int>();
        var open = monitors.ToList();
        foreach (var layer in topFirst)
        {
            for (var i = open.Count - 1; i >= 0; i--)
            {
                var b = open[i].Bounds;
                var bounds = layer.Bounds;
                if (layer.CanCover && bounds.Left <= b.Left && bounds.Top <= b.Top && bounds.Right >= b.Right && bounds.Bottom >= b.Bottom)
                {
                    covered.Add(open[i].Index);
                    open.RemoveAt(i);
                }
                else if (layer.Blocks && bounds.Intersects(b))
                {
                    open.RemoveAt(i);
                }
            }
            if (open.Count == 0) break;
        }
        return covered;
    }

    private static bool ReadDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Notifications turned off altogether in Settings.</summary>
    private static bool NotificationsDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings");
            return key?.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}

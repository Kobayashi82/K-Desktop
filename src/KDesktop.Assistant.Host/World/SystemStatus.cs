using KDesktop.Assistant.Sdk;
using Microsoft.Win32;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.World;

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
    /// The window in front covering a whole monitor (video, borderless game, presentation). Maximized windows cover
    /// the work area, not the whole monitor, so they do not count.
    /// </summary>
    private void DetectFullscreen(IReadOnlyList<MonitorInfo> monitors, ISet<nint> ownWindows)
    {
        FullscreenMonitors.Clear();
        var foreground = GetForegroundWindow();
        if (foreground == 0 || ownWindows.Contains(foreground) || foreground == GetShellWindow() || foreground == GetDesktopWindow()) return;
        if (WindowScanner.GetClassName(foreground) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow")
            return;
        if (!GetWindowRect(foreground, out var rect)) return;
        foreach (var monitor in monitors)
        {
            var b = monitor.Bounds;
            if (rect.Left <= b.Left && rect.Top <= b.Top && rect.Right >= b.Right && rect.Bottom >= b.Bottom) FullscreenMonitors.Add(monitor.Index);
        }
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

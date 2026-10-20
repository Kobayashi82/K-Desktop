namespace KDesktop.Assistant.Sdk;

/// <summary>
/// Things that happen on the PC, delivered to <see cref="IAssistant.OnSystemEvent"/> on the engine thread. They
/// are what lets an assistant react on its own (the Lander jumps when a notification appears, goes to sleep when
/// the user leaves…). <see cref="IWorld.Current"/> is already up to date when one arrives.
/// </summary>
public abstract record SystemEvent
{
    public sealed record WindowOpened(WindowInfo Window) : SystemEvent;

    public sealed record WindowClosed(long Handle) : SystemEvent;

    public sealed record WindowActivated(WindowInfo Window) : SystemEvent;

    /// <summary>A window moved or changed size (raised once it settles, not for every pixel).</summary>
    public sealed record WindowMoved(WindowInfo Window, PixelRect Previous) : SystemEvent;

    /// <summary>A Windows notification (toast) appeared at <paramref name="Bounds"/>.</summary>
    public sealed record NotificationShown(PixelRect Bounds) : SystemEvent;

    /// <summary>No input for <paramref name="Seconds"/> (raised at 60 s and then every few minutes).</summary>
    public sealed record UserIdle(double Seconds) : SystemEvent;

    public sealed record UserReturned(double AwaySeconds) : SystemEvent;

    public sealed record PowerChanged(bool OnBattery, bool BatteryLow, bool PowerSaver) : SystemEvent;

    public sealed record RecycleBinEmptied : SystemEvent;

    /// <summary>The user scrolled hard (wheel). <paramref name="Strength"/> 0…1, positive downwards.</summary>
    public sealed record FastScroll(double Strength) : SystemEvent;

    public sealed record ThemeChanged(bool Dark) : SystemEvent;

    /// <summary>The clock struck an hour (local time).</summary>
    public sealed record HourStruck(int Hour) : SystemEvent;

    public sealed record FullscreenChanged(IReadOnlySet<int> Monitors) : SystemEvent;

    public sealed record QuietModeChanged(bool Quiet) : SystemEvent;

    public sealed record MonitorsChanged(IReadOnlyList<MonitorInfo> Monitors) : SystemEvent;

    /// <summary>K-Desktop was paused or resumed.</summary>
    public sealed record PauseChanged(bool Paused) : SystemEvent;
}

using System.Windows;

namespace KDesktop.Assistant.Sdk;

/// <summary>A monitor: its bounds, its work area (without the taskbar) and its scaling (1.0 = 100 %).</summary>
public sealed record MonitorInfo(int Index, PixelRect Bounds, PixelRect WorkArea, double DpiScale, bool IsPrimary);

/// <summary>The minimize, maximize/restore and close buttons of a window, when they can be found.</summary>
public sealed record CaptionButtons(PixelRect Minimize, PixelRect Maximize, PixelRect Close)
{
    public PixelRect this[CaptionButton button] => button switch
    {
        CaptionButton.Minimize => Minimize,
        CaptionButton.Close => Close,
        _ => Maximize,
    };
}

public enum CaptionButton { Minimize, Maximize, Restore, Close }

/// <summary>The beat of the music playing (SDK 1.7, <see cref="IWorld.Music"/>).</summary>
/// <param name="Playing">Something with a clear beat is sounding (speech or noise do not count).</param>
/// <param name="Bpm">Beats per minute (0 while not known yet).</param>
/// <param name="Beat">Beats counted at <paramref name="At"/> (it grows by one per beat; the fraction is the phase).</param>
/// <param name="At">Engine clock (<see cref="TickInfo.Now"/>) of that count.</param>
public readonly record struct MusicBeat(bool Playing, double Bpm, double Beat, double At)
{
    /// <summary>The beat count at <paramref name="now"/> (engine clock), following the tempo since the last reading.</summary>
    public double BeatAt(double now) => Bpm > 0 ? Beat + (now - At) * Bpm / 60 : Beat;
}

/// <summary>
/// A visible top-level window as the window manager sees it.
/// </summary>
/// <param name="Handle">Window handle (HWND). Stable while the window lives.</param>
/// <param name="Bounds">Real bounds (DWM, without shadows or invisible borders).</param>
/// <param name="AppId">Process name in lower case, without «.exe».</param>
/// <param name="Elevated">
/// Belongs to an elevated process: the assistant cannot move it itself (<see cref="IWindowServices.TryMove"/>
/// returns false); K-Desktop places it when the gesture ends.
/// </param>
/// <param name="Buttons">Its caption buttons, if known (null: assume the top-right corner of the title bar).</param>
public sealed record WindowInfo(
    long Handle,
    PixelRect Bounds,
    string Title,
    string? AppId,
    bool IsMaximized = false,
    bool Elevated = false,
    CaptionButtons? Buttons = null);

/// <summary>The taskbar of one monitor and, on the main one, the notification area and the clock.</summary>
public sealed record TaskbarInfo(int Monitor, PixelRect Bounds, PixelRect? Tray, PixelRect? Clock, bool AutoHide);

/// <summary>
/// A picture of the desktop at one moment. Immutable: a new one is made whenever something changes
/// (<see cref="IWorld.Changed"/>).
/// </summary>
/// <param name="Windows">Visible top-level windows in Z order, topmost first (the assistant's own are left out).</param>
/// <param name="Timestamp">Seconds on the host clock (<see cref="TickInfo.Now"/>) when it was taken.</param>
public sealed record WorldSnapshot(
    IReadOnlyList<MonitorInfo> Monitors,
    IReadOnlyList<WindowInfo> Windows,
    IReadOnlyList<TaskbarInfo> Taskbars,
    double Timestamp)
{
    public static WorldSnapshot Empty { get; } = new([], [], [], 0);

    public MonitorInfo? PrimaryMonitor => Monitors.FirstOrDefault(m => m.IsPrimary) ?? (Monitors.Count > 0 ? Monitors[0] : null);

    public MonitorInfo? MonitorAt(Point p) => Monitors.FirstOrDefault(m => m.Bounds.Contains(p));

    public WindowInfo? Window(long handle) => Windows.FirstOrDefault(w => w.Handle == handle);
}

/// <summary>What the host knows about the user and the PC right now. Read it at any time from the engine thread.</summary>
public sealed record SystemState
{
    /// <summary>Cursor position, or null when it cannot be read (secure desktop, remote session).</summary>
    public Point? Cursor { get; init; }

    /// <summary>Smoothed cursor speed (px/s).</summary>
    public Vector CursorVelocity { get; init; }

    /// <summary>Seconds since the last keyboard or mouse input.</summary>
    public double UserIdleSeconds { get; init; }

    /// <summary>Handle of the window in front (0 if none).</summary>
    public long ActiveWindow { get; init; }

    public bool DarkMode { get; init; }
    public bool BatteryLow { get; init; }
    public bool OnBattery { get; init; }
    public bool PowerSaver { get; init; }

    /// <summary>«Do not disturb» / focus assist: stay still and silent.</summary>
    public bool QuietMode { get; init; }

    /// <summary>
    /// Monitors with a full-screen application (game, video): no manoeuvres there. With an exclusive game or a
    /// presentation all of them (the host hides its overlay then).
    /// </summary>
    public IReadOnlySet<int> FullscreenMonitors { get; init; } = new HashSet<int>();

    /// <summary>K-Desktop is paused: the assistant may keep flying, but takes no orders.</summary>
    public bool Paused { get; init; }

    public DateTime LocalTime { get; init; }
}

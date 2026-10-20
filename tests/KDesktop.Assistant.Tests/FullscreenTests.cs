using KDesktop.Assistant.Host.World;
using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Which monitors have something full screen: the topmost window that covers a monitor counts whether it has the focus
/// or not (a film on one monitor while the user works on the other), and overlays never hide it.
/// </summary>
public class FullscreenTests
{
    private static readonly MonitorInfo Main = new(0, new PixelRect(0, 0, 3440, 1440), new PixelRect(0, 0, 3440, 1400), 1, true);
    private static readonly MonitorInfo Side = new(1, new PixelRect(-1920, 278, 0, 1358), new PixelRect(-1920, 278, 0, 1318), 1, false);
    private static readonly MonitorInfo[] Monitors = [Main, Side];

    private static ScreenLayer Film => new(Main.Bounds, CanCover: true, Blocks: true);

    [Fact]
    public void AFilmWithoutTheFocusStillCounts()
    {
        // The window the user works in, on the other monitor, is above the film in Z order.
        var editor = new ScreenLayer(new PixelRect(-1900, 300, -100, 1300), CanCover: true, Blocks: true);
        Assert.Equal([0], SystemStatus.CoveredMonitors([editor, Film], Monitors));
    }

    [Fact]
    public void AWindowOverPartOfTheMonitorMeansItIsNotFullScreen()
    {
        var window = new ScreenLayer(new PixelRect(400, 300, 1600, 1000), CanCover: true, Blocks: true);
        Assert.Empty(SystemStatus.CoveredMonitors([window, Film], Monitors));
    }

    [Fact]
    public void OverlaysAndAlwaysOnTopWindowsDoNotHideIt()
    {
        // A picture-in-picture or sticky notes («always on top») and a see-through overlay over the whole monitor.
        var pip = new ScreenLayer(new PixelRect(3000, 1000, 3400, 1300), CanCover: true, Blocks: false);
        var overlay = new ScreenLayer(Main.Bounds, CanCover: false, Blocks: false);
        Assert.Equal([0], SystemStatus.CoveredMonitors([overlay, pip, Film], Monitors));
    }

    [Fact]
    public void AMaximizedWindowIsNotFullScreen()
    {
        // TryLayer marks a maximized window with its title bar as unable to cover; with a hidden taskbar it would.
        var maximized = new ScreenLayer(Main.Bounds, CanCover: false, Blocks: true);
        Assert.Empty(SystemStatus.CoveredMonitors([maximized, Film], Monitors));
    }

    [Fact]
    public void OneWindowCanDecideBothMonitors()
    {
        var wide = new ScreenLayer(new PixelRect(-1920, 0, 3440, 1440), CanCover: true, Blocks: true);
        Assert.Equal([0, 1], SystemStatus.CoveredMonitors([wide], Monitors).Order());
        var half = new ScreenLayer(new PixelRect(-1000, 300, 1000, 1200), CanCover: true, Blocks: true);
        Assert.Empty(SystemStatus.CoveredMonitors([half, Film], Monitors));
    }
}

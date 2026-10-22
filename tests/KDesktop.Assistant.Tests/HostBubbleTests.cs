using System.Windows;
using System.Windows.Media;
using KDesktop.Assistant.Host.Rendering;
using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 3.6: the module's bubble follows its character, stays in its monitor and goes when its time is up.</summary>
public class HostBubbleTests
{
    private static readonly MonitorInfo Monitor = new(0, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1.25, true);

    [Fact]
    public void ItFollowsTheAnchorAndFadesOut()
    {
        var surface = new FakeSurface();
        using var bubble = new HostBubble(surface, "Te aviso a las 17:42.", dark: true, now: 0, seconds: 3);

        // Popping in: every frame.
        Assert.Equal(1 / 60.0, bubble.Update(0.05, new Point(800, 600), Monitor, 1 / 60.0), 6);
        var first = Assert.Single(surface.Areas);
        Assert.True(first.Bottom <= 600 + 15 && first.Top < 600 - 40, "Above the character (its area has room for the shadow)");
        Assert.True(first.Width > 100 && first.Height > 30);

        // Settled and still: nothing new is drawn.
        bubble.Update(1, new Point(800, 600), Monitor, 1 / 60.0);
        var count = surface.Areas.Count;
        bubble.Update(1.2, new Point(800, 600), Monitor, 1 / 60.0);
        Assert.Equal(count, surface.Areas.Count);

        // The character moves: the bubble goes with it.
        bubble.Update(1.3, new Point(900, 600), Monitor, 1 / 60.0);
        Assert.Equal(100, surface.Areas[^1].Left - surface.Areas[count - 1].Left);

        // Hidden character: no bubble.
        bubble.Update(1.4, null, Monitor, 1 / 60.0);
        Assert.True(surface.Hidden);

        Assert.False(bubble.IsOver(3.1));
        Assert.True(bubble.IsOver(3.3));
    }

    [Fact]
    public void ItGoesBelowAtTheTopAndStaysInsideTheWorkArea()
    {
        var surface = new FakeSurface();
        using var bubble = new HostBubble(surface, "Una respuesta bastante larga para que el bocadillo ocupe varias líneas y no quepa encima.", false, 0, 5);
        bubble.Update(1, new Point(5, 20), Monitor, 1 / 60.0);
        var area = surface.Areas[^1];
        Assert.True(area.Top >= 20 - 20, "Below the anchor");
        Assert.True(area.Left >= -20, "Inside the monitor (but for the shadow)");
    }

    [Fact]
    public void ClosingEndsItSoon()
    {
        using var bubble = new HostBubble(new FakeSurface(), "Hola", false, 0, 10);
        bubble.Close(2);
        Assert.True(bubble.IsOver(2.3));
    }

    private sealed class FakeSurface : ISurface
    {
        public List<PixelRect> Areas { get; } = [];

        public bool Hidden { get; private set; }

        public bool IsVisible => !Hidden && Areas.Count > 0;

        public void Present(PixelRect area, Action<DrawingContext> draw)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) draw(dc);
            Areas.Add(area);
            Hidden = false;
        }

        public void Hide() => Hidden = true;

        public void Dispose() { }
    }
}

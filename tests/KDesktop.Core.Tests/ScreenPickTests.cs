using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Tests;

/// <summary>E3 (5.0.383): «Cuentagotas» writes the colour as asked, «Regla» measures between the edges.</summary>
public class ScreenPickTests
{
    [Theory]
    [InlineData(30, 144, 255, ColorFormat.Hex, "#1E90FF")]
    [InlineData(30, 144, 255, ColorFormat.Rgb, "rgb(30, 144, 255)")]
    [InlineData(30, 144, 255, ColorFormat.Hsl, "hsl(210, 100%, 56%)")]
    [InlineData(0, 0, 0, ColorFormat.Hsl, "hsl(0, 0%, 0%)")]
    [InlineData(255, 0, 0, ColorFormat.Hsl, "hsl(0, 100%, 50%)")]
    [InlineData(255, 0, 255, ColorFormat.Hsl, "hsl(300, 100%, 50%)")]
    public void TheColourAsAsked(byte r, byte g, byte b, ColorFormat format, string text) =>
        Assert.Equal(text, new PixelColor(r, g, b).Format(format));

    /// <summary>A white picture with a grey box from (10, 20) to (49, 39).</summary>
    private static BgraImage Picture()
    {
        const int w = 100, h = 60;
        var pixels = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var grey = x is >= 10 and < 50 && y is >= 20 and < 40;
                var v = (byte)(grey ? 128 : 255);
                var i = (y * w + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = v;
                pixels[i + 3] = 255;
            }
        // A pixel a little different inside the box still counts as the box (antialiasing).
        pixels[(25 * w + 30) * 4] = 134;
        return new BgraImage(w, h, pixels);
    }

    [Fact]
    public void InsideABoxItsSizeAndOutsideToTheEdges()
    {
        var image = Picture();
        var all = new ScreenRect(0, 0, image.Width, image.Height);
        Assert.Equal((10, 20, 49, 39), ScreenMeasure.Spacing(image, 25, 30, all));
        // Above the box: across, the whole picture; down, until the box.
        Assert.Equal((0, 0, 99, 19), ScreenMeasure.Spacing(image, 30, 5, all));
        // Only within the monitor it is on.
        Assert.Equal((60, 0, 79, 59), ScreenMeasure.Spacing(image, 70, 10, new ScreenRect(60, 0, 20, 60)));
        Assert.Equal("40 × 20", ScreenMeasure.Text(49 - 10 + 1, 39 - 20 + 1));
    }

    [Fact]
    public void TheyGiveAResultAndAreSaved()
    {
        Assert.True(new ColorPickerAction().GivesResult);
        Assert.True(new RulerAction().GivesResult);
        var json = Json.Serialize<CommandAction>(new ColorPickerAction { Format = ColorFormat.Rgb });
        Assert.Equal(ColorFormat.Rgb, Assert.IsType<ColorPickerAction>(Json.Deserialize<CommandAction>(json)).Format);
        Assert.IsType<RulerAction>(Json.Deserialize<CommandAction>(Json.Serialize<CommandAction>(new RulerAction())));
    }
}

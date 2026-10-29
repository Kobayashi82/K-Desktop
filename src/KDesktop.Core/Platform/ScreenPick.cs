using System.Globalization;
using KDesktop.Core.Model;

namespace KDesktop.Core.Platform;

/// <summary>The colour of one pixel of the screen.</summary>
public readonly record struct PixelColor(byte R, byte G, byte B)
{
    /// <summary>Pixel (<paramref name="x"/>, <paramref name="y"/>) of <paramref name="image"/> (clamped to it).</summary>
    public static PixelColor At(BgraImage image, int x, int y)
    {
        var i = Math.Clamp(y, 0, image.Height - 1) * image.Stride + Math.Clamp(x, 0, image.Width - 1) * 4;
        return new PixelColor(image.Pixels[i + 2], image.Pixels[i + 1], image.Pixels[i]);
    }

    /// <summary>As «Cuentagotas» copies it (always the same way, whatever the language: it is for pasting in code).</summary>
    public string Format(ColorFormat format) => format switch
    {
        ColorFormat.Rgb => string.Create(CultureInfo.InvariantCulture, $"rgb({R}, {G}, {B})"),
        ColorFormat.Hsl => Hsl(),
        _ => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}"),
    };

    private string Hsl()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        var l = (max + min) / 2;
        double h = 0, s = 0;
        if (delta > 0)
        {
            s = delta / (1 - Math.Abs(2 * l - 1));
            h = max == r ? 60 * ((g - b) / delta % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
            if (h < 0) h += 360;
        }
        return string.Create(CultureInfo.InvariantCulture, $"hsl({Math.Round(h) % 360}, {Math.Round(s * 100)}%, {Math.Round(l * 100)}%)");
    }

    /// <summary>Text on it should be dark (a light colour) or light.</summary>
    public bool IsLight => 0.299 * R + 0.587 * G + 0.114 * B > 150;
}

/// <summary>«Regla» (E3): how far the same colour goes around a pixel, which is the distance between the edges there.</summary>
public static class ScreenMeasure
{
    /// <summary>How different a pixel can be (in each of red, green and blue) and still be «the same» (antialiasing, gradients).</summary>
    public const int Tolerance = 12;

    /// <summary>
    /// From (<paramref name="x"/>, <paramref name="y"/>) to the left, right, up and down while the colour stays like that
    /// pixel's, within <paramref name="limit"/> (picture coordinates, the monitor it is on): the first and last pixel of
    /// each line, both inside. A plain area to the end of the monitor goes to the end.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom) Spacing(BgraImage image, int x, int y, ScreenRect limit, int tolerance = Tolerance)
    {
        int minX = Math.Max(0, limit.Left), maxX = Math.Min(image.Width, limit.Right) - 1;
        int minY = Math.Max(0, limit.Top), maxY = Math.Min(image.Height, limit.Bottom) - 1;
        x = Math.Clamp(x, minX, maxX);
        y = Math.Clamp(y, minY, maxY);
        var start = PixelColor.At(image, x, y);
        bool Same(int px, int py)
        {
            var c = PixelColor.At(image, px, py);
            return Math.Abs(c.R - start.R) <= tolerance && Math.Abs(c.G - start.G) <= tolerance && Math.Abs(c.B - start.B) <= tolerance;
        }
        int left = x, right = x, top = y, bottom = y;
        while (left > minX && Same(left - 1, y)) left--;
        while (right < maxX && Same(right + 1, y)) right++;
        while (top > minY && Same(x, top - 1)) top--;
        while (bottom < maxY && Same(x, bottom + 1)) bottom++;
        return (left, top, right, bottom);
    }

    /// <summary>What «Regla» copies: «320 × 200».</summary>
    public static string Text(int width, int height) => string.Create(CultureInfo.InvariantCulture, $"{width} × {height}");
}

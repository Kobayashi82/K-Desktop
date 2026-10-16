using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KDesktop.Services;

public static partial class IconProvider
{
    // Results per source image for the light theme (a weak table: icons dropped from the cache are not kept alive).
    private static ConditionalWeakTable<ImageSource, ImageSource> _lightVersions = new();

    static IconProvider()
    {
        ThemeService.Changed += () =>
        {
            _lightVersions = new();
            BumpRevision();
        };
    }

    /// <summary>
    /// Many icons people pick are white glyphs made for dark backgrounds; on the light theme they all but vanish. In
    /// the light theme such an icon (almost every visible pixel light and colourless) is shown inverted, so it reads
    /// dark on light. Coloured icons are left alone.
    /// </summary>
    private static ImageSource? ForTheme(ImageSource? source)
    {
        var light = _background switch { IconBackground.Dark => false, IconBackground.Light => true, _ => !ThemeService.IsDark };
        if (source is not BitmapSource bitmap || !light) return source;
        return _lightVersions.GetValue(bitmap, _ => IsLightMonochrome(bitmap) ? Inverted(bitmap) : bitmap);
    }

    public enum IconBackground { App, Dark, Light }

    [ThreadStatic] private static IconBackground _background;

    /// <summary>
    /// Icons for a background other than the app's own theme (the web app has its own): runs <paramref name="get"/>
    /// with every theme adaptation done for <paramref name="background"/>.
    /// </summary>
    public static T For<T>(IconBackground background, Func<T> get)
    {
        var previous = _background;
        _background = background;
        try { return get(); }
        finally { _background = previous; }
    }

    private static byte[]? Pixels(BitmapSource source, out int width, out int height)
    {
        width = source.PixelWidth;
        height = source.PixelHeight;
        if (width <= 0 || height <= 0 || width * height > 512 * 512) return null;
        try
        {
            var converted = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[width * height * 4];
            converted.CopyPixels(pixels, width * 4, 0);
            return pixels;
        }
        catch { return null; }
    }

    private static bool IsLightMonochrome(BitmapSource source)
    {
        if (Pixels(source, out _, out _) is not { } px) return false;
        // Weighted by opacity: average lightness and average colourfulness (max - min channel) of the visible pixels.
        double total = 0, luminance = 0, chroma = 0;
        for (var i = 0; i < px.Length; i += 4)
        {
            var a = px[i + 3] / 255.0;
            if (a < 0.1) continue;
            int b = px[i], g = px[i + 1], r = px[i + 2];
            total += a;
            luminance += a * (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
            chroma += a * (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)));
        }
        return total > 0 && chroma / total < 20 && luminance / total > 0.62;
    }

    private static ImageSource Inverted(BitmapSource source)
    {
        if (Pixels(source, out var w, out var h) is not { } px) return source;
        for (var i = 0; i < px.Length; i += 4)
        {
            // Premultiplied-free BGRA: invert the colour, keep the transparency.
            px[i] = (byte)(255 - px[i]);
            px[i + 1] = (byte)(255 - px[i + 1]);
            px[i + 2] = (byte)(255 - px[i + 2]);
        }
        var result = BitmapSource.Create(w, h, source.DpiX, source.DpiY, PixelFormats.Bgra32, null, px, w * 4);
        result.Freeze();
        return result;
    }
}

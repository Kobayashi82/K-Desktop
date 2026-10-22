using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace KDesktop.Core.Platform;

/// <summary>A language Windows can read text in (its OCR is installed with the language's optional features).</summary>
/// <param name="Tag">«es-ES», «en-US».</param>
/// <param name="Name">For people, in that language («español (España, alfabetización internacional)»).</param>
public sealed record OcrLanguage(string Tag, string Name);

/// <summary>The text read from a picture: one line per line seen, a blank line between paragraphs; empty: no text.</summary>
public sealed record ReadText(string Text, int Lines, string Language);

/// <summary>
/// The text of a picture of the screen with the OCR of Windows (<c>Windows.Media.Ocr</c>, stage 10.1), without any AI:
/// the same that PowerToys' Text Extractor uses. Small pictures are enlarged first (screen text is tiny for it) and a
/// big gap between lines makes a new paragraph. Thread-safe; nothing is kept.
/// </summary>
public static partial class ScreenText
{
    /// <summary>
    /// How much bigger the picture is read: screen text is tiny for it. Measured with Segoe UI at 9 pt: 19 of 39 words
    /// right as it is, 33 twice as big, no better three times; a whole 2560×1440 screen takes the same time (~0,55 s).
    /// </summary>
    private const double Enlarge = 2;

    /// <summary>The languages with OCR on this PC; empty when none is installed.</summary>
    public static IReadOnlyList<OcrLanguage> Languages
    {
        get
        {
            try { return [.. OcrEngine.AvailableRecognizerLanguages.Select(l => new OcrLanguage(l.LanguageTag, l.NativeName))]; }
            catch (Exception ex) when (ex is COMException or TypeLoadException or PlatformNotSupportedException) { return []; }
        }
    }

    /// <summary>Whether some text can be read (an OCR language is installed).</summary>
    public static bool Available => Languages.Count > 0;

    /// <summary>
    /// Reads <paramref name="image"/> in <paramref name="language"/> («es», «es-ES»; null or empty: the user's
    /// languages of Windows, else the first installed). Null when there is no OCR at all.
    /// </summary>
    public static async Task<ReadText?> ReadAsync(BgraImage image, string? language = null, bool singleLine = false, CancellationToken cancellationToken = default)
    {
        if (EngineFor(language) is not { } engine) return null;
        var max = (double)OcrEngine.MaxImageDimension;
        var factor = Math.Min(Enlarge, Math.Min(max / image.Width, max / image.Height));
        var picture = Math.Abs(factor - 1) < 0.01 ? image : Resize(image, factor);

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(picture.Pixels.AsBuffer(), BitmapPixelFormat.Bgra8, picture.Width, picture.Height, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
        var tag = engine.RecognizerLanguage.LanguageTag;
        var lines = result.Lines.Select(line => new Line(Join(line, tag), Bounds(line))).Where(l => l.Text.Length > 0).ToList();
        return new ReadText(Layout(lines, singleLine), lines.Count, tag);
    }

    /// <summary>The engine for <paramref name="language"/>: that one installed, a region of it («es» → «es-ES»), the user's, the first.</summary>
    private static OcrEngine? EngineFor(string? language)
    {
        try
        {
            var installed = OcrEngine.AvailableRecognizerLanguages;
            if (installed.Count == 0) return null;
            if (!string.IsNullOrWhiteSpace(language))
            {
                var wanted = language.Trim();
                var chosen = installed.FirstOrDefault(l => string.Equals(l.LanguageTag, wanted, StringComparison.OrdinalIgnoreCase))
                             ?? installed.FirstOrDefault(l => l.LanguageTag.Split('-')[0].Equals(wanted.Split('-')[0], StringComparison.OrdinalIgnoreCase));
                if (chosen is not null && OcrEngine.TryCreateFromLanguage(chosen) is { } own) return own;
            }
            return OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Language(installed[0].LanguageTag));
        }
        catch (Exception ex) when (ex is COMException or TypeLoadException or PlatformNotSupportedException)
        {
            Log.Warn($"OCR: no disponible ({ex.Message})");
            return null;
        }
    }

    private sealed record Line(string Text, (double Top, double Bottom) Span);

    /// <summary>Chinese and Japanese words go together; the rest with a space, and tidied.</summary>
    private static string Join(OcrLine line, string language)
    {
        var together = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) || language.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
        return together ? string.Concat(line.Words.Select(w => w.Text)).Trim() : Tidy(line.Text.Trim());
    }

    /// <summary>
    /// What Windows' OCR gets wrong again and again with screen fonts: «EI» for «El» before a word in lower case, and a
    /// letter O, I or l among figures («v5.O.143», «1l0»).
    /// </summary>
    public static string Tidy(string line)
    {
        line = CapitalEI().Replace(line, "El");
        line = LetterO().Replace(line, "0");
        return LetterOne().Replace(line, "1");
    }

    [GeneratedRegex(@"\bEI(?= \p{Ll})")]
    private static partial Regex CapitalEI();

    [GeneratedRegex(@"(?<=\d[.,:]?)O(?=[.,:]?\d)")]
    private static partial Regex LetterO();

    [GeneratedRegex(@"(?<=\d[.,]?)[Il](?=[.,]?\d)")]
    private static partial Regex LetterOne();

    private static (double, double) Bounds(OcrLine line)
    {
        double top = double.MaxValue, bottom = double.MinValue;
        foreach (var word in line.Words)
        {
            top = Math.Min(top, word.BoundingRect.Top);
            bottom = Math.Max(bottom, word.BoundingRect.Bottom);
        }
        return top <= bottom ? (top, bottom) : (0, 0);
    }

    /// <summary>One line each; a blank line where the gap above is taller than most lines (a new paragraph, another part of the window).</summary>
    private static string Layout(List<Line> lines, bool singleLine)
    {
        if (singleLine) return string.Join(" ", lines.Select(l => l.Text));
        if (lines.Count == 0) return string.Empty;
        var heights = lines.Select(l => l.Span.Bottom - l.Span.Top).Order().ToList();
        var typical = Math.Max(1, heights[heights.Count / 2]);
        var text = new StringBuilder(lines[0].Text);
        for (var i = 1; i < lines.Count; i++)
        {
            var gap = lines[i].Span.Top - lines[i - 1].Span.Bottom;
            text.Append(gap > typical * 0.9 ? "\n\n" : "\n").Append(lines[i].Text);
        }
        return text.ToString();
    }

    /// <summary>Bilinear resize (both ways), alpha set opaque.</summary>
    public static BgraImage Resize(BgraImage image, double factor)
    {
        int width = Math.Max(1, (int)Math.Round(image.Width * factor)), height = Math.Max(1, (int)Math.Round(image.Height * factor));
        var source = image.Pixels;
        var pixels = new byte[width * height * 4];
        double sx = (double)image.Width / width, sy = (double)image.Height / height;
        Parallel.For(0, height, y =>
        {
            var fy = Math.Max(0, (y + 0.5) * sy - 0.5);
            int y0 = Math.Min((int)fy, image.Height - 1), y1 = Math.Min(y0 + 1, image.Height - 1);
            var wy = fy - y0;
            for (var x = 0; x < width; x++)
            {
                var fx = Math.Max(0, (x + 0.5) * sx - 0.5);
                int x0 = Math.Min((int)fx, image.Width - 1), x1 = Math.Min(x0 + 1, image.Width - 1);
                var wx = fx - x0;
                int a = (y0 * image.Width + x0) * 4, b = (y0 * image.Width + x1) * 4, c = (y1 * image.Width + x0) * 4, d = (y1 * image.Width + x1) * 4;
                var o = (y * width + x) * 4;
                for (var k = 0; k < 3; k++)
                {
                    var top = source[a + k] + (source[b + k] - source[a + k]) * wx;
                    var bottom = source[c + k] + (source[d + k] - source[c + k]) * wx;
                    pixels[o + k] = (byte)Math.Clamp((int)Math.Round(top + (bottom - top) * wy), 0, 255);
                }
                pixels[o + 3] = 255;
            }
        });
        return new BgraImage(width, height, pixels);
    }
}

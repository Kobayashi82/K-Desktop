using System.Runtime.InteropServices;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core;
using KDesktop.Core.Platform;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace KDesktop.Assistant.Tools.Knowledge;

/// <summary>The text of a document of the index, and whether some of it was read from pictures.</summary>
public sealed record KnowledgeText(FileContent Content, bool Ocr);

/// <summary>
/// The files of the user's folders the assistants learn from (stage 19.2), and their text: plain text, Markdown and code,
/// web pages, Word, PowerPoint and PDF with text (<see cref="FileText"/>, PdfPig), and with the OCR of Windows, pictures and
/// the scanned pages of a PDF (rendered by Windows' own PDF reader). Anything else (programs, videos, music, archives) is not
/// looked at.
/// </summary>
public static class KnowledgeReader
{
    /// <summary>Texts and documents: read whole.</summary>
    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".text", ".md", ".markdown", ".mdx", ".rst", ".org", ".adoc", ".tex", ".srt", ".vtt", ".eml",
        ".pdf", ".docx", ".docm", ".dotx", ".pptx", ".pptm", ".html", ".htm", ".xhtml",
    };

    /// <summary>Code and data: also read, unless they are big (a dump of data is not a document).</summary>
    private static readonly HashSet<string> Code = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".vb", ".fs", ".py", ".js", ".mjs", ".ts", ".tsx", ".jsx", ".java", ".kt", ".c", ".h", ".cpp", ".hpp", ".cc",
        ".go", ".rs", ".rb", ".php", ".swift", ".sql", ".sh", ".ps1", ".psm1", ".bat", ".cmd", ".lua", ".r", ".scala", ".dart",
        ".css", ".scss", ".xaml", ".xml", ".json", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".csv", ".tsv",
    };

    /// <summary>Pictures, read only with the OCR (a photo without text is kept as «nothing to read»).</summary>
    private static readonly HashSet<string> Pictures = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp", ".gif" };

    /// <summary>The biggest code or data file read: bigger ones are data, not something to ask about.</summary>
    public const long MaxCodeBytes = 2L << 20;

    /// <summary>The biggest picture read (a photo straight from a camera can be 20 MB).</summary>
    public const long MaxPictureBytes = 30L << 20;

    /// <summary>A page of a PDF with fewer letters than this is a scanned one: its picture is read.</summary>
    public const int ScannedPage = 40;

    /// <summary>Scanned pages read in one PDF at most (a scanned book takes long; the first ones say what it is).</summary>
    public const int MostScannedPages = 200;

    /// <summary>Whether <paramref name="file"/> is something to learn from (by its kind and size; pictures only with the OCR).</summary>
    public static bool Takes(FileInfo file, bool ocr)
    {
        var extension = file.Extension;
        if (file.Name.StartsWith('~') || file.Name.StartsWith('.')) return false;
        if (Documents.Contains(extension)) return file.Length <= FileText.MaxBytes;
        if (Code.Contains(extension)) return file.Length <= MaxCodeBytes && !file.Name.Contains(".min.", StringComparison.OrdinalIgnoreCase);
        return ocr && Pictures.Contains(extension) && file.Length is > 8 * 1024 and <= MaxPictureBytes;
    }

    public static bool IsPicture(string path) => Pictures.Contains(Path.GetExtension(path));

    /// <summary>
    /// Its text: empty when it has none, null when it could not be read. <paramref name="ocr"/> null: pictures and scanned
    /// pages are left without text.
    /// </summary>
    public static async Task<KnowledgeText?> ReadAsync(string path, IScreenReader? ocr, CancellationToken cancellationToken)
    {
        if (IsPicture(path))
        {
            if (ocr is not { CanRead: true }) return new KnowledgeText(new FileContent(string.Empty, [0]), false);
            var picture = await PictureFile.LoadAsync(path, 2000, cancellationToken).ConfigureAwait(false);
            if (picture is null) return null;
            var read = await ocr.ReadAsync(picture.Value.Pixels, cancellationToken).ConfigureAwait(false);
            return new KnowledgeText(new FileContent(read?.Text ?? string.Empty, [0]), true);
        }
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            return await Task.Run(() => FileText.Read(path), cancellationToken).ConfigureAwait(false) is { } content ? new KnowledgeText(content, false) : null;

        List<string> pages;
        try { pages = await Task.Run(() => FileText.PdfPages(path), cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A PDF PdfPig cannot open (damaged, protected): Windows' reader may still render it.
            Log.Warn($"Asistente: PdfPig no abre {Path.GetFileName(path)}: {ex.Message}");
            pages = [];
        }
        var scanned = Enumerable.Range(0, pages.Count).Where(i => pages[i].Count(c => !char.IsWhiteSpace(c)) < ScannedPage).ToList();
        var usedOcr = false;
        if ((scanned.Count > 0 || pages.Count == 0) && ocr is { CanRead: true })
        {
            var read = await ScannedPagesAsync(path, pages, scanned, ocr, cancellationToken).ConfigureAwait(false);
            if (read is null && pages.Count == 0) return null;
            usedOcr = read > 0;
        }
        return new KnowledgeText(FileText.Join(pages), usedOcr);
    }

    /// <summary>
    /// Reads the pictures of <paramref name="scanned"/> pages (all of them when PdfPig could not open it) into
    /// <paramref name="pages"/>; how many had text, or null when Windows could not open it either.
    /// </summary>
    private static async Task<int?> ScannedPagesAsync(string path, List<string> pages, List<int> scanned, IScreenReader ocr, CancellationToken cancellationToken)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(cancellationToken).ConfigureAwait(false);
            var document = await PdfDocument.LoadFromFileAsync(file).AsTask(cancellationToken).ConfigureAwait(false);
            if (pages.Count == 0)
            {
                pages.AddRange(Enumerable.Repeat(string.Empty, (int)Math.Min(document.PageCount, 2000)));
                scanned = [.. Enumerable.Range(0, pages.Count)];
            }
            var read = 0;
            foreach (var index in scanned.Take(MostScannedPages))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (index >= document.PageCount) break;
                using var page = document.GetPage((uint)index);
                if (await RenderAsync(page, cancellationToken).ConfigureAwait(false) is not { } pixels) continue;
                var text = await ocr.ReadAsync(pixels, cancellationToken).ConfigureAwait(false);
                if (text is { Text.Length: > 0 })
                {
                    pages[index] = text.Text;
                    read++;
                }
            }
            return read;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Warn($"Asistente: Windows no abre {Path.GetFileName(path)} para leer sus páginas escaneadas: {ex.Message}");
            return null;
        }
    }

    /// <summary>A page as a picture some 1600 pixels wide (some 190 ppp for an A4: enough for the OCR, which doubles it).</summary>
    private static async Task<BgraImage?> RenderAsync(PdfPage page, CancellationToken cancellationToken)
    {
        using var stream = new InMemoryRandomAccessStream();
        var width = (uint)Math.Clamp(page.Size.Width * 2.2, 800, 1600);
        await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = width }).AsTask(cancellationToken).ConfigureAwait(false);
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken).ConfigureAwait(false);
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(cancellationToken).ConfigureAwait(false);
        return new BgraImage((int)decoder.PixelWidth, (int)decoder.PixelHeight, data.DetachPixelData());
    }
}

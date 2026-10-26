using System.Runtime.InteropServices;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core;
using KDesktop.Core.Assistant;
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
    /// <summary>The biggest code or data file read: bigger ones are data, not something to ask about.</summary>
    public const long MaxCodeBytes = 2L << 20;

    /// <summary>The biggest picture read (a photo straight from a camera can be 20 MB).</summary>
    public const long MaxPictureBytes = 30L << 20;

    /// <summary>A page of a PDF with fewer letters than this is a scanned one: its picture is read.</summary>
    public const int ScannedPage = 40;

    /// <summary>Scanned pages read in one PDF at most (a scanned book takes long; the first ones say what it is).</summary>
    public const int MostScannedPages = 200;

    /// <summary>
    /// Whether <paramref name="file"/> is something to learn from (by its kind and size; pictures only with the OCR; the lists
    /// are <see cref="KnowledgeFolders"/>'s). One the user <paramref name="chose"/> by itself is read whatever its size (up to
    /// what can be read): in a folder, big data files, minified code and tiny pictures (icons) are left out.
    /// </summary>
    public static bool Takes(FileInfo file, bool ocr, bool chose = false)
    {
        var extension = file.Extension;
        if (!chose && (file.Name.StartsWith('~') || file.Name.StartsWith('.'))) return false;
        if (KnowledgeFolders.Documents.Contains(extension)) return file.Length <= FileText.MaxBytes;
        if (KnowledgeFolders.Code.Contains(extension))
            return chose ? file.Length <= FileText.MaxBytes
                : file.Length <= MaxCodeBytes && !file.Name.Contains(".min.", StringComparison.OrdinalIgnoreCase) && !LockFiles.Contains(file.Name);
        return ocr && KnowledgeFolders.Pictures.Contains(extension) && file.Length <= MaxPictureBytes && (chose || file.Length > 8 * 1024);
    }

    public static bool IsPicture(string path) => KnowledgeFolders.Pictures.Contains(Path.GetExtension(path));

    /// <summary>Lines this long on average are not written by a person: minified or generated code.</summary>
    private const int MinifiedLine = 500;

    /// <summary>Code of some size whose lines are, on average, longer than <see cref="MinifiedLine"/>.</summary>
    public static bool IsMinified(string text)
    {
        if (text.Length < 4 * MinifiedLine) return false;
        var lines = 1 + text.AsSpan().Count('\n');
        return text.Length / lines > MinifiedLine;
    }

    /// <summary>Code of others kept in a project (libraries) and what building makes: not the user's to ask about.</summary>
    private static readonly HashSet<string> LibraryFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "third_party", "thirdparty", "3rdparty", "third-party", "vendor", "vendors", "deps", "_deps", "external", "extern", "dist", "build", "target",
    };

    /// <summary>The lists of versions package managers write: long and nothing to ask about.</summary>
    private static readonly HashSet<string> LockFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml", "composer.lock", "Cargo.lock", "poetry.lock", "Pipfile.lock", "Gemfile.lock", "packages.lock.json",
    };

    /// <summary>A folder of a chosen one that is never walked into: programs', libraries', builds' or a saved page's.</summary>
    public static bool SkipsFolder(DirectoryInfo folder) =>
        FileSearch.IsProgramsFolder(folder.Name) || LibraryFolders.Contains(folder.Name) || IsSavedPageFolder(folder);

    /// <summary>
    /// The folder of the pictures, styles and scripts of a web page saved with the browser («Página_files» next to
    /// «Página.html»): the page is read; that, never.
    /// </summary>
    public static bool IsSavedPageFolder(DirectoryInfo folder)
    {
        if (!folder.Name.EndsWith("_files", StringComparison.OrdinalIgnoreCase) || folder.Parent is not { } parent) return false;
        var page = folder.Name[..^"_files".Length];
        return File.Exists(Path.Combine(parent.FullName, page + ".html")) || File.Exists(Path.Combine(parent.FullName, page + ".htm"));
    }

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
        {
            if (await Task.Run(() => FileText.Read(path), cancellationToken).ConfigureAwait(false) is not { } content) return null;
            // Minified code (a whole stylesheet on one line) is no text to ask about, and would be hundreds of pieces.
            if (KnowledgeFolders.Code.Contains(Path.GetExtension(path)) && IsMinified(content.Text))
            {
                Log.Info($"Asistente: lo que sabe deja {Path.GetFileName(path)} sin texto: es código minificado");
                return new KnowledgeText(new FileContent(string.Empty, [0]), false);
            }
            return new KnowledgeText(content, false);
        }

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

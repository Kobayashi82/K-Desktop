using System.IO.Compression;
using System.Text;
using System.Xml;
using KDesktop.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// The text of a file and where each of its pages starts in it (a PDF, a presentation's slides); one page for the rest.
/// </summary>
public sealed record FileContent(string Text, IReadOnlyList<int> PageStarts)
{
    /// <summary>The page (1…) that the character at <paramref name="index"/> is on.</summary>
    public int PageAt(int index)
    {
        var page = 1;
        for (var i = 1; i < PageStarts.Count && PageStarts[i] <= index; i++) page = i + 1;
        return page;
    }

    public int Pages => Math.Max(1, PageStarts.Count);
}

/// <summary>
/// Reading the text of a file the user gives an assistant (stage 14.1): plain text and code in any encoding, web pages,
/// Word (.docx), PowerPoint (.pptx) and PDF with text (PdfPig). Pictures are not read here (the OCR or the model's vision
/// does them); a scanned PDF has no text.
/// </summary>
public static class FileText
{
    /// <summary>The biggest file read whole.</summary>
    public const long MaxBytes = 64L << 20;

    /// <summary>The most text kept of one file (a book is some 500 000 letters).</summary>
    public const int MaxChars = 2_000_000;

    public static readonly IReadOnlySet<string> Pictures = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic", ".heif" };

    public static bool IsPicture(string path) => Pictures.Contains(Path.GetExtension(path));

    /// <summary>Its text, or null when it is not something with text (a video, a program, a zip…) or cannot be read.</summary>
    public static FileContent? Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBytes) return null;
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".pdf" => Pdf(path),
                ".docx" or ".docm" or ".dotx" => Office(path, "word/document.xml", paragraph: "p", text: "t"),
                ".pptx" or ".pptm" => Slides(path),
                ".html" or ".htm" or ".xhtml" => One(WebText.Of(Decode(File.ReadAllBytes(path)) ?? string.Empty, MaxChars).Text),
                _ when IsPicture(path) || FileKinds.Runs.Contains(Path.GetExtension(path)) && !IsScript(path) => null,
                _ => Decode(File.ReadAllBytes(path)) is { } text ? One(text) : null,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or XmlException
                                       or InvalidOperationException or ArgumentException or FormatException or NotSupportedException)
        {
            Log.Warn($"Asistente: no se ha podido leer {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Scripts are text to read (never run).</summary>
    private static bool IsScript(string path) => Path.GetExtension(path).ToLowerInvariant() is ".bat" or ".cmd" or ".ps1" or ".psm1" or ".vbs" or ".js" or ".py" or ".sh" or ".reg" or ".inf";

    private static FileContent One(string text) => new(Cap(text), [0]);

    private static string Cap(string text) => text.Length > MaxChars ? text[..MaxChars] : text;

    /// <summary>
    /// Bytes as text: with its BOM, else UTF-8 when it is valid, else Windows-1252 (Spanish text from old programs); null
    /// when they look binary (zero bytes in the first 8 KB).
    /// </summary>
    internal static string? Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF))
            return new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true).ReadToEnd();
        var head = bytes.AsSpan(0, Math.Min(bytes.Length, 8192));
        if (head.Contains((byte)0)) return null;
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿'); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    private static FileContent Pdf(string path) => Join(PdfPages(path));

    /// <summary>The text of each page of a PDF, in order (empty for a scanned one); up to <see cref="MaxChars"/> in all.</summary>
    internal static List<string> PdfPages(string path)
    {
        using var document = PdfDocument.Open(path);
        var pages = new List<string>();
        var total = 0;
        foreach (var page in document.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page).Trim();
            pages.Add(text);
            total += text.Length + 2;
            if (total > MaxChars) break;
        }
        return pages;
    }

    /// <summary>Pages one after another, a blank line between them, and where each one starts.</summary>
    internal static FileContent Join(IReadOnlyList<string> pages)
    {
        var text = new StringBuilder();
        var starts = new List<int>();
        foreach (var page in pages)
        {
            starts.Add(text.Length);
            text.Append(page).Append("\n\n");
        }
        return new FileContent(Cap(text.ToString()), starts.Count > 0 ? starts : [0]);
    }

    /// <summary>A part of an Office file: its paragraphs (<paramref name="paragraph"/>) with the text of their runs.</summary>
    private static FileContent Office(string path, string part, string paragraph, string text)
    {
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry(part) ?? throw new InvalidDataException("no " + part);
        return One(Paragraphs(entry, paragraph, text));
    }

    private static FileContent Slides(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var slides = zip.Entries.Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => int.TryParse(Path.GetFileNameWithoutExtension(e.Name)[5..], out var n) ? n : int.MaxValue).ToList();
        var all = new StringBuilder();
        var starts = new List<int>();
        foreach (var slide in slides)
        {
            starts.Add(all.Length);
            all.Append(Paragraphs(slide, "p", "t").Trim()).Append("\n\n");
        }
        return new FileContent(Cap(all.ToString()), starts.Count > 0 ? starts : [0]);
    }

    /// <summary>The text of the <c>t</c> elements, a line per <c>p</c>, tabs and breaks kept (any namespace prefix).</summary>
    private static string Paragraphs(ZipArchiveEntry entry, string paragraph, string text)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var all = new StringBuilder();
        var more = reader.Read();
        while (more && all.Length <= MaxChars)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == text && !reader.IsEmptyElement)
            {
                // It leaves the reader on the node after it, not read yet.
                all.Append(reader.ReadElementContentAsString());
                more = !reader.EOF;
                continue;
            }
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "tab") all.Append('\t');
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "br" or "cr") all.Append('\n');
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == paragraph) all.Append('\n');
            more = reader.Read();
        }
        return all.ToString();
    }
}

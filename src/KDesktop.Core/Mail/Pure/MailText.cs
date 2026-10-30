using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace KDesktop.Core.Mail;

/// <summary>
/// The text of a mail, clean (mail, phase 1.4): HTML to text without pictures nor tracking pixels, each link with its
/// real domain beside it (a link that says «tubanco.es» and goes elsewhere shows where it goes), and the quoted
/// replies («El lunes, Juan escribió:») and the signature cut off before it goes to the assistant.
/// </summary>
public static partial class MailText
{
    [GeneratedRegex(@"<(script|style|head|title|template|svg)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Invisible();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex(@"<a\b[^>]*?href\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))[^>]*>(.*?)</a\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Links();

    [GeneratedRegex(@"<\s*(br|/p|/div|/tr|/h[1-6]|/li|/blockquote|/table|hr)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Breaks();

    [GeneratedRegex(@"<\s*li\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Items();

    [GeneratedRegex(@"<\s*/?\s*(td|th)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Cells();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n[ \t]*(\n[ \t]*){2,}")]
    private static partial Regex BlankLines();

    /// <summary>HTML → text: blocks become lines, lists get «•», links keep their domain, no pictures.</summary>
    public static string FromHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var s = Comments().Replace(html, string.Empty);
        s = Invisible().Replace(s, string.Empty);
        s = Links().Replace(s, m =>
        {
            var href = WebUtility.HtmlDecode(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value).Trim();
            var text = WebUtility.HtmlDecode(Tags().Replace(m.Groups[4].Value, string.Empty)).Trim();
            return LinkText(text, href);
        });
        s = Breaks().Replace(s, "\n");
        s = Items().Replace(s, "\n• ");
        s = Cells().Replace(s, " ");
        s = Tags().Replace(s, string.Empty);
        s = WebUtility.HtmlDecode(s);
        return Tidy(s);
    }

    /// <summary>«Pagar ahora (paypa1-seguro.com)»: the text of a link with the domain it really goes to (only http and https).</summary>
    public static string LinkText(string text, string href)
    {
        var domain = Domain(href);
        if (domain is null) return text;
        if (text.Length == 0) return domain;
        // A link whose text is already its address: once.
        if (Domain(text.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? text : "https://" + text) == domain && !text.Contains(' ', StringComparison.Ordinal))
            return text;
        return $"{text} ({domain})";
    }

    /// <summary>The domain of an address («https://www.tubanco.es/x» → «tubanco.es»); null for anything else.</summary>
    public static string? Domain(string href)
    {
        if (!Uri.TryCreate(href.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        var host = uri.IdnHost;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    /// <summary>Spaces and blank lines tidied up.</summary>
    public static string Tidy(string text)
    {
        var s = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        s = string.Join('\n', s.Split('\n').Select(line => Spaces().Replace(line, " ").Trim()));
        s = BlankLines().Replace(s, "\n\n");
        return s.Trim();
    }

    /// <summary>The lines where an earlier mail starts being quoted, in the six languages and the usual clients.</summary>
    [GeneratedRegex(@"^(?:On .{4,200}wrote:|El .{4,200}escribió:|Le .{4,200}a écrit\s?:|Am .{4,200}schrieb .{0,200}:|Il .{4,200}ha scritto:|Em .{4,200}escreveu:|-{2,}\s*(?:Original Message|Mensaje original|Message d'origine|Ursprüngliche Nachricht|Messaggio originale|Mensagem original)\s*-{2,}|_{20,})\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex QuoteStart();

    /// <summary>Outlook's header block of the quoted mail: «De: …» followed by «Enviado: …» / «From: … Sent: …».</summary>
    [GeneratedRegex(@"^(?:From|De|Von|Da|Van)\s*:.*\n(?:.*\n){0,2}?(?:Sent|Enviado|Envoyé|Gesendet|Inviato|Enviada|Date|Fecha|Datum|Data)\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex OutlookHeader();

    [GeneratedRegex(@"^(?:--\s?|Enviado desde mi .{1,40}|Sent from my .{1,40}|Envoyé de mon .{1,40}|Von meinem .{1,40} gesendet|Inviato da .{1,40}|Enviado do meu .{1,40}|Get Outlook for .{1,30}|Obtener Outlook para .{1,30})$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex SignatureStart();

    /// <summary>
    /// Without the earlier mails quoted below it nor the signature: what this mail itself says. Quoted lines («&gt; …»)
    /// in the middle go too. Never leaves it empty: when everything was «quoted», the text stays as it was.
    /// </summary>
    public static string WithoutQuotes(string text)
    {
        var s = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var cut = s.Length;
        if (QuoteStart().Match(s) is { Success: true } q) cut = Math.Min(cut, q.Index);
        if (OutlookHeader().Match(s) is { Success: true } o && o.Index > 0) cut = Math.Min(cut, o.Index);
        if (SignatureStart().Match(s) is { Success: true } sig && sig.Index > 0) cut = Math.Min(cut, sig.Index);
        var own = s[..cut];
        own = string.Join('\n', own.Split('\n').Where(line => !line.TrimStart().StartsWith('>')));
        own = Tidy(own);
        return own.Length > 0 ? own : Tidy(s);
    }

    /// <summary>The first words of a text, in one line, for the lists and the notices.</summary>
    public static string Snippet(string text, int max = 160)
    {
        var line = Spaces().Replace(text.Replace('\n', ' ').Replace('\r', ' '), " ").Trim();
        return line.Length <= max ? line : line[..max].TrimEnd() + "…";
    }

    /// <summary>The text of a mail from its plain and HTML parts (the plain one when it says something).</summary>
    public static string Body(string? plain, string? html)
    {
        if (!string.IsNullOrWhiteSpace(plain) && plain.Trim().Length > 20) return Tidy(plain);
        if (!string.IsNullOrWhiteSpace(html)) return FromHtml(html);
        return Tidy(plain ?? string.Empty);
    }

    /// <summary>A mail's text for the assistant: clean, without quotes, and at most <paramref name="max"/> characters.</summary>
    public static string ForAssistant(string? plain, string? html, int max = 6000)
    {
        var text = WithoutQuotes(Body(plain, html));
        return text.Length <= max ? text : text[..max] + "\n[…]";
    }

    /// <summary>For a file name: what a mail's attachment may be saved as (no path, nothing Windows refuses).</summary>
    public static string SafeFileName(string name)
    {
        var file = Path.GetFileName(name.Replace('\\', '/').Split('/').Last());
        var sb = new StringBuilder();
        foreach (var c in file) sb.Append(Path.GetInvalidFileNameChars().Contains(c) || c < 32 || "<>:\"/\\|?*".Contains(c, StringComparison.Ordinal) ? '_' : c);
        var safe = sb.ToString().Trim(' ', '.');
        return safe.Length == 0 ? "adjunto" : safe.Length > 120 ? safe[..120] : safe;
    }
}

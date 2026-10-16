using System.Globalization;
using System.Text.RegularExpressions;
using KDesktop.Core.Model;

namespace KDesktop.Core.Engine;

/// <summary>The changes «Portapapeles» makes to a text (pure: no clipboard here).</summary>
public static partial class ClipboardText
{
    /// <summary>The text after <paramref name="mode"/>; null when the mode doesn't change a text (set, clear).</summary>
    public static string? Transform(ClipboardMode mode, string text, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return mode switch
        {
            ClipboardMode.PlainText => text,
            ClipboardMode.Upper => text.ToUpper(culture),
            ClipboardMode.Lower => text.ToLower(culture),
            ClipboardMode.Capitalize => culture.TextInfo.ToTitleCase(text.ToLower(culture)),
            ClipboardMode.Trim => Tidy(text),
            ClipboardMode.CleanUrl => CleanUrls(text),
            _ => null,
        };
    }

    /// <summary>Without spaces at the ends of the lines, runs of spaces as one, and no blank lines at the start or end.</summary>
    public static string Tidy(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n').Select(l => Spaces().Replace(l, " ").Trim());
        return string.Join(Environment.NewLine, lines).Trim('\r', '\n');
    }

    [GeneratedRegex(@"[ \t ]{2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex Urls();

    /// <summary>Tracking parameters taken out of links (exact names; «utm_» is a prefix).</summary>
    private static readonly HashSet<string> Trackers = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "gclsrc", "dclid", "gbraid", "wbraid", "msclkid", "yclid", "twclid", "ttclid", "li_fat_id",
        "igshid", "igsh", "mc_cid", "mc_eid", "_hsenc", "_hsmi", "mkt_tok", "oly_anon_id", "oly_enc_id", "vero_id",
        "spm", "scm", "si", "ref_src", "ref_url", "s_cid", "trk", "trkCampaign", "sc_campaign", "sc_channel",
        "pk_campaign", "pk_kwd", "pk_source", "pk_medium", "piwik_campaign", "ncid", "cmpid", "rb_clickid", "__s",
        "_ga", "_gl", "srsltid",
    };

    /// <summary>Takes the tracking parameters (utm_*, fbclid, gclid…) out of every link in <paramref name="text"/>.</summary>
    public static string CleanUrls(string text) => Urls().Replace(text, m => CleanUrl(m.Value));

    public static string CleanUrl(string url)
    {
        // Punctuation that ends a sentence is not part of the link.
        var tail = string.Empty;
        while (url.Length > 0 && ".,;:!?)]}".Contains(url[^1]))
        {
            tail = url[^1] + tail;
            url = url[..^1];
        }
        var hashAt = url.IndexOf('#');
        var fragment = hashAt >= 0 ? url[hashAt..] : string.Empty;
        var rest = hashAt >= 0 ? url[..hashAt] : url;
        var queryAt = rest.IndexOf('?');
        if (queryAt < 0) return url + tail;
        var kept = rest[(queryAt + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries).Where(p =>
        {
            var name = Uri.UnescapeDataString(p.Split('=')[0]);
            return !name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) && !Trackers.Contains(name);
        }).ToList();
        var basePart = rest[..queryAt];
        return (kept.Count == 0 ? basePart : basePart + "?" + string.Join('&', kept)) + fragment + tail;
    }
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KDesktop.Assistant.Brain.Talk;

/// <summary>
/// What the model writes, made fit for a bubble and a voice: without its thinking (<c>&lt;think&gt;</c>), tags (some
/// models answer «&lt;bocadillo&gt;…&lt;/bocadillo&gt;» when told it goes in a bubble), Markdown, lists, emojis or line
/// breaks. Also for the text so far of an answer being written.
/// </summary>
public static partial class ReplyText
{
    public static string Clean(string raw)
    {
        // Thinking: what is closed goes; one still open hides the rest.
        var text = Thinking().Replace(raw, " ");
        var open = text.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (open >= 0) text = text[..open];
        text = Tag().Replace(text, " ");

        text = Bullet().Replace(text, " ");
        text = Heading().Replace(text, " ");
        text = Link().Replace(text, "$1");
        var clean = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '*' or '`' or '#' or '~' or '_' && !IsInsideWord(text, i)) continue;
            if (char.IsSurrogate(c) || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.OtherSymbol or UnicodeCategory.ModifierSymbol && c is not ('°' or '©' or '®'))
                continue;
            if (c is '️' or '‍') continue;
            clean.Append(char.IsWhiteSpace(c) ? ' ' : c);
        }
        var result = Spaces().Replace(clean.ToString(), " ").Trim();
        // A whole answer in quotes.
        if (result.Length > 2 && result[0] is '"' or '«' or '“' && result[^1] is '"' or '»' or '”') result = result[1..^1].Trim();
        return result;
    }

    /// <summary>«snake_case» keeps its «_».</summary>
    private static bool IsInsideWord(string text, int i) =>
        text[i] == '_' && i > 0 && i < text.Length - 1 && char.IsLetterOrDigit(text[i - 1]) && char.IsLetterOrDigit(text[i + 1]);

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Thinking();

    [GeneratedRegex(@"</?[A-Za-z][\w-]{0,30}(?:\s[^<>]{0,200})?/?>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"(^|\n)\s*(?:[-*•]|\d+[.)])\s+")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"(^|\n)\s*#+\s*")]
    private static partial Regex Heading();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

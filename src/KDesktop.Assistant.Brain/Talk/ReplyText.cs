using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

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
        // Drawings and tables for a screen (the hangman's gallows, «Palabra: _ _ _ _»): a voice read «+---+ | | | ===».
        // A code block goes (one still open hides the rest), and so do lines that are mostly symbols.
        text = Fenced().Replace(text, "\n");
        var fence = text.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0) text = text[..fence];
        if (text.Contains('\n', StringComparison.Ordinal)) text = string.Join('\n', text.Split('\n').Where(l => !IsDrawing(l)));
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
        return DropClosers(result);
    }

    /// <summary>
    /// Without the empty offer small models end with («¿Hay algo más con lo que pueda ayudarte?»): asked aloud, K-Desktop
    /// would listen for an answer nobody gives. A last question with words of <c>assistant.reply.closers</c> (any language)
    /// goes, twice at most; never the whole answer.
    /// </summary>
    public static string DropClosers(string text)
    {
        for (var round = 0; round < 2; round++)
        {
            var trimmed = text.TrimEnd();
            if (!trimmed.EndsWith('?')) break;
            var start = trimmed.Length - 1;
            while (start > 0 && trimmed[start - 1] is not ('.' or '!' or '?' or '…')) start--;
            if (start == 0) break;
            var last = " " + string.Join(' ', TextSimilarity.Words(trimmed[start..])) + " ";
            if (!Closers.Value.Any(c => last.Contains(c, StringComparison.Ordinal))) break;
            text = trimmed[..start].TrimEnd();
        }
        return text;
    }

    /// <summary>
    /// Without the questions at the end of an answer that says something before them («Aterrizado en la web, Houston.
    /// ¿Listo para despegar?»): for a turn in which a tool did or looked something up, where nothing is missing; asked
    /// aloud, K-Desktop would listen for an answer nobody gives. An answer that is only a question stays.
    /// </summary>
    public static string DropQuestions(string text)
    {
        while (true)
        {
            var trimmed = text.TrimEnd();
            if (!trimmed.EndsWith('?')) return text;
            var start = trimmed.Length - 1;
            while (start > 0 && trimmed[start - 1] is not ('.' or '!' or '?' or '…')) start--;
            if (start == 0) return text;
            var before = trimmed[..start].TrimEnd();
            // «¿A? ¿B?»: all questions, nothing said before them: it stays as it is.
            if (before.Length == 0 || TextSimilarity.Words(before).Length == 0) return text;
            text = before;
        }
    }

    /// <summary>The words of an empty offer in every language, folded, with spaces around (whole words).</summary>
    private static readonly Lazy<string[]> Closers = new(() =>
    [
        .. Loc.Available()
            .Select(l => Loc.Strings(l.Code, "assistant.reply.closers").GetValueOrDefault("assistant.reply.closers") ?? string.Empty)
            .SelectMany(v => v.Split('|', StringSplitOptions.RemoveEmptyEntries))
            .Select(c => " " + string.Join(' ', TextSimilarity.Words(c)) + " ")
            .Where(c => c.Trim().Length > 0)
            .Distinct(StringComparer.Ordinal),
    ]);

    /// <summary>«snake_case» keeps its «_».</summary>
    private static bool IsInsideWord(string text, int i) =>
        text[i] == '_' && i > 0 && i < text.Length - 1 && char.IsLetterOrDigit(text[i - 1]) && char.IsLetterOrDigit(text[i + 1]);

    /// <summary>A line with no letter or digit («|», «+---+»), or of three or more marks less than half of them letters or digits («_ _ _ a _»).</summary>
    private static bool IsDrawing(string line)
    {
        var marks = line.Count(c => !char.IsWhiteSpace(c));
        var letters = line.Count(char.IsLetterOrDigit);
        return marks > 0 && (letters == 0 || marks >= 3 && letters * 2 < marks);
    }

    [GeneratedRegex(@"```.*?```", RegexOptions.Singleline)]
    private static partial Regex Fenced();

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

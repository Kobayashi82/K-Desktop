using System.Text.RegularExpressions;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Brain.Talk;

/// <summary>
/// Long answers (stage 14.2): asked for («explícamelo bien», «hazme una lista», «redáctame…»), the model writes a summary
/// of one or two sentences, a line with only <see cref="Separator"/> and then the whole text in Markdown. The voice and the
/// bubble say the summary; K-Desktop shows the whole text on a reading sheet next to the assistant, and in the chats. An
/// answer not asked to be long that does not fit a bubble anyway (many letters, a list, a table, code) is written down
/// too, with its first sentences as what is said.
/// </summary>
public static partial class LongAnswers
{
    /// <summary>The line between the summary and the whole text.</summary>
    public const string Separator = "---";

    /// <summary>A clean answer longer than this is not for a bubble: it is written down.</summary>
    public const int LongChars = 600;

    /// <summary>Items of a list for an answer to be written down.</summary>
    public const int ListItems = 4;

    /// <summary>What a voice says ahead of an answer (and of one written down): two finished sentences, and this many letters, at most.</summary>
    public const int SpokenSentences = 2;

    public const int SpokenChars = 280;

    /// <summary>A whole text shorter than this (clean) is no text to read on a sheet: the answer stays as it is.</summary>
    public const int LeastChars = 160;

    /// <summary>Whether the order asks for a long answer (<c>assistant.words.long</c>, this language and English).</summary>
    public static bool Asked(string said)
    {
        var folded = " " + string.Join(' ', TextSimilarity.Words(said)) + " ";
        return new[] { Loc.T("assistant.words.long"), Loc.Strings(Loc.Fallback, "assistant.words.").GetValueOrDefault("assistant.words.long") }
            .OfType<string>().Where(p => !p.StartsWith("assistant.", StringComparison.Ordinal))
            .SelectMany(p => p.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(p => " " + string.Join(' ', TextSimilarity.Words(p)) + " ")
            .Any(p => p.Trim().Length > 0 && folded.Contains(p, StringComparison.Ordinal));
    }

    /// <summary>The model's text as Markdown for the sheet: without its thinking, trimmed.</summary>
    public static string Markdown(string raw)
    {
        var text = Thinking().Replace(raw, string.Empty);
        var open = text.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        if (open >= 0) text = text[..open];
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }

    /// <summary>
    /// The summary and the whole text of an answer written in the long form. Without the separator line (or with one that
    /// has more than a summary before it: a rule inside the text) the summary is empty and all of it is the text.
    /// </summary>
    public static (string Summary, string Text) Split(string raw)
    {
        var text = Markdown(raw);
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!IsSeparator(lines[i])) continue;
            var before = string.Join('\n', lines[..i]).Trim();
            if (ReplyText.Clean(before).Length > LongChars || IsStructured(before)) break;
            // «Aquí tienes la guía:» is no summary: the first sentences of the text are said instead.
            var summary = WithoutLabel(before);
            if (ReplyText.Clean(summary).EndsWith(':')) summary = string.Empty;
            return (summary, string.Join('\n', lines[(i + 1)..]).Trim());
        }
        return (string.Empty, text);
    }

    /// <summary>
    /// The summary without the label a small model puts before it («Aquí tienes un resumen de una o dos frases para decir en
    /// voz alta:», «**Resumen:**»): what goes up to a colon near the start, when it names the summary in any language.
    /// </summary>
    internal static string WithoutLabel(string summary)
    {
        var colon = summary.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0 || colon > 120) return summary;
        var label = " " + string.Join(' ', TextSimilarity.Words(summary[..colon])) + " ";
        return SummaryWords.Any(w => label.Contains(w, StringComparison.Ordinal)) ? summary[(colon + 1)..].TrimStart('*', '_', ' ', '\n') : summary;
    }

    /// <summary>Words that name a summary, folded, with spaces around.</summary>
    private static readonly string[] SummaryWords = [.. new[] { "resumen", "summary", "resume", "zusammenfassung", "riassunto", "resumo", "voz alta", "aloud" }
        .Select(w => " " + string.Join(' ', TextSimilarity.Words(w)) + " ")];

    /// <summary>Whether the separator has come yet (while the answer is being written).</summary>
    public static bool HasSeparator(string raw) => Markdown(raw).Split('\n').Any(IsSeparator);

    private static bool IsSeparator(string line) => line.Trim() is { Length: >= 3 } t && t.All(c => c == '-');

    /// <summary>
    /// Whether a whole answer that was not asked to be long is too long for a bubble: many letters, a list of
    /// <see cref="ListItems"/> or more, a table, or code.
    /// </summary>
    public static bool TooLong(string raw)
    {
        var text = Markdown(raw);
        if (ReplyText.Clean(text).Length > LongChars || text.Contains("```", StringComparison.Ordinal)) return true;
        return IsStructured(text);
    }

    /// <summary>A list of <see cref="ListItems"/> items or more, or a table.</summary>
    private static bool IsStructured(string text)
    {
        var lines = text.Split('\n');
        return lines.Count(l => ListItem().IsMatch(l)) >= ListItems || lines.Count(l => l.TrimStart().StartsWith('|')) >= 2;
    }

    /// <summary>
    /// What a voice says of a clean text: its first <see cref="SpokenSentences"/> finished sentences within
    /// <see cref="SpokenChars"/> letters; none finished yet, all of it while it is short, else up to a comma. Growing text
    /// gives growing results (what was said ahead is always the start of what is said at the end).
    /// </summary>
    public static string Spoken(string clean)
    {
        var end = 0;
        var sentences = 0;
        for (var i = 0; i < clean.Length && sentences < SpokenSentences; i++)
        {
            if (clean[i] is not ('.' or '!' or '?' or '…')) continue;
            var after = i + 1;
            while (after < clean.Length && clean[after] is '.' or '!' or '?' or '…' or '»' or '"' or '”' or ')') after++;
            if (after < clean.Length && !char.IsWhiteSpace(clean[after])) continue;
            if (after > SpokenChars) break;
            end = after;
            sentences++;
            i = after - 1;
        }
        if (end > 0) return clean[..end].TrimEnd();
        if (clean.Length <= SpokenChars) return clean;
        var cut = clean.LastIndexOfAny([',', ';', ':'], SpokenChars - 1);
        if (cut < SpokenChars / 2) cut = clean.LastIndexOf(' ', SpokenChars - 1);
        return cut > 0 ? clean[..(cut + 1)].TrimEnd() : clean[..SpokenChars];
    }

    /// <summary>
    /// What is said of a text written down without a summary of its own: its first sentences and «te lo dejo por escrito»
    /// (<c>longWritten</c>).
    /// </summary>
    public static string Lead(string markdown, string written)
    {
        var spoken = Spoken(ReplyText.Clean(markdown)).TrimEnd(',', ';', ':', ' ');
        if (spoken.Length > 0 && spoken[^1] is not ('.' or '!' or '?' or '…' or '»' or '"' or '”')) spoken += "…";
        return (spoken + " " + written).Trim();
    }

    /// <summary>
    /// Markdown as sentences to read aloud: each heading, item or row ends like a sentence (a list read clean ran its items
    /// together), code and table rules left out.
    /// </summary>
    public static string Readable(string markdown)
    {
        var said = new List<string>();
        var code = false;
        foreach (var raw in Markdown(markdown).Split('\n'))
        {
            if (raw.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                code = !code;
                continue;
            }
            if (code || IsSeparator(raw)) continue;
            var line = raw.Trim().TrimStart('>').Trim();
            // A table: its cells, separated as a list is.
            if (line.StartsWith('|'))
            {
                if (line.Trim('|', '-', ':', ' ').Length == 0) continue;
                line = string.Join(", ", line.Trim('|').Split('|').Select(c => c.Trim()).Where(c => c.Length > 0));
            }
            var clean = ReplyText.Clean(line);
            if (clean.Length == 0) continue;
            if (clean[^1] is not ('.' or '!' or '?' or '…' or ':' or ';')) clean += ".";
            said.Add(clean);
        }
        return string.Join(' ', said);
    }

    /// <summary>A long text for the history: the model must know what it wrote («¿y el tercer punto?»), without filling the context.</summary>
    public const int HistoryChars = 3000;

    public static string ForHistory(string text, string? written)
    {
        if (written is null) return text;
        var body = written.Length > HistoryChars ? written[..HistoryChars] + "…" : written;
        return text + "\n\n" + body;
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Thinking();

    [GeneratedRegex(@"^\s*(?:[-*•+]|\d+[.)])\s+\S")]
    private static partial Regex ListItem();
}

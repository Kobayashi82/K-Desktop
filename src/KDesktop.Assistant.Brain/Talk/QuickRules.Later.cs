using System.Text.RegularExpressions;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Brain.Talk;

public static partial class QuickRules
{
    /// <summary>
    /// Whether an order says when it is for, later («a las siete vacía la papelera», «dentro de dos horas apaga el PC»,
    /// «mañana mira el tiempo», «19:00»), in any language: then it is not done now by the user's commands or the rules about
    /// programs (they would run it at once), but goes to the AI with the tool <c>schedule</c>. A question («¿qué tiempo
    /// hará mañana?») is not. The words are <c>assistant.words.atTime</c> (before a clock time), <c>assistant.words.inTime</c>
    /// (before a duration) and <c>assistant.words.whenDay</c> (a day, or how often; with «^», only starting the order:
    /// «mañana…» but not «…por la mañana»).
    /// </summary>
    public static bool IsForLater(string text, IEnumerable<string>? names = null)
    {
        if (text.Contains('?', StringComparison.Ordinal)) return false;
        if (Clock().IsMatch(text)) return true;
        var w = Words;
        var tokens = Trim(Tokenize(text), w, [.. (names ?? []).Select(TextSimilarity.Words).Where(n => n.Length > 0)]);
        var words = tokens.Select(t => t.Folded).ToList();
        var later = LaterWords.Value;
        for (var i = 0; i < tokens.Count; i++)
        {
            foreach (var lead in later.AtTime)
                if (StartsWith(tokens, lead.Words, i) && SpokenNumbers.TryRead(words, i + lead.Words.Length, out var hour, out _) && hour is >= 0 and <= 24)
                    return true;
            foreach (var lead in later.InTime)
                if (StartsWith(tokens, lead.Words, i) && ReadDuration(tokens, i + lead.Words.Length, w) is { Seconds: > 0 })
                    return true;
            foreach (var day in later.Days)
                if ((i == 0 || !day.AtStart) && StartsWith(tokens, day.Words, i))
                    return true;
        }
        return false;
    }

    private sealed record LaterPhrase(string[] Words, bool AtStart);

    private sealed record Later(LaterPhrase[] AtTime, LaterPhrase[] InTime, LaterPhrase[] Days);

    private static readonly Lazy<Later> LaterWords = new(() => new Later(Phrases("assistant.words.atTime"), Phrases("assistant.words.inTime"), Phrases("assistant.words.whenDay")));

    /// <summary>The phrases of a key in every language, folded into words.</summary>
    private static LaterPhrase[] Phrases(string key) =>
    [
        .. Loc.Available()
            .Select(l => Loc.Strings(l.Code, key).GetValueOrDefault(key) ?? string.Empty)
            .SelectMany(v => v.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(p => new LaterPhrase(TextSimilarity.Words(p), p.StartsWith('^')))
            .Where(p => p.Words.Length > 0)
            .DistinctBy(p => (string.Join(' ', p.Words), p.AtStart)),
    ];

    /// <summary>A time of day written as such: «19:00», «7:30».</summary>
    [GeneratedRegex(@"(?<![\d:.,])([01]?\d|2[0-3]):[0-5]\d(?![\d:])")]
    private static partial Regex Clock();
}

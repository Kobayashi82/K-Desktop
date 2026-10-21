using System.Globalization;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Voice;

/// <summary>
/// Numbers said with words or digits («treinta y cinco», «fünfunddreißig», «35»), in all the installed languages at
/// once (like <see cref="VoiceVocabulary"/>). The words come from the language files:
/// <list type="bullet">
/// <item><c>voice.numbers</c>: number words, <c>word=value</c> separated by spaces; compounds of them are understood
/// («treinta y cinco», «fünfunddreißig», «trentacinque», «quatre-vingt»);</item>
/// <item><c>voice.numbers.levels</c>: words that name a level («máximo=100 mitad=50»);</item>
/// <item><c>voice.numbers.and</c>: what joins number words («y», «and»…).</item>
/// </list>
/// Words are the folded ones of <see cref="TextSimilarity.Words"/>.
/// </summary>
public static class SpokenNumbers
{
    private sealed class Vocabulary
    {
        public readonly Dictionary<string, int> Numbers = new(StringComparer.Ordinal);
        public readonly HashSet<string> Levels = new(StringComparer.Ordinal);
        public readonly HashSet<string> And = new(StringComparer.Ordinal);
    }

    private static Vocabulary? _current;
    private static readonly object Gate = new();

    static SpokenNumbers() => Loc.Instance.Changed += () => { lock (Gate) _current = null; };

    private static Vocabulary Words
    {
        get
        {
            lock (Gate) return _current ??= Build();
        }
    }

    private static Vocabulary Build()
    {
        var v = new Vocabulary();
        foreach (var language in Loc.Available())
            foreach (var (key, value) in Loc.Strings(language.Code, "voice.numbers"))
            {
                switch (key)
                {
                    case "voice.numbers": Add(v, value, level: false); break;
                    case "voice.numbers.levels": Add(v, value, level: true); break;
                    case "voice.numbers.and": v.And.UnionWith(TextSimilarity.Words(value)); break;
                }
            }
        return v;
    }

    private static void Add(Vocabulary v, string list, bool level)
    {
        foreach (var pair in list.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.LastIndexOf('=');
            if (equals <= 0 || !int.TryParse(pair[(equals + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) continue;
            foreach (var word in TextSimilarity.Words(pair[..equals]))
            {
                v.Numbers.TryAdd(word, number);
                if (level) v.Levels.Add(word);
            }
        }
    }

    /// <summary>A word that joins number words («y» in «treinta y cinco»).</summary>
    public static bool IsAnd(string word) => Words.And.Contains(word);

    /// <summary>
    /// Reads the number that starts at <paramref name="start"/> (one word or several: «treinta y cinco»), and gives
    /// where it ends (exclusive). <paramref name="level"/>: one of its words names a level («máximo», «mitad»).
    /// </summary>
    public static bool TryRead(IReadOnlyList<string> words, int start, out int value, out int end, out bool level)
    {
        var w = Words;
        value = 0;
        end = start;
        level = false;
        if (start >= words.Count || !TryWord(words[start], w, out var first, out level)) return false;
        var values = new List<int> { first };
        end = start + 1;
        while (end < words.Count)
        {
            if (TryWord(words[end], w, out var next, out var l)) { values.Add(next); level |= l; end++; }
            else if (w.And.Contains(words[end]) && end + 1 < words.Count && TryWord(words[end + 1], w, out next, out l))
            {
                values.Add(next);
                level |= l;
                end += 2;
            }
            else break;
        }
        value = Combine(values);
        return true;
    }

    /// <summary>The same without the level: only plain numbers («mitad» is not one).</summary>
    public static bool TryRead(IReadOnlyList<string> words, int start, out int value, out int end) =>
        TryRead(words, start, out value, out end, out var level) && !level;

    /// <summary>«treinta» «cinco» → 35; «one» «hundred» → 100; «quatre» «vingt» «dix» → 90.</summary>
    private static int Combine(List<int> values)
    {
        var total = 0;
        foreach (var v in values)
        {
            if (total is >= 1 and <= 9 && (v == 100 || (v == 20 && total == 4))) total *= v;
            else total += v;
        }
        return total;
    }

    /// <summary>A number word, digits, or a word made of number words («fünfunddreißig», «trentacinque»).</summary>
    private static bool TryWord(string word, Vocabulary w, out int value, out bool level)
    {
        level = w.Levels.Contains(word);
        if (w.Numbers.TryGetValue(word, out value)) return true;
        if (word.All(char.IsAsciiDigit) && word.Length <= 3) return int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        var parts = Split(word, w);
        if (parts is not { Count: >= 2 }) return false;
        value = Combine(parts);
        return true;
    }

    /// <summary>The number words a compound is made of (joiners of three letters or more may sit between them).</summary>
    private static List<int>? Split(string word, Vocabulary w)
    {
        if (word.Length < 5) return null;
        for (var cut = word.Length - 2; cut >= 2; cut--)
        {
            var head = word[..cut];
            if (!w.Numbers.TryGetValue(head, out var value) || w.Levels.Contains(head)) continue;
            var rest = word[cut..];
            if (w.Numbers.TryGetValue(rest, out var last) && !w.Levels.Contains(rest)) return [value, last];
            foreach (var joiner in w.And.Where(j => j.Length >= 3 && rest.StartsWith(j, StringComparison.Ordinal)))
                if (w.Numbers.TryGetValue(rest[joiner.Length..], out last)) return [value, last];
            if (Split(rest, w) is { } more) return [value, .. more];
        }
        return null;
    }
}

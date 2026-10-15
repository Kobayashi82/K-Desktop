using KDesktop.Core.Engine;

namespace KDesktop.Core.Voice;

/// <summary>Word-level helpers for comparing spoken text: folding, splitting and edit distance.</summary>
public static class TextSimilarity
{
    /// <summary>Lower case, no accents, only letters and digits; split into words.</summary>
    public static string[] Words(string text)
    {
        var chars = text.Select(c => char.IsLetterOrDigit(c) ? FuzzyMatch.Fold(c) : ' ').ToArray();
        return new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    public static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>1 for equal strings, 0 for completely different ones.</summary>
    public static double Ratio(string a, string b)
    {
        var max = Math.Max(a.Length, b.Length);
        return max == 0 ? 1 : 1 - (double)Levenshtein(a, b) / max;
    }

    /// <summary>
    /// How alike two folded words are, 0–1: equal; same stem (abre / abrir, carpeta / carpetas); or close spelling
    /// (a transcription slip). Words of one or two letters only count when equal.
    /// </summary>
    public static double WordSimilarity(string a, string b)
    {
        if (a == b) return 1;
        if (a.Length <= 2 || b.Length <= 2) return 0;
        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
        var shorter = Math.Min(a.Length, b.Length);
        var stem = prefix >= 4 || (prefix >= 3 && prefix >= 0.75 * shorter) ? 0.85 : 0;
        return Math.Max(stem, Ratio(a, b));
    }
}

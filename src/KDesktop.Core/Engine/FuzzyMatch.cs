using System.Globalization;
using System.Text;

namespace KDesktop.Core.Engine;

/// <summary>
/// A text prepared for <see cref="FuzzyMatch"/>: folded (lower case, no accents, same length as the original) and
/// with its word starts marked ("Open WebUI", "OpenWebUI", "open-webui" and "ACE_Step2" all have word boundaries).
/// </summary>
public sealed class SearchKey
{
    public SearchKey(string text)
    {
        Text = text;
        var folded = new char[text.Length];
        Starts = new bool[text.Length];
        var initials = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            folded[i] = FuzzyMatch.Fold(c);
            var prev = i > 0 ? text[i - 1] : ' ';
            var start = char.IsLetterOrDigit(c) && (!char.IsLetterOrDigit(prev) ||
                        (char.IsUpper(c) && char.IsLower(prev)) || (char.IsDigit(c) != char.IsDigit(prev)));
            Starts[i] = start;
            if (start) initials.Append(folded[i]);
        }
        Folded = new string(folded);
        Initials = initials.ToString();
    }

    public string Text { get; }
    public string Folded { get; }
    public bool[] Starts { get; }
    public string Initials { get; }
}

/// <summary>
/// Launcher-style matching. Every word of the query has to match the text; a word scores more the "stronger" its match:
/// whole text &gt; start of the text &gt; start of a word &gt; initials ("owu" → Open WebUI) &gt; inside a word &gt;
/// letters in order ("cmfy" → ComfyUI). Accents and case are ignored. 0 means no match.
/// </summary>
public static class FuzzyMatch
{
    public static char Fold(char c)
    {
        if (c < 128) return char.ToLowerInvariant(c);
        if (char.IsSurrogate(c)) return c; // half of an emoji: it cannot be normalised on its own
        var d = c.ToString().Normalize(NormalizationForm.FormD);
        return char.ToLowerInvariant(CharUnicodeInfo.GetUnicodeCategory(d[0]) == UnicodeCategory.NonSpacingMark ? c : d[0]);
    }

    public static string[] Tokens(string query) =>
        new string(query.Select(Fold).ToArray()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    public static int Score(IReadOnlyList<string> tokens, SearchKey key)
    {
        if (tokens.Count == 0) return 0;
        var total = 0;
        foreach (var token in tokens)
        {
            var s = TokenScore(token, key);
            if (s == 0) return 0;
            total += s;
        }
        var score = total / tokens.Count;
        // The whole query typed as is ("comfy ui" in "ComfyUI Web") is better than its words scattered.
        if (tokens.Count > 1 && key.Folded.Contains(string.Join(' ', tokens), StringComparison.Ordinal)) score += 50;
        return score;
    }

    private static int TokenScore(string token, SearchKey key)
    {
        var text = key.Folded;
        if (text.Length == 0) return 0;
        if (text == token) return 1000;
        if (text.StartsWith(token, StringComparison.Ordinal)) return 900 + 50 * token.Length / text.Length;

        // Start of a word.
        for (var i = 1; i <= text.Length - token.Length; i++)
            if (key.Starts[i] && string.CompareOrdinal(text, i, token, 0, token.Length) == 0)
                return 800 - Math.Min(i, 50);

        if (token.Length >= 2 && key.Initials.StartsWith(token, StringComparison.Ordinal)) return 720;

        var inside = text.IndexOf(token, StringComparison.Ordinal);
        if (inside >= 0) return token.Length >= 2 ? 600 - Math.Min(inside, 100) : 0;

        return token.Length >= 3 ? Subsequence(token, key) : 0;
    }

    /// <summary>Letters of the token in order; fewer and shorter gaps score more. Must start on a word.</summary>
    private static int Subsequence(string token, SearchKey key)
    {
        var text = key.Folded;
        var best = 0;
        for (var start = 0; start < text.Length; start++)
        {
            if (!key.Starts[start] || text[start] != token[0]) continue;
            int last = start, gaps = 0, gapChars = 0, matched = 1;
            for (var pos = start + 1; matched < token.Length && pos < text.Length; pos++)
            {
                if (text[pos] != token[matched]) continue;
                if (pos > last + 1)
                {
                    gaps++;
                    gapChars += pos - last - 1;
                }
                last = pos;
                matched++;
            }
            if (matched < token.Length || gapChars > token.Length * 3) continue;
            best = Math.Max(best, 450 - gaps * 40 - gapChars * 5 - Math.Min(start, 30));
        }
        return Math.Max(best, 0);
    }
}

using KDesktop.Core.Localization;

namespace KDesktop.Core.Voice;

/// <summary>Where a wake word was found in a transcription.</summary>
/// <param name="Score">0–1, how much the best stretch of words sounds like the word.</param>
/// <param name="Start">First word of that stretch.</param>
/// <param name="End">Word after the stretch.</param>
/// <param name="Rest">What was said after the word (the order), without leading punctuation.</param>
public sealed record WakeMatch(WakeWord Word, double Score, int Start, int End, string Rest)
{
    public bool Accepted => Score >= Word.EffectiveThreshold;
}

/// <summary>
/// Wake word, phase 2: Parakeet transcribes the last couple of seconds and this checks, by sound (<see cref="PhoneticKey"/>),
/// whether some stretch of words is one of the ways Parakeet wrote the word while it was being recorded.
/// </summary>
public static class WakeWordVerifier
{
    /// <summary>Score needed when nothing has been calibrated.</summary>
    public const double DefaultThreshold = 0.75;

    /// <summary>The longest stretch of words tried, beyond the words of the longest variant.</summary>
    private const int ExtraWords = 2;

    /// <summary>The best match of <paramref name="word"/> in <paramref name="text"/> (null for empty text).</summary>
    public static WakeMatch? Find(string text, WakeWord word) => Find(text, word, word.Keys);

    private static WakeMatch? Find(string text, WakeWord word, IReadOnlyList<string> keys)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || keys.Count == 0) return null;
        var tokenKeys = tokens.Select(PhoneticKey.Of).ToArray();
        var longest = word.Variants.Append(word.Name).Max(v => v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
        var span = Math.Clamp(longest + ExtraWords, 1, 6);

        WakeMatch? best = null;
        for (var start = 0; start < tokens.Length; start++)
        {
            if (tokenKeys[start].Length == 0) continue;
            var joined = "";
            for (var end = start + 1; end <= Math.Min(tokens.Length, start + span); end++)
            {
                joined += tokenKeys[end - 1];
                foreach (var key in keys)
                {
                    var score = PhoneticKey.Similarity(joined, key);
                    // Same score: the shorter stretch wins (it leaves more for the order).
                    if (best is not null && score <= best.Score) continue;
                    best = new WakeMatch(word, score, start, end, RestOf(tokens, end));
                }
            }
        }
        return best;
    }

    /// <summary>The best accepted match among <paramref name="words"/>, or null.</summary>
    public static WakeMatch? FindAny(string text, IEnumerable<WakeWord> words) =>
        words.Select(w => Find(text, w)).Where(m => m is { Accepted: true }).MaxBy(m => m!.Score - m.Word.EffectiveThreshold);

    /// <summary>Words before the word, in its sentence, from which it is said in passing (<see cref="SaidInPassing"/>).</summary>
    public const int PassingWords = 2;

    /// <summary>
    /// Whether the word is talked about rather than to (5.0.200): in its sentence, <see cref="PassingWords"/> words or more
    /// come before it that are not a lead-in («oye», «vale», «hey»…, <c>voice.wake.leadIns</c> of every language) nor
    /// shorter than three letters. «le estaba enseñando a mi hermano el Lander» is; «Eh. Vale, Lander», «pero Lander» are
    /// not. Mentioned first («Kobii es la criatura…») it cannot tell; that goes on as an order.
    /// </summary>
    public static bool SaidInPassing(string text, WakeMatch match)
    {
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var from = 0;
        for (var i = 0; i < Math.Min(match.Start, tokens.Length); i++)
            if (tokens[i].AsSpan().IndexOfAny(SentenceEnds) >= 0) from = i + 1;
        var leadIns = LeadIns.Value;
        var count = 0;
        for (var i = from; i < Math.Min(match.Start, tokens.Length); i++)
            count += TextSimilarity.Words(tokens[i]).Count(w => w.Length >= 3 && !leadIns.Contains(w));
        return count >= PassingWords;
    }

    private static readonly System.Buffers.SearchValues<char> SentenceEnds = System.Buffers.SearchValues.Create(".?!¿¡;:");

    private static readonly Lazy<HashSet<string>> LeadIns = new(() =>
        [.. Loc.Available()
            .SelectMany(l => Loc.Strings(l.Code, "voice.wake.leadIns").Where(p => p.Key == "voice.wake.leadIns").Select(p => p.Value))
            .SelectMany(v => v.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .SelectMany(TextSimilarity.Words)]);

    /// <summary>A transcription of the word without the punctuation around it («KDesktop.» → «KDesktop»).</summary>
    public static string Clean(string text) => text.Trim().Trim('.', ',', ';', ':', '!', '?', '¡', '¿', '-', ' ');

    private static string RestOf(string[] tokens, int from) =>
        string.Join(' ', tokens.Skip(from)).TrimStart(',', '.', ';', ':', '!', '?', '¡', '¿', '-', ' ');

    // ── calibration ──────────────────────────────────────────────────────

    /// <summary>What the calibration found.</summary>
    /// <param name="Positives">Each recording against the others (leave one out): how sure a real «KDesktop» is.</param>
    /// <param name="Negative">Best score that normal speech reached.</param>
    public sealed record Calibration(double Threshold, IReadOnlyList<double> Positives, double Negative);

    /// <summary>
    /// Picks the threshold: above what normal speech reaches (with a margin), but under what the word's own
    /// recordings reach, so the user's «KDesktop» passes and their ordinary talk does not.
    /// </summary>
    /// <param name="variants">How Parakeet wrote each recording (one per recording, may repeat).</param>
    /// <param name="negatives">Transcriptions of normal speech without the word.</param>
    public static Calibration Calibrate(string name, IReadOnlyList<string> variants, IEnumerable<string> negatives)
    {
        var word = new WakeWord { Name = name, Variants = variants.Distinct().ToList() };

        var positives = new List<double>();
        for (var i = 0; i < variants.Count; i++)
        {
            // Without this recording's own transcription (unless another recording wrote the same).
            var others = variants.Where((_, j) => j != i).Append(name).Select(PhoneticKey.Of).Where(k => k.Length > 0).Distinct().ToList();
            positives.Add(Find(variants[i], word, others)?.Score ?? 0);
        }

        var negative = negatives.Select(n => Find(n, word)?.Score ?? 0).DefaultIfEmpty(0).Max();

        var threshold = Math.Max(DefaultThreshold, negative + 0.1);
        if (positives.Count > 0)
        {
            // Most recordings must pass: the second-worst one sets the ceiling (one odd take does not drag it down).
            var sorted = positives.Order().ToList();
            var ceiling = sorted[Math.Min(1, sorted.Count - 1)] - 0.02;
            if (threshold > ceiling) threshold = Math.Max(ceiling, negative + 0.03);
        }
        return new Calibration(Math.Clamp(threshold, 0.55, 0.95), positives, negative);
    }
}

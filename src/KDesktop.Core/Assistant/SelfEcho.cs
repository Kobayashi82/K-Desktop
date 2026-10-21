using KDesktop.Core.Voice;

namespace KDesktop.Core.Assistant;

/// <summary>
/// An assistant's own voice taken for an order (stage 5.7): the echo canceller takes most of it out of the microphone,
/// but what is left may still sound like a wake word («Hola, soy Lander…», «…en tu escritorio, …») and then what follows
/// would be its own sentence. This remembers what each assistant is saying and until when it may still sound, and tells
/// whether something heard is that: at least <see cref="MinWords"/> words, nearly all of them in the same order as in
/// something said while it was sounding. Short orders («para», «calla») are never taken for an echo. Thread-safe.
/// </summary>
public sealed class SelfEcho
{
    /// <summary>Fewer words than this are never an echo: «Lander, para» must always reach it.</summary>
    public const int MinWords = 3;

    /// <summary>The part of the heard words that must be found, in order, in what was said.</summary>
    public const double Share = 0.7;

    /// <summary>What is said stays this long after it may have stopped sounding (the reverberation, the last capture).</summary>
    private static readonly TimeSpan Tail = TimeSpan.FromSeconds(1.5);

    private readonly Dictionary<string, Line> _lines = new(StringComparer.Ordinal);

    private sealed record Line(string[] Words, DateTime From, DateTime Until);

    /// <summary>
    /// How long a text takes to be said aloud, at most (about 13 letters a second, slower than the voices are, and the
    /// start of the voice).
    /// </summary>
    public static TimeSpan SayingTime(string text) => TimeSpan.FromSeconds(1 + text.Length * 0.08);

    /// <summary>
    /// Something an assistant says (<paramref name="key"/>: its turn; the text so far, again and again while it is
    /// written): it may sound from now until it has had time to be said.
    /// </summary>
    public void Said(string key, string text, DateTime at)
    {
        var words = TextSimilarity.Words(text);
        if (words.Length == 0) return;
        lock (_lines)
        {
            var from = _lines.TryGetValue(key, out var known) ? known.From : at;
            var until = at + SayingTime(text);
            if (known is not null && known.Until > until) until = known.Until;
            _lines[key] = new Line(words, from, until);
            foreach (var old in _lines.Where(l => l.Value.Until + Tail < at).Select(l => l.Key).ToList()) _lines.Remove(old);
        }
    }

    /// <summary>
    /// Whether <paramref name="heard"/>, heard at <paramref name="at"/>, is what an assistant was saying then; with what it
    /// was in <paramref name="said"/>.
    /// </summary>
    public bool IsEcho(string heard, DateTime at, out string? said)
    {
        said = null;
        var words = TextSimilarity.Words(heard);
        if (words.Length < MinWords) return false;
        lock (_lines)
        {
            foreach (var line in _lines.Values)
            {
                if (at < line.From - Tail || at > line.Until + Tail) continue;
                if (InOrder(words, line.Words) >= Share * words.Length)
                {
                    said = string.Join(' ', line.Words);
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>How many of <paramref name="heard"/> appear in <paramref name="said"/> in the same order (alike words count).</summary>
    public static int InOrder(string[] heard, string[] said)
    {
        // The longest common subsequence, with words that are alike (a transcription slip) as equal.
        var row = new int[said.Length + 1];
        foreach (var word in heard)
        {
            var diagonal = 0;
            for (var j = 1; j <= said.Length; j++)
            {
                var above = row[j];
                row[j] = TextSimilarity.WordSimilarity(word, said[j - 1]) >= 0.8 ? diagonal + 1 : Math.Max(row[j], row[j - 1]);
                diagonal = above;
            }
        }
        return row[said.Length];
    }
}

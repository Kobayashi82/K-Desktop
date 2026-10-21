using System.Text;

namespace KDesktop.Assistant.Voice;

/// <summary>
/// An answer being written, in whole sentences (stage 5.4): each time more text comes, the sentences it has finished (an
/// end mark followed by a space) go to be said, so the voice starts before the AI ends. At the end, the rest. Not
/// thread-safe.
/// </summary>
public sealed class SentenceSplitter
{
    /// <summary>A sentence with no end mark that grows past this is cut at its last comma (it would wait too long).</summary>
    public const int LongSentence = 160;

    private int _taken;
    private string _seen = string.Empty;
    private string? _held;

    /// <summary>
    /// A sentence of one or two words («Recibido.») waits for the next one and goes with it (Piper says a lone word badly;
    /// Kokoro does not need it, and would start later).
    /// </summary>
    public bool JoinShort { get; set; }

    /// <summary>Words of a sentence that is said with the next one, at most.</summary>
    public const int ShortWords = 2;

    /// <summary>What has been handed out to be said so far.</summary>
    public string Taken => _seen[.._taken];

    /// <summary>The new sentences finished in <paramref name="textSoFar"/> (all of the text so far, as it grows).</summary>
    public IReadOnlyList<string> Feed(string textSoFar)
    {
        if (!textSoFar.StartsWith(Taken, StringComparison.Ordinal)) return [];
        _seen = textSoFar;
        var result = new List<string>();
        while (Next(textSoFar, _taken, final: false) is { } end)
        {
            Add(result, textSoFar[_taken..end]);
            _taken = end;
        }
        return result;
    }

    /// <summary>
    /// The final text: what is left to say after what was already handed out. <paramref name="diverged"/>: the final text
    /// does not continue what was said (the answer was replaced: by the exact sentences of a tool, for example).
    /// </summary>
    public IReadOnlyList<string> Finish(string finalText, out bool diverged)
    {
        diverged = !finalText.StartsWith(Taken, StringComparison.Ordinal);
        if (diverged) _held = null;
        var from = diverged ? 0 : _taken;
        _seen = finalText;
        var result = new List<string>();
        while (from < finalText.Length)
        {
            var end = Next(finalText, from, final: true) ?? finalText.Length;
            Add(result, finalText[from..end]);
            from = end;
        }
        // A short one that waited for a next one that never came: alone.
        if (_held is { } held) result.Add(held);
        _held = null;
        _taken = finalText.Length;
        return result;
    }

    /// <summary>Where the sentence that begins at <paramref name="from"/> ends, or null if it has not ended yet.</summary>
    private static int? Next(string text, int from, bool final)
    {
        for (var i = from; i < text.Length; i++)
        {
            if (text[i] is not ('.' or '!' or '?' or '…' or '\n')) continue;
            // A run of marks («?!», «...») stays with what it closes.
            var end = i + 1;
            while (end < text.Length && text[end] is '.' or '!' or '?' or '…' or '"' or '»' or ')') end++;
            if (end < text.Length && char.IsWhiteSpace(text[end])) return end;
            if (end == text.Length && final) return end;
            i = end - 1;
        }
        if (!final && text.Length - from > LongSentence)
        {
            var comma = text.LastIndexOf(',', text.Length - 1, text.Length - from);
            if (comma > from + 20) return comma + 1;
        }
        return null;
    }

    private void Add(List<string> sentences, string piece)
    {
        piece = piece.Trim();
        if (!piece.Any(char.IsLetterOrDigit)) return;
        if (_held is { } held)
        {
            piece = held + " " + piece;
            _held = null;
        }
        if (JoinShort && piece.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= ShortWords) _held = piece;
        else sentences.Add(piece);
    }

    /// <summary>The punctuation that is read (it gives the intonation).</summary>
    private const string Punctuation = ".,;:!?¡¿'\"()-";

    /// <summary>Longest text said in one go (the rest stays in the bubble).</summary>
    public const int MaxSpokenLength = 400;

    /// <summary>
    /// What can be said aloud of a sentence: no emojis or symbols (letters, digits and the usual punctuation only) and,
    /// when very long, up to the last sentence that fits. Null when no letter is left. From Kobii.
    /// </summary>
    public static string? Speakable(string text)
    {
        var clean = new StringBuilder(text.Length);
        foreach (var rune in text.Replace("…", "...", StringComparison.Ordinal).EnumerateRunes())
        {
            // The rest (emojis, symbols, spaces) separates words.
            var keep = Rune.IsLetterOrDigit(rune) || (rune.IsBmp && Punctuation.Contains((char)rune.Value, StringComparison.Ordinal));
            clean.Append(keep ? rune.ToString() : " ");
        }
        var spoken = string.Join(' ', clean.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (!spoken.Any(char.IsLetterOrDigit)) return null;
        if (spoken.Length > MaxSpokenLength)
        {
            var cut = spoken.LastIndexOfAny(['.', '!', '?'], MaxSpokenLength - 1);
            spoken = cut > 0 ? spoken[..(cut + 1)] : spoken[..MaxSpokenLength];
        }
        return spoken;
    }
}

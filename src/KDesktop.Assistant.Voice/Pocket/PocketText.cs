namespace KDesktop.Assistant.Voice.Pocket;

/// <summary>
/// How Pocket TTS prepares the text before saying it (like Kyutai's implementation): a capital at the start, a full stop at
/// the end and, if long, in pieces of whole sentences that fit what the model can say at once (it was trained on single
/// sentences and gets lost in long texts). Ported from Kobii.
/// </summary>
public static class PocketText
{
    /// <summary>Text pieces per second of voice, roughly (a cap on what is generated).</summary>
    public const double TokensPerSecond = 3;

    /// <summary>Seconds of margin on that cap.</summary>
    public const double PaddingSeconds = 2;

    /// <summary>The text as the model likes it; null when there is nothing to say.</summary>
    public static string? Prepare(string text)
    {
        text = text.Trim().Replace('\n', ' ').Replace('\r', ' ').Replace("  ", " ", StringComparison.Ordinal);
        if (text.Length == 0) return null;
        if (!char.IsUpper(text[0])) text = char.ToUpperInvariant(text[0]) + text[1..];
        if (char.IsLetterOrDigit(text[^1])) text += ".";
        return text;
    }

    /// <summary>Frames still generated after the model says it has finished (short texts say so too early).</summary>
    public static int FramesAfterEnd(string text) => (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4 ? 3 : 1) + 2;

    /// <summary>Most frames for a text of so many pieces (in case it never says it has finished).</summary>
    public static int MaxFrames(int tokens, double frameRate) => (int)Math.Ceiling((tokens / TokensPerSecond + PaddingSeconds) * frameRate);

    /// <summary>
    /// The text in pieces that can be said at once: by sentences first and, when one is still too long, by commas,
    /// semicolons and colons; then the pieces in a row are joined while they fit.
    /// </summary>
    public static List<string> Chunks(string text, SentencePiece tokenizer, int maxTokens)
    {
        if (Prepare(text) is not { } prepared) return [];
        var tokens = tokenizer.Encode(prepared);
        var sentenceEnds = Boundaries(tokenizer, ".!...?");
        var refined = new List<(int Count, string Text)>();
        foreach (var segment in Segments(tokenizer, tokens, sentenceEnds))
        {
            if (segment.Count <= maxTokens)
            {
                refined.Add(segment);
                continue;
            }
            var parts = Segments(tokenizer, tokenizer.Encode(segment.Text.Trim()), Boundaries(tokenizer, ",;:"));
            if (parts.Count > 1) refined.AddRange(parts);
            else refined.Add(segment);
        }

        var chunks = new List<string>();
        var current = string.Empty;
        var count = 0;
        foreach (var (size, piece) in refined)
        {
            if (current.Length == 0) (current, count) = (piece, size);
            else if (count + size > maxTokens)
            {
                chunks.Add(current.Trim());
                (current, count) = (piece, size);
            }
            else
            {
                current += " " + piece;
                count += size;
            }
        }
        if (current.Length > 0) chunks.Add(current.Trim());
        return [.. chunks.Where(c => c.Length > 0)];
    }

    /// <summary>The punctuation pieces that close a chunk (without the leading-space one the tokenizer adds).</summary>
    private static HashSet<int> Boundaries(SentencePiece tokenizer, string marks) => [.. tokenizer.Encode(marks).Skip(1)];

    private static List<(int Count, string Text)> Segments(SentencePiece tokenizer, List<int> tokens, HashSet<int> boundaries)
    {
        // Each chunk ends after a run of marks («...» counts as one), and the mark stays with what it closes.
        var cuts = new List<int> { 0 };
        var afterBoundary = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (boundaries.Contains(tokens[i])) afterBoundary = true;
            else
            {
                if (afterBoundary) cuts.Add(i);
                afterBoundary = false;
            }
        }
        cuts.Add(tokens.Count);
        var segments = new List<(int, string)>();
        for (var i = 0; i < cuts.Count - 1; i++)
            segments.Add((cuts[i + 1] - cuts[i], tokenizer.Decode(tokens.GetRange(cuts[i], cuts[i + 1] - cuts[i]))));
        return segments;
    }
}

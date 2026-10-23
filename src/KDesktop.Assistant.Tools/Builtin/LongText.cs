using System.Collections.Concurrent;
using System.Text;
using KDesktop.Core;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>The meaning of texts as vectors (EmbeddingGemma in K-Desktop): one unit vector per text, or null without a model.</summary>
public interface ITextMeaning
{
    Task<IReadOnlyList<float[]>?> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}

/// <summary>A piece of a long text: where it starts, its page and its words.</summary>
public sealed record TextChunk(int Index, int Start, int Page, string Text);

/// <summary>
/// A text too long for the model's context (stage 14.3): cut in pieces of some <see cref="ChunkChars"/> letters at
/// paragraph or sentence ends, and the pieces that matter for a question found by words and by meaning together (names,
/// numbers and codes fail with the meaning alone; a question in other words fails with the words alone). Nothing is kept on
/// disk; the vectors of the last few files stay in memory for the next questions about them.
/// </summary>
public static class LongText
{
    public const int ChunkChars = 1200;

    /// <summary>How many pieces the words pick before the meaning sorts them (all of them would take too long to embed).</summary>
    private const int MeaningShortlist = 40;

    /// <summary>The pieces of <paramref name="content"/>, in order.</summary>
    public static IReadOnlyList<TextChunk> Chunks(FileContent content)
    {
        var text = content.Text;
        var chunks = new List<TextChunk>();
        var start = 0;
        while (start < text.Length)
        {
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
            if (start >= text.Length) break;
            var end = Math.Min(text.Length, start + ChunkChars);
            // A piece never runs into the next page: each one says its page.
            var nextPage = NextPage(content.PageStarts, start);
            if (nextPage < end) end = nextPage;
            else if (end < text.Length)
            {
                // At the end of a paragraph, else of a sentence, else of a word, in the last third of the piece.
                var from = start + ChunkChars * 2 / 3;
                var cut = text.LastIndexOf("\n\n", end, end - from, StringComparison.Ordinal);
                if (cut < 0) cut = LastSentenceEnd(text, from, end);
                if (cut < 0) cut = text.LastIndexOf(' ', end - 1, end - from);
                if (cut > start) end = cut + 1;
            }
            var piece = text[start..end].Trim();
            if (piece.Length > 0) chunks.Add(new TextChunk(chunks.Count, start, content.PageAt(start), piece));
            start = end;
        }
        return chunks;
    }

    private static int NextPage(IReadOnlyList<int> starts, int after)
    {
        foreach (var start in starts)
            if (start > after) return start;
        return int.MaxValue;
    }

    private static int LastSentenceEnd(string text, int from, int end)
    {
        for (var i = end - 1; i > from; i--)
            if (text[i] is '.' or '!' or '?' or '\n' && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1])) return i;
        return -1;
    }

    /// <summary>Evenly spread pieces up to <paramref name="budget"/> letters (for a summary): the first ones, then across the rest.</summary>
    public static IReadOnlyList<TextChunk> Spread(IReadOnlyList<TextChunk> chunks, int budget)
    {
        var fits = Math.Max(1, budget / ChunkChars);
        if (chunks.Count <= fits) return chunks;
        // The beginning (it says what it is about), the end (its conclusions) and evenly in between.
        var picked = new SortedSet<int> { 0 };
        if (fits > 1) picked.Add(chunks.Count - 1);
        if (fits > 2) picked.Add(1);
        for (var step = 1; picked.Count < fits && step < chunks.Count; step++)
            for (var i = 1; i < fits && picked.Count < fits; i++) picked.Add((int)Math.Round(i * (chunks.Count - 1) / (double)fits) + step - 1);
        return [.. picked.Where(i => i < chunks.Count).Select(i => chunks[i])];
    }

    /// <summary>The vectors of the last files' pieces: (file, piece index) → vector.</summary>
    private static readonly ConcurrentDictionary<(string File, int Index), float[]> Vectors = new();

    /// <summary>
    /// The pieces that best answer <paramref name="question"/>, up to <paramref name="budget"/> letters, in the document's
    /// order. <paramref name="key"/> names the file and its version, to keep the vectors.
    /// </summary>
    public static async Task<IReadOnlyList<TextChunk>> BestAsync(IReadOnlyList<TextChunk> chunks, string question, ITextMeaning? meaning, string key,
        int budget, CancellationToken cancellationToken)
    {
        var fits = Math.Max(1, budget / ChunkChars);
        if (chunks.Count <= fits) return chunks;
        var words = WordScores(chunks, question);
        var shortlist = Enumerable.Range(0, chunks.Count).OrderByDescending(i => words[i]).Take(MeaningShortlist).ToList();
        var score = new double[chunks.Count];
        var top = Math.Max(1e-9, words.Max());
        foreach (var i in shortlist) score[i] = words[i] / top;
        if (meaning is not null)
        {
            var missing = shortlist.Where(i => !Vectors.ContainsKey((key, i))).ToList();
            var asked = new List<string> { question };
            asked.AddRange(missing.Select(i => chunks[i].Text));
            var vectors = await meaning.EmbedAsync(asked, cancellationToken).ConfigureAwait(false);
            if (vectors is { Count: > 0 } && vectors.Count == asked.Count)
            {
                if (Vectors.Count > 4000) Vectors.Clear();
                for (var j = 0; j < missing.Count; j++) Vectors[(key, missing[j])] = vectors[j + 1];
                // Words and meaning together: the meaning counts a bit more (a question in other words).
                foreach (var i in shortlist)
                    if (Vectors.TryGetValue((key, i), out var vector)) score[i] = 0.4 * score[i] + 0.6 * Math.Max(0, Dot(vectors[0], vector));
            }
            else Log.Info("Asistente: sin significados para el documento: solo por palabras");
        }
        return [.. shortlist.OrderByDescending(i => score[i]).Take(fits).Order().Select(i => chunks[i])];
    }

    /// <summary>How much each piece has of the question's words, the rare ones counting more.</summary>
    private static double[] WordScores(IReadOnlyList<TextChunk> chunks, string question)
    {
        var wanted = TextSimilarity.Words(question).Where(w => w.Length > 2 || w.Any(char.IsDigit)).Distinct().ToList();
        var pieces = chunks.Select(c => TextSimilarity.Words(c.Text).ToHashSet(StringComparer.Ordinal)).ToList();
        var scores = new double[chunks.Count];
        foreach (var word in wanted)
        {
            var with = pieces.Count(p => p.Contains(word));
            if (with == 0) continue;
            var rarity = Math.Log(1 + chunks.Count / (double)with);
            for (var i = 0; i < pieces.Count; i++) if (pieces[i].Contains(word)) scores[i] += rarity;
        }
        return scores;
    }

    private static double Dot(float[] a, float[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++) sum += a[i] * b[i];
        return sum;
    }

    /// <summary>The pieces for the model, each with its page when the file has pages.</summary>
    public static string Describe(IReadOnlyList<TextChunk> chunks, bool pages)
    {
        var all = new StringBuilder();
        foreach (var chunk in chunks)
        {
            if (all.Length > 0) all.Append("\n[…]\n");
            if (pages) all.Append("[page ").Append(chunk.Page).Append("] ");
            all.Append(chunk.Text);
        }
        return all.ToString();
    }
}

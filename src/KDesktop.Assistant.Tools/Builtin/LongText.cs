using System.Collections.Concurrent;
using System.Text;
using KDesktop.Core;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// The meaning of texts as vectors (EmbeddingGemma in K-Desktop) for finding pieces of documents: questions
/// (<see cref="EmbedUse.Query"/>) and pieces (<see cref="EmbedUse.Document"/>), of <see cref="LongText.Dimensions"/>
/// numbers. Null without a model or an answer in time.
/// </summary>
public interface ITextMeaning
{
    Task<TextVectors?> EmbedAsync(IReadOnlyList<string> texts, EmbedUse use, CancellationToken cancellationToken);
}

/// <summary>One unit vector per text, in order, and which model made them (vectors of different models do not compare).</summary>
public sealed record TextVectors(string Model, IReadOnlyList<float[]> Vectors);

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

    /// <summary>The numbers of each vector of a piece or a question (EmbeddingGemma's first 256: Matryoshka).</summary>
    public const int Dimensions = 256;

    /// <summary>
    /// How many pieces the words pick before the meaning sorts them: each whole piece takes EmbeddingGemma some 0,35 s
    /// (all of them would take too long while the user waits).
    /// </summary>
    private const int MeaningShortlist = 24;

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
        // The pieces with the question's words; when few have them, pieces spread across it fill the list (the meaning may find what the words do not).
        var shortlist = Enumerable.Range(0, chunks.Count).Where(i => words[i] > 0).OrderByDescending(i => words[i]).Take(MeaningShortlist).ToList();
        foreach (var piece in Spread(chunks, (MeaningShortlist - shortlist.Count) * ChunkChars))
            if (shortlist.Count < MeaningShortlist && !shortlist.Contains(piece.Index)) shortlist.Add(piece.Index);
        var score = new double[chunks.Count];
        var top = Math.Max(1e-9, words.Max());
        foreach (var i in shortlist) score[i] = words[i] / top;
        if (meaning is not null)
        {
            var missing = shortlist.Where(i => !Vectors.ContainsKey((key, i))).ToList();
            var asked = await meaning.EmbedAsync([question], EmbedUse.Query, cancellationToken).ConfigureAwait(false);
            var pieces = missing.Count == 0 || asked is null ? null
                : await meaning.EmbedAsync([.. missing.Select(i => chunks[i].Text)], EmbedUse.Document, cancellationToken).ConfigureAwait(false);
            if (asked is { Vectors.Count: 1 } && (missing.Count == 0 || pieces?.Vectors.Count == missing.Count))
            {
                if (Vectors.Count > 4000) Vectors.Clear();
                for (var j = 0; j < missing.Count; j++) Vectors[(key, missing[j])] = pieces!.Vectors[j];
                // Words and meaning together: the meaning counts a bit more (a question in other words).
                foreach (var i in shortlist)
                    if (Vectors.TryGetValue((key, i), out var vector)) score[i] = 0.4 * score[i] + 0.6 * Math.Max(0, Dot(asked.Vectors[0], vector));
            }
            else Log.Info("Asistente: sin significados para el documento: solo por palabras");
        }
        return [.. shortlist.OrderByDescending(i => score[i]).Take(fits).Order().Select(i => chunks[i])];
    }

    /// <summary>How much each piece has of the question's words, the rare ones counting more.</summary>
    private static double[] WordScores(IReadOnlyList<TextChunk> chunks, string question)
    {
        var wanted = Terms(question).Distinct().ToList();
        var pieces = chunks.Select(c => Terms(c.Text).ToHashSet(StringComparer.Ordinal)).ToList();
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

    /// <summary>The words of a text that can find it: lower case and without accents, of 3 letters or more, or with a figure («2026», «a4»).</summary>
    public static IEnumerable<string> Terms(string text) => TextSimilarity.Words(text).Where(w => w.Length > 2 || w.Any(char.IsDigit));

    internal static double Dot(float[] a, float[] b)
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

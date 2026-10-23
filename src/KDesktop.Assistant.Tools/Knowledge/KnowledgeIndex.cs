using System.Collections.Concurrent;
using KDesktop.Assistant.Tools.Builtin;

namespace KDesktop.Assistant.Tools.Knowledge;

/// <summary>A piece of a document as the search sees it: its words (as numbers, sorted, with how many times) and its vector.</summary>
internal sealed class IndexedPiece(int page, int[] terms, ushort[] counts, int length, float[]? vector)
{
    public int Page { get; } = page;

    public int[] Terms { get; } = terms;

    public ushort[] Counts { get; } = counts;

    /// <summary>Its words in all (BM25 favours short pieces a bit).</summary>
    public int Length { get; } = length;

    public float[]? Vector { get; } = vector;

    public int CountOf(int term)
    {
        var at = Array.BinarySearch(Terms, term);
        return at >= 0 ? Counts[at] : 0;
    }
}

/// <summary>A document as the search sees it (its texts stay on disk).</summary>
internal sealed record IndexedDocument(string Path, long Size, long Written, string Chunker, string Model, KnowledgeState State, int Pages, bool Ocr,
    IndexedPiece[] Pieces);

/// <summary>A piece that answers a search: where it is, and how well it answers (0…1).</summary>
/// <param name="Words">How much of the question's words it has (the rare ones count more).</param>
/// <param name="Meaning">How close it is in meaning (0…1 scaled; 0 without vectors).</param>
public sealed record KnowledgeHit(string Path, int Piece, int Page, int Pages, double Score, double Words, double Meaning)
{
    /// <summary>Its text, read from the index on disk afterwards.</summary>
    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// The documents in memory and the mixed search (stage 19.3): by words —BM25 with the rare words counting more, light
/// plurals («facturas» finds «factura»), the file's name in every piece; names, figures and codes are found this way— and by
/// meaning, the cosine of EmbeddingGemma's vectors (a question in other words, or another language), the two together.
/// Thread-safe: documents are swapped whole, never changed.
/// </summary>
internal sealed class KnowledgeIndex
{
    /// <summary>BM25's usual numbers.</summary>
    private const double K1 = 1.2, B = 0.75;

    /// <summary>Candidates taken from each list before mixing them.</summary>
    private const int Candidates = 80;

    /// <summary>
    /// Cosines of EmbeddingGemma (query against piece, 256 numbers) below this are unrelated, and from <see cref="CosineHigh"/>
    /// on, a piece about just that: measured on the user's kind of documents.
    /// </summary>
    internal const double CosineLow = 0.30, CosineHigh = 0.62;

    /// <summary>Words and meaning: the meaning counts a bit more (a question in other words), the words keep names and figures.</summary>
    internal const double WordWeight = 0.45;

    private readonly ConcurrentDictionary<string, IndexedDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _vocabulary = new(StringComparer.Ordinal);
    private int _nextTerm;

    public int Count => _documents.Count;

    public IndexedDocument? Find(string path) => _documents.TryGetValue(path, out var document) ? document : null;

    public IEnumerable<IndexedDocument> All => _documents.Values;

    public void Remove(string path) => _documents.TryRemove(path, out _);

    public void Clear() => _documents.Clear();

    /// <summary>Puts (or replaces) a document as read: its words counted, its texts let go.</summary>
    public IndexedDocument Put(KnowledgeRecord record)
    {
        var title = TermIds(NameTerms(record.Path), add: true);
        var pieces = record.Pieces.Select(p => Piece(p, title)).ToArray();
        var document = new IndexedDocument(record.Path, record.Size, record.Written, record.Chunker, record.Model, record.State, record.Pages, record.Ocr, pieces);
        _documents[record.Path] = document;
        return document;
    }

    /// <summary>The words of a file's name («FacturaLuz-2026.pdf» → factura, luz, 2026).</summary>
    internal static IEnumerable<string> NameTerms(string path) =>
        LongText.Terms(string.Join(' ', FileSearch.NameWords(Path.GetFileNameWithoutExtension(path))));

    private IndexedPiece Piece(KnowledgeStoredPiece stored, int[] title)
    {
        var counts = new Dictionary<int, int>();
        var length = 0;
        foreach (var term in TermIds(Words(stored.Text), add: true))
        {
            counts[term] = counts.GetValueOrDefault(term) + 1;
            length++;
        }
        foreach (var term in title) counts.TryAdd(term, 1);
        var ids = counts.Keys.Order().ToArray();
        return new IndexedPiece(stored.Page, ids, [.. ids.Select(i => (ushort)Math.Min(counts[i], ushort.MaxValue))], Math.Max(1, length), stored.Vector);
    }

    /// <summary>The words that find a text, with light plurals taken off (both in the documents and in the questions).</summary>
    internal static IEnumerable<string> Words(string text) => LongText.Terms(text).Select(Stem);

    /// <summary>
    /// Plurals off, so that the singular and the plural give the same, in documents and questions: a final «s», then a final
    /// «e» after l, n, r, d, j or x («papeles» and «papel» → «papel», «tomates» and «tomate» → «tomate», «canciones» →
    /// «cancion», "lines" and "line" → "line", "invoices" → "invoice"). Figures as they are.
    /// </summary>
    internal static string Stem(string word)
    {
        if (word.Any(char.IsDigit)) return word;
        if (word.Length >= 4 && word[^1] == 's' && word[^2] != 's') word = word[..^1];
        if (word.Length >= 5 && word[^1] == 'e' && word[^2] is 'l' or 'n' or 'r' or 'd' or 'j' or 'x') word = word[..^1];
        return word;
    }

    private int[] TermIds(IEnumerable<string> words, bool add)
    {
        var ids = new List<int>();
        foreach (var word in words)
        {
            if (_vocabulary.TryGetValue(word, out var id)) ids.Add(id);
            else if (add) ids.Add(_vocabulary.GetOrAdd(word, _ => Interlocked.Increment(ref _nextTerm)));
        }
        return [.. ids];
    }

    /// <summary>
    /// The pieces that answer <paramref name="question"/>, best first, from <paramref name="floor"/> on: by words, and by meaning
    /// with <paramref name="vector"/> when given (only against the pieces whose vectors are of <paramref name="model"/>).
    /// <paramref name="inFile"/> narrows it to the documents whose name has those words.
    /// </summary>
    public List<KnowledgeHit> Search(string question, float[]? vector, string? model, int most, int perDocument, double floor, string? inFile = null)
    {
        var documents = _documents.Values.Where(d => d.State == KnowledgeState.Ready && d.Pieces.Length > 0).ToList();
        if (inFile is { Length: > 0 })
        {
            var wanted = NameTerms(inFile + ".x").ToHashSet(StringComparer.Ordinal);
            var named = documents.Where(d => NameTerms(d.Path).Any(wanted.Contains)).ToList();
            if (named.Count > 0) documents = named;
        }
        var pieces = documents.SelectMany(d => d.Pieces.Select((p, i) => (Document: d, Index: i, Piece: p))).ToList();
        if (pieces.Count == 0) return [];

        // Words: BM25, and how much of the question's weight each piece has (a piece with all its rare words = 1).
        var terms = Words(question).Distinct().ToList();
        var known = TermIds(terms, add: false).Distinct().ToArray();
        var total = pieces.Count;
        var frequency = new int[known.Length];
        foreach (var (_, _, piece) in pieces)
            for (var t = 0; t < known.Length; t++)
                if (piece.CountOf(known[t]) > 0) frequency[t]++;
        var idf = frequency.Select(f => f == 0 ? 0 : Math.Log(1 + (total - f + 0.5) / (f + 0.5))).ToArray();
        // A word of the question in no document still weighs (that piece of the question is not answered by the words).
        var weight = idf.Sum() + (terms.Count - known.Length) * Math.Log(1 + total);
        var average = pieces.Average(p => p.Piece.Length);
        var words = new double[total];
        var bm25 = new double[total];
        for (var i = 0; i < total; i++)
        {
            var piece = pieces[i].Piece;
            double have = 0, score = 0;
            for (var t = 0; t < known.Length; t++)
            {
                var count = piece.CountOf(known[t]);
                if (count == 0) continue;
                have += idf[t];
                score += idf[t] * count * (K1 + 1) / (count + K1 * (1 - B + B * piece.Length / average));
            }
            words[i] = weight > 0 ? have / weight : 0;
            bm25[i] = score;
        }

        // Meaning: the cosine, scaled between unrelated and «just that».
        var meaning = new double[total];
        var mixed = vector is not null;
        if (mixed)
            for (var i = 0; i < total; i++)
                if (pieces[i].Piece.Vector is { } v && v.Length == vector!.Length && pieces[i].Document.Model == model)
                    meaning[i] = Math.Clamp((LongText.Dot(vector, v) - CosineLow) / (CosineHigh - CosineLow), 0, 1);

        var candidates = Enumerable.Range(0, total).Where(i => bm25[i] > 0).OrderByDescending(i => bm25[i]).Take(Candidates)
            .Concat(mixed ? Enumerable.Range(0, total).Where(i => meaning[i] > 0).OrderByDescending(i => meaning[i]).Take(Candidates) : [])
            .Distinct();
        var hits = new List<KnowledgeHit>();
        foreach (var i in candidates)
        {
            var (document, index, piece) = pieces[i];
            // Without a vector (words only, or a document not yet with this model's), the words alone.
            var hasMeaning = mixed && piece.Vector is not null && document.Model == model;
            var score = hasMeaning ? WordWeight * words[i] + (1 - WordWeight) * meaning[i] : words[i];
            if (score >= floor) hits.Add(new KnowledgeHit(document.Path, index, piece.Page, document.Pages, score, words[i], meaning[i]));
        }
        return [.. hits.OrderByDescending(h => h.Score).ThenBy(h => h.Path, StringComparer.OrdinalIgnoreCase)
            .GroupBy(h => h.Path, StringComparer.OrdinalIgnoreCase).SelectMany(g => g.Take(perDocument))
            .OrderByDescending(h => h.Score).Take(most)];
    }
}

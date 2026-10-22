using System.Globalization;
using KDesktop.Assistant.Tools;
using KDesktop.Core;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Brain.Talk;

/// <summary>Vectors of meaning (the host asks K-Desktop's EmbeddingGemma for them through the pipe).</summary>
public interface IMeaning
{
    /// <summary>
    /// One unit-length vector per text, and the model that made them; null when there is no model, or no answer in time.
    /// </summary>
    /// <param name="keep">Texts asked for again and again (the example phrases): worth keeping on disk.</param>
    Task<MeaningVectors?> EmbedAsync(IReadOnlyList<string> texts, bool keep, CancellationToken cancellationToken);
}

public sealed record MeaningVectors(string Model, IReadOnlyList<float[]> Vectors);

/// <param name="Tools">The tools offered to the model for this order, closest first.</param>
/// <param name="How"><c>all</c> (few enough to offer them all), <c>meaning</c> or <c>words</c> (no model for the meaning).</param>
public sealed record ToolChoice(IReadOnlyList<ITool> Tools, string How);

/// <summary>
/// Which tools reach the model for an order (stage 3.5, plan §5.3): a small model chooses well among a few and badly
/// among many, and every tool costs instructions. Each tool's example phrases (<see cref="ITool.Examples"/>; its
/// description when it has none) become vectors once, kept by K-Desktop; the order's vector is compared with them
/// and the closest <see cref="Least"/>–<see cref="Most"/> are offered: those clearly behind the best are left out.
/// Without the meaning model, the words of the order against the words of the examples (rare words weigh more):
/// worse, but it works. The tools used in the last exchange are always offered («¿y cuánto le queda?»). Thread-safe.
/// </summary>
public sealed partial class ToolChooser(IMeaning? meaning)
{
    /// <summary>At most this many tools reach the model by their meaning (those that must be at hand come on top).</summary>
    public const int Most = 5;

    /// <summary>At least this many (when there are), however far: the meaning of a short order can be misleading.</summary>
    public const int Least = 3;

    /// <summary>
    /// At most this many more tools that must be at hand (used a moment before, with the rest of their small MCP server;
    /// the command tools…).
    /// </summary>
    public const int MostPinned = 10;

    /// <summary>Beyond <see cref="Least"/>, only tools this close to the best one.</summary>
    public const double Spread = 0.08;

    private readonly MeaningShelf? _shelf = meaning is null ? null : new MeaningShelf(meaning, "frase(s) de herramientas");

    /// <summary>The vectors of meaning, or null without them.</summary>
    public IMeaning? Meaning => meaning;

    /// <summary>Gets the vectors of the example phrases ready (connected: before the first order needs them).</summary>
    public async Task WarmAsync(IReadOnlyCollection<ITool> tools, CancellationToken cancellationToken)
    {
        if (_shelf is null || tools.Count <= Most) return;
        try { await _shelf.GetAsync(Anchors(tools), null, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Log.Warn($"Asistente: no se han podido preparar las frases de las herramientas: {ex.Message}"); }
    }

    /// <param name="recent">The tools used in the last exchange.</param>
    public async Task<ToolChoice> ChooseAsync(IReadOnlyCollection<ITool> tools, string said, IReadOnlyCollection<string> recent, CancellationToken cancellationToken)
    {
        MeaningVectors? order = null;
        if (_shelf is not null && tools.Count > Most)
        {
            try { order = await _shelf.Meaning.EmbedAsync([said], keep: false, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                Log.Warn($"Asistente: sin significado para elegir herramientas: {ex.Message}");
            }
        }
        return await ChooseAsync(tools, said, recent, order, cancellationToken).ConfigureAwait(false);
    }

    /// <param name="order">The order's vector, already asked for (null: without the meaning, by words).</param>
    public async Task<ToolChoice> ChooseAsync(IReadOnlyCollection<ITool> tools, string said, IReadOnlyCollection<string> recent, MeaningVectors? order, CancellationToken cancellationToken)
    {
        if (tools.Count <= Most) return new ToolChoice([.. tools], "all");
        var how = "meaning";
        IReadOnlyList<(ITool Tool, double Score)>? ranked = null;
        try { ranked = await MeaningAsync(tools, order, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Log.Warn($"Asistente: sin significado para elegir herramientas: {ex.Message}");
        }
        if (ranked is null)
        {
            how = "words";
            ranked = Words(tools, said);
        }

        // Beyond Least: by meaning, those close to the best; by words, those that share a word with the order.
        var best = ranked[0].Score;
        var chosen = ranked
            .Where((r, i) => i < Least || (i < Most && (how == "words" ? r.Score > 0 : r.Score >= best - Spread)))
            .Select(r => r.Tool).ToList();
        // An order of several parts: the best of each part too («…mira quién está conectado y envíamelo a Telegram, y dime
        // qué tengo en el calendario»: the whole order meant the calendar and 42, and send_telegram never came).
        var parts = Parts(said);
        if (parts.Count >= 2)
        {
            IReadOnlyList<IReadOnlyList<(ITool Tool, double Score)>>? byPart = null;
            try { byPart = how == "meaning" ? await PartsByMeaningAsync(tools, parts, cancellationToken).ConfigureAwait(false) : null; }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                Log.Warn($"Asistente: sin significado para las partes de la orden: {ex.Message}");
            }
            byPart ??= [.. parts.Select(p => Words(tools, p))];
            foreach (var part in byPart)
                foreach (var (tool, score) in part.Take(2))
                    if (!chosen.Contains(tool) && (how == "meaning" ? score >= PartClear && score >= part[0].Score - Spread : score > 0 && tool == part[0].Tool))
                        chosen.Add(tool);
        }
        // What must be at hand comes on top of those, never instead of another of its own: one by one into a full list,
        // each pushed the one before out («¿y el mes que viene?» lost list_events, used a moment before, to open_web).
        var pinned = recent.Distinct(StringComparer.Ordinal).Take(MostPinned)
            .Select(name => tools.FirstOrDefault(t => t.Name == name)).OfType<ITool>().Where(t => !chosen.Contains(t)).ToList();
        chosen.AddRange(pinned);
        var scores = ranked.ToDictionary(r => r.Tool, r => r.Score);
        Log.Info($"Asistente: herramientas ({how}): " + string.Join(", ", chosen.Select(t => $"{t.Name} {scores[t].ToString("0.00", CultureInfo.InvariantCulture)}")));
        return new ToolChoice(chosen, how);
    }

    /// <summary>
    /// The tools closest to what the model looks for (<c>find_tools</c>, 5.0.140): by meaning, else by words; at most
    /// <paramref name="most"/>, best first.
    /// </summary>
    public async Task<IReadOnlyList<ITool>> SearchAsync(IReadOnlyCollection<ITool> tools, string query, int most, CancellationToken cancellationToken)
    {
        if (tools.Count == 0 || query.Trim().Length == 0) return [];
        IReadOnlyList<(ITool Tool, double Score)>? ranked = null;
        if (_shelf is not null)
        {
            try
            {
                var vectors = await _shelf.Meaning.EmbedAsync([query], keep: false, cancellationToken).ConfigureAwait(false);
                ranked = await MeaningAsync(tools, vectors, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                Log.Warn($"Asistente: sin significado para buscar herramientas: {ex.Message}");
            }
        }
        ranked ??= [.. Words(tools, query).Where(r => r.Score > 0)];
        return [.. ranked.Take(most).Select(r => r.Tool)];
    }

    /// <summary>A part of an order this close to a tool means it (it came from one part, not the whole order).</summary>
    public const double PartClear = 0.60;

    /// <summary>
    /// The parts of an order of several («mira quién está conectado y envíamelo a Telegram, y dime…»): split at punctuation
    /// and at «y», «también», «luego»… (and their like in the other languages); none of fewer than two words. One part: the order.
    /// </summary>
    public static IReadOnlyList<string> Parts(string said) =>
        [.. PartBreak().Split(said).Select(p => LeadingJoin().Replace(p.Trim(), string.Empty))
            .Where(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2)];

    [System.Text.RegularExpressions.GeneratedRegex(@"^(?:(?:y|e|and|et|und|also|también|tambien|además|ademas|luego|después|despues|then|puis|dann|poi|depois|anche|aussi|auch)\s+)+",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex LeadingJoin();

    [System.Text.RegularExpressions.GeneratedRegex(@"[.,;:!?¿¡]+|\s(?:y|e|and|et|und|also|también|tambien|además|ademas|luego|después|despues|then|puis|dann|poi|depois|anche|aussi|auch)\s",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex PartBreak();

    /// <summary>Each part's tools by meaning, closest first; null without the model.</summary>
    private async Task<IReadOnlyList<IReadOnlyList<(ITool Tool, double Score)>>?> PartsByMeaningAsync(IReadOnlyCollection<ITool> tools, IReadOnlyList<string> parts, CancellationToken cancellationToken)
    {
        if (_shelf is null) return null;
        var vectors = await _shelf.Meaning.EmbedAsync(parts, keep: false, cancellationToken).ConfigureAwait(false);
        if (vectors is null) return null;
        var result = new List<IReadOnlyList<(ITool Tool, double Score)>>();
        foreach (var vector in vectors.Vectors)
            if (await MeaningAsync(tools, new MeaningVectors(vectors.Model, [vector]), cancellationToken).ConfigureAwait(false) is { } ranked)
                result.Add(ranked);
        return result;
    }

    // ── by meaning ───────────────────────────────────────────────────────

    /// <summary>Each tool with its closest example to the order, closest first; null without the model.</summary>
    private async Task<IReadOnlyList<(ITool Tool, double Score)>?> MeaningAsync(IReadOnlyCollection<ITool> tools, MeaningVectors? order, CancellationToken cancellationToken)
    {
        if (_shelf is null || order is null) return null;
        if (await _shelf.GetAsync(Anchors(tools), order.Model, cancellationToken).ConfigureAwait(false) is not { } anchors) return null;
        var vector = order.Vectors[0];
        return [.. tools
            .Select(t => (Tool: t, Score: AnchorsOf(t).Max(a => anchors.Vectors.TryGetValue(a, out var v) ? TextEmbedder.Similarity(vector, v) : -1)))
            .OrderByDescending(r => r.Score)];
    }

    /// <summary>What stands for a tool's meaning: its example phrases, or its description.</summary>
    private static IReadOnlyList<string> AnchorsOf(ITool tool) => tool.Examples.Count > 0 ? tool.Examples : [tool.Description];

    private static List<string> Anchors(IReadOnlyCollection<ITool> tools) => [.. tools.SelectMany(AnchorsOf).Distinct(StringComparer.Ordinal)];

    // ── by words ─────────────────────────────────────────────────────────

    /// <summary>
    /// Each tool by the words of the order found among the words of its examples (also with another ending: «apunta» /
    /// «apuntar»), each weighing more the fewer tools use it, best first; ties keep the registry's order.
    /// </summary>
    public static IReadOnlyList<(ITool Tool, double Score)> Words(IReadOnlyCollection<ITool> tools, string said)
    {
        var vocabularies = tools.Select(t => AnchorsOf(t).SelectMany(TextSimilarity.Words).Where(w => w.Length >= 3).ToHashSet(StringComparer.Ordinal)).ToList();
        var asked = TextSimilarity.Words(said).Where(w => w.Length >= 3).Distinct().ToList();
        var scores = new double[vocabularies.Count];
        foreach (var word in asked)
        {
            // How alike each tool's closest word is, and in how many tools there is one (the rarer, the more telling).
            var alike = vocabularies.Select(v => v.Count == 0 ? 0 : v.Max(w => TextSimilarity.WordSimilarity(word, w))).ToArray();
            var found = alike.Count(a => a >= 0.85);
            if (found == 0) continue;
            var weight = Math.Log(1 + (double)vocabularies.Count / found);
            for (var i = 0; i < scores.Length; i++)
                if (alike[i] >= 0.85) scores[i] += alike[i] * weight;
        }
        return [.. tools.Select((t, i) => (Tool: t, Score: scores[i])).OrderByDescending(r => r.Score)];
    }
}

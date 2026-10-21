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
public sealed class ToolChooser(IMeaning? meaning)
{
    /// <summary>At most this many tools reach the model.</summary>
    public const int Most = 5;

    /// <summary>At least this many (when there are), however far: the meaning of a short order can be misleading.</summary>
    public const int Least = 3;

    /// <summary>Beyond <see cref="Least"/>, only tools this close to the best one.</summary>
    public const double Spread = 0.08;

    /// <summary>Texts per <c>embed</c> (the protocol's limit).</summary>
    private const int Chunk = Core.Assistant.Pipe.EmbedMessage.MaxTexts;

    private readonly object _gate = new();
    /// <summary>The vectors of the phrases: replaced whole, never changed once published (read without the lock).</summary>
    private Dictionary<string, float[]> _anchors = new(StringComparer.Ordinal);
    private string? _model;
    /// <summary>The phrases being asked for now: whoever needs them meanwhile waits for it instead of asking again.</summary>
    private Task? _filling;

    /// <summary>Gets the vectors of the example phrases ready (connected: before the first order needs them).</summary>
    public async Task WarmAsync(IReadOnlyCollection<ITool> tools, CancellationToken cancellationToken)
    {
        if (meaning is null || tools.Count <= Most) return;
        try { await AnchorsAsync(tools, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Log.Warn($"Asistente: no se han podido preparar las frases de las herramientas: {ex.Message}"); }
    }

    /// <param name="recent">The tools used in the last exchange.</param>
    public async Task<ToolChoice> ChooseAsync(IReadOnlyCollection<ITool> tools, string said, IReadOnlyCollection<string> recent, CancellationToken cancellationToken)
    {
        if (tools.Count <= Most) return new ToolChoice([.. tools], "all");
        var how = "meaning";
        IReadOnlyList<(ITool Tool, double Score)>? ranked = null;
        try { ranked = await MeaningAsync(tools, said, cancellationToken).ConfigureAwait(false); }
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
        foreach (var name in recent)
        {
            if (chosen.Any(t => t.Name == name) || tools.FirstOrDefault(t => t.Name == name) is not { } again) continue;
            if (chosen.Count >= Most) chosen.RemoveAt(chosen.Count - 1);
            chosen.Add(again);
        }
        var scores = ranked.ToDictionary(r => r.Tool, r => r.Score);
        Log.Info($"Asistente: herramientas ({how}): " + string.Join(", ", chosen.Select(t => $"{t.Name} {scores[t].ToString("0.00", CultureInfo.InvariantCulture)}")));
        return new ToolChoice(chosen, how);
    }

    // ── by meaning ───────────────────────────────────────────────────────

    /// <summary>Each tool with its closest example to the order, closest first; null without the model.</summary>
    private async Task<IReadOnlyList<(ITool Tool, double Score)>?> MeaningAsync(IReadOnlyCollection<ITool> tools, string said, CancellationToken cancellationToken)
    {
        if (meaning is null) return null;
        var order = meaning.EmbedAsync([said], keep: false, cancellationToken);
        var anchors = await AnchorsAsync(tools, cancellationToken).ConfigureAwait(false);
        if (await order.ConfigureAwait(false) is not { } asked || anchors is null) return null;
        if (asked.Model != anchors.Value.Model)
        {
            // Another model now (downloaded again): the phrases' vectors no longer compare.
            Log.Info($"Asistente: modelo de significado nuevo ({asked.Model}): se vuelven a preparar las frases");
            lock (_gate)
                if (_model == anchors.Value.Model)
                {
                    _anchors = new Dictionary<string, float[]>(StringComparer.Ordinal);
                    _model = null;
                }
            anchors = await AnchorsAsync(tools, cancellationToken).ConfigureAwait(false);
            if (anchors is null || asked.Model != anchors.Value.Model) return null;
        }
        var vector = asked.Vectors[0];
        return [.. tools
            .Select(t => (Tool: t, Score: AnchorsOf(t).Max(a => anchors.Value.Vectors.TryGetValue(a, out var v) ? TextEmbedder.Similarity(vector, v) : -1)))
            .OrderByDescending(r => r.Score)];
    }

    /// <summary>What stands for a tool's meaning: its example phrases, or its description.</summary>
    private static IReadOnlyList<string> AnchorsOf(ITool tool) => tool.Examples.Count > 0 ? tool.Examples : [tool.Description];

    /// <summary>The vectors of every tool's phrases, asking for those still missing; null without the model.</summary>
    private async Task<(string Model, Dictionary<string, float[]> Vectors)?> AnchorsAsync(IReadOnlyCollection<ITool> tools, CancellationToken cancellationToken)
    {
        var all = tools.SelectMany(AnchorsOf).Distinct(StringComparer.Ordinal).ToList();
        // Twice at most: someone else's filling may have been for other tools.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (Ready(all) is { } ready) return ready;
            Task filling;
            bool mine;
            lock (_gate)
            {
                mine = _filling is not { IsCompleted: false };
                if (mine) _filling = FillAsync(all);
                filling = _filling!;
            }
            await filling.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (mine) break;
        }
        return Ready(all);
    }

    /// <summary>Asks K-Desktop for the phrases still missing (not for one order: it is not cancelled with it).</summary>
    private async Task FillAsync(List<string> all)
    {
        List<string> missing;
        lock (_gate) missing = [.. all.Where(a => !_anchors.ContainsKey(a))];
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var start = 0; start < missing.Count; start += Chunk)
        {
            var part = missing.Skip(start).Take(Chunk).ToList();
            if (await meaning!.EmbedAsync(part, keep: true, CancellationToken.None).ConfigureAwait(false) is not { } got) return;
            lock (_gate)
            {
                // Another model (downloaded again): what was there no longer compares.
                var changed = got.Model != _model;
                var next = changed ? new Dictionary<string, float[]>(StringComparer.Ordinal) : new Dictionary<string, float[]>(_anchors, StringComparer.Ordinal);
                for (var i = 0; i < part.Count; i++) next[part[i]] = got.Vectors[i];
                _anchors = next;
                _model = got.Model;
                if (changed && start > 0) return;
            }
        }
        if (missing.Count > 0) Log.Info($"Asistente: {missing.Count} frase(s) de herramientas preparadas en {watch.ElapsedMilliseconds} ms");
    }

    private (string Model, Dictionary<string, float[]> Vectors)? Ready(List<string> all)
    {
        lock (_gate) return _model is { } model && all.All(_anchors.ContainsKey) ? (model, _anchors) : null;
    }

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

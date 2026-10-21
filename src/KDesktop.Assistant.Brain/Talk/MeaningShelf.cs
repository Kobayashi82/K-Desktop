using KDesktop.Core;

namespace KDesktop.Assistant.Brain.Talk;

/// <summary>
/// The vectors of texts asked for again and again (the example phrases of the tools, the memories), kept in the process
/// for the model that made them: only the missing ones are asked for (<c>embed</c> with <c>keep</c>, so K-Desktop keeps
/// them on disk too), and another model starts afresh. Whoever needs texts that are being asked for waits for that
/// instead of asking again. Thread-safe.
/// </summary>
/// <param name="what">What the texts are, for the log («frase(s) de herramientas»).</param>
public sealed class MeaningShelf(IMeaning meaning, string what)
{
    /// <summary>Texts per <c>embed</c> (the protocol's limit).</summary>
    private const int Chunk = Core.Assistant.Pipe.EmbedMessage.MaxTexts;

    private readonly object _gate = new();
    /// <summary>Replaced whole, never changed once published (read without the lock).</summary>
    private Dictionary<string, float[]> _vectors = new(StringComparer.Ordinal);
    private string? _model;
    private Task? _filling;

    public IMeaning Meaning => meaning;

    /// <summary>
    /// The vectors of <paramref name="texts"/> made by <paramref name="model"/> (null: whichever K-Desktop has), asking for
    /// those missing; null without the model or when they cannot be had now.
    /// </summary>
    public async Task<(string Model, IReadOnlyDictionary<string, float[]> Vectors)?> GetAsync(IReadOnlyCollection<string> texts, string? model, CancellationToken cancellationToken)
    {
        var all = texts.Distinct(StringComparer.Ordinal).ToList();
        if (model is not null)
            lock (_gate)
                if (_model is not null && _model != model)
                {
                    // Another model now (downloaded again): what is here no longer compares.
                    Log.Info($"Asistente: modelo de significado nuevo ({model}): se vuelven a preparar las {what}");
                    _vectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
                    _model = null;
                }
        // Twice at most: someone else's filling may have been for other texts.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (Ready(all, model) is { } ready) return ready;
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
        return Ready(all, model);
    }

    /// <summary>Asks for the texts still missing (not for one order: it is not cancelled with it).</summary>
    private async Task FillAsync(List<string> all)
    {
        List<string> missing;
        lock (_gate) missing = [.. all.Where(a => !_vectors.ContainsKey(a))];
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var start = 0; start < missing.Count; start += Chunk)
        {
            var part = missing.Skip(start).Take(Chunk).ToList();
            if (await meaning.EmbedAsync(part, keep: true, CancellationToken.None).ConfigureAwait(false) is not { } got) return;
            lock (_gate)
            {
                var changed = got.Model != _model;
                var next = changed ? new Dictionary<string, float[]>(StringComparer.Ordinal) : new Dictionary<string, float[]>(_vectors, StringComparer.Ordinal);
                for (var i = 0; i < part.Count; i++) next[part[i]] = got.Vectors[i];
                _vectors = next;
                _model = got.Model;
                if (changed && start > 0) return;
            }
        }
        if (missing.Count > 0) Log.Info($"Asistente: {missing.Count} {what} preparadas en {watch.ElapsedMilliseconds} ms");
    }

    private (string Model, IReadOnlyDictionary<string, float[]> Vectors)? Ready(List<string> all, string? model)
    {
        lock (_gate)
            return _model is { } have && (model is null || model == have) && all.All(_vectors.ContainsKey) ? (have, _vectors) : null;
    }
}

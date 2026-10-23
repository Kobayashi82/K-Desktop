using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Assistant;

/// <summary>
/// K-Desktop's side of <c>embed</c> (stage 3.5): the assistant process asks for vectors of meaning and K-Desktop makes
/// them with the EmbeddingGemma it already loads for the voice (one copy of the model in memory, not two). What the
/// process asks with <c>keep</c> (the example phrases of its tools) is kept on disk (<see cref="CacheFile"/>), so a new
/// process gets them at once; its orders are never written down. Thread-safe.
/// </summary>
public sealed class AssistantMeaning(TextEmbedder embedder)
{
    /// <summary>Which vectors these are (<c>embedded.model</c>): another model or prompt would need another name.</summary>
    public const string ModelName = "embeddinggemma-300m-int8";

    /// <summary>More kept phrases than this start afresh (tools come and go; nothing else prunes them).</summary>
    private const int MaxKept = 5000;

    private const string CacheMagic = "KDTOOLS1";

    private readonly object _gate = new();
    private VectorCache? _cache;

    public static string CacheFile => Path.Combine(VoiceModels.EmbeddingDir, "assistant.bin");

    /// <summary>The answer to send back: <c>embedded</c>, or an <c>error</c> with the request's id.</summary>
    public PipeMessage Answer(EmbedMessage request)
    {
        if (request.Texts.Count > EmbedMessage.MaxTexts)
            return new ErrorMessage("badRequest", $"Como mucho {EmbedMessage.MaxTexts} textos por «embed»", request.Id);
        if (!TextEmbedder.Available) return new ErrorMessage("noModel", "EmbeddingGemma no está descargado", request.Id);
        var use = request.Use switch
        {
            EmbedMessage.Query => EmbedUse.Query,
            EmbedMessage.Document => EmbedUse.Document,
            null => EmbedUse.Similarity,
            _ => (EmbedUse?)null,
        };
        if (use is null) return new ErrorMessage("badRequest", $"«use» desconocido: {request.Use}", request.Id);
        var size = request.Size ?? TextEmbedder.Size;
        try
        {
            var vectors = request.Keep == true && use == EmbedUse.Similarity && size == TextEmbedder.Size
                ? Kept(request.Texts)
                : embedder.Embed(request.Texts, use.Value, size);
            return new EmbeddedMessage(request.Id, vectors, ModelName);
        }
        catch (Exception ex)
        {
            Log.Error("Asistente: no se han podido calcular los significados", ex);
            return new ErrorMessage("noModel", ex.Message, request.Id);
        }
    }

    private float[][] Kept(IReadOnlyList<string> texts)
    {
        lock (_gate)
        {
            _cache ??= new VectorCache(CacheFile, CacheMagic);
            var missing = texts.Where(t => !_cache.TryGet(t, out _)).Distinct().ToList();
            if (missing.Count > 0)
            {
                if (_cache.Count + missing.Count > MaxKept) _cache.Clear();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var vectors = embedder.Embed(missing);
                for (var i = 0; i < missing.Count; i++) _cache.Put(missing[i], vectors[i]);
                Log.Info($"Asistente: {missing.Count} frase(s) de ejemplo nuevas preparadas en {watch.ElapsedMilliseconds} ms");
            }
            if (_cache.Dirty) _cache.Save();
            return [.. texts.Select(t => _cache.TryGet(t, out var v) ? v : throw new InvalidOperationException(t))];
        }
    }
}

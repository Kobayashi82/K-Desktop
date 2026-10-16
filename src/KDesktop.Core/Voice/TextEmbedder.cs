using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace KDesktop.Core.Voice;

/// <summary>
/// Text → meaning, with EmbeddingGemma 300M: a 768-number vector per text, and texts that mean the same point the same
/// way («limpia la papelera» and "Vaciar Papelera", in any language). Its own SentencePiece tokenizer
/// (<c>&lt;bos&gt;</c> text <c>&lt;eos&gt;</c>), the int8 ONNX model whose <c>sentence_embedding</c> output already
/// includes the mean pooling and the projection. Loaded on first use (or <see cref="Preload"/>, ~1 s) and released
/// after <see cref="IdleUnload"/> without use. Thread-safe.
/// </summary>
public sealed class TextEmbedder : IDisposable
{
    /// <summary>
    /// EmbeddingGemma's prompt for comparing short texts with each other (the order and the command names are both
    /// short): measured better than the retrieval and classification prompts on the user's real commands.
    /// </summary>
    public const string Prompt = "task: sentence similarity | query: ";

    public const int Size = 768;

    /// <summary>Texts per run (they are padded to the longest one).</summary>
    private const int Batch = 16;

    private const int MaxTokens = 64;

    private readonly object _gate = new();
    private readonly Timer _unloadTimer;
    private InferenceSession? _session;
    private SentencePieceTokenizer? _tokenizer;

    public TextEmbedder() => _unloadTimer = new Timer(_ => Unload(), null, Timeout.Infinite, Timeout.Infinite);

    public TimeSpan IdleUnload { get; set; } = TimeSpan.FromMinutes(10);

    public static bool Available => VoiceModels.HasEmbeddings;

    /// <summary>Loads the model in the background (while the user is still speaking).</summary>
    public void Preload()
    {
        if (!Available) return;
        Task.Run(() =>
        {
            try { lock (_gate) EnsureLoaded(); }
            catch (Exception ex) { Log.Error("No se pudo cargar el modelo de significado", ex); }
        });
    }

    /// <summary>The token ids of a text as the model sees it (the prompt included), for the diagnostics.</summary>
    public IReadOnlyList<int> Tokens(string text)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return Encode(text);
        }
    }

    /// <summary>One unit-length vector per text (<see cref="Prompt"/> is added here).</summary>
    public float[][] Embed(IReadOnlyList<string> texts)
    {
        var result = new float[texts.Count][];
        if (texts.Count == 0) return result;
        lock (_gate)
        {
            var session = EnsureLoaded();
            for (var start = 0; start < texts.Count; start += Batch)
            {
                var ids = texts.Skip(start).Take(Batch).Select(Encode).ToList();
                var length = ids.Max(i => i.Count);
                var inputIds = new long[ids.Count * length];
                var mask = new long[ids.Count * length];
                // Padded on the right with <pad> (0), as the reference tokenizer does.
                for (var b = 0; b < ids.Count; b++)
                    for (var t = 0; t < ids[b].Count; t++)
                    {
                        inputIds[b * length + t] = ids[b][t];
                        mask[b * length + t] = 1;
                    }
                using var outputs = session.Run(
                    [
                        NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, [ids.Count, length])),
                        NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(mask, [ids.Count, length])),
                    ],
                    ["sentence_embedding"]);
                var vectors = outputs[0].AsEnumerable<float>().ToArray();
                for (var b = 0; b < ids.Count; b++) result[start + b] = Normalize(vectors.AsSpan(b * Size, Size));
            }
            _unloadTimer.Change(IdleUnload, Timeout.InfiniteTimeSpan);
        }
        return result;
    }

    /// <summary>Cosine similarity of two unit vectors.</summary>
    public static double Similarity(float[] a, float[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }

    private List<int> Encode(string text)
    {
        var ids = _tokenizer!.EncodeToIds(Prompt + text.Trim(), MaxTokens - 1, out _, out _).ToList();
        if (ids.Count == 0 || ids[^1] != _tokenizer.EndOfSentenceId) ids.Add(_tokenizer.EndOfSentenceId);
        return ids;
    }

    private static float[] Normalize(ReadOnlySpan<float> v)
    {
        var norm = 0.0;
        foreach (var x in v) norm += x * x;
        norm = Math.Sqrt(norm);
        var result = new float[v.Length];
        for (var i = 0; i < v.Length; i++) result[i] = (float)(v[i] / (norm > 0 ? norm : 1));
        return result;
    }

    private InferenceSession EnsureLoaded()
    {
        if (_session is { } loaded) return loaded;
        if (!Available) throw new FileNotFoundException("Faltan los archivos de EmbeddingGemma", VoiceModels.EmbeddingDir);
        var watch = Stopwatch.StartNew();
        using (var stream = File.OpenRead(VoiceModels.EmbeddingTokenizer))
            _tokenizer = SentencePieceTokenizer.Create(stream, true, true); // <bos> … <eos>
        using var options = new SessionOptions
        {
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        _session = new InferenceSession(VoiceModels.EmbeddingModel, options);
        Log.Info($"Modelo de significado (EmbeddingGemma) cargado en {watch.ElapsedMilliseconds} ms");
        _unloadTimer.Change(IdleUnload, Timeout.InfiniteTimeSpan);
        return _session;
    }

    public void Unload()
    {
        lock (_gate)
        {
            if (_session is null) return;
            _session.Dispose();
            _session = null;
            _tokenizer = null;
            Log.Info("Modelo de significado descargado de la memoria (sin uso)");
        }
        GC.Collect(2, GCCollectionMode.Optimized, false);
    }

    public void Dispose()
    {
        _unloadTimer.Dispose();
        Unload();
    }
}

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KDesktop.Core.Voice;

/// <summary>
/// openWakeWord's audio features, ported from its <c>AudioFeatures</c> class (openwakeword/utils.py, Apache 2.0):
/// 16 kHz audio as 16-bit values → <c>melspectrogram.onnx</c> (32 mel bins, a frame every 10 ms) → x/10 + 2 →
/// windows of 76 frames every 8 frames → <c>embedding_model.onnx</c> (Google's speech_embedding) → 96 numbers every
/// 80 ms. Thread-safe (ONNX Runtime sessions are), one thread each, so idle CPU use stays tiny.
/// </summary>
public sealed class OwwFeatures : IDisposable
{
    public const int Chunk = 1280, Hop = 160, Context = 160 * 3, MelBins = 32, Window = 76, Step = 8, Size = 96;

    private readonly InferenceSession _mel, _embedding;
    private readonly string _melInput, _embeddingInput;

    public OwwFeatures()
    {
        using var options = new SessionOptions
        {
            IntraOpNumThreads = 1,
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        _mel = new InferenceSession(VoiceModels.WakeMelspec, options);
        _embedding = new InferenceSession(VoiceModels.WakeEmbedding, options);
        _melInput = _mel.InputMetadata.Keys.First();             // "input"
        _embeddingInput = _embedding.InputMetadata.Keys.First(); // "input_1"
    }

    /// <summary>Audio −1…1 → the 16-bit values the models expect (as float, like <c>x.astype(np.float32)</c>).</summary>
    public static float[] ToPcm16(ReadOnlySpan<float> samples)
    {
        var pcm = new float[samples.Length];
        for (var i = 0; i < samples.Length; i++) pcm[i] = (short)Math.Clamp(Math.Round(samples[i] * 32767.0), short.MinValue, short.MaxValue);
        return pcm;
    }

    /// <summary>Mel spectrogram of 16-bit-valued audio: one row of 32 per 10 ms frame, already transformed (x/10 + 2).</summary>
    public float[][] Melspectrogram(float[] pcm16)
    {
        var input = new DenseTensor<float>(pcm16, [1, pcm16.Length]);
        using var results = _mel.Run([NamedOnnxValue.CreateFromTensor(_melInput, input)]);
        var output = results[0].AsEnumerable<float>().ToArray(); // (1, 1, frames, 32)
        var frames = output.Length / MelBins;
        var spec = new float[frames][];
        for (var f = 0; f < frames; f++)
        {
            var row = spec[f] = new float[MelBins];
            for (var b = 0; b < MelBins; b++) row[b] = output[f * MelBins + b] / 10 + 2;
        }
        return spec;
    }

    /// <summary>Embeddings of several 76-frame windows at once (batch × 76 × 32 × 1 → batch × 96).</summary>
    public float[][] Embed(IReadOnlyList<float[][]> windows)
    {
        if (windows.Count == 0) return [];
        var data = new float[windows.Count * Window * MelBins];
        for (var w = 0; w < windows.Count; w++)
            for (var f = 0; f < Window; f++)
                Array.Copy(windows[w][f], 0, data, (w * Window + f) * MelBins, MelBins);
        var input = new DenseTensor<float>(data, [windows.Count, Window, MelBins, 1]);
        using var results = _embedding.Run([NamedOnnxValue.CreateFromTensor(_embeddingInput, input)]);
        var output = results[0].AsEnumerable<float>().ToArray();
        var embeddings = new float[windows.Count][];
        for (var w = 0; w < windows.Count; w++) embeddings[w] = output.AsSpan(w * Size, Size).ToArray();
        return embeddings;
    }

    /// <summary>
    /// Embeddings of a whole clip (audio −1…1), like <c>_get_embeddings</c>: windows of 76 mel frames every 8; the
    /// last ones that would be short are dropped. Embedding k covers roughly samples 1280·k … 1280·k + 12 640.
    /// </summary>
    public float[][] EmbedClip(ReadOnlySpan<float> samples)
    {
        var spec = Melspectrogram(ToPcm16(samples));
        var windows = new List<float[][]>();
        for (var i = 0; i + Window <= spec.Length; i += Step) windows.Add(spec[i..(i + Window)]);
        return Embed(windows);
    }

    /// <summary>Sample (from the start of the clip) where the window of embedding <paramref name="k"/> ends.</summary>
    public static int WindowEnd(int k) => (Step * k + Window + 3) * Hop;

    public void Dispose()
    {
        _mel.Dispose();
        _embedding.Dispose();
    }
}

/// <summary>
/// The streaming side of <see cref="OwwFeatures"/>, like <c>_streaming_features</c>: audio is taken in 80 ms chunks
/// (1280 samples); each chunk, with the 480 samples before it, gives 8 new mel frames, and the last 76 frames one
/// new embedding. The mel buffer starts as ones, as in openWakeWord.
/// </summary>
public sealed class OwwStream(OwwFeatures features)
{
    private readonly List<float> _pending = new(OwwFeatures.Chunk * 2);
    private readonly float[] _history = new float[OwwFeatures.Context];
    private int _historyLength;
    private readonly List<float[]> _mel = Enumerable.Range(0, OwwFeatures.Window).Select(_ => Enumerable.Repeat(1f, OwwFeatures.MelBins).ToArray()).ToList();

    /// <summary>Feeds audio (−1…1); returns the embeddings it completed (one per 80 ms).</summary>
    public List<float[]> Accept(ReadOnlySpan<float> samples)
    {
        _pending.AddRange(OwwFeatures.ToPcm16(samples));
        var output = new List<float[]>();
        while (_pending.Count >= OwwFeatures.Chunk)
        {
            var input = new float[_historyLength + OwwFeatures.Chunk];
            Array.Copy(_history, OwwFeatures.Context - _historyLength, input, 0, _historyLength);
            _pending.CopyTo(0, input, _historyLength, OwwFeatures.Chunk);
            _pending.RemoveRange(0, OwwFeatures.Chunk);
            // The last 480 samples of this chunk are the context of the next one.
            Array.Copy(input, input.Length - OwwFeatures.Context, _history, 0, OwwFeatures.Context);
            _historyLength = OwwFeatures.Context;

            _mel.AddRange(features.Melspectrogram(input));
            if (_mel.Count > OwwFeatures.Window * 2) _mel.RemoveRange(0, _mel.Count - OwwFeatures.Window);
            output.Add(features.Embed([_mel.GetRange(_mel.Count - OwwFeatures.Window, OwwFeatures.Window).ToArray()])[0]);
        }
        return output;
    }
}

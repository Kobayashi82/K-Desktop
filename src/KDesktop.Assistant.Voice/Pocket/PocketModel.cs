using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using KDesktop.Core;
using Microsoft.ML.OnnxRuntime;

namespace KDesktop.Assistant.Voice.Pocket;

/// <summary>
/// Pocket TTS of one language, loaded (ported from Kobii). Five pieces: the tokenizer (SentencePiece),
/// <c>text_conditioner</c> (text to vectors), <c>flow_lm_main</c> (a transformer that goes frame by frame with explicit
/// state: each step gives a condition vector and whether it has finished), <c>flow_lm_flow</c> (turns noise into the next
/// frame of 32 numbers) and <c>mimi_decoder</c> (frames to 24 kHz audio, 1920 samples per frame). The voice is the state
/// of the transformer after hearing the person. It follows Kyutai's implementation (Apache 2.0) and phoonnx's for the
/// state contract of <c>bundle.json</c>. Not thread-safe: one sentence at a time.
/// </summary>
public sealed class PocketModel : IDisposable
{
    public const int SampleRate = 24_000;

    /// <summary>Threshold of «I have finished».</summary>
    private const float EndThreshold = -4;

    /// <summary>
    /// Frames per text piece before which «I have finished» is ignored: it sometimes says so right at the start (it is
    /// usually some two frames per piece).
    /// </summary>
    private const double MinFramesPerToken = 1;

    /// <summary>Frames decoded at once.</summary>
    private const int DecodeChunk = 15;

    /// <summary>A tensor of a model's state: its name, shape, type and how it starts.</summary>
    private sealed record StateEntry(string Input, int Index, string Module, string Key, long[] Shape, string Type, string Fill);

    private readonly InferenceSession _conditioner;
    private readonly InferenceSession _main;
    private readonly InferenceSession _flow;
    private readonly InferenceSession _decoder;
    private readonly StateEntry[] _mainState;
    private readonly StateEntry[] _decoderState;
    private readonly string[] _mainInputs;
    private readonly string[] _mainOutputs;
    private readonly string[] _decoderInputs;
    private readonly string[] _decoderOutputs;
    private readonly int _latentDim;
    private readonly int _conditioningDim;
    private readonly double _frameRate;
    private OrtValue[] _voice = [];

    private PocketModel(string language, string folder, SessionOptions options)
    {
        Language = language;
        using var bundle = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "bundle.json")));
        var root = bundle.RootElement;
        _latentDim = root.GetProperty("latent_dim").GetInt32();
        _conditioningDim = root.GetProperty("conditioning_dim").GetInt32();
        _frameRate = root.GetProperty("frame_rate").GetDouble();
        MaxTokensPerChunk = root.TryGetProperty("max_token_per_chunk", out var max) ? max.GetInt32() : 50;
        if (root.GetProperty("sample_rate").GetInt32() != SampleRate) throw new InvalidDataException("La voz no es de 24 kHz");

        _mainState = Manifest(root.GetProperty("flow_lm_state_manifest"));
        _decoderState = Manifest(root.GetProperty("mimi_state_manifest"));
        _mainInputs = ["sequence", "text_embeddings", .. _mainState.Select(e => e.Input)];
        _mainOutputs = ["conditioning", "eos_logit", .. _mainState.Select(e => "out_" + e.Input)];
        _decoderInputs = ["latent", .. _decoderState.Select(e => e.Input)];
        _decoderOutputs = ["audio_frame", .. _decoderState.Select(e => "out_" + e.Input)];

        Tokenizer = SentencePiece.Load(File.ReadAllBytes(Path.Combine(folder, "tokenizer.model")));
        _conditioner = new InferenceSession(Path.Combine(folder, "text_conditioner.onnx"), options);
        _main = new InferenceSession(Path.Combine(folder, "flow_lm_main_int8.onnx"), options);
        _flow = new InferenceSession(Path.Combine(folder, "flow_lm_flow_int8.onnx"), options);
        _decoder = new InferenceSession(Path.Combine(folder, "mimi_decoder_int8.onnx"), options);
    }

    public string Language { get; }

    /// <summary>The voice whose state is loaded (the name of its <c>.safetensors</c>).</summary>
    public string? Voice { get; private set; }

    public SentencePiece Tokenizer { get; }

    public int MaxTokensPerChunk { get; }

    /// <summary>Loads the model of a language from its folder (slow: a second or two).</summary>
    public static PocketModel Load(string language, string folder)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var options = new SessionOptions
        {
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        var model = new PocketModel(language, folder, options);
        Log.Info($"Asistente: voz Pocket TTS ({language}) cargada en {watch.ElapsedMilliseconds} ms");
        return model;
    }

    /// <summary>Uses another voice (its state file: cheap, the model stays).</summary>
    public void UseVoice(string name, string path)
    {
        if (Voice == name) return;
        var state = VoiceState(path);
        foreach (var value in _voice) value.Dispose();
        _voice = state;
        Voice = name;
    }

    /// <summary>
    /// A piece of text in audio (24 kHz, the voice as it is), handed out as it is made (<paramref name="onAudio"/>: the
    /// first bit after a few frames, then about every 1.2 s), so it can sound before it is finished. At most
    /// <paramref name="maxSeconds"/> (it sometimes rambles on). Returns all of it.
    /// </summary>
    public float[] Say(string chunk, Random random, float temperature, CancellationToken cancellationToken, Action<float[]>? onAudio = null, double maxSeconds = double.MaxValue)
    {
        if (_voice.Length == 0) throw new InvalidOperationException("Sin voz cargada");
        var prepared = PocketText.Prepare(chunk) ?? string.Empty;
        var ids = Tokenizer.Encode(prepared);
        if (ids.Count == 0) return [];
        var audio = new List<float>();
        using var decoder = new StreamingDecoder(this);
        var decoded = 0;
        var maxFrames = maxSeconds >= double.MaxValue ? int.MaxValue : (int)(maxSeconds * _frameRate);
        Generate(ids, PocketText.FramesAfterEnd(prepared), random, temperature, maxFrames, latents =>
        {
            // The first audio as soon as there is a little; then in larger pieces (fewer runs of the decoder).
            var due = decoded == 0 ? FirstDecode : DecodeChunk;
            if (latents.Count - decoded < due) return;
            Emit(decoder.Decode(latents, decoded, latents.Count - decoded));
            decoded = latents.Count;
        }, latents =>
        {
            if (latents.Count > decoded) Emit(decoder.Decode(latents, decoded, latents.Count - decoded));
        }, cancellationToken);
        return [.. audio];

        void Emit(float[] piece)
        {
            audio.AddRange(piece);
            if (piece.Length > 0) onAudio?.Invoke(piece);
        }
    }

    /// <summary>Frames decoded before the first audio goes out (0.4 s).</summary>
    private const int FirstDecode = 5;

    public void Dispose()
    {
        foreach (var value in _voice) value.Dispose();
        _conditioner.Dispose();
        _main.Dispose();
        _flow.Dispose();
        _decoder.Dispose();
    }

    /// <summary>
    /// The loop of frames: the transformer's condition and «have I finished?», and the flow to the next frame. After each
    /// frame <paramref name="progress"/> sees them all so far; at the end, <paramref name="finished"/>.
    /// </summary>
    private void Generate(List<int> ids, int framesAfterEnd, Random random, float temperature, int maxFrames,
        Action<List<float[]>> progress, Action<List<float[]>> finished, CancellationToken cancellationToken)
    {
        using var run = new RunOptions();
        using var tokens = OrtValue.CreateTensorValueFromMemory(ids.Select(i => (long)i).ToArray(), [1, ids.Count]);
        using var embedded = _conditioner.Run(run, ["token_ids"], [tokens], ["embeddings"]);
        using var noSequence = OrtValue.CreateTensorValueFromMemory(Array.Empty<float>(), [1, 0, _latentDim]);
        using var noText = OrtValue.CreateTensorValueFromMemory(Array.Empty<float>(), [1, 0, _conditioningDim]);

        // First it hears the whole text, from the state of the voice (which is not touched: the outputs are new).
        var state = _main.Run(run, _mainInputs, [noSequence, embedded[0], .. _voice], _mainOutputs);
        var latents = new List<float[]>();
        var sequence = new float[_latentDim];
        Array.Fill(sequence, float.NaN);
        using var from = OrtValue.CreateTensorValueFromMemory(new float[] { 0 }, [1, 1]);
        using var to = OrtValue.CreateTensorValueFromMemory(new float[] { 1 }, [1, 1]);
        try
        {
            int? endAt = null;
            var limit = Math.Min(maxFrames, PocketText.MaxFrames(ids.Count, _frameRate));
            for (var step = 0; step < limit; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var current = OrtValue.CreateTensorValueFromMemory(sequence, [1, 1, _latentDim]);
                var next = _main.Run(run, _mainInputs, [current, noText, .. state.Skip(2)], _mainOutputs);
                state.Dispose();
                state = next;
                if (endAt is null && step >= MinFramesPerToken * ids.Count && state[1].GetTensorDataAsSpan<float>()[0] > EndThreshold) endAt = step;
                if (endAt is { } ended && step >= ended + framesAfterEnd) break;

                // One Euler step, as Kyutai: from noise to the frame, x + flow.
                var frame = new float[_latentDim];
                var scale = MathF.Sqrt(temperature);
                for (var i = 0; i < frame.Length; i++) frame[i] = Gaussian(random) * scale;
                using (var x = OrtValue.CreateTensorValueFromMemory(frame, [1, _latentDim]))
                using (var flow = _flow.Run(run, ["c", "s", "t", "x"], [state[0], from, to, x], ["flow_dir"]))
                {
                    var direction = flow[0].GetTensorDataAsSpan<float>();
                    for (var i = 0; i < frame.Length; i++) frame[i] += direction[i];
                }
                latents.Add(frame);
                sequence = frame;
                progress(latents);
            }
            finished(latents);
        }
        finally
        {
            state.Dispose();
        }
    }

    /// <summary>The frames into audio a few at a time, carrying the decoder's state (no seams between the pieces).</summary>
    private sealed class StreamingDecoder(PocketModel model) : IDisposable
    {
        private readonly RunOptions _run = new();
        private readonly OrtValue[] _initial = model._decoderState.Select(Initial).ToArray();
        private IDisposableReadOnlyCollection<OrtValue>? _state;

        public float[] Decode(List<float[]> latents, int start, int count)
        {
            var audio = new List<float>(count * 1920);
            for (var from = start; from < start + count; from += DecodeChunk)
            {
                var n = Math.Min(DecodeChunk, start + count - from);
                var data = new float[n * model._latentDim];
                for (var i = 0; i < n; i++) latents[from + i].CopyTo(data, i * model._latentDim);
                using var latent = OrtValue.CreateTensorValueFromMemory(data, [1, n, model._latentDim]);
                IReadOnlyCollection<OrtValue> inputs = _state is null ? [latent, .. _initial] : [latent, .. _state.Skip(1)];
                var next = model._decoder.Run(_run, model._decoderInputs, inputs, model._decoderOutputs);
                _state?.Dispose();
                _state = next;
                audio.AddRange(_state[0].GetTensorDataAsSpan<float>());
            }
            return [.. audio];
        }

        public void Dispose()
        {
            _state?.Dispose();
            foreach (var value in _initial) value.Dispose();
            _run.Dispose();
        }
    }

    /// <summary>The voice's state: the one in its file fitted into the tensors the model expects.</summary>
    private OrtValue[] VoiceState(string path)
    {
        var saved = SafeTensors.Read(path);
        var values = new OrtValue[_mainState.Length];
        for (var i = 0; i < _mainState.Length; i++)
        {
            var entry = _mainState[i];
            var key = $"{entry.Module}/{entry.Key}";
            // Each export calls the step counter its own way.
            if (entry.Key == "step" && !saved.ContainsKey(key) && saved.ContainsKey($"{entry.Module}/offset")) key = $"{entry.Module}/offset";
            values[i] = saved.TryGetValue(key, out var tensor) ? Fit(tensor, entry) : Initial(entry);
        }
        return values;
    }

    /// <summary>
    /// Fits a saved tensor into the shape the model declares: the voice's caches are as long as the audio it heard and the
    /// model wants them of a fixed size, so they are copied at the start of a new one.
    /// </summary>
    private static OrtValue Fit(SafeTensors.Tensor tensor, StateEntry entry)
    {
        var count = entry.Shape.Aggregate(1L, (a, b) => a * b);
        if (entry.Type == "int64")
        {
            var longs = new long[count];
            tensor.Longs.AsSpan(0, (int)Math.Min(count, tensor.Longs.Length)).CopyTo(longs);
            return OrtValue.CreateTensorValueFromMemory(longs, entry.Shape);
        }
        var floats = new float[count];
        if (tensor.Floats.Length == count) tensor.Floats.CopyTo(floats, 0);
        else
        {
            Array.Fill(floats, entry.Fill == "nan" ? float.NaN : entry.Fill == "ones" ? 1 : 0);
            if (tensor.Shape.Length == entry.Shape.Length) CopyLeading(tensor.Floats, tensor.Shape, floats, entry.Shape);
        }
        return OrtValue.CreateTensorValueFromMemory(floats, entry.Shape);
    }

    /// <summary>Copies the common corner of two tensors of the same rank (of different shapes).</summary>
    private static void CopyLeading(float[] source, long[] sourceShape, float[] target, long[] targetShape)
    {
        var rank = sourceShape.Length;
        var common = new long[rank];
        for (var d = 0; d < rank; d++) common[d] = Math.Min(sourceShape[d], targetShape[d]);

        // The last dimension is copied at once; the others are walked.
        var row = (int)common[rank - 1];
        var index = new long[rank - 1];
        while (true)
        {
            long sourceOffset = 0, targetOffset = 0;
            for (var d = 0; d < rank - 1; d++)
            {
                sourceOffset = (sourceOffset + index[d]) * sourceShape[d + 1];
                targetOffset = (targetOffset + index[d]) * targetShape[d + 1];
            }
            Array.Copy(source, sourceOffset, target, targetOffset, row);
            var dim = rank - 2;
            while (dim >= 0 && ++index[dim] >= common[dim])
            {
                index[dim] = 0;
                dim--;
            }
            if (dim < 0) return;
        }
    }

    /// <summary>A tensor of the state as it starts.</summary>
    private static OrtValue Initial(StateEntry entry)
    {
        var count = entry.Shape.Aggregate(1L, (a, b) => a * b);
        switch (entry.Type)
        {
            case "bool":
                var flags = new bool[count];
                Array.Fill(flags, entry.Fill == "ones");
                return OrtValue.CreateTensorValueFromMemory(flags, entry.Shape);
            case "int64":
                var longs = new long[count];
                Array.Fill(longs, entry.Fill == "ones" ? 1 : 0);
                return OrtValue.CreateTensorValueFromMemory(longs, entry.Shape);
            default:
                var floats = new float[count];
                Array.Fill(floats, entry.Fill == "nan" ? float.NaN : entry.Fill == "ones" ? 1 : 0);
                return OrtValue.CreateTensorValueFromMemory(floats, entry.Shape);
        }
    }

    private static StateEntry[] Manifest(JsonElement manifest) =>
    [
        .. manifest.EnumerateArray()
            .Select(e => new StateEntry(
                e.GetProperty("input_name").GetString()!,
                e.GetProperty("index").GetInt32(),
                e.GetProperty("module").GetString()!,
                e.GetProperty("key").GetString()!,
                [.. e.GetProperty("shape").EnumerateArray().Select(d => d.GetInt64())],
                e.GetProperty("dtype").GetString()!,
                e.GetProperty("fill").GetString()!))
            .OrderBy(e => e.Index),
    ];

    /// <summary>A normally distributed random number (Box-Muller).</summary>
    private static float Gaussian(Random random)
    {
        var u = 1 - random.NextDouble();
        var v = random.NextDouble();
        return (float)(Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v));
    }

    /// <summary>Reads a .safetensors file (a JSON header and the data after it), float32 and int64 only.</summary>
    private static class SafeTensors
    {
        public sealed record Tensor(long[] Shape, float[] Floats, long[] Longs);

        public static Dictionary<string, Tensor> Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var headerLength = (int)BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            using var header = JsonDocument.Parse(Encoding.UTF8.GetString(bytes, 8, headerLength));
            var data = 8 + headerLength;
            var tensors = new Dictionary<string, Tensor>(StringComparer.Ordinal);
            foreach (var property in header.RootElement.EnumerateObject())
            {
                if (property.Name == "__metadata__") continue;
                var shape = property.Value.GetProperty("shape").EnumerateArray().Select(d => d.GetInt64()).ToArray();
                var offsets = property.Value.GetProperty("data_offsets").EnumerateArray().Select(d => (int)d.GetInt64()).ToArray();
                var span = bytes.AsSpan(data + offsets[0], offsets[1] - offsets[0]);
                tensors[property.Name] = property.Value.GetProperty("dtype").GetString() switch
                {
                    "F32" => new Tensor(shape, [.. MemoryMarshal.Cast<byte, float>(span)], []),
                    "I64" => new Tensor(shape, [], [.. MemoryMarshal.Cast<byte, long>(span)]),
                    var other => throw new InvalidDataException($"{property.Name}: tipo {other} no soportado"),
                };
            }
            return tensors;
        }
    }
}

using System.Text.Json;
using Microsoft.ML.OnnxRuntime;

namespace KDesktop.Assistant.Voice.Piper;

/// <summary>
/// One Piper voice (rhasspy's VITS, exported to ONNX): espeak phonemes in, audio out at the voice's rate, in one go. Its
/// <c>.onnx.json</c> says which number each phoneme is, the rate, how it was trained to vary (<c>noise_scale</c>,
/// <c>length_scale</c>, <c>noise_w</c>) and, for a voice of several speakers, their numbers. Not thread-safe: one sentence
/// at a time.
/// </summary>
public sealed class PiperModel : IDisposable
{
    private readonly InferenceSession _session;
    private readonly PiperIds _ids;
    private readonly IReadOnlyDictionary<string, long> _speakers;
    private readonly long _defaultSpeaker;
    private readonly float _noise, _length, _noiseW;
    private readonly bool _takesSpeaker;

    private PiperModel(InferenceSession session, JsonElement config)
    {
        _session = session;
        SampleRate = config.GetProperty("audio").GetProperty("sample_rate").GetInt32();
        EspeakVoice = config.TryGetProperty("espeak", out var espeak) && espeak.TryGetProperty("voice", out var voice) ? voice.GetString() ?? "en-us" : "en-us";
        _ids = new PiperIds(config);
        var speakers = new Dictionary<string, long>(StringComparer.Ordinal);
        if (config.TryGetProperty("speaker_id_map", out var map) && map.ValueKind == JsonValueKind.Object)
            foreach (var speaker in map.EnumerateObject()) speakers[speaker.Name] = speaker.Value.GetInt64();
        _speakers = speakers;
        _defaultSpeaker = config.TryGetProperty("default_speaker_id", out var first) && first.ValueKind == JsonValueKind.Number ? first.GetInt64() : 0;
        var inference = config.GetProperty("inference");
        _noise = Number(inference, "noise_scale", 0.667f);
        _length = Number(inference, "length_scale", 1);
        _noiseW = Number(inference, "noise_w", 0.8f);
        _takesSpeaker = session.InputMetadata.ContainsKey("sid");
    }

    /// <summary>Hz of its audio (16 000, 22 050 or 44 100).</summary>
    public int SampleRate { get; }

    /// <summary>The espeak-ng voice it was trained with («es», «en-us», «es-419»…).</summary>
    public string EspeakVoice { get; }

    public static PiperModel Load(string model, string config)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(config));
        using var options = new SessionOptions
        {
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 8),
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        var session = new InferenceSession(model, options);
        try
        {
            return new PiperModel(session, doc.RootElement.Clone());
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private static float Number(JsonElement parent, string name, float otherwise) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetSingle() : otherwise;

    /// <summary>The numbers this voice gives some phonemes (<see cref="PiperIds.Of"/>).</summary>
    public List<long> Ids(string phonemes) => _ids.Of(phonemes);

    /// <summary>
    /// Says some phonemes (<see cref="EspeakPhonemes.Ipa"/>) with one of its speakers (null: its default one) at a speed
    /// (1 = as it was trained). Mono at <see cref="SampleRate"/>.
    /// </summary>
    public float[] Say(string phonemes, string? speaker, float speed)
    {
        var ids = Ids(phonemes);
        if (ids.Count <= 4) return [];
        var input = ids.ToArray();
        using var inputValue = OrtValue.CreateTensorValueFromMemory(input, [1, input.Length]);
        using var lengthValue = OrtValue.CreateTensorValueFromMemory(new long[] { input.Length }, [1]);
        // A longer «length» is slower speech.
        using var scalesValue = OrtValue.CreateTensorValueFromMemory(new[] { _noise, _length / Math.Clamp(speed, 0.5f, 2f), _noiseW }, [3]);
        List<string> names = ["input", "input_lengths", "scales"];
        List<OrtValue> values = [inputValue, lengthValue, scalesValue];
        OrtValue? speakerValue = null;
        try
        {
            if (_takesSpeaker)
            {
                var sid = speaker is not null && _speakers.TryGetValue(speaker, out var chosen) ? chosen : _defaultSpeaker;
                speakerValue = OrtValue.CreateTensorValueFromMemory(new[] { sid }, [1]);
                names.Add("sid");
                values.Add(speakerValue);
            }
            using var run = new RunOptions();
            using var outputs = _session.Run(run, names, values, _session.OutputNames);
            return outputs[0].GetTensorDataAsSpan<float>().ToArray();
        }
        finally
        {
            speakerValue?.Dispose();
        }
    }

    public void Dispose() => _session.Dispose();
}

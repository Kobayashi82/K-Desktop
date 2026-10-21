using System.Runtime.InteropServices;
using KDesktop.Core.Assistant.Voice;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace KDesktop.Assistant.Voice.Kokoro;

/// <summary>
/// Kokoro 82M (hexgrad, Apache 2.0), the ONNX export of onnx-community: phonemes in, 24 kHz audio out, in one go. The voice
/// is a «style» of 256 numbers taken from its pack by the length of what is said (510 rows, one per length); voices are
/// mixed by averaging their packs. Not thread-safe: one sentence at a time.
/// </summary>
public sealed class KokoroModel : IDisposable
{
    public const int SampleRate = 24_000;

    /// <summary>Phonemes the model takes at once (its context is 512 with the two pads).</summary>
    public const int MaxPhonemes = 510;

    /// <summary>Numbers of a style.</summary>
    public const int StyleSize = 256;

    private readonly InferenceSession _session;
    private readonly string _idsName;
    private readonly string _styleName;
    private readonly string _speedName;
    private readonly bool _speedIsInt;

    private KokoroModel(InferenceSession session)
    {
        _session = session;
        var inputs = session.InputMetadata;
        _idsName = inputs.Keys.First(k => k.Contains("input", StringComparison.OrdinalIgnoreCase) || k.Contains("token", StringComparison.OrdinalIgnoreCase));
        _styleName = inputs.Keys.First(k => k.Contains("style", StringComparison.OrdinalIgnoreCase));
        _speedName = inputs.Keys.First(k => k.Contains("speed", StringComparison.OrdinalIgnoreCase));
        _speedIsInt = inputs[_speedName].ElementDataType == TensorElementType.Int32;
    }

    public static KokoroModel Load(string file)
    {
        using var options = new SessionOptions
        {
            IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 8),
            InterOpNumThreads = 1,
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        };
        return new KokoroModel(new InferenceSession(file, options));
    }

    /// <summary>The model's symbols (its <c>config.json</c> vocabulary); anything else is left out.</summary>
    private static readonly Dictionary<char, long> Vocabulary = new()
    {
        [';'] = 1, [':'] = 2, [','] = 3, ['.'] = 4, ['!'] = 5, ['?'] = 6, ['—'] = 9, ['…'] = 10, ['"'] = 11, ['('] = 12, [')'] = 13,
        ['“'] = 14, ['”'] = 15, [' '] = 16, ['̃'] = 17, ['ʣ'] = 18, ['ʥ'] = 19, ['ʦ'] = 20, ['ʨ'] = 21, ['ᵝ'] = 22, ['ꭧ'] = 23,
        ['A'] = 24, ['I'] = 25, ['O'] = 31, ['Q'] = 33, ['S'] = 35, ['T'] = 36, ['W'] = 39, ['Y'] = 41, ['ᵊ'] = 42, ['a'] = 43,
        ['b'] = 44, ['c'] = 45, ['d'] = 46, ['e'] = 47, ['f'] = 48, ['h'] = 50, ['i'] = 51, ['j'] = 52, ['k'] = 53, ['l'] = 54,
        ['m'] = 55, ['n'] = 56, ['o'] = 57, ['p'] = 58, ['q'] = 59, ['r'] = 60, ['s'] = 61, ['t'] = 62, ['u'] = 63, ['v'] = 64,
        ['w'] = 65, ['x'] = 66, ['y'] = 67, ['z'] = 68, ['ɑ'] = 69, ['ɐ'] = 70, ['ɒ'] = 71, ['æ'] = 72, ['β'] = 75, ['ɔ'] = 76,
        ['ɕ'] = 77, ['ç'] = 78, ['ɖ'] = 80, ['ð'] = 81, ['ʤ'] = 82, ['ə'] = 83, ['ɚ'] = 85, ['ɛ'] = 86, ['ɜ'] = 87, ['ɟ'] = 90,
        ['ɡ'] = 92, ['ɥ'] = 99, ['ɨ'] = 101, ['ɪ'] = 102, ['ʝ'] = 103, ['ɯ'] = 110, ['ɰ'] = 111, ['ŋ'] = 112, ['ɳ'] = 113,
        ['ɲ'] = 114, ['ɴ'] = 115, ['ø'] = 116, ['ɸ'] = 118, ['θ'] = 119, ['œ'] = 120, ['ɹ'] = 123, ['ɾ'] = 125, ['ɻ'] = 126,
        ['ʁ'] = 128, ['ɽ'] = 129, ['ʂ'] = 130, ['ʃ'] = 131, ['ʈ'] = 132, ['ʧ'] = 133, ['ʊ'] = 135, ['ʋ'] = 136, ['ʌ'] = 138,
        ['ɣ'] = 139, ['ɤ'] = 140, ['χ'] = 142, ['ʎ'] = 143, ['ʒ'] = 147, ['ʔ'] = 148, ['ˈ'] = 156, ['ˌ'] = 157, ['ː'] = 158,
        ['ʰ'] = 162, ['ʲ'] = 164, ['↓'] = 169, ['→'] = 171, ['↗'] = 172, ['↘'] = 173, ['ᵻ'] = 177,
    };

    /// <summary>The ids of the phonemes the model knows.</summary>
    public static List<long> Tokens(string phonemes)
    {
        var ids = new List<long>(phonemes.Length);
        foreach (var c in phonemes)
            if (Vocabulary.TryGetValue(c, out var id)) ids.Add(id);
        return ids;
    }

    /// <summary>
    /// Reads a voice pack (510 × 256 floats) or several mixed: «ef_dora» or «ef_dora:0.7,em_alex:0.3» (weights relative, 1
    /// when left out), from <paramref name="fileOf"/> (name → file).
    /// </summary>
    public static float[] Voice(string spec, Func<string, string> fileOf)
    {
        var parts = KokoroModels.Parts(spec);
        if (parts.Count == 0) throw new ArgumentException("Sin voz", nameof(spec));
        var total = parts.Sum(p => p.Weight);
        float[]? mixed = null;
        foreach (var (name, weight) in parts)
        {
            var bytes = File.ReadAllBytes(fileOf(name));
            var pack = MemoryMarshal.Cast<byte, float>(bytes).ToArray();
            if (pack.Length < MaxPhonemes * StyleSize) throw new InvalidDataException($"Voz «{name}» incompleta");
            mixed ??= new float[pack.Length];
            var share = (float)(weight / total);
            for (var i = 0; i < mixed.Length && i < pack.Length; i++) mixed[i] += pack[i] * share;
        }
        return mixed!;
    }

    /// <summary>
    /// Says some phonemes (at most <see cref="MaxPhonemes"/> of the model's; more are cut) with a voice pack, at a speed
    /// (1 = normal). 24 kHz mono.
    /// </summary>
    public float[] Say(string phonemes, float[] voice, float speed)
    {
        var tokens = Tokens(phonemes);
        if (tokens.Count == 0) return [];
        if (tokens.Count > MaxPhonemes) tokens = tokens[..MaxPhonemes];
        var ids = new long[tokens.Count + 2];
        tokens.CopyTo(ids, 1);
        var row = Math.Clamp(tokens.Count - 1, 0, MaxPhonemes - 1);
        var style = voice.AsSpan(row * StyleSize, StyleSize).ToArray();

        using var idsValue = OrtValue.CreateTensorValueFromMemory(ids, [1, ids.Length]);
        using var styleValue = OrtValue.CreateTensorValueFromMemory(style, [1, StyleSize]);
        using var speedValue = _speedIsInt
            ? OrtValue.CreateTensorValueFromMemory(new[] { (int)Math.Round(speed) }, [1])
            : OrtValue.CreateTensorValueFromMemory(new[] { speed }, [1]);
        using var run = new RunOptions();
        using var outputs = _session.Run(run, [_idsName, _styleName, _speedName], [idsValue, styleValue, speedValue], _session.OutputNames);
        return outputs[0].GetTensorDataAsSpan<float>().ToArray();
    }

    public void Dispose() => _session.Dispose();
}

using System.Text.Json.Nodes;
using KDesktop.Core;
using KDesktop.Core.Assistant.Voice;

namespace KDesktop.Assistant.Voice.Effects;

/// <summary>
/// The effects one sentence goes through (stage 5.5): the speed (only for a voice without its own: Pocket TTS), the chain
/// of its profile, the effects of the moods it is in (<c>error</c>, <c>far</c>, <c>happy</c>…) and a leveler that keeps
/// every voice equally loud. Piece by piece, as it is made; one per sentence.
/// </summary>
public sealed class VoiceChain
{
    private readonly List<IVoiceEffect> _effects;

    private VoiceChain(List<IVoiceEffect> effects) => _effects = effects;

    /// <summary>Nothing to do (no effect, no speed): the voice goes as it comes.</summary>
    public bool IsEmpty => _effects.Count == 0;

    /// <param name="speed">How much faster (1 = as it is); null: the voice already speaks at its speed.</param>
    public static VoiceChain Build(VoiceProfile profile, IEnumerable<string> moods, int sampleRate, double? speed, int seed)
    {
        var effects = new List<IVoiceEffect>();
        if (speed is { } s && Math.Abs(s - 1) > 0.01) effects.Add(new Speed(s, sampleRate));
        var chain = profile.Chain.OfType<JsonObject>().ToList();
        foreach (var mood in moods)
            if (profile.Moods.TryGetValue(mood, out var extra)) chain.AddRange(extra.OfType<JsonObject>());
        foreach (var fx in chain)
        {
            if (fx["enabled"]?.GetValue<bool>() == false) continue;
            try
            {
                if (Create(fx, sampleRate, seed++) is { } effect) effects.Add(effect);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                Log.Warn($"Asistente: efecto de voz no válido ({fx.ToJsonString()}): {ex.Message}");
            }
        }
        // Only effects that change the loudness need it, but it costs nothing: the same level for every profile.
        if (effects.Count > 0) effects.Add(new Leveler(sampleRate));
        return new VoiceChain(effects);
    }

    public float[] Process(float[] input)
    {
        foreach (var effect in _effects) input = effect.Process(input);
        return input;
    }

    /// <summary>The sentence is over: what the effects still hold, through the rest of the chain.</summary>
    public float[] Finish()
    {
        var tail = new List<float>();
        for (var i = 0; i < _effects.Count; i++)
        {
            var held = _effects[i].Finish();
            for (var j = i + 1; j < _effects.Count && held.Length > 0; j++) held = _effects[j].Process(held);
            tail.AddRange(held);
        }
        return [.. tail];
    }

    private static IVoiceEffect? Create(JsonObject fx, int sampleRate, int seed)
    {
        double Value(string name, double fallback) => fx[name]?.GetValue<double>() ?? fallback;
        return fx["fx"]?.GetValue<string>() switch
        {
            "pitch" => new PitchShift(Value("semitones", 0), sampleRate),
            "formant" => new FormantShift(Value("shift", 1), sampleRate),
            "ring" => new RingModulator(Value("freq", 50), Value("mix", 0.5), sampleRate),
            "vocoder" => new Vocoder(HzToNote(Value("note", 130.8)), (int)Value("bands", 16), sampleRate),
            "bitcrush" => new Bitcrusher(Value("bits", 8), Value("rate", 8000), sampleRate),
            "radio" => new Radio(Value("bandwidth", 3000), Value("noise", 0.03), fx["beep"]?.GetValue<bool>() ?? true, sampleRate, seed),
            "chorus" => new Chorus(Value("rate", 1.5), Value("depth", 0.5), sampleRate),
            // A flanger is a chorus faster and deeper (the same delay that moves).
            "flanger" => new Chorus(Value("rate", 0.3) * 2, Math.Min(1, Value("depth", 0.7) * 1.3), sampleRate),
            "echo" => new Echo(Value("ms", 120), Value("feedback", 0.3), Value("mix", 0.3), sampleRate),
            "glitch" => new Glitch(Value("chance", 0.1), sampleRate, seed),
            _ => null,
        };
    }

    /// <summary>The vocoder's note comes in hertz (the schema): as a MIDI note.</summary>
    private static double HzToNote(double hz) => 69 + 12 * Math.Log2(Math.Max(20, hz) / 440);
}

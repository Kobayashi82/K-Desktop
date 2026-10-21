using System.Collections.Concurrent;
using KDesktop.Assistant.Voice.Pocket;
using KDesktop.Core;
using KDesktop.Core.Assistant.Voice;

namespace KDesktop.Assistant.Voice;

/// <summary>Which voice says the answers (Options → Assistant → Voice).</summary>
public enum SpeechEngineKind
{
    /// <summary>Pocket TTS if it is downloaded for the language, else Windows' voices, else beeps.</summary>
    Auto,
    Pocket,
    Windows,
    Beeps,

    /// <summary>Only the bubble.</summary>
    Off,
}

/// <summary>How an assistant speaks.</summary>
/// <param name="PocketVoice">The voice of Pocket TTS («eve»…); null: the default one.</param>
/// <param name="WindowsVoice">The Windows voice by name; null: the first one of the language.</param>
/// <param name="Speed">1 = normal (Windows' voices and beeps; Pocket TTS with the effects of stage 5.5).</param>
public sealed record VoiceSettings(SpeechEngineKind Engine = SpeechEngineKind.Auto, string? PocketVoice = null, string? WindowsVoice = null, double Speed = 1);

/// <summary>A piece of a sentence being said: mono samples at their rate, and which voice says it.</summary>
public sealed record SpokenChunk(float[] Samples, int SampleRate, SpeechEngineKind Engine);

/// <summary>A sentence said: mono samples at their rate, and which voice said it.</summary>
public sealed record SpokenAudio(float[] Samples, int SampleRate, SpeechEngineKind Engine)
{
    public TimeSpan Duration => TimeSpan.FromSeconds(Samples.Length / (double)SampleRate);
}

/// <summary>
/// The voice of the assistant process (stage 5.4): one sentence into audio with the engine chosen, falling back to the
/// next one when it cannot (not downloaded, no Windows voice for the language, a failure). Thread-safe.
/// </summary>
public sealed class Speech : IDisposable
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _windows = new(StringComparer.Ordinal);

    public PocketEngine Pocket { get; } = new();

    /// <summary>The engine that will really speak: <see cref="SpeechEngineKind.Auto"/> resolved, and what is not there skipped.</summary>
    public SpeechEngineKind Resolve(string language, VoiceSettings settings)
    {
        if (settings.Engine is SpeechEngineKind.Off or SpeechEngineKind.Beeps) return settings.Engine;
        var pocket = PocketModels.Supports(language) && PocketEngine.Available(language, PocketModels.VoiceOrDefault(language, settings.PocketVoice));
        // The one chosen if it is there; else the other one; else beeps.
        if (settings.Engine == SpeechEngineKind.Windows)
            return HasWindows(language) ? SpeechEngineKind.Windows : pocket ? SpeechEngineKind.Pocket : SpeechEngineKind.Beeps;
        return pocket ? SpeechEngineKind.Pocket : HasWindows(language) ? SpeechEngineKind.Windows : SpeechEngineKind.Beeps;
    }

    /// <summary>Windows has a voice for this language.</summary>
    public bool HasWindows(string language) => WindowsVoicesFor(language).Count > 0;

    public IReadOnlyList<string> WindowsVoicesFor(string language) => _windows.GetOrAdd(language, WindowsVoices.For);

    /// <summary>Gets the voice ready while the AI thinks (Pocket's model takes a second or two to load).</summary>
    public void Warm(string language, VoiceSettings settings)
    {
        if (Resolve(language, settings) == SpeechEngineKind.Pocket) Pocket.Warm(language, PocketModels.VoiceOrDefault(language, settings.PocketVoice));
    }

    /// <summary>One sentence in audio, whole, or null when it is not said aloud (off, nothing speakable, cancelled).</summary>
    public async Task<SpokenAudio?> SayAsync(string sentence, string language, VoiceSettings settings, CancellationToken cancellationToken)
    {
        var audio = new List<float>();
        SpokenChunk? last = null;
        await foreach (var chunk in StreamAsync(sentence, language, settings, cancellationToken).ConfigureAwait(false))
        {
            audio.AddRange(chunk.Samples);
            last = chunk;
        }
        return last is null ? null : new SpokenAudio([.. audio], last.SampleRate, last.Engine);
    }

    /// <summary>
    /// One sentence in audio as it is made (stage 5.4: Pocket TTS sounds a few tenths of a second after it starts, the
    /// rest comes while it plays; the other voices come whole), without the silence at the start and all at the same
    /// loudness. Nothing when it is not said aloud (off, nothing speakable, cancelled). A voice that fails before giving
    /// anything is replaced by the next one (Pocket → Windows → beeps).
    /// </summary>
    public async IAsyncEnumerable<SpokenChunk> StreamAsync(string sentence, string language, VoiceSettings settings,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (SentenceSplitter.Speakable(sentence) is not { } text) yield break;
        var engine = Resolve(language, settings);
        while (engine != SpeechEngineKind.Off)
        {
            var given = false;
            var pieces = System.Threading.Channels.Channel.CreateUnbounded<float[]>();
            var rate = engine switch { SpeechEngineKind.Pocket => PocketModel.SampleRate, SpeechEngineKind.Windows => WindowsVoices.SampleRate, _ => Beeps.SampleRate };
            var making = Task.Run(async () =>
            {
                try
                {
                    switch (engine)
                    {
                        case SpeechEngineKind.Pocket:
                            var shaper = new Shaper(rate);
                            await Pocket.SayAsync(text, language, PocketModels.VoiceOrDefault(language, settings.PocketVoice), Seed(text), cancellationToken,
                                piece => { if (shaper.Shape(piece) is { Length: > 0 } shaped) pieces.Writer.TryWrite(shaped); }).ConfigureAwait(false);
                            break;
                        case SpeechEngineKind.Windows:
                            var windows = WindowsVoices.Say(text, language, settings.WindowsVoice, settings.Speed);
                            if (windows.Length == 0) throw new InvalidOperationException("Sin voz de Windows");
                            pieces.Writer.TryWrite(Normalize(Trim(windows, rate)));
                            break;
                        default:
                            pieces.Writer.TryWrite(Beeps.Say(text, settings.Speed));
                            break;
                    }
                    pieces.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    pieces.Writer.TryComplete(ex);
                }
            }, CancellationToken.None);

            var failed = false;
            var reader = pieces.Reader;
            while (true)
            {
                float[]? piece = null;
                try
                {
                    if (!await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) break;
                    reader.TryRead(out piece);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    yield break;
                }
                catch (Exception ex)
                {
                    // Once something has been said, the sentence just ends there; before, another voice says it.
                    if (given) yield break;
                    Log.Warn($"Asistente: la voz {engine} ha fallado ({ex.Message}); se usa otra");
                    failed = true;
                    break;
                }
                if (piece is null) continue;
                given = true;
                yield return new SpokenChunk(piece, rate, engine);
            }
            await making.ConfigureAwait(false);
            if (!failed) yield break;
            engine = engine == SpeechEngineKind.Pocket && HasWindows(language) ? SpeechEngineKind.Windows : SpeechEngineKind.Beeps;
        }
    }

    /// <summary>
    /// The pieces of a voice that comes bit by bit, shaped on the way: the silence before the first sound goes (keeping a
    /// little), and a gain chosen from the first loud stretch is kept for the whole sentence, with a soft limit.
    /// </summary>
    private sealed class Shaper(int sampleRate)
    {
        private const float Threshold = 0.02f;
        private bool _started;
        private float _gain = 1;

        public float[] Shape(float[] piece)
        {
            var from = 0;
            if (!_started)
            {
                var first = Array.FindIndex(piece, s => Math.Abs(s) > Threshold);
                if (first < 0) return [];
                _started = true;
                from = Math.Max(0, first - sampleRate * 6 / 100);
                var peak = 1e-3f;
                for (var i = first; i < piece.Length; i++) peak = Math.Max(peak, Math.Abs(piece[i]));
                _gain = Math.Clamp(0.75f / peak, 0.6f, 3f);
            }
            var shaped = new float[piece.Length - from];
            for (var i = 0; i < shaped.Length; i++) shaped[i] = MathF.Tanh(piece[from + i] * _gain);
            return shaped;
        }
    }

    /// <summary>The same text, the same seed: «repite» sounds the same (string hashes change between runs).</summary>
    private static int Seed(string text)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in text) hash = (hash ^ c) * 16777619;
            return hash;
        }
    }

    /// <summary>
    /// Without the silence at both ends, by the energy of 20 ms blocks against the loudest one (soft starts are kept: 60 ms
    /// before the first block that counts, 160 ms after the last). From Kobii.
    /// </summary>
    public static float[] Trim(float[] audio, int sampleRate)
    {
        var n = Math.Max(1, (int)(0.02 * sampleRate));
        var blocks = (audio.Length + n - 1) / n;
        if (blocks == 0) return audio;
        var energy = new double[blocks];
        for (var b = 0; b < blocks; b++)
        {
            double sum = 0;
            var end = Math.Min(audio.Length, (b + 1) * n);
            for (var i = b * n; i < end; i++) sum += audio[i] * audio[i];
            energy[b] = Math.Sqrt(sum / (end - b * n));
        }
        var threshold = 0.08 * Math.Max(energy.Max(), 1e-6);
        var first = Array.FindIndex(energy, e => e > threshold);
        var last = Array.FindLastIndex(energy, e => e > threshold);
        if (first < 0) return audio;
        return audio[Math.Max(0, (first - 3) * n)..Math.Min(audio.Length, (last + 8) * n)];
    }

    /// <summary>The same loudness for every sentence and engine: the peak at <paramref name="peak"/>.</summary>
    public static float[] Normalize(float[] audio, float peak = 0.85f)
    {
        var max = 1e-3f;
        foreach (var s in audio) max = Math.Max(max, Math.Abs(s));
        var gain = peak / max;
        for (var i = 0; i < audio.Length; i++) audio[i] *= gain;
        return audio;
    }

    public void Dispose() => Pocket.Dispose();
}

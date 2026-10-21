using System.Collections.Concurrent;
using KDesktop.Assistant.Voice.Effects;
using KDesktop.Assistant.Voice.Kokoro;
using KDesktop.Assistant.Voice.Piper;
using KDesktop.Core;
using KDesktop.Core.Assistant.Voice;

namespace KDesktop.Assistant.Voice;

/// <summary>Which voice says the answers (Options → Assistant → Voice).</summary>
public enum SpeechEngineKind
{
    /// <summary>Kokoro if it is downloaded for the language, else Piper, else Windows' voices, else beeps.</summary>
    Auto,
    Kokoro,

    /// <summary>Piper (5.0.110): voices of its own for each language, German too.</summary>
    Piper,
    Windows,
    Beeps,

    /// <summary>Only the bubble.</summary>
    Off,
}

/// <summary>How an assistant speaks.</summary>
/// <param name="Engine">Options' choice; <see cref="SpeechEngineKind.Auto"/> follows the profile's.</param>
/// <param name="KokoroVoice">The voice of Kokoro chosen in Options, or a mix («ef_dora:0.7,em_alex:0.3»); null: the language's default.</param>
/// <param name="WindowsVoice">The Windows voice by name; null: the first one of the language.</param>
/// <param name="Speed">1 = normal (on top of the profile's).</param>
/// <param name="Profile">Its voice profile (stage 5.5: engine and effects); null: «Natural».</param>
/// <param name="Moods">The situations it is in (<c>error</c>, <c>far</c>, <c>happy</c>…): their effects are added.</param>
/// <param name="PiperVoice">The Piper voice chosen in Options («key» or «key#speaker»); null: the language's default.</param>
public sealed record VoiceSettings(SpeechEngineKind Engine = SpeechEngineKind.Auto, string? KokoroVoice = null, string? WindowsVoice = null, double Speed = 1,
    VoiceProfile? Profile = null, IReadOnlyList<string>? Moods = null, string? PiperVoice = null)
{
    public VoiceProfile EffectiveProfile => Profile ?? VoiceProfile.Default;

    /// <summary>Its speed and the profile's together.</summary>
    public double EffectiveSpeed => Math.Clamp(Speed * EffectiveProfile.Speed, 0.5, 2);

    /// <summary>The Kokoro voice: the profile's own if it is of the language and downloaded, else the one chosen in Options.</summary>
    public string KokoroVoiceFor(string language) =>
        EffectiveProfile.OwnVoice is { } own && KokoroModels.VoiceOrDefault(language, own) == own && KokoroModels.Installed(language, own)
            ? own
            : KokoroModels.VoiceOrDefault(language, KokoroVoice);

    /// <summary>The Piper voice: the profile's own if it is a downloaded one of the language, else the one chosen in Options.</summary>
    public string PiperVoiceFor(string language) =>
        EffectiveProfile.OwnVoice is { } own && PiperModels.VoiceOrDefault(language, own) == own && PiperModels.Installed(language, own)
            ? own
            : PiperModels.VoiceOrDefault(language, PiperVoice);
}

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

    public KokoroEngine Kokoro { get; } = new();

    public PiperEngine Piper { get; } = new();

    /// <summary>The engine that will really speak: <see cref="SpeechEngineKind.Auto"/> resolved, and what is not there skipped.</summary>
    public SpeechEngineKind Resolve(string language, VoiceSettings settings)
    {
        // Automatic: the engine of the profile (a profile of beeps speaks in beeps).
        var wanted = settings.Engine != SpeechEngineKind.Auto ? settings.Engine : settings.EffectiveProfile.Engine switch
        {
            "beeps" => SpeechEngineKind.Beeps,
            "windows" => SpeechEngineKind.Windows,
            "piper" => SpeechEngineKind.Piper,
            _ => SpeechEngineKind.Auto,
        };
        if (wanted is SpeechEngineKind.Off or SpeechEngineKind.Beeps) return wanted;
        // The one chosen if it is there; else the others in order; else beeps.
        SpeechEngineKind[] order = wanted switch
        {
            SpeechEngineKind.Windows => [SpeechEngineKind.Windows, SpeechEngineKind.Kokoro, SpeechEngineKind.Piper],
            SpeechEngineKind.Piper => [SpeechEngineKind.Piper, SpeechEngineKind.Kokoro, SpeechEngineKind.Windows],
            _ => [SpeechEngineKind.Kokoro, SpeechEngineKind.Piper, SpeechEngineKind.Windows],
        };
        return order.FirstOrDefault(e => Has(e, language, settings), SpeechEngineKind.Beeps);
    }

    /// <summary>That voice can speak the language now (downloaded, or Windows has one).</summary>
    private bool Has(SpeechEngineKind engine, string language, VoiceSettings settings) => engine switch
    {
        SpeechEngineKind.Kokoro => KokoroModels.Supports(language) && KokoroEngine.Available(language, settings.KokoroVoiceFor(language)),
        SpeechEngineKind.Piper => PiperEngine.Available(language, settings.PiperVoiceFor(language)),
        SpeechEngineKind.Windows => HasWindows(language),
        _ => true,
    };

    /// <summary>Who says it when <paramref name="failed"/> failed before saying anything: the next voice down, at last beeps.</summary>
    private SpeechEngineKind After(SpeechEngineKind failed, string language, VoiceSettings settings)
    {
        SpeechEngineKind[] next = failed switch
        {
            SpeechEngineKind.Kokoro => [SpeechEngineKind.Piper, SpeechEngineKind.Windows],
            SpeechEngineKind.Piper => [SpeechEngineKind.Windows],
            _ => [],
        };
        return next.FirstOrDefault(e => Has(e, language, settings), SpeechEngineKind.Beeps);
    }

    /// <summary>Windows has a voice for this language.</summary>
    public bool HasWindows(string language) => WindowsVoicesFor(language).Count > 0;

    public IReadOnlyList<string> WindowsVoicesFor(string language) => _windows.GetOrAdd(language, WindowsVoices.For);

    /// <summary>Gets the voice ready while the AI thinks (Kokoro's model takes about half a second to load).</summary>
    public void Warm(string language, VoiceSettings settings)
    {
        switch (Resolve(language, settings))
        {
            case SpeechEngineKind.Kokoro:
                Kokoro.Warm(settings.KokoroVoiceFor(language));
                break;
            case SpeechEngineKind.Piper:
                Piper.Warm(settings.PiperVoiceFor(language));
                break;
        }
    }

    /// <summary>Lets the natural voices and espeak-ng go now (K-Desktop is about to delete their files).</summary>
    public void Release()
    {
        Kokoro.Release();
        Piper.Release();
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
    /// One sentence in audio as it is made (Kokoro gives a long sentence in pieces, the first one sounding while the rest
    /// is made, Piper one sentence after another; the other voices come whole), without the silence at the start and all at
    /// the same loudness, through the effects of its profile and its moods (stage 5.5). Nothing when it is not said aloud
    /// (off, nothing speakable, cancelled). A voice that fails before giving anything is replaced by the next one (Kokoro →
    /// Piper → Windows → beeps).
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
            var rate = engine switch
            {
                SpeechEngineKind.Kokoro => KokoroModel.SampleRate,
                SpeechEngineKind.Piper => PiperEngine.SampleRateOf(settings.PiperVoiceFor(language)),
                SpeechEngineKind.Windows => WindowsVoices.SampleRate,
                _ => Beeps.SampleRate,
            };
            // Every voice says it at its own speed (Kokoro's and Piper's models take one).
            var chain = VoiceChain.Build(settings.EffectiveProfile, settings.Moods ?? [], rate, null, Seed(text));
            void Put(float[] piece)
            {
                var done = chain.Process(piece);
                if (done.Length > 0) pieces.Writer.TryWrite(done);
            }
            var making = Task.Run(async () =>
            {
                try
                {
                    switch (engine)
                    {
                        case SpeechEngineKind.Kokoro:
                            var shaper = new Shaper(rate);
                            await Kokoro.SayAsync(text, language, settings.KokoroVoiceFor(language), settings.EffectiveSpeed, cancellationToken,
                                piece => { if (shaper.Shape(piece) is { Length: > 0 } shaped) Put(shaped); }).ConfigureAwait(false);
                            break;
                        case SpeechEngineKind.Piper:
                            var piperShaper = new Shaper(rate);
                            await Piper.SayAsync(text, settings.PiperVoiceFor(language), settings.EffectiveSpeed, cancellationToken,
                                piece => { if (piperShaper.Shape(piece) is { Length: > 0 } shaped) Put(shaped); }).ConfigureAwait(false);
                            break;
                        case SpeechEngineKind.Windows:
                            var windows = WindowsVoices.Say(text, language, settings.WindowsVoice, settings.EffectiveSpeed);
                            if (windows.Length == 0) throw new InvalidOperationException("Sin voz de Windows");
                            Put(Normalize(Trim(windows, rate)));
                            break;
                        default:
                            Put(Beeps.Say(text, settings.EffectiveSpeed));
                            break;
                    }
                    if (chain.Finish() is { Length: > 0 } tail) pieces.Writer.TryWrite(tail);
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
            engine = After(engine, language, settings);
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

    public void Dispose()
    {
        Kokoro.Dispose();
        Piper.Dispose();
    }
}

using KDesktop.Core;
using KDesktop.Core.Assistant.Voice;

namespace KDesktop.Assistant.Voice.Pocket;

/// <summary>
/// Speaking with Pocket TTS: the model of the language in use is loaded on first use (or when warmed up while the AI
/// thinks), says one sentence at a time and leaves memory after <see cref="IdleUnload"/> unused. Thread-safe.
/// </summary>
public sealed class PocketEngine : IDisposable
{
    /// <summary>
    /// Temperature of the starting noise: more, more expressive; less, flatter but clearer. Kobii measured with Parakeet
    /// 22 % of words wrong at Kyutai's 0.7 and 14 % at 0.3; here too (stage 5.4: «eve» in Spanish, 0.3 better than 0.5).
    /// </summary>
    public float Temperature { get; set; } = 0.3f;

    public static readonly TimeSpan IdleUnload = TimeSpan.FromMinutes(3);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _idle;
    private PocketModel? _model;

    public PocketEngine() => _idle = new Timer(_ => Unload());

    /// <summary>The model is in memory now (the first sentence comes at once).</summary>
    public bool IsLoaded => _model is not null;

    /// <summary>That language and voice are downloaded.</summary>
    public static bool Available(string language, string voice) => PocketModels.Installed(language, voice);

    /// <summary>
    /// One sentence in audio (24 kHz mono, the voice as it is), handed to <paramref name="onAudio"/> as it is made. The
    /// same text and seed sound the same. Throws when the model is not there or cannot load.
    /// </summary>
    public async Task<float[]> SayAsync(string text, string language, string voice, int seed, CancellationToken cancellationToken, Action<float[]>? onAudio = null)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _idle.Change(Timeout.Infinite, Timeout.Infinite);
            return await Task.Run(() =>
            {
                var model = Ready(language, voice);
                var audio = new List<float>();
                foreach (var chunk in PocketText.Chunks(text, model.Tokenizer, model.MaxTokensPerChunk))
                {
                    // Now and then it rambles on (it is cut at the longest that is normal) or stops at the start (once more,
                    // with another seed).
                    var (_, longest) = Bounds(chunk);
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var said = model.Say(chunk, new Random(seed + attempt * 7919), Temperature, cancellationToken, onAudio, longest);
                        audio.AddRange(said);
                        var seconds = said.Length / (double)PocketModel.SampleRate;
                        if (seconds >= Bounds(chunk).Shortest) break;
                        Log.Info($"Asistente: la voz ha dicho «{chunk}» en {seconds:0.0} s: otra vez");
                    }
                }
                return audio.ToArray();
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _idle.Change(IdleUnload, Timeout.InfiniteTimeSpan);
            _gate.Release();
        }
    }

    /// <summary>
    /// How long a text may take said aloud without something having gone wrong (about 13 letters a second; between three
    /// times as fast and a third as fast, with a margin).
    /// </summary>
    public static (double Shortest, double Longest) Bounds(string text)
    {
        var letters = text.Count(char.IsLetterOrDigit);
        return (letters * 0.03 + 0.2, letters * 0.12 + 0.8);
    }

    /// <summary>Loads the model in the background so the first sentence does not wait for it (the AI is thinking).</summary>
    public void Warm(string language, string voice)
    {
        if (_model is { } m && m.Language == language && m.Voice == voice) return;
        _ = Task.Run(async () =>
        {
            if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return;
            try
            {
                _idle.Change(Timeout.Infinite, Timeout.Infinite);
                Ready(language, voice);
            }
            catch (Exception ex)
            {
                Log.Warn($"Asistente: no se ha podido preparar la voz: {ex.Message}");
            }
            finally
            {
                _idle.Change(IdleUnload, Timeout.InfiniteTimeSpan);
                _gate.Release();
            }
        });
    }

    /// <summary>The model of that language with that voice (under the gate).</summary>
    private PocketModel Ready(string language, string voice)
    {
        if (_model is not { } model || model.Language != language)
        {
            _model?.Dispose();
            _model = null;
            _model = model = PocketModel.Load(language, PocketModels.Folder(language));
        }
        model.UseVoice(voice, PocketModels.VoiceFile(language, voice));
        return model;
    }

    public void Dispose()
    {
        _idle.Dispose();
        Unload();
        _gate.Dispose();
    }

    /// <summary>Lets the model go now (K-Desktop is about to delete its files).</summary>
    public void Release() => Unload();

    private void Unload()
    {
        if (!_gate.Wait(0)) return;
        try
        {
            if (_model is null) return;
            _model.Dispose();
            _model = null;
            Log.Info("Asistente: voz Pocket TTS soltada de la memoria");
        }
        finally
        {
            _gate.Release();
        }
    }
}

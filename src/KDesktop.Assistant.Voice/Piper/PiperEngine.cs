using KDesktop.Core;
using KDesktop.Core.Assistant.Voice;

namespace KDesktop.Assistant.Voice.Piper;

/// <summary>
/// Speaking with Piper (5.0.110): the voice chosen is loaded on first use (or warmed up while the AI thinks), one at a time
/// (another voice replaces it), says what it is given in one go (a long text in pieces cut at its marks) and leaves memory
/// after <see cref="IdleUnload"/> unused. The text becomes phonemes through espeak-ng, in the voice it was trained with.
/// Thread-safe.
/// </summary>
public sealed class PiperEngine : IDisposable
{
    public static readonly TimeSpan IdleUnload = TimeSpan.FromMinutes(3);

    /// <summary>Phonemes of a piece at most: a longer sentence is said in pieces cut at its marks (or spaces).</summary>
    public const int PieceLength = 400;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _idle;
    private (string Key, PiperModel Model)? _loaded;

    public PiperEngine() => _idle = new Timer(_ => Unload());

    /// <summary>The key of the voice in memory now, or null.</summary>
    public string? Loaded => _loaded?.Key;

    /// <summary>The voice (of that language) and espeak-ng are downloaded.</summary>
    public static bool Available(string language, string spec) => PiperModels.Installed(language, spec);

    /// <summary>Hz of the audio of a voice.</summary>
    public static int SampleRateOf(string spec) => PiperModels.Find(spec)?.SampleRate ?? 22_050;

    /// <summary>
    /// One sentence in audio (mono at the voice's rate), handed to <paramref name="onAudio"/> piece by piece as it is made.
    /// Throws when the files are not there or cannot load.
    /// </summary>
    public async Task<float[]> SayAsync(string text, string spec, double speed, CancellationToken cancellationToken, Action<float[]>? onAudio = null)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _idle.Change(Timeout.Infinite, Timeout.Infinite);
            return await Task.Run(() =>
            {
                var model = Ready(spec);
                var audio = new List<float>();
                foreach (var piece in Pieces(EspeakPhonemes.Ipa(text, model.EspeakVoice, EspeakFiles.Folder)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var said = model.Say(piece, PiperModels.SpeakerOf(spec), (float)speed);
                    audio.AddRange(said);
                    onAudio?.Invoke(said);
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
    /// The phonemes of a text in pieces of at most <see cref="PieceLength"/>, cut after the last sentence that fits, else
    /// after a mark, else at a space. A text that fits goes whole, several sentences too: a voice says them better in one
    /// go than one by one (measured with Parakeet: «Recibido. Abro la papelera.» whole, «Decidido. Par la parelera.» apart).
    /// </summary>
    public static IReadOnlyList<string> Pieces(string phonemes)
    {
        var pieces = new List<string>();
        var rest = phonemes.Trim();
        while (rest.Length > PieceLength)
        {
            var cut = rest.LastIndexOfAny(['.', '!', '?'], PieceLength);
            if (cut < PieceLength / 3) cut = rest.LastIndexOfAny([',', ';', ':'], PieceLength);
            if (cut < PieceLength / 3) cut = rest.LastIndexOf(' ', PieceLength);
            if (cut <= 0) cut = PieceLength;
            var piece = rest[..(cut + 1)].Trim();
            if (piece.Length > 0) pieces.Add(piece);
            rest = rest[(cut + 1)..].Trim();
        }
        if (rest.Length > 0) pieces.Add(rest);
        return pieces;
    }

    /// <summary>Loads the voice in the background so the first sentence does not wait for it (the AI is thinking).</summary>
    public void Warm(string spec)
    {
        if (_loaded?.Key == PiperModels.KeyOf(spec)) return;
        _ = Task.Run(async () =>
        {
            if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return;
            try
            {
                _idle.Change(Timeout.Infinite, Timeout.Infinite);
                Ready(spec);
            }
            catch (Exception ex)
            {
                Log.Warn($"Asistente: no se ha podido preparar la voz de Piper: {ex.Message}");
            }
            finally
            {
                _idle.Change(IdleUnload, Timeout.InfiniteTimeSpan);
                _gate.Release();
            }
        });
    }

    /// <summary>The voice ready (under the gate): another one leaves the memory first.</summary>
    private PiperModel Ready(string spec)
    {
        var key = PiperModels.KeyOf(spec);
        if (_loaded is { } loaded && loaded.Key == key) return loaded.Model;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _loaded?.Model.Dispose();
        _loaded = null;
        var model = PiperModel.Load(PiperModels.ModelFile(key), PiperModels.ConfigFile(key));
        _loaded = (key, model);
        Log.Info($"Asistente: voz Piper «{key}» lista en {watch.ElapsedMilliseconds} ms");
        return model;
    }

    public void Dispose()
    {
        _idle.Dispose();
        Unload();
        _gate.Dispose();
    }

    /// <summary>Lets the voice and espeak-ng go now (K-Desktop is about to delete their files).</summary>
    public void Release()
    {
        Unload();
        EspeakPhonemes.Release();
    }

    private void Unload()
    {
        if (!_gate.Wait(0)) return;
        try
        {
            if (_loaded is not { } loaded) return;
            loaded.Model.Dispose();
            _loaded = null;
            Log.Info("Asistente: voz Piper soltada de la memoria");
        }
        finally
        {
            _gate.Release();
        }
    }
}

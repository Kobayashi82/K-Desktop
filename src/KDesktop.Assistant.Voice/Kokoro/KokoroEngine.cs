using KDesktop.Core;
using KDesktop.Core.Assistant.Voice;

namespace KDesktop.Assistant.Voice.Kokoro;

/// <summary>
/// Speaking with Kokoro: the model is loaded on first use (or warmed up while the AI thinks), says one sentence at a time
/// (a long one in pieces, so the first one sounds sooner) and leaves memory after <see cref="IdleUnload"/> unused. Spanish
/// gets its phonemes by our own rules; the other languages through espeak-ng, if it is downloaded. Thread-safe.
/// </summary>
public sealed class KokoroEngine : IDisposable
{
    public static readonly TimeSpan IdleUnload = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Phonemes of a piece at most: a sentence longer than this is said in pieces cut at its commas (or spaces). The model
    /// takes 510; shorter pieces sound sooner.
    /// </summary>
    public const int PieceLength = 260;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _idle;
    private KokoroModel? _model;
    private (string Spec, float[] Pack)? _voice;

    public KokoroEngine() => _idle = new Timer(_ => Unload());

    /// <summary>The model is in memory now (the first sentence comes at once).</summary>
    public bool IsLoaded => _model is not null;

    /// <summary>Everything that language and voice need is downloaded.</summary>
    public static bool Available(string language, string voice) => KokoroModels.Installed(language, voice);

    /// <summary>The phonemes of a text in a language, as Kokoro learnt them.</summary>
    public static string Phonemes(string text, string language, string voice) =>
        language == "es"
            ? SpanishPhonemes.ToPhonemes(text)
            : EspeakPhonemes.ToPhonemes(text, KokoroModels.EspeakVoice(language, KokoroModels.Names(voice) is [var first, ..] ? first : "")
                ?? throw new InvalidOperationException($"Kokoro no habla «{language}»"), EspeakFiles.Folder);

    /// <summary>
    /// One sentence in audio (24 kHz mono, the voice as it is), handed to <paramref name="onAudio"/> piece by piece as it is
    /// made. Throws when the files are not there or cannot load.
    /// </summary>
    public async Task<float[]> SayAsync(string text, string language, string voice, double speed, CancellationToken cancellationToken, Action<float[]>? onAudio = null)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _idle.Change(Timeout.Infinite, Timeout.Infinite);
            return await Task.Run(() =>
            {
                var (model, pack) = Ready(voice);
                var audio = new List<float>();
                foreach (var piece in Pieces(Phonemes(text, language, voice)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var said = model.Say(piece, pack, (float)Math.Clamp(speed, 0.5, 2));
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
    /// A sentence longer than this starts with a shorter piece, up to its first comma: it sounds sooner (Kokoro makes a
    /// piece in some 0.15 of what it lasts, all at once).
    /// </summary>
    public const int FirstPieceLength = 90;

    /// <summary>
    /// The phonemes of a sentence in pieces of at most <see cref="PieceLength"/>, cut after a mark or at a space; a long
    /// one starts with what goes up to its first comma, if that is short.
    /// </summary>
    public static IReadOnlyList<string> Pieces(string phonemes)
    {
        var pieces = new List<string>();
        var rest = phonemes.Trim();
        if (KokoroModel.Tokens(rest).Count > FirstPieceLength)
        {
            var comma = rest.IndexOfAny([',', ';', ':'], 20);
            if (comma > 0 && comma < FirstPieceLength && comma < rest.Length - 20)
            {
                pieces.Add(rest[..(comma + 1)].Trim());
                rest = rest[(comma + 1)..].Trim();
            }
        }
        while (KokoroModel.Tokens(rest).Count > PieceLength)
        {
            // The last mark (or else space) before the limit, by characters (close enough to the tokens).
            var limit = Math.Min(rest.Length - 1, PieceLength);
            var cut = rest.LastIndexOfAny([',', ';', ':', '.', '!', '?', '…', '—'], limit);
            if (cut < PieceLength / 3) cut = rest.LastIndexOf(' ', limit);
            if (cut <= 0) cut = limit;
            pieces.Add(rest[..(cut + 1)].Trim());
            rest = rest[(cut + 1)..].Trim();
        }
        if (rest.Length > 0) pieces.Add(rest);
        return pieces;
    }

    /// <summary>Loads the model and the voice in the background so the first sentence does not wait for them (the AI is thinking).</summary>
    public void Warm(string voice)
    {
        if (_model is not null && _voice?.Spec == voice) return;
        _ = Task.Run(async () =>
        {
            if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return;
            try
            {
                _idle.Change(Timeout.Infinite, Timeout.Infinite);
                Ready(voice);
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

    /// <summary>The model and the voice (or mix) ready (under the gate).</summary>
    private (KokoroModel Model, float[] Pack) Ready(string voice)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _model ??= KokoroModel.Load(KokoroModels.ModelFile);
        if (_voice is not { } loaded || loaded.Spec != voice)
        {
            _voice = loaded = (voice, KokoroModel.Voice(voice, KokoroModels.VoiceFile));
            Log.Info($"Asistente: voz Kokoro «{voice}» lista en {watch.ElapsedMilliseconds} ms");
        }
        return (_model, loaded.Pack);
    }

    public void Dispose()
    {
        _idle.Dispose();
        Unload();
        _gate.Dispose();
    }

    /// <summary>Lets the model and espeak-ng go now (K-Desktop is about to delete their files).</summary>
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
            if (_model is null) return;
            _model.Dispose();
            _model = null;
            _voice = null;
            Log.Info("Asistente: voz Kokoro soltada de la memoria");
        }
        finally
        {
            _gate.Release();
        }
    }
}

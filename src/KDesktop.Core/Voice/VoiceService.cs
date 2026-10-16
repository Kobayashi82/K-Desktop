using System.Diagnostics;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;

namespace KDesktop.Core.Voice;

/// <summary>
/// Voice orders: listen (while a key is held, or until the speaker goes quiet) → transcribe with Parakeet → pick the
/// command → run it. Nothing listens in between: the microphone is only open during a session.
/// </summary>
public sealed class VoiceService : IDisposable
{
    private static readonly TimeSpan EndSilence = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan TapLimit = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan NothingSaid = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxSession = TimeSpan.FromSeconds(15);

    private readonly IUiServices _ui;
    private readonly Func<AppConfig> _config;
    private readonly Func<Command, Task> _run;
    private readonly MicCapture _mic = new();
    private volatile bool _stopRequested;
    private int _active;

    /// <param name="run">Runs a chosen command (with the voice trigger source).</param>
    public VoiceService(IUiServices ui, Func<AppConfig> config, Func<Command, Task> run)
    {
        _ui = ui;
        _config = config;
        _run = run;
    }

    public SpeechToText SpeechToText { get; } = new();

    /// <summary>The wake words recorded by the user (Settings › Voice).</summary>
    public WakeWordStore WakeWords { get; } = new();

    /// <summary>Downloads the models (Settings › Voice).</summary>
    public ModelDownloader Models { get; } = new();

    /// <summary>Frees the recognizer and removes the model files.</summary>
    public void DeleteModels()
    {
        SpeechToText.Unload();
        ModelDownloader.Delete();
        StateChanged?.Invoke();
    }

    public bool IsListening => _active != 0;

    /// <summary>Raised (any thread) when a session starts or ends.</summary>
    public event Action? StateChanged;

    /// <summary>The commands a voice order can run.</summary>
    public IEnumerable<Command> Commands() => VoiceCommands(_config());

    public static IEnumerable<Command> VoiceCommands(AppConfig config) =>
        config.Commands.Where(c => c.Enabled
            // Until voice confirmations exist, nothing that switches the PC off or logs out runs by voice.
            && !c.Actions.Any(a => a is PowerAction));

    private CancellationTokenSource? _abort;

    /// <summary>Drops the current session without running anything (Escape).</summary>
    public void Cancel()
    {
        try { _abort?.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// One listening session (the voice hotkey, later the wake word). With <paramref name="held"/> it lasts while
    /// that returns true (a quick tap instead switches to «until silence»); without it, until the speaker goes
    /// quiet. A second call during a session ends the first one early, keeping what was said.
    /// </summary>
    public async Task ListenAsync(Func<bool>? held)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            _stopRequested = true;
            return;
        }
        using var abort = new CancellationTokenSource();
        _abort = abort;
        try
        {
            if (!VoiceModels.Ready)
            {
                _ui.ShowOsd(Loc.T("voice.noModels"), OsdKind.Warning, TimeSpan.FromSeconds(4));
                return;
            }
            _stopRequested = false;
            StateChanged?.Invoke();
            await SessionAsync(held, abort.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("Error en la orden de voz", ex);
        }
        finally
        {
            _abort = null;
            _active = 0;
            StateChanged?.Invoke();
        }
    }

    private async Task SessionAsync(Func<bool>? held, CancellationToken abort)
    {
        var settings = _config().Settings;
        SpeechToText.IdleUnload = TimeSpan.FromMinutes(Math.Max(1, settings.VoiceUnloadMinutes));
        SpeechToText.Preload(); // loads while the user speaks

        var audio = new List<float>(MicCapture.SampleRate * 6);
        using var vad = new VoiceActivity(EndSilence);
        var gate = new object();
        void OnSamples(float[] samples)
        {
            lock (gate)
            {
                audio.AddRange(samples);
                vad.Accept(samples);
            }
        }

        using var osd = _ui.ShowVoiceOsd(Loc.T("voice.listening"));
        if (Environment.GetEnvironmentVariable("KDESKTOP_VOICE_FAKE_STREAM") is { Length: > 0 } || Environment.GetEnvironmentVariable("KDESKTOP_VOICE_FAKE") is { Length: > 0 })
        {
            // Tests with simulated audio never open the real microphone.
            Log.Info("Escucha simulada: la sesión no abre el micrófono");
            osd.Finish(Loc.T("voice.nothingHeard"), OsdKind.Info);
            return;
        }
        _mic.Samples += OnSamples;
        try
        {
            _mic.Start(settings.VoiceMicId);
        }
        catch (Exception ex)
        {
            _mic.Samples -= OnSamples;
            Log.Error("No se pudo abrir el micrófono", ex);
            osd.Finish(Loc.T("voice.micError"), OsdKind.Warning);
            return;
        }

        var watch = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                await Task.Delay(40, CancellationToken.None).ConfigureAwait(false);
                osd.SetLevel(_mic.Level);
                if (abort.IsCancellationRequested)
                {
                    osd.Finish(Loc.T("voice.cancelled"), OsdKind.Info);
                    return;
                }
                if (_stopRequested) break;
                if (held is not null)
                {
                    if (held()) continue;
                    // A tap rather than a hold: keep listening until the speaker goes quiet.
                    if (watch.Elapsed < TapLimit) held = null;
                    else break;
                }
                bool ended, heard;
                lock (gate) (ended, heard) = (vad.Ended, vad.HeardSpeech);
                if (ended || watch.Elapsed > MaxSession) break;
                if (!heard && watch.Elapsed > NothingSaid) break;
            }
        }
        finally
        {
            _mic.Samples -= OnSamples;
            _mic.Stop();
        }

        float[] clip;
        lock (gate)
        {
            vad.Flush();
            clip = Trim(audio, vad.Segments, vad.HeardSpeech);
        }
        if (clip.Length < MicCapture.SampleRate / 5)
        {
            osd.Finish(Loc.T("voice.nothingHeard"), OsdKind.Info);
            return;
        }

        osd.SetText(Loc.T("voice.thinking"));
        string text;
        try
        {
            text = await Task.Run(() => SpeechToText.Transcribe(clip), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("Error transcribiendo la orden de voz", ex);
            osd.Finish(Loc.T("voice.sttError"), OsdKind.Warning);
            return;
        }
        if (abort.IsCancellationRequested)
        {
            osd.Finish(Loc.T("voice.cancelled"), OsdKind.Info);
            return;
        }
        if (text.Length == 0)
        {
            osd.Finish(Loc.T("voice.notUnderstood"), OsdKind.Warning);
            return;
        }

        // «KDesktop, abre Firefox»: the wake word at the start is not part of the order.
        if (WakeWordVerifier.FindAny(text, WakeWords.Active) is { Start: <= 1 } wake)
        {
            Log.Info($"Voz: palabra de activación «{wake.Word.Name}» ({wake.Score:0.00}) al principio de la orden");
            text = wake.Rest;
            if (text.Length == 0)
            {
                osd.Finish(Loc.T("voice.wakeOnly", wake.Word.Name), OsdKind.Info);
                return;
            }
        }

        await RunTextAsync(text, osd).ConfigureAwait(false);
    }

    /// <summary>The spoken order → the command → run it, telling the user on the OSD.</summary>
    private async Task RunTextAsync(string text, IVoiceOsd osd)
    {
        var result = IntentMatcher.Match(text, Commands());
        Log.Info($"Voz → {(result.Command?.Name ?? "nada")} · " +
                 string.Join(", ", result.Candidates.Select(c => $"{c.Command.Name} {c.Score:0.00}")));
        if (result.Command is { } command)
        {
            osd.Finish(Loc.T("voice.running", text, command.Name), OsdKind.Success);
            await _run(command).ConfigureAwait(false);
        }
        else if (result.Candidates.Count > 0 && result.Confidence >= 0.6)
            osd.Finish(Loc.T("voice.unsure", text, result.Candidates[0].Command.Name), OsdKind.Warning);
        else
            osd.Finish(Loc.T("voice.noMatch", text), OsdKind.Warning);
    }

    // ── wake word ────────────────────────────────────────────────────────

    private readonly object _featuresGate = new();
    private OwwFeatures? _features;

    /// <summary>openWakeWord's feature models, loaded on first use and shared (ONNX sessions are thread-safe).</summary>
    public OwwFeatures Features
    {
        get
        {
            lock (_featuresGate) return _features ??= new OwwFeatures();
        }
    }

    /// <summary>
    /// The always-on detector heard <paramref name="word"/> (phase 1, <paramref name="score"/>): <paramref name="audio"/>
    /// runs from a couple of seconds before it until the speaker went quiet. Phase 2 checks it with Parakeet; what was
    /// said after the word is the order, and if nothing was, a normal listening session starts.
    /// </summary>
    public async Task HandleWakeAsync(float[] audio, WakeWord word, double score)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0) return;
        var listen = false;
        try
        {
            if (!VoiceModels.Ready) return;
            StateChanged?.Invoke();
            var settings = _config().Settings;
            SpeechToText.IdleUnload = TimeSpan.FromMinutes(Math.Max(1, settings.VoiceUnloadMinutes));
            var text = await Task.Run(() => SpeechToText.Transcribe(audio)).ConfigureAwait(false);
            var match = WakeWordVerifier.Find(text, word);
            var trusted = settings.VoiceWakeTrust && score >= Math.Max(0.9, word.DetectorThreshold + 0.25);
            if (match is not { Accepted: true } && !trusted)
            {
                Log.Info($"Palabra de activación «{word.Name}» descartada por la verificación: «{text}» → {match?.Score ?? 0:0.00} " +
                         $"(necesita {word.EffectiveThreshold:0.00}; detector {score:0.00})");
                return;
            }
            var order = match is { Accepted: true } ? match.Rest : "";
            Log.Info($"Palabra de activación «{word.Name}»: detector {score:0.00}, verificación {match?.Score ?? 0:0.00}" +
                     (trusted && match is not { Accepted: true } ? " (sin verificar: el detector estaba muy seguro)" : "") +
                     (order.Length > 0 ? $"; orden «{order}»" : "; sin orden: escuchando"));
            WakeHeard?.Invoke(word);
            if (order.Length == 0)
            {
                listen = true;
                return;
            }
            using var osd = _ui.ShowVoiceOsd(Loc.T("voice.thinking"));
            await RunTextAsync(order, osd).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("Error tras la palabra de activación", ex);
        }
        finally
        {
            _active = 0;
            StateChanged?.Invoke();
        }
        // Only the word: listen for the order now (until the speaker goes quiet).
        if (listen) await ListenAsync(null).ConfigureAwait(false);
    }

    /// <summary>Raised (any thread) when a wake word passed both phases.</summary>
    public event Action<WakeWord>? WakeHeard;

    /// <summary>
    /// The audio to transcribe: from a little before the first speech to a little after the last. Without detected
    /// speech, all of it when the key was held (VAD may miss a single short word), else nothing.
    /// </summary>
    private static float[] Trim(List<float> audio, List<(int Start, int Length)> segments, bool heard)
    {
        if (segments.Count == 0) return heard ? audio.ToArray() : [];
        var pad = MicCapture.SampleRate * 3 / 10;
        var start = Math.Clamp(segments[0].Start - pad, 0, audio.Count);
        var end = Math.Clamp(segments[^1].Start + segments[^1].Length + pad, start, audio.Count);
        return audio.GetRange(start, end - start).ToArray();
    }

    public void Dispose()
    {
        _mic.Dispose();
        SpeechToText.Dispose();
        WakeWords.Dispose();
        lock (_featuresGate) _features?.Dispose();
    }
}

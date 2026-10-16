using System.Diagnostics;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Voice;

/// <summary>
/// Voice orders: listen (while a key is held, or until the speaker goes quiet) → transcribe with Parakeet → pick the
/// command → run it. Nothing listens in between: the microphone is only open during a session.
/// </summary>
public sealed partial class VoiceService : IDisposable
{
    private static readonly TimeSpan EndSilence = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan TapLimit = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan NothingSaid = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxSession = TimeSpan.FromSeconds(15);

    private readonly IUiServices _ui;
    private readonly Func<AppConfig> _config;
    private readonly Func<Command, Task> _run;
    private readonly Func<CommandAction, Task> _runAction;
    private readonly MicCapture _mic = new();
    private volatile bool _stopRequested;
    private int _active;

    /// <param name="run">Runs a chosen command (with the voice trigger source).</param>
    /// <param name="runAction">Runs a single action outside any command (the media orders start and close programs).</param>
    public VoiceService(IUiServices ui, Func<AppConfig> config, Func<Command, Task> run, Func<CommandAction, Task> runAction)
    {
        _ui = ui;
        _config = config;
        _run = run;
        _runAction = runAction;
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
        config.Commands.Where(c => c.Enabled);

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
        using var duck = Duck(settings);
        try
        {
            _mic.EchoCancel = settings.VoiceEchoCancel;
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
        finally
        {
            duck?.Dispose(); // the order is said: the others sound again (before it runs: «pausa» pauses them anyway)
        }
        if (abort.IsCancellationRequested)
        {
            osd.Finish(Loc.T("voice.cancelled"), OsdKind.Info);
            return;
        }
        await HandleTextAsync(text, osd).ConfigureAwait(false);
    }

    /// <summary>The transcribed order (without a leading wake word) → run it, telling the user on the OSD.</summary>
    private async Task HandleTextAsync(string text, IVoiceOsd osd)
    {
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
    private Task RunTextAsync(string text, IVoiceOsd osd) => RunPlanAsync(IntentMatcher.Plan(text, Commands(), MediaEnabled), osd);

    /// <summary>The built-in media orders are on (Settings › Multimedia).</summary>
    private bool MediaEnabled => _config().Settings.VoiceMedia;

    /// <summary>Runs what the order asked for (one command or several, in order), telling the user on the OSD.</summary>
    private async Task RunPlanAsync(IntentPlan plan, IVoiceOsd osd)
    {
        var whole = plan.Whole;
        var text = whole.Text;
        Log.Info($"Voz → {(plan.Parts.Count == 0 ? "nada" : string.Join(" + ", plan.Parts.Select(p => p.Winner!.Name)))} · " +
                 string.Join(", ", whole.Candidates.Select(c => $"{c.Describe} {c.Score:0.00}")) +
                 (plan.Parts.Count > 1 || plan.Unmatched.Count > 0
                     ? " · por partes: " + string.Join(" | ", plan.Parts.Select(p => $"«{p.Text}» → {p.Winner!.Name} {p.Confidence:0.00}")
                         .Concat(plan.Unmatched.Select(u => $"«{u}» → nada")))
                     : ""));
        if (plan.Parts.Count == 0)
        {
            if (whole.Candidates.Count > 0 && whole.Confidence >= 0.6)
                osd.Finish(Loc.T("voice.unsure", text, whole.Candidates[0].Name), OsdKind.Warning);
            else
                osd.Finish(Loc.T("voice.noMatch", text), OsdKind.Warning);
            return;
        }
        if (plan.Parts is [{ Winner.Media: { } single }] && plan.Unmatched.Count == 0)
        {
            // A media order says how it went («Volumen: 30 %», «No suena nada»).
            var (message, kind) = await RunMediaAsync(single).ConfigureAwait(false);
            osd.Finish(Loc.T("voice.running", text, message), kind);
            return;
        }
        if (plan.Parts.Count == 1 && plan.Unmatched.Count == 0)
            osd.Finish(Loc.T("voice.running", text, plan.Parts[0].Winner!.Name), OsdKind.Success);
        else
            osd.Finish(Loc.T("voice.runningMany", string.Join(" · ", plan.Parts.Select(p => p.Winner!.Name)))
                       + (plan.Unmatched.Count > 0 ? "\n" + Loc.T("voice.partNotUnderstood", string.Join("», «", plan.Unmatched)) : ""),
                       plan.Unmatched.Count > 0 ? OsdKind.Warning : OsdKind.Success);
        foreach (var part in plan.Parts)
        {
            if (part.Winner!.Media is { } media) await RunMediaAsync(media).ConfigureAwait(false);
            else await _run(part.Winner.Command!).ConfigureAwait(false);
        }
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

    /// <summary>How long, after the wake word, the user may wait before starting the order.</summary>
    private static readonly TimeSpan WaitForOrder = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The always-on detector heard <paramref name="word"/> (phase 1, <paramref name="score"/>). Phase 2: Parakeet
    /// transcribes the word alone (trimmed, as in the set-up) and it must sound like the word; when the detector is very
    /// sure, a somewhat looser transcription is enough. Then the order: <paramref name="capture"/> has been recording
    /// since the trigger without a gap, so it may already be on its way or come after a pause («Escuchando…»).
    /// </summary>
    public async Task HandleWakeAsync(WakeWord word, double score, WakeCapture capture)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            capture.Stop();
            return;
        }
        using var abort = new CancellationTokenSource();
        _abort = abort;
        _stopRequested = false;
        try
        {
            if (!VoiceModels.Ready) return;
            var settings = _config().Settings;
            SpeechToText.IdleUnload = TimeSpan.FromMinutes(Math.Max(1, settings.VoiceUnloadMinutes));

            // Turned down at once, so the order that follows is heard over the music; back up if the word isn't confirmed.
            using var duck = Duck(settings);

            // Phase 2, on the word itself.
            var sure = score >= word.DetectorThreshold + 0.25;
            bool Accepted(double verification) =>
                verification >= word.EffectiveThreshold
                || (sure && verification >= word.EffectiveThreshold - 0.15)
                || (settings.VoiceWakeTrust && score >= Math.Max(0.9, word.DetectorThreshold + 0.25));
            var wordClip = LastUtterance(capture.Preroll);
            var wordText = wordClip.Length > 0 ? await Task.Run(() => SpeechToText.Transcribe(wordClip)).ConfigureAwait(false) : "";
            var match = WakeWordVerifier.Find(wordText, word);
            if (!Accepted(match?.Score ?? 0) && capture.Preroll.Length > 0)
            {
                // With sound in the background (what the echo canceller leaves of music, a noisy room) the speech
                // detector may cut the word badly: the last moments as they are, uncut.
                var tail = capture.Preroll[Math.Max(0, capture.Preroll.Length - MicCapture.SampleRate * 8 / 5)..];
                var tailText = await Task.Run(() => SpeechToText.Transcribe(tail)).ConfigureAwait(false);
                if (WakeWordVerifier.Find(tailText, word) is { } tailMatch && tailMatch.Score > (match?.Score ?? 0))
                {
                    Log.Info($"Palabra de activación «{word.Name}»: recortada «{wordText}», entera «{tailText}»");
                    (wordClip, wordText, match) = (tail, tailText, tailMatch);
                }
            }
            var verification = match?.Score ?? 0;
            var accepted = Accepted(verification);
            // The detector very sure but the word alone not understood (alone and with music behind, Parakeet may take it
            // for another language): it is confirmed afterwards with the order, transcribed together with it.
            var provisional = !accepted && sure;
            if (!accepted && !provisional)
            {
                Log.Info($"Palabra de activación «{word.Name}» descartada por la verificación: «{wordText}» → {verification:0.00} " +
                         $"(necesita {word.EffectiveThreshold:0.00}{(sure ? $" o {word.EffectiveThreshold - 0.15:0.00} con el detector tan seguro" : "")}; detector {score:0.00})");
                return;
            }
            Log.Info($"Palabra de activación «{word.Name}»: detector {score:0.00}, verificación «{wordText}» {verification:0.00}" +
                     (provisional ? " (dudosa: se confirmará con la orden)" : ""));
            WakeHeard?.Invoke(word);
            StateChanged?.Invoke();

            // The order: until the speaker goes quiet, or nothing said in a few seconds.
            using var osd = _ui.ShowVoiceOsd(Loc.T("voice.listening"));
            while (true)
            {
                await Task.Delay(40, CancellationToken.None).ConfigureAwait(false);
                osd.SetLevel(capture.Level);
                if (abort.IsCancellationRequested)
                {
                    osd.Finish(Loc.T("voice.cancelled"), OsdKind.Info);
                    return;
                }
                if (_stopRequested || capture.Ended || capture.Elapsed > MaxSession) break;
                if (!capture.HeardSpeech && capture.Elapsed > WaitForOrder) break;
            }
            var (after, segments) = capture.Stop();
            osd.SetText(Loc.T("voice.thinking"));

            // What to take as the order: what came after the trigger; or, said without a pause, the word and the order
            // transcribed together with the word cut out; or the start of the order caught in the word's own clip.
            var options = new List<string>();
            var confirmed = !provisional;
            if (!provisional && match?.Rest is { Length: > 0 } inWord) options.Add(inWord);
            if (segments.Count > 0)
            {
                var pad = MicCapture.SampleRate / 5;
                var from = Math.Max(0, segments[0].Start - pad);
                var to = Math.Min(after.Length, segments[^1].Start + segments[^1].Length + pad);
                var orderClip = after[from..to];
                options.Add(await Task.Run(() => SpeechToText.Transcribe(orderClip)).ConfigureAwait(false));
                var joined = new float[wordClip.Length + orderClip.Length];
                wordClip.CopyTo(joined, 0);
                orderClip.CopyTo(joined, wordClip.Length);
                var full = await Task.Run(() => SpeechToText.Transcribe(joined)).ConfigureAwait(false);
                if (WakeWordVerifier.Find(full, word) is { } cut && cut.Score >= word.EffectiveThreshold - 0.15)
                {
                    confirmed = true;
                    if (cut.Rest.Length > 0) options.Add(cut.Rest);
                }
                if (provisional) Log.Info($"Palabra de activación «{word.Name}» con la orden: «{full}» → {(confirmed ? "confirmada" : "no está")}");
            }
            duck?.Dispose();
            if (!confirmed)
            {
                osd.Finish(Loc.T("voice.nothingHeard"), OsdKind.Info);
                return;
            }
            if (abort.IsCancellationRequested)
            {
                osd.Finish(Loc.T("voice.cancelled"), OsdKind.Info);
                return;
            }
            options = options.Select(o => o.Trim()).Where(o => o.Any(char.IsLetterOrDigit)).Distinct().ToList();
            if (options.Count == 0)
            {
                osd.Finish(Loc.T("voice.nothingHeard"), OsdKind.Info);
                return;
            }
            var commands = Commands().ToList();
            var plans = options.Select(o => IntentMatcher.Plan(o, commands, MediaEnabled)).ToList();
            Log.Info("Voz: orden tras la palabra, opciones: " + string.Join(" | ", plans.Select(p => $"«{p.Whole.Text}» {p.Score:0.00}")));
            await RunPlanAsync(plans.MaxBy(p => p.Score)!, osd).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("Error tras la palabra de activación", ex);
        }
        finally
        {
            capture.Stop();
            _abort = null;
            _active = 0;
            StateChanged?.Invoke();
        }
    }

    /// <summary>The other programs turned down while an order is said (Settings › Voz), or null.</summary>
    private static AudioDucker? Duck(AppSettings settings) =>
        settings.VoiceDuck && settings.VoiceDuckLevel < 100 ? AudioDucker.Duck(settings.VoiceDuckLevel / 100.0) : null;

    /// <summary>The last stretch of speech in <paramref name="audio"/> (the wake word), a little silence around it.</summary>
    private static float[] LastUtterance(float[] audio)
    {
        var segments = WakeWordRecorder.Segments(audio);
        if (segments.Count == 0) return audio[Math.Max(0, audio.Length - MicCapture.SampleRate * 6 / 5)..];
        var pad = MicCapture.SampleRate / 8;
        var (start, length) = segments[^1];
        return audio[Math.Max(0, start - pad)..Math.Min(audio.Length, start + length + pad)];
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

using System.Diagnostics;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Voice;

/// <summary>
/// Someone else an order is for (an assistant of the module, stage 5.1): what is said goes to it as text instead of
/// running K-Desktop's commands.
/// </summary>
/// <param name="Name">Who, for the OSD («Lander»).</param>
/// <param name="Send">Hands it the order (the text and where it came from: <c>voice</c> or <c>phone</c>); false when nobody could take it (it says why).</param>
/// <param name="Listening">It is being listened to / no longer (the character shows it); null: nothing to show.</param>
public sealed record VoiceAddressee(string Name, Func<string, string, bool> Send, Action<bool>? Listening = null);

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

    /// <summary>What an order means, when the words name no command (optional model, <see cref="SemanticMatcher"/>).</summary>
    public SemanticMatcher Semantic { get; } = new();

    /// <summary>The wake words recorded by the user (Settings › Voice).</summary>
    public WakeWordStore WakeWords { get; } = new();

    /// <summary>Downloads the models (Settings › Voice).</summary>
    public ModelDownloader Models { get; } = new();

    /// <summary>
    /// Who an order called with a wake word (its id) is for when it is not K-Desktop: an assistant's own word, with the
    /// module on. Null, or a null answer: K-Desktop's commands, as always. Asked from any thread.
    /// </summary>
    public Func<string, VoiceAddressee?>? AddresseeOf { get; set; }

    /// <summary>
    /// What the assistants are saying aloud (stage 5.7): heard after a wake word, it is their own voice, not an order (the
    /// app tells it what they say).
    /// </summary>
    public Assistant.SelfEcho OwnVoices { get; } = new();

    /// <summary>Frees the recognizer and removes the model files.</summary>
    public void DeleteModels()
    {
        SpeechToText.Unload();
        Semantic.Embedder.Unload();
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
    /// quiet. A second call during a session ends the first one early, keeping what was said. With <paramref name="to"/>
    /// (an assistant's talk key, a long click on it: stage 5.2) what is said goes to that assistant, unless it begins with
    /// another wake word.
    /// </summary>
    public Task ListenAsync(Func<bool>? held, VoiceAddressee? to = null) => ListenCoreAsync(held, to, NothingSaid, brief: false, default);

    /// <summary>
    /// A short listening without the wake word for <paramref name="to"/> (stage 5.3): after a question of an assistant, to
    /// hear its answer. It waits up to <paramref name="wait"/> for the user to start speaking, then until they go quiet;
    /// nothing said (or <paramref name="stop"/>) closes it without a word. Never interrupts a session going on.
    /// </summary>
    public Task ListenForAsync(VoiceAddressee to, TimeSpan wait, CancellationToken stop = default) =>
        _active != 0 ? Task.CompletedTask : ListenCoreAsync(null, to, wait, brief: true, stop);

    private async Task ListenCoreAsync(Func<bool>? held, VoiceAddressee? to, TimeSpan nothingSaid, bool brief, CancellationToken stop)
    {
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            if (!brief) _stopRequested = true;
            return;
        }
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(stop);
        _abort = abort;
        var listening = false;
        try
        {
            if (!VoiceModels.Ready)
            {
                if (!brief) _ui.ShowOsd(Loc.T("voice.noModels"), OsdKind.Warning, TimeSpan.FromSeconds(4));
                return;
            }
            _stopRequested = false;
            StateChanged?.Invoke();
            if (to?.Listening is { } shows)
            {
                shows(true);
                listening = true;
                // Once it has the order it says itself that it is no longer listening.
                to = to with { Listening = active => { listening = false; shows(active); } };
            }
            await SessionAsync(held, to, nothingSaid, brief, abort.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("Error en la orden de voz", ex);
        }
        finally
        {
            if (listening) to?.Listening?.Invoke(false);
            _abort = null;
            _active = 0;
            StateChanged?.Invoke();
        }
    }

    /// <param name="nothingSaid">How long to wait for speech before giving up.</param>
    /// <param name="brief">A follow-up listening: when nothing is said, or it is stopped, the OSD just goes away.</param>
    private async Task SessionAsync(Func<bool>? held, VoiceAddressee? to, TimeSpan nothingSaid, bool brief, CancellationToken abort)
    {
        var settings = _config().Settings;
        PreloadModels(settings); // they load while the user speaks

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
        ListenSound(settings);
        // A brief listening that hears nothing just goes away: the user did not ask for it.
        void End(string text)
        {
            if (brief) osd.Dispose();
            else osd.Finish(text, OsdKind.Info);
        }
        using var fake = new CancellationTokenSource();
        var simulated = Environment.GetEnvironmentVariable("KDESKTOP_VOICE_FAKE_STREAM") is { Length: > 0 } || Environment.GetEnvironmentVariable("KDESKTOP_VOICE_FAKE") is { Length: > 0 };
        if (simulated)
        {
            // Tests with simulated audio never open the real microphone; KDESKTOP_VOICE_FAKE_SESSION (a .wav) is what is said.
            if (Environment.GetEnvironmentVariable("KDESKTOP_VOICE_FAKE_SESSION") is not { Length: > 0 } said || !File.Exists(said))
            {
                Log.Info("Escucha simulada: la sesión no abre el micrófono");
                End(Loc.T("voice.nothingHeard"));
                return;
            }
            Log.Info($"Escucha simulada: la sesión oye {said}");
            PlayFake(VoiceDiagnostics.ReadWav(said), OnSamples, fake.Token);
        }
        else _mic.Samples += OnSamples;
        using var duck = Duck(settings);
        try
        {
            _mic.EchoCancel = settings.VoiceEchoCancel;
            if (!simulated) _mic.Start(settings.VoiceMicId);
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
                    End(Loc.T("voice.cancelled"));
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
                if (!heard && watch.Elapsed > nothingSaid) break;
            }
        }
        finally
        {
            fake.Cancel();
            _mic.Samples -= OnSamples;
            if (!simulated) _mic.Stop();
        }

        float[] clip;
        lock (gate)
        {
            vad.Flush();
            clip = Trim(audio, vad.Segments, vad.HeardSpeech);
        }
        if (clip.Length < MicCapture.SampleRate / 5)
        {
            End(Loc.T("voice.nothingHeard"));
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
            End(Loc.T("voice.cancelled"));
            return;
        }
        if (to is not null) await HandleTextForAsync(text, osd, to).ConfigureAwait(false);
        else await HandleTextAsync(text, osd).ConfigureAwait(false);
    }

    /// <summary>
    /// What was said to an assistant (its key, a long click on it). Begun with a wake word, that word decides: another
    /// assistant's sends it there, K-Desktop's runs commands.
    /// </summary>
    private async Task HandleTextForAsync(string text, IVoiceOsd osd, VoiceAddressee to)
    {
        if (text.Length == 0)
        {
            osd.Finish(Loc.T("voice.notUnderstood"), OsdKind.Warning);
            return;
        }
        if (WakeWordVerifier.FindAny(text, WakeWords.Active) is { Start: <= 1 } wake)
        {
            text = wake.Rest;
            if (text.Length == 0)
            {
                osd.Finish(Loc.T("voice.wakeOnly", wake.Word.Name), OsdKind.Info);
                return;
            }
            if (AddresseeOf?.Invoke(wake.Word.Id) is not { } other)
            {
                Log.Info($"Voz: «{wake.Word.Name}» al principio de lo dicho a «{to.Name}»: es de K-Desktop");
                await RunTextAsync(text, osd).ConfigureAwait(false);
                return;
            }
            to.Listening?.Invoke(false);
            to = other;
        }
        SendTo(to, text, "voice", osd);
    }

    /// <summary>
    /// The transcribed order (without a leading wake word) → run it, telling the user on the OSD. Begun with an assistant's
    /// word («Lander, ¿qué hora es?»), it goes to that assistant instead (<paramref name="source"/>: <c>voice</c> or <c>phone</c>).
    /// </summary>
    private async Task HandleTextAsync(string text, IVoiceOsd osd, string source = "voice")
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
            if (AddresseeOf?.Invoke(wake.Word.Id) is { } addressee)
            {
                SendTo(addressee, text, source, osd);
                return;
            }
        }

        await RunTextAsync(text, osd).ConfigureAwait(false);
    }

    /// <summary>An order for an assistant: it gets the text, and the OSD says who heard what.</summary>
    private static void SendTo(VoiceAddressee addressee, string text, string source, IVoiceOsd osd)
    {
        Log.Info($"Voz → asistente «{addressee.Name}»: «{text}»");
        addressee.Listening?.Invoke(false);
        if (addressee.Send(text, source)) osd.Finish(Loc.T("voice.toAssistant", addressee.Name, text), OsdKind.Info);
        else osd.Dispose();
    }

    /// <summary>The spoken order → the command → run it, telling the user on the OSD.</summary>
    private async Task RunTextAsync(string text, IVoiceOsd osd) => await RunPlanAsync(await PlanAsync(text, Commands().ToList()).ConfigureAwait(false), osd).ConfigureAwait(false);

    /// <summary>What to run for a text: the words, then (if they name nothing clearly) the meaning. Off the caller's thread.</summary>
    private Task<IntentPlan> PlanAsync(string text, IReadOnlyCollection<Command> commands)
    {
        var media = MediaEnabled;
        return Task.Run(() => IntentMatcher.Plan(text, commands, media, Semantic));
    }

    /// <summary>How long the models stay in memory without use (0 = «Nunca»: they stay loaded).</summary>
    private static TimeSpan UnloadAfter(AppSettings settings) =>
        settings.VoiceUnloadMinutes <= 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMinutes(settings.VoiceUnloadMinutes);

    /// <summary>Parakeet and the meaning model load in the background while the user is still speaking.</summary>
    private void PreloadModels(AppSettings settings)
    {
        var idle = UnloadAfter(settings);
        SpeechToText.IdleUnload = idle;
        Semantic.IdleUnload = idle;
        SpeechToText.Preload();
        Semantic.Preload(Commands().ToList(), settings.VoiceMedia);
    }

    /// <summary>The built-in media orders are on (Settings › Multimedia).</summary>
    private bool MediaEnabled => _config().Settings.VoiceMedia;

    /// <summary>Runs what the order asked for (one command or several, in order), telling the user on the OSD.</summary>
    private async Task RunPlanAsync(IntentPlan plan, IVoiceOsd osd)
    {
        var whole = plan.Whole;
        var text = whole.Text;
        Log.Info($"Voz → {(plan.Parts.Count == 0 ? "nada" : string.Join(" + ", plan.Parts.Select(p => p.Winner!.Describe)))} · " +
                 string.Join(", ", whole.Candidates.Select(c => $"{c.Describe} {c.Score:0.00}")) +
                 (plan.Parts.Count > 1 || plan.Unmatched.Count > 0
                     ? " · por partes: " + string.Join(" | ", plan.Parts.Select(p => $"«{p.Text}» → {p.Winner!.Describe} {p.Confidence:0.00}")
                         .Concat(plan.Unmatched.Select(u => $"«{u}» → nada")))
                     : "") +
                 (whole.Note is { } note ? " · " + note : ""));
        var heard = $"«{text}»";
        if (plan.Parts.Count == 0)
        {
            var unsure = whole.Candidates.Count > 0 && whole.Confidence >= 0.6;
            var why = unsure ? Loc.T("voice.unsure", text, whole.Candidates[0].Name) : Loc.T("voice.noMatch", text);
            osd.Finish(why, OsdKind.Warning);
            History?.Add(new HistoryEntry(DateTime.Now, null, heard, TriggerSource.Voice, null, RunOutcome.NotUnderstood,
                unsure ? Loc.T("history.voiceUnsure", whole.Candidates[0].Name) : null, 0));
            return;
        }
        if (plan.Parts is [{ Winner.Media: { } single }] && plan.Unmatched.Count == 0)
        {
            // A media order says how it went («Volumen: 30 %», «No suena nada»).
            OrderRun?.Invoke(plan.Parts[0].Winner!);
            var started = Environment.TickCount64;
            var (message, kind) = await RunMediaAsync(single).ConfigureAwait(false);
            osd.Finish(Loc.T("voice.running", text, message), kind);
            History?.Add(new HistoryEntry(DateTime.Now, null, plan.Parts[0].Winner!.Name, TriggerSource.Voice, heard,
                kind == OsdKind.Warning ? RunOutcome.Error : RunOutcome.Ok, message, (int)(Environment.TickCount64 - started)));
            return;
        }
        if (plan.Parts.Count == 1 && plan.Unmatched.Count == 0)
            osd.Finish(Loc.T("voice.running", text, plan.Parts[0].Winner!.Name), OsdKind.Success);
        else
            osd.Finish(Loc.T("voice.runningMany", string.Join(" · ", plan.Parts.Select(p => p.Winner!.Name)))
                       + (plan.Unmatched.Count > 0 ? "\n" + Loc.T("voice.partNotUnderstood", string.Join("», «", plan.Unmatched)) : ""),
                       plan.Unmatched.Count > 0 ? OsdKind.Warning : OsdKind.Success);
        foreach (var part in plan.Parts) OrderRun?.Invoke(part.Winner!);
        // Not awaited: a command may take long (a countdown, a macro) and the next order — «cancela» — must be heard.
        _ = Task.Run(async () =>
        {
            using var origin = ExecutionHistory.Describe(heard);
            foreach (var part in plan.Parts)
            {
                try
                {
                    if (part.Winner!.Media is { } media) await RunMediaAsync(media).ConfigureAwait(false);
                    else await _run(part.Winner.Command!).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Error($"Error ejecutando «{part.Winner!.Name}» por voz", ex);
                }
            }
        });
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

    /// <summary>How long the order may still take after something that was not one (the end of the word, a noise).</summary>
    private static readonly TimeSpan WaitAfterPause = TimeSpan.FromSeconds(3);

    /// <summary>The order alone has this many words more than with the wake word in front: the latter lost some.</summary>
    private const int LostWords = 3;

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
        VoiceAddressee? addressee = null;
        var wakeAt = DateTime.Now;
        try
        {
            if (!VoiceModels.Ready) return;
            var settings = _config().Settings;
            var cold = !SpeechToText.IsLoaded;
            PreloadModels(settings);

            // Turned down at once, so the order that follows is heard over the music; back up if the word isn't confirmed.
            using var duck = Duck(settings);

            // With Parakeet still to load (a second or two) the verification would hold «Escuchando…» back: shown at
            // once instead, since the capture is already recording; hidden without a word if the word isn't confirmed.
            using var early = cold ? _ui.ShowVoiceOsd(Loc.T("voice.listening")) : null;
            if (early is not null) ListenSound(settings);
            async Task<string> TranscribeWord(float[] clip)
            {
                var task = Task.Run(() => SpeechToText.Transcribe(clip));
                while (early is not null && !task.IsCompleted)
                {
                    early.SetLevel(capture.Level);
                    await Task.WhenAny(task, Task.Delay(40)).ConfigureAwait(false);
                }
                return await task.ConfigureAwait(false);
            }

            // Phase 2, on the word itself.
            var sure = score >= word.DetectorThreshold + 0.25;
            bool Accepted(double verification) =>
                verification >= word.EffectiveThreshold
                || (sure && verification >= word.EffectiveThreshold - 0.15)
                || (settings.VoiceWakeTrust && score >= Math.Max(0.9, word.DetectorThreshold + 0.25));
            var wordClip = LastUtterance(capture.Preroll);
            var wordText = wordClip.Length > 0 ? await TranscribeWord(wordClip).ConfigureAwait(false) : "";
            var match = WakeWordVerifier.Find(wordText, word);
            if (!Accepted(match?.Score ?? 0) && capture.Preroll.Length > 0)
            {
                // With sound in the background (what the echo canceller leaves of music, a noisy room) the speech
                // detector may cut the word badly: the last moments as they are, uncut.
                var tail = capture.Preroll[Math.Max(0, capture.Preroll.Length - MicCapture.SampleRate * 8 / 5)..];
                var tailText = await TranscribeWord(tail).ConfigureAwait(false);
                if (WakeWordVerifier.Find(tailText, word) is { } tailMatch && tailMatch.Score > (match?.Score ?? 0))
                {
                    Log.Info($"Palabra de activación «{word.Name}»: recortada «{wordText}», entera «{tailText}»");
                    (wordClip, wordText, match) = (tail, tailText, tailMatch);
                }
            }
            // An assistant saying the word itself («Hola, soy Lander»): the words around it are its sentence.
            if (OwnVoices.IsEcho(wordText, wakeAt, out var echoed))
            {
                Log.Info($"Palabra de activación «{word.Name}» descartada: «{wordText}» es la voz de un asistente («{echoed}»)");
                return;
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
            // An assistant's own word (stage 5.1): what follows goes to it as it was said, not to the commands.
            addressee = AddresseeOf?.Invoke(word.Id);
            addressee?.Listening?.Invoke(true);

            // The order: until the speaker goes quiet, or nothing said in a few seconds. The detector fires before the
            // word is over, so what comes first may be only its last syllable: if that says nothing, the order may
            // still come after a pause («KDesktop… apaga el ordenador») and the listening goes on.
            using var osd = early ?? _ui.ShowVoiceOsd(Loc.T("voice.listening"));
            if (early is null) ListenSound(settings);
            var commands = addressee is null ? Commands().ToList() : [];
            (List<IntentPlan> Plans, string? Said, bool Confirmed)? found = null;
            bool IsOrder((List<IntentPlan> Plans, string? Said, bool Confirmed) f) =>
                addressee is null ? f.Plans.Any(p => p.Parts.Count > 0) : f.Said is not null;
            var checkedSegments = 0;
            var waitFrom = TimeSpan.Zero;
            // Stage 5.7: at each short pause, what was said so far is already transcribed; if the order ends there, that
            // is used instead of transcribing it again after the end silence.
            var guessedPauses = 0;
            (List<(int Start, int Length)> Segments, Task<(List<IntentPlan>, string?, bool)> Plans)? guess = null;
            while (true)
            {
                await Task.Delay(40, CancellationToken.None).ConfigureAwait(false);
                osd.SetLevel(capture.Level);
                if (abort.IsCancellationRequested)
                {
                    osd.Finish(Loc.T("voice.cancelled"), OsdKind.Info);
                    return;
                }
                if (_stopRequested || capture.Elapsed > MaxSession) break;
                if (capture.SegmentCount <= checkedSegments && capture.PauseCount > guessedPauses)
                {
                    guessedPauses = capture.PauseCount;
                    if (capture.Guess() is { } sofarGuess)
                        guess = (sofarGuess.Segments, OrderPlansAsync(sofarGuess.Audio, sofarGuess.Segments, checkedSegments));
                }
                if (capture.SegmentCount > checkedSegments)
                {
                    var (sofar, finished) = capture.Snapshot();
                    if (guess is { } ready && WakeCapture.SameSpeech(ready.Segments, finished))
                    {
                        found = await ready.Plans.ConfigureAwait(false);
                        Log.Info("Voz: la orden ya estaba transcrita en la pausa");
                    }
                    else found = await OrderPlansAsync(sofar, finished, checkedSegments).ConfigureAwait(false);
                    guess = null;
                    if (!found.Value.Confirmed || IsOrder(found.Value)) break;
                    Log.Info("Voz: lo oído tras la palabra no es una orden (" +
                             (addressee is null ? string.Join(" | ", found.Value.Plans.Select(p => $"«{p.Whole.Text}»")) : "solo la palabra") +
                             "); se sigue escuchando");
                    checkedSegments = finished.Count;
                    waitFrom = capture.Elapsed;
                    continue;
                }
                if (checkedSegments == 0 && !capture.HeardSpeech && capture.Elapsed > WaitForOrder) break;
                if (checkedSegments > 0 && !capture.Speaking && capture.Elapsed - waitFrom > WaitAfterPause) break;
            }
            var (after, segments) = capture.Stop();
            osd.SetText(Loc.T("voice.thinking"));
            if (found is null || segments.Count > checkedSegments && !IsOrder(found.Value))
                found = await OrderPlansAsync(after, segments, checkedSegments).ConfigureAwait(false);
            duck?.Dispose();
            if (!found.Value.Confirmed)
            {
                osd.Finish(Loc.T("voice.nothingHeard"), OsdKind.Info);
                return;
            }
            if (abort.IsCancellationRequested)
            {
                osd.Finish(Loc.T("voice.cancelled"), OsdKind.Info);
                return;
            }
            // What followed the word is what an assistant was saying: its own voice, not an order.
            var heardText = addressee is not null ? found.Value.Said : found.Value.Plans.MaxBy(p => p.Score)?.Whole.Text;
            if (heardText is not null && OwnVoices.IsEcho(heardText, wakeAt, out var echo))
            {
                Log.Info($"Voz: «{heardText}» tras «{word.Name}» es la voz de un asistente («{echo}»): se ignora");
                osd.Finish(Loc.T("voice.nothingHeard"), OsdKind.Info);
                return;
            }
            if (addressee is not null)
            {
                if (found.Value.Said is { } said)
                {
                    SendTo(addressee, said, "voice", osd);
                    addressee = null; // it has the order: it says it is no longer listening itself
                }
                else osd.Finish(Loc.T("voice.wakeOnly", word.Name), OsdKind.Info);
                return;
            }
            var plans = found.Value.Plans;
            if (plans.Count == 0)
            {
                osd.Finish(Loc.T("voice.nothingHeard"), OsdKind.Info);
                return;
            }
            Log.Info("Voz: orden tras la palabra, opciones: " + string.Join(" | ", plans.Select(p => $"«{p.Whole.Text}» {p.Score:0.00}")));
            await RunPlanAsync(plans.MaxBy(p => p.Score)!, osd).ConfigureAwait(false);

            // What to take as the order: what came after the trigger (and, after a pause, only what came after the
            // pause); or, said without a pause, the word and the order transcribed together with the word cut out; or
            // the start of the order caught in the word's own clip. For an assistant (which takes any words, not only a
            // command's) one of them: what came after the pause; else the word and the order together with the word cut
            // out (nothing left: it was only the end of the word); else the order alone.
            async Task<(List<IntentPlan>, string?, bool)> OrderPlansAsync(float[] audio, List<(int Start, int Length)> parts, int fresh)
            {
                var options = new List<string>();
                var confirmed = !provisional;
                string? whole = null, afterPauseText = null, joinedRest = null;
                if (!provisional && match?.Rest is { Length: > 0 } inWord) options.Add(inWord);
                if (parts.Count > 0)
                {
                    var orderClip = Span(audio, parts, 0);
                    whole = await Task.Run(() => SpeechToText.Transcribe(orderClip)).ConfigureAwait(false);
                    options.Add(whole);
                    if (fresh > 0 && fresh < parts.Count)
                    {
                        var afterPause = Span(audio, parts, fresh);
                        afterPauseText = await Task.Run(() => SpeechToText.Transcribe(afterPause)).ConfigureAwait(false);
                        options.Add(afterPauseText);
                    }
                    var joined = new float[wordClip.Length + orderClip.Length];
                    wordClip.CopyTo(joined, 0);
                    orderClip.CopyTo(joined, wordClip.Length);
                    var full = await Task.Run(() => SpeechToText.Transcribe(joined)).ConfigureAwait(false);
                    if (WakeWordVerifier.Find(full, word) is { } cut && cut.Score >= word.EffectiveThreshold - 0.15)
                    {
                        confirmed = true;
                        joinedRest = cut.Rest;
                        if (cut.Rest.Length > 0) options.Add(cut.Rest);
                    }
                    if (provisional) Log.Info($"Palabra de activación «{word.Name}» con la orden: «{full}» → {(confirmed ? "confirmada" : "no está")}");
                }
                if (addressee is not null)
                {
                    static string? Words(string? text) => text?.Trim() is { } t && t.Any(char.IsLetterOrDigit) ? t : null;
                    // With the word in front, Parakeet may drop the end of a long order (it lost «Cierra la ventana de la
                    // papelera…» after three sentences): the order alone, when it has clearly more words.
                    var alone = Words(StripWake(whole));
                    var withWord = joinedRest is not null ? Words(joinedRest) : null;
                    if (withWord is not null && alone is not null && Count(alone) >= Count(withWord) + LostWords) withWord = alone;
                    var said = Words(afterPauseText) ?? (joinedRest is not null ? withWord : alone)
                               ?? (!provisional ? Words(match?.Rest) : null);
                    Log.Info($"Voz: para «{addressee.Name}»: sola «{whole}», tras la pausa «{afterPauseText}», con la palabra «{joinedRest}» → «{said}»");
                    return ([], said, confirmed);
                }
                var plans = new List<IntentPlan>();
                foreach (var option in options.Select(o => o.Trim()).Where(o => o.Any(char.IsLetterOrDigit)).Distinct())
                    plans.Add(await PlanAsync(option, commands).ConfigureAwait(false));
                return (plans, null, confirmed);
            }

            static int Count(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

            // The order alone may still begin with the end of the word («der, ¿qué hora es?» is not stripped, «Lander, …» is).
            string? StripWake(string? text) =>
                text is not null && WakeWordVerifier.Find(text, word) is { Start: <= 1 } begins ? begins.Rest : text;
        }
        catch (Exception ex)
        {
            Log.Error("Error tras la palabra de activación", ex);
        }
        finally
        {
            addressee?.Listening?.Invoke(false);
            capture.Stop();
            _abort = null;
            _active = 0;
            StateChanged?.Invoke();
        }
    }

    /// <summary>Tests only: <paramref name="audio"/> in real time as if from the microphone (in 30 ms pieces), then silence.</summary>
    private static void PlayFake(float[] audio, Action<float[]> samples, CancellationToken stop) => Task.Run(async () =>
    {
        var watch = Stopwatch.StartNew();
        var sent = 0L;
        while (!stop.IsCancellationRequested)
        {
            await Task.Delay(30, CancellationToken.None).ConfigureAwait(false);
            var due = watch.ElapsedMilliseconds * MicCapture.SampleRate / 1000 - sent;
            if (due <= 0) continue;
            var chunk = new float[due];
            for (var i = 0; i < chunk.Length; i++) chunk[i] = sent + i < audio.Length ? audio[sent + i] : 0;
            sent += due;
            samples(chunk);
        }
    }, CancellationToken.None);

    /// <summary>The order's audio from segment <paramref name="first"/> to the last, a little silence around it.</summary>
    private static float[] Span(float[] audio, List<(int Start, int Length)> segments, int first)
    {
        var pad = MicCapture.SampleRate / 5;
        var from = Math.Max(0, segments[first].Start - pad);
        var to = Math.Min(audio.Length, segments[^1].Start + segments[^1].Length + pad);
        return audio[from..to];
    }

    /// <summary>
    /// «I'm listening» (Settings › Voz): short, so it is over before the order starts; the echo canceller takes it out of
    /// the microphone like any other sound of the PC.
    /// </summary>
    private static void ListenSound(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.VoiceListenSound)) return;
        try { Sounds.Play(settings.VoiceListenSound); }
        catch (Exception ex) { Log.Warn($"No se pudo reproducir el sonido de escucha: {ex.Message}"); }
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

    /// <summary>Raised (any thread) for each order run by voice (statistics).</summary>
    public event Action<IntentCandidate>? OrderRun;

    /// <summary>The «Registro»: orders not understood and built-in media orders go there too (commands, by the engine).</summary>
    public ExecutionHistory? History { get; set; }

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
        Models.Dispose();
        _mic.Dispose();
        SpeechToText.Dispose();
        Semantic.Dispose();
        WakeWords.Dispose();
        lock (_featuresGate) _features?.Dispose();
    }
}

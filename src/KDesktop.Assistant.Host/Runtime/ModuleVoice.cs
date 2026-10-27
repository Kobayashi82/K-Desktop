using System.Text.Json.Nodes;
using KDesktop.Assistant.Host.Audio;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Voice;
using KDesktop.Core;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Assistant.Voice;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The voice of the assistants (stage 5.4): what each one answers is said aloud, sentence by sentence, while the AI still
/// writes it (<see cref="Ahead"/>), and the rest when the answer is final (<see cref="SayAsync"/>). One synthesis at a
/// time for the whole process (the processor), one sentence after another per assistant, through the host's mixer: its
/// level reaches the character (<c>OnVoiceLevel</c>) and it sounds from where it is. K-Desktop answering without an
/// assistant (null) speaks too since 5.0.111, with its own voice (Options → Assistant module → K-Desktop), from the middle.
/// </summary>
internal sealed class ModuleVoice : IDisposable
{
    /// <summary>How often the level of the voice reaches the character.</summary>
    private static readonly TimeSpan LevelEvery = TimeSpan.FromMilliseconds(33);

    private readonly EngineLoop _engine;
    private readonly AssistantRuntime _runtime;
    private readonly ModulePersonal _personal;
    private readonly Dictionary<string, Speaker> _speakers = new(StringComparer.Ordinal);
    /// <summary>The moods of the tests of Options (turn → moods), instead of the ones of the moment.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<string>> _testMoods = new(StringComparer.Ordinal);
    /// <summary>The profile being edited of a test (turn → profile), instead of the speaker's.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, VoiceProfile> _testProfiles = new(StringComparer.Ordinal);
    /// <summary>The engine a test of Options → Voz → Voces sounds with (turn → engine): the general voices, plain.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SpeechEngineKind> _testEngines = new(StringComparer.Ordinal);
    private volatile VoiceSettings _settings = new();
    private volatile string? _profile;
    private double _volume = 1;
    private volatile bool _pan = true, _night, _hushFullscreen, _hushed;
    private CancellationTokenSource? _test;

    public ModuleVoice(EngineLoop engine, AssistantRuntime runtime, ModulePersonal personal)
    {
        _engine = engine;
        _runtime = runtime;
        _personal = personal;
    }

    public Speech Speech { get; } = new();

    /// <summary>The voice profile an assistant brings (its package), or null (stage 5.5).</summary>
    public Func<string, VoiceProfile?>? ProfileOf { get; set; }

    /// <summary>
    /// How an assistant speaks now: Options' settings with what it has of its own on top (Options → Assistants → it,
    /// 5.0.105); the profile chosen for it, or else the one fixed for all of them, or else its package's (or «Natural»);
    /// and the moods it is in.
    /// </summary>
    public VoiceSettings SettingsFor(string assistant, IReadOnlyList<string> moods)
    {
        var settings = _settings;
        var own = _personal.Of(assistant);
        if (own is { HasOwnVoice: true })
            settings = settings with
            {
                Engine = Enum.TryParse<SpeechEngineKind>(own.Engine, true, out var engine) ? engine : settings.Engine,
                KokoroVoice = own.KokoroVoice.Length > 0 ? own.KokoroVoice : settings.KokoroVoice,
                PiperVoice = own.PiperVoice.Length > 0 ? own.PiperVoice : settings.PiperVoice,
                WindowsVoice = own.WindowsVoice.Length > 0 ? own.WindowsVoice : settings.WindowsVoice,
                Speed = own.Speed is { } speed ? Math.Clamp(speed, 0.5, 2) : settings.Speed,
            };
        var package = assistant.Length > 0 ? ProfileOf?.Invoke(assistant) : null;
        var profile = own?.Profile == Core.Assistant.AssistantPersonal.OwnProfile
            ? package ?? VoiceProfile.Default
            : VoiceProfiles.Find(own?.Profile is { Length: > 0 } chosen ? chosen : null) ?? VoiceProfiles.Find(_profile) ?? package ?? VoiceProfile.Default;
        return settings with { Profile = profile, Moods = moods };
    }

    /// <summary>The language <paramref name="assistant"/> speaks in (empty: the general one).</summary>
    private string LanguageOf(string assistant) => _personal.LanguageOf(assistant.Length > 0 ? assistant : null);

    /// <summary>Options → Assistant → Voice (<c>config</c> section <c>voice</c>).</summary>
    public void Configure(JsonObject? voice)
    {
        if (voice is null) return;
        if (voice["piperFolders"] is JsonArray folders) PiperModels.UseFolders(folders.Select(f => f?.GetValue<string>() ?? string.Empty));
        if (voice["kokoroFolders"] is JsonArray kokoro) KokoroModels.UseFolders(kokoro.Select(f => f?.GetValue<string>() ?? string.Empty));
        var engine = Enum.TryParse<SpeechEngineKind>(voice["engine"]?.GetValue<string>(), true, out var e) ? e : SpeechEngineKind.Auto;
        _settings = new VoiceSettings(engine, voice["kokoroVoice"]?.GetValue<string>(), voice["windowsVoice"]?.GetValue<string>(),
            voice["speed"]?.GetValue<double>() ?? 1, PiperVoice: voice["piperVoice"]?.GetValue<string>());
        _volume = Math.Clamp(voice["volume"]?.GetValue<double>() ?? 1, 0, 1);
        _profile = voice["profile"]?.GetValue<string>() is { Length: > 0 } fixedProfile ? fixedProfile : null;
        _pan = voice["pan"]?.GetValue<bool>() ?? true;
        _night = voice["night"]?.GetValue<bool>() ?? false;
        _hushFullscreen = voice["fullscreen"]?.GetValue<bool>() ?? false;
        Log.Info($"Asistente: voz {engine} (en uso: {Speech.Resolve(Loc.Instance.Language, _settings)}), velocidad {_settings.Speed:0.00}, volumen {_volume:0.00}"
            + $"{(_pan ? "" : ", sin panorama")}{(_night ? ", más bajo de noche" : "")}{(_hushFullscreen ? ", calla con pantalla completa" : "")}");
    }

    /// <summary>At night (22:00–8:00), with that option, half as loud.</summary>
    public static double NightFactor(DateTime now) => now.Hour is >= 22 or < 8 ? 0.5 : 1;

    /// <summary>How loud a voice plays now: Options' volume (or its own, 5.0.289), at night perhaps less.</summary>
    private double VolumeOf(string assistant) =>
        (_personal.Of(assistant)?.Volume is { } own ? Math.Clamp(own, 0, 1) : _volume) * (_night ? NightFactor(DateTime.Now) : 1);

    /// <summary>How loud its voice stays while you talk to it (K-Desktop listening): enough to know it was still talking.</summary>
    public const double ListeningGain = 0.2;

    /// <summary>Who K-Desktop is listening to now ("": the module without a body, that is, anyone).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _listening = new(StringComparer.Ordinal);

    /// <summary>K-Desktop listens to <paramref name="assistant"/> ("": to the module): while it does, its voice steps back.</summary>
    public void Listening(string assistant, bool active)
    {
        if (active) _listening[assistant] = true;
        else _listening.TryRemove(assistant, out _);
    }

    /// <summary>How loud the voice of <paramref name="assistant"/> plays now.</summary>
    private double GainFor(string assistant) =>
        VolumeOf(assistant) * (_listening.ContainsKey(assistant) || _listening.ContainsKey(string.Empty) ? ListeningGain : 1);

    /// <summary>
    /// Options → Voice → «Probar» (stage 5.6): <paramref name="say"/> says <paramref name="text"/> in a turn of its own,
    /// with <paramref name="mood"/> (none: plain, not even «far») instead of the moods of the moment, and even with something
    /// full screen. A new test or <see cref="StopTest"/> stops the one before.
    /// </summary>
    /// <param name="profile">A profile being edited (Options → Voice → Perfiles de voz, stage 9.4): the test sounds with it
    /// instead of the one of whoever says it.</param>
    /// <param name="engine">That engine with the general voices and language, plain (Options → Voz → Voces).</param>
    public void Test(string text, string? mood, Func<string, string, CancellationToken, Task> say, VoiceProfile? profile = null, string? engine = null)
    {
        var turn = "test-" + Guid.NewGuid().ToString("N")[..8];
        _testMoods[turn] = mood is { Length: > 0 } ? [mood] : [];
        if (profile is not null) _testProfiles[turn] = profile;
        if (Enum.TryParse<SpeechEngineKind>(engine, true, out var kind) && kind is SpeechEngineKind.Kokoro or SpeechEngineKind.Piper or SpeechEngineKind.Windows)
            _testEngines[turn] = kind;
        var stop = new CancellationTokenSource();
        Interlocked.Exchange(ref _test, stop)?.Cancel();
        Log.Info($"Asistente: prueba de voz{(mood is { Length: > 0 } ? $" ({mood})" : "")}: «{text}»");
        _ = say(turn, text, stop.Token).ContinueWith(_ =>
        {
            _testMoods.TryRemove(turn, out var _);
            _testProfiles.TryRemove(turn, out var _);
            _testEngines.TryRemove(turn, out var _);
            Interlocked.CompareExchange(ref _test, null, stop);
            stop.Dispose();
        }, TaskScheduler.Default);
    }

    /// <summary>Stops the test of Options that is sounding, if any.</summary>
    public void StopTest()
    {
        if (Interlocked.Exchange(ref _test, null) is not { } test) return;
        try { test.Cancel(); }
        catch (ObjectDisposedException) { return; }
        Log.Info("Asistente: prueba de voz parada");
    }

    private bool IsTest(string turn) => _testMoods.ContainsKey(turn.TrimEnd('+'));

    /// <summary>
    /// Whether a turn is said aloud: a voice chosen and the sound on, and not hushed by something full screen (tests are
    /// said anyway: they were asked for).
    /// </summary>
    private bool SpeaksNow(string turn)
    {
        if (!Speaks) return false;
        var hushed = _hushFullscreen && _runtime.World.System.FullscreenMonitors.Count > 0 && !IsTest(turn);
        if (hushed != _hushed)
        {
            _hushed = hushed;
            if (hushed) Log.Info("Asistente: hay algo a pantalla completa: solo el bocadillo");
        }
        return !hushed;
    }

    /// <summary>How the voice is, for Options → Assistant → Voice.</summary>
    public VoiceStateMessage State()
    {
        var language = Loc.Instance.Language;
        var engine = _runtime.Audio.Enabled ? Speech.Resolve(language, _settings) : SpeechEngineKind.Off;
        var others = Loc.Available().Select(l => l.Code).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(code => code, Speech.WindowsVoicesFor, StringComparer.OrdinalIgnoreCase);
        return new VoiceStateMessage(engine.ToString().ToLowerInvariant(), Speech.WindowsVoicesFor(language), Speech.Kokoro.IsLoaded, others, Speech.Piper.Loaded);
    }

    /// <summary>Whether answers are said aloud at all now (a voice chosen and the sound on).</summary>
    private bool Speaks => _settings.Engine != SpeechEngineKind.Off && _runtime.Audio.Enabled;

    /// <summary>The AI is thinking: Kokoro's model loads meanwhile, so the first sentence does not wait for it.</summary>
    public void Warm(string? assistant = null)
    {
        if (!Speaks) return;
        var who = assistant ?? string.Empty;
        Speech.Warm(LanguageOf(who), SettingsFor(who, []));
    }

    /// <summary>
    /// More of an answer being written (any thread): its finished sentences start being said. A new turn replaces the
    /// one before.
    /// </summary>
    public void Ahead(string? assistant, string turn, string textSoFar)
    {
        if (!SpeaksNow(turn)) return;
        var speaker = For(assistant ?? string.Empty, turn);
        foreach (var sentence in speaker.Splitter.Feed(textSoFar)) speaker.Queue(sentence);
    }

    /// <summary>
    /// A few words before a turn's answer («Un momento.»): queued on that turn's speaker without going through what the
    /// answer's text says, so it does not count as part of it.
    /// </summary>
    public void Preface(string? assistant, string turn, string phrase)
    {
        if (!SpeaksNow(turn)) return;
        Log.Info($"Asistente: «{(assistant is { Length: > 0 } ? assistant : "K-Desktop")}» piensa: «{phrase}»");
        For(assistant ?? string.Empty, turn).Queue(phrase);
    }

    /// <summary>
    /// The final answer: what was not said yet is said (all of it again if it is not what was being said). Completes when
    /// it has been said, or stopped (<paramref name="cancellationToken"/>, another turn).
    /// </summary>
    public Task SayAsync(string? assistant, string turn, string text, CancellationToken cancellationToken)
    {
        if (!SpeaksNow(turn)) return Task.CompletedTask;
        assistant ??= string.Empty;
        var speaker = For(assistant, turn);
        var rest = speaker.Splitter.Finish(text, out var diverged);
        if (diverged)
        {
            // Replaced (by the exact sentences of a tool, for example): what was going to be said goes; the final one, whole.
            speaker.Stop();
            speaker = For(assistant, turn + "+");
            rest = speaker.Splitter.Finish(text, out _);
        }
        Log.Info($"Asistente: respuesta final de «{(assistant.Length > 0 ? assistant : "K-Desktop")}»: {rest.Count} frase(s) por decir{(diverged ? " (cambió: se dice entera)" : "")}");
        foreach (var sentence in rest) speaker.Queue(sentence);
        speaker.Close();
        cancellationToken.Register(speaker.Stop);
        return speaker.Done;
    }

    /// <summary>Stops what an assistant is saying (it was cancelled, or it left).</summary>
    public void Stop(string? assistant)
    {
        Speaker? speaker;
        lock (_speakers) _speakers.Remove(assistant ?? string.Empty, out speaker);
        speaker?.Stop();
    }

    /// <summary>The speaker of that assistant for that turn (a new one, stopping the old one, for another turn).</summary>
    private Speaker For(string assistant, string turn)
    {
        Speaker? old = null;
        Speaker current;
        lock (_speakers)
        {
            if (_speakers.TryGetValue(assistant, out var known) && known.Turn == turn) return known;
            old = known;
            _speakers[assistant] = current = new Speaker(this, assistant, turn);
        }
        old?.Stop();
        return current;
    }

    public void Dispose()
    {
        lock (_speakers)
        {
            foreach (var speaker in _speakers.Values) speaker.Stop();
            _speakers.Clear();
        }
        Speech.Dispose();
    }

    /// <summary>One turn of one assistant: its sentences synthesized and played in order.</summary>
    private sealed class Speaker : IDisposable
    {
        private readonly ModuleVoice _owner;
        private readonly string _assistant;
        private readonly CancellationTokenSource _stop = new();
        private readonly System.Threading.Channels.Channel<string> _sentences = System.Threading.Channels.Channel.CreateUnbounded<string>();
        private readonly System.Threading.Channels.Channel<SpeechLine> _lines = System.Threading.Channels.Channel.CreateUnbounded<SpeechLine>();
        private SpeechLine? _playing;

        public Speaker(ModuleVoice owner, string assistant, string turn)
        {
            _owner = owner;
            _assistant = assistant;
            Turn = turn;
            // Piper says a lone «Recibido.» badly: it goes with the next sentence.
            Splitter.JoinShort = owner.Speech.Resolve(owner.LanguageOf(assistant), owner.SettingsFor(assistant, [])) == SpeechEngineKind.Piper;
            var token = _stop.Token;
            Done = Task.WhenAll(Task.Run(() => MakeAsync(token)), Task.Run(() => PlayAsync(token))).ContinueWith(_ =>
            {
                _owner._engine.Post(() => _owner._runtime.VoiceLevel(_assistant, 0));
                Dispose();
            }, TaskScheduler.Default);
        }

        public string Turn { get; }

        /// <summary>Its name for the log.</summary>
        private string Who => _assistant.Length > 0 ? _assistant : "K-Desktop";

        public SentenceSplitter Splitter { get; } = new();

        public Task Done { get; }

        public void Queue(string sentence) => _sentences.Writer.TryWrite(sentence);

        /// <summary>Nothing more will come: it ends after the last one.</summary>
        public void Close() => _sentences.Writer.TryComplete();

        public void Stop()
        {
            _sentences.Writer.TryComplete();
            try { _stop.Cancel(); } catch (ObjectDisposedException) { }
            _playing?.Stop();
            while (_lines.Reader.TryRead(out var waiting)) waiting.Drop();
        }

        /// <summary>Over: its token goes (a later <see cref="Stop"/> does nothing).</summary>
        public void Dispose() => _stop.Dispose();

        /// <summary>
        /// The sentences into audio, one after another: each one goes to be played as soon as its first piece is there,
        /// and the rest arrives while it plays (and the next one is made meanwhile).
        /// </summary>
        private async Task MakeAsync(CancellationToken token)
        {
            var language = _owner.LanguageOf(_assistant);
            try
            {
                await foreach (var sentence in _sentences.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    SpeechLine? line = null;
                    var settings = _owner.SettingsFor(_assistant, await MoodsAsync().ConfigureAwait(false));
                    if (_owner._testProfiles.TryGetValue(Turn.TrimEnd('+'), out var edited)) settings = settings with { Profile = edited };
                    var said = language;
                    // A test of one engine: the general voices and language, nothing of anyone's own, no effects.
                    if (_owner._testEngines.TryGetValue(Turn.TrimEnd('+'), out var engine))
                    {
                        settings = _owner._settings with { Engine = engine, Profile = VoiceProfile.Default, Moods = [] };
                        said = Loc.Instance.Language;
                    }
                    await foreach (var chunk in _owner.Speech.StreamAsync(sentence, said, settings, token).ConfigureAwait(false))
                    {
                        if (line is null)
                        {
                            line = new SpeechLine(chunk.SampleRate);
                            _lines.Writer.TryWrite(line);
                            Log.Info($"Asistente: «{Who}» empieza «{sentence}» ({chunk.Engine}, {settings.EffectiveProfile.Id}{(settings.Moods is { Count: > 0 } m ? " " + string.Join('+', m) : "")}, primer trozo en {watch.ElapsedMilliseconds} ms)");
                        }
                        line.Append(chunk.Samples);
                    }
                    line?.Complete();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Stopped.
            }
            catch (Exception ex)
            {
                Log.Error($"Asistente: «{Who}» no ha podido hablar", ex);
            }
            finally
            {
                _lines.Writer.TryComplete();
            }
        }

        /// <summary>The sentences through the mixer in order, from where the character is, with their level for the character.</summary>
        private async Task PlayAsync(CancellationToken token)
        {
            try
            {
                await foreach (var line in _lines.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    _playing = line;
                    _owner._runtime.Audio.Speak(line, _owner.GainFor(_assistant), _owner._pan ? await PanAsync().ConfigureAwait(false) : 0);
                    if (token.IsCancellationRequested) line.Stop();
                    while (!line.Ended.IsCompleted)
                    {
                        line.Gain = _owner.GainFor(_assistant);
                        var level = line.LevelAt(line.Position);
                        _owner._engine.Post(() => _owner._runtime.VoiceLevel(_assistant, level));
                        await Task.WhenAny(line.Ended, Task.Delay(LevelEvery, CancellationToken.None)).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Stopped.
            }
        }

        /// <summary>The moods of the next sentence (engine thread: where it is, what it set).</summary>
        private Task<IReadOnlyList<string>> MoodsAsync()
        {
            if (_owner._testMoods.TryGetValue(Turn.TrimEnd('+'), out var test)) return Task.FromResult(test);
            if (_assistant.Length == 0) return Task.FromResult<IReadOnlyList<string>>([]);
            var moods = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _owner._engine.Post(() => moods.TrySetResult(_owner._runtime.VoiceMoods(_assistant)));
            return moods.Task.WaitAsync(TimeSpan.FromMilliseconds(300)).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : [], TaskScheduler.Default);
        }

        private Task<double> PanAsync()
        {
            var pan = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
            _owner._engine.Post(() => pan.TrySetResult(_owner._runtime.PanOf(_assistant)));
            return pan.Task.WaitAsync(TimeSpan.FromMilliseconds(300)).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : 0, TaskScheduler.Default);
        }
    }
}

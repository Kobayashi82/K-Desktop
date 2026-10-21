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
/// level reaches the character (<c>OnVoiceLevel</c>) and it sounds from where it is. Without a body nothing is said aloud.
/// </summary>
internal sealed class ModuleVoice : IDisposable
{
    /// <summary>How often the level of the voice reaches the character.</summary>
    private static readonly TimeSpan LevelEvery = TimeSpan.FromMilliseconds(33);

    private readonly EngineLoop _engine;
    private readonly AssistantRuntime _runtime;
    private readonly Dictionary<string, Speaker> _speakers = new(StringComparer.Ordinal);
    private volatile VoiceSettings _settings = new();
    private volatile string? _profile;
    private double _volume = 1;

    public ModuleVoice(EngineLoop engine, AssistantRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public Speech Speech { get; } = new();

    /// <summary>The voice profile an assistant brings (its package), or null (stage 5.5).</summary>
    public Func<string, VoiceProfile?>? ProfileOf { get; set; }

    /// <summary>
    /// How an assistant speaks now: Options' settings with the profile fixed there for all of them, or else its own
    /// (or «Natural»), and the moods it is in.
    /// </summary>
    public VoiceSettings SettingsFor(string assistant, IReadOnlyList<string> moods)
    {
        var profile = VoiceProfile.Find(_profile) ?? (assistant.Length > 0 ? ProfileOf?.Invoke(assistant) : null) ?? VoiceProfile.Default;
        return _settings with { Profile = profile, Moods = moods };
    }

    /// <summary>Options → Assistant → Voice (<c>config</c> section <c>voice</c>).</summary>
    public void Configure(JsonObject? voice)
    {
        if (voice is null) return;
        var engine = Enum.TryParse<SpeechEngineKind>(voice["engine"]?.GetValue<string>(), true, out var e) ? e : SpeechEngineKind.Auto;
        _settings = new VoiceSettings(engine, voice["pocketVoice"]?.GetValue<string>(), voice["windowsVoice"]?.GetValue<string>(),
            voice["speed"]?.GetValue<double>() ?? 1);
        _volume = Math.Clamp(voice["volume"]?.GetValue<double>() ?? 1, 0, 1);
        _profile = voice["profile"]?.GetValue<string>() is { Length: > 0 } fixedProfile ? fixedProfile : null;
        Log.Info($"Asistente: voz {engine} (en uso: {Speech.Resolve(Loc.Instance.Language, _settings)}), velocidad {_settings.Speed:0.00}, volumen {_volume:0.00}");
    }

    /// <summary>How the voice is, for Options → Assistant → Voice.</summary>
    public VoiceStateMessage State()
    {
        var language = Loc.Instance.Language;
        var engine = _runtime.Audio.Enabled ? Speech.Resolve(language, _settings) : SpeechEngineKind.Off;
        return new VoiceStateMessage(engine.ToString().ToLowerInvariant(), Speech.WindowsVoicesFor(language), Speech.Pocket.IsLoaded);
    }

    /// <summary>Options → Voice → Test with nobody on the desktop: only the voice.</summary>
    public Task SayAloneAsync(string text) => SayAsync(string.Empty, Guid.NewGuid().ToString("N")[..12], text, CancellationToken.None);

    /// <summary>Whether answers are said aloud at all now (a voice chosen and the sound on).</summary>
    private bool Speaks => _settings.Engine != SpeechEngineKind.Off && _runtime.Audio.Enabled;

    /// <summary>The AI is thinking: Pocket's model loads meanwhile, so the first sentence does not wait for it.</summary>
    public void Warm()
    {
        if (Speaks) Speech.Warm(Loc.Instance.Language, _settings);
    }

    /// <summary>
    /// More of an answer being written (any thread): its finished sentences start being said. A new turn replaces the
    /// one before.
    /// </summary>
    public void Ahead(string? assistant, string turn, string textSoFar)
    {
        if (assistant is null || !Speaks) return;
        var speaker = For(assistant, turn);
        foreach (var sentence in speaker.Splitter.Feed(textSoFar)) speaker.Queue(sentence);
    }

    /// <summary>
    /// The final answer: what was not said yet is said (all of it again if it is not what was being said). Completes when
    /// it has been said, or stopped (<paramref name="cancellationToken"/>, another turn).
    /// </summary>
    public Task SayAsync(string? assistant, string turn, string text, CancellationToken cancellationToken)
    {
        if (assistant is null || !Speaks) return Task.CompletedTask;
        var speaker = For(assistant, turn);
        var rest = speaker.Splitter.Finish(text, out var diverged);
        if (diverged)
        {
            // Replaced (by the exact sentences of a tool, for example): what was going to be said goes; the final one, whole.
            speaker.Stop();
            speaker = For(assistant, turn + "+");
            rest = speaker.Splitter.Finish(text, out _);
        }
        Log.Info($"Asistente: respuesta final de «{assistant}»: {rest.Count} frase(s) por decir{(diverged ? " (cambió: se dice entera)" : "")}");
        foreach (var sentence in rest) speaker.Queue(sentence);
        speaker.Close();
        cancellationToken.Register(speaker.Stop);
        return speaker.Done;
    }

    /// <summary>Stops what an assistant is saying (it was cancelled, or it left).</summary>
    public void Stop(string? assistant)
    {
        if (assistant is null) return;
        Speaker? speaker;
        lock (_speakers) _speakers.Remove(assistant, out speaker);
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
            var token = _stop.Token;
            Done = Task.WhenAll(Task.Run(() => MakeAsync(token)), Task.Run(() => PlayAsync(token))).ContinueWith(_ =>
            {
                _owner._engine.Post(() => _owner._runtime.VoiceLevel(_assistant, 0));
                Dispose();
            }, TaskScheduler.Default);
        }

        public string Turn { get; }

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
            var language = Loc.Instance.Language;
            try
            {
                await foreach (var sentence in _sentences.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    SpeechLine? line = null;
                    var settings = _owner.SettingsFor(_assistant, await MoodsAsync().ConfigureAwait(false));
                    await foreach (var chunk in _owner.Speech.StreamAsync(sentence, language, settings, token).ConfigureAwait(false))
                    {
                        if (line is null)
                        {
                            line = new SpeechLine(chunk.SampleRate);
                            _lines.Writer.TryWrite(line);
                            Log.Info($"Asistente: «{_assistant}» empieza «{sentence}» ({chunk.Engine}, {settings.EffectiveProfile.Id}{(settings.Moods is { Count: > 0 } m ? " " + string.Join('+', m) : "")}, primer trozo en {watch.ElapsedMilliseconds} ms)");
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
                Log.Error($"Asistente: «{_assistant}» no ha podido hablar", ex);
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
                    _owner._runtime.Audio.Speak(line, _owner._volume, await PanAsync().ConfigureAwait(false));
                    if (token.IsCancellationRequested) line.Stop();
                    while (!line.Ended.IsCompleted)
                    {
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

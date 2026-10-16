using System.Diagnostics;
using System.Threading.Channels;
using KDesktop.Core.Model;

namespace KDesktop.Core.Voice;

/// <summary>
/// Always-on wake word detection (phase 1): while there is an active wake word with a trained detector and the option
/// is on, the microphone stays open; every 80 ms one openWakeWord embedding is computed and each detector scores the
/// last ones. When one fires, it keeps recording until the speaker goes quiet and hands the audio (with the 2 s before
/// the trigger) to <see cref="VoiceService.HandleWakeAsync"/> for phase 2. The work runs on its own thread so the
/// capture never waits.
/// </summary>
public sealed class WakeWordListener : IDisposable
{
    private const int Preroll = MicCapture.SampleRate * 2;
    private const int KeepEmbeddings = 40;
    private static readonly TimeSpan PauseAfterTrigger = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan NothingAfter = TimeSpan.FromMilliseconds(900), MaxAfter = TimeSpan.FromSeconds(8);

    private readonly VoiceService _voice;
    private readonly Func<AppSettings> _settings;
    private readonly MicCapture _mic = new();
    private readonly object _gate = new();
    private readonly float[] _ring = new float[Preroll];
    private int _ringPos;
    private bool _ringFull;
    private readonly List<float[]> _recent = [];
    private Channel<float[]>? _queue;
    private Task? _worker;
    private OwwStream? _stream;
    private DateTime _quietUntil;
    private DateTime _lastNearMiss;
    private int _training;
    private volatile bool _clearRecent;

    // After a trigger: the audio until the speaker goes quiet.
    private List<float>? _after;
    private VoiceActivity? _afterVad;

    public WakeWordListener(VoiceService voice, Func<AppSettings> settings)
    {
        _voice = voice;
        _settings = settings;
        _voice.WakeWords.Changed += Refresh;
    }

    public bool IsRunning { get; private set; }

    /// <summary>Raised (any thread) when the listening starts or stops.</summary>
    public event Action? StateChanged;

    /// <summary>
    /// Starts or stops listening to match the settings, the models and the words (call after any of them changes).
    /// Words without a detector yet are trained first, in the background.
    /// </summary>
    public void Refresh()
    {
        try
        {
            var s = _settings();
            var models = VoiceModels.AllInstalled;
            var words = _voice.WakeWords.Active.ToList();
            if (models && words.Any(w => w.Detector is null)) TrainMissing(words);
            var wanted = s.VoiceWakeListen && !s.Paused && models && words.Any(w => w.Detector is not null);
            lock (_gate)
            {
                if (wanted && IsRunning && _micId != s.VoiceMicId) StopLocked(); // another microphone chosen
                if (wanted && !IsRunning) StartLocked(s.VoiceMicId);
                else if (!wanted && IsRunning) StopLocked();
            }
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo poner en marcha la escucha de la palabra de activación", ex);
            lock (_gate) StopLocked();
        }
        StateChanged?.Invoke();
    }

    private void TrainMissing(IEnumerable<WakeWord> words)
    {
        if (Interlocked.Exchange(ref _training, 1) != 0) return;
        var pending = words.Where(w => w.Detector is null).ToList();
        Task.Run(() =>
        {
            try
            {
                foreach (var word in pending) _voice.WakeWords.Train(word, _voice.Features);
            }
            catch (Exception ex)
            {
                Log.Error("Error entrenando el detector de la palabra de activación", ex);
            }
            finally
            {
                _training = 0;
            }
            // Saving a detector raises Changed, which calls Refresh again.
        });
    }

    private string? _micId;

    private void StartLocked(string? micId)
    {
        _stream = new OwwStream(_voice.Features);
        _recent.Clear();
        _ringPos = 0;
        _ringFull = false;
        _queue = Channel.CreateBounded<float[]>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        var queue = _queue;
        _worker = Task.Factory.StartNew(() => Work(queue), TaskCreationOptions.LongRunning);
        if (Environment.GetEnvironmentVariable("KDESKTOP_VOICE_FAKE_STREAM") is { Length: > 0 } fake) StartFake(File.Exists(fake) ? fake : null);
        else if (Environment.GetEnvironmentVariable("KDESKTOP_VOICE_FAKE") is { Length: > 0 }) StartFake(null); // tests: never the real microphone
        else
        {
            _mic.Samples += OnSamples;
            _mic.Start(micId);
        }
        _micId = micId;
        IsRunning = true;
        Log.Info("Escuchando las palabras de activación: " + string.Join(", ", _voice.WakeWords.Active.Where(w => w.Detector is not null).Select(w => $"«{w.Name}» ({w.DetectorThreshold:0.00})")));
    }

    private CancellationTokenSource? _fake;

    /// <summary>
    /// Tests only (<c>KDESKTOP_VOICE_FAKE_STREAM</c> = a .wav, or <c>KDESKTOP_VOICE_FAKE</c> set): plays that file in real
    /// time instead of the microphone, after a second of silence, then silence (only silence without a file).
    /// </summary>
    private void StartFake(string? path)
    {
        var audio = path is null ? [] : VoiceDiagnostics.ReadWav(path);
        var cts = _fake = new CancellationTokenSource();
        Log.Info("Escucha simulada con " + (path ?? "silencio"));
        Task.Run(async () =>
        {
            var position = -MicCapture.SampleRate;
            var watch = Stopwatch.StartNew();
            var sent = 0L;
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(30).ConfigureAwait(false);
                var due = watch.ElapsedMilliseconds * MicCapture.SampleRate / 1000 - sent;
                if (due <= 0) continue;
                var chunk = new float[due];
                for (var i = 0; i < chunk.Length; i++, position++)
                    chunk[i] = position >= 0 && position < audio.Length ? audio[position] : 0;
                sent += due;
                OnSamples(chunk);
            }
        });
    }

    private void StopLocked()
    {
        if (!IsRunning) return;
        _fake?.Cancel();
        _fake = null;
        _mic.Samples -= OnSamples;
        _mic.Stop();
        _queue?.Writer.TryComplete();
        _queue = null;
        IsRunning = false;
        Log.Info("Escucha de las palabras de activación detenida");
    }

    private void OnSamples(float[] samples) => _queue?.Writer.TryWrite(samples);

    private void Work(Channel<float[]> queue)
    {
        var reader = queue.Reader;
        while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            while (reader.TryRead(out var samples))
            {
                try { Process(samples); }
                catch (Exception ex)
                {
                    Log.Error("Error en la escucha de la palabra de activación", ex);
                    Thread.Sleep(1000);
                }
            }
    }

    private void Process(float[] samples)
    {
        // Last 2 s of audio, for phase 2.
        foreach (var s in samples)
        {
            _ring[_ringPos++] = s;
            if (_ringPos == _ring.Length)
            {
                _ringPos = 0;
                _ringFull = true;
            }
        }
        if (_after is { } after)
            lock (after)
                if (ReferenceEquals(_after, after))
                {
                    after.AddRange(samples);
                    _afterVad!.Accept(samples);
                }
        if (_clearRecent)
        {
            _clearRecent = false;
            _recent.Clear();
        }

        var embeddings = _stream!.Accept(samples);
        foreach (var e in embeddings)
        {
            _recent.Add(e);
            if (_recent.Count > KeepEmbeddings) _recent.RemoveAt(0);
        }
        if (embeddings.Count == 0 || _after is not null || _voice.IsListening || DateTime.Now < _quietUntil) return;

        foreach (var word in _voice.WakeWords.Active)
        {
            if (word.Detector is not { } detector || _recent.Count < WakeDetector.Frames) continue;
            var (score, dtw, cls) = detector.Score(_recent);
            var threshold = word.DetectorThreshold;
            if (score >= threshold)
            {
                Log.Info($"Palabra de activación «{word.Name}» detectada: {score:0.00} (DTW {dtw:0.00}, clasificador {cls:0.00}; umbral {threshold:0.00})");
                Trigger(word, score);
                return;
            }
            // Close calls, at most every 2 s, so a word that is not caught can be tuned (sensitivity).
            if (score >= threshold - 0.15 && DateTime.Now - _lastNearMiss > TimeSpan.FromSeconds(2))
            {
                _lastNearMiss = DateTime.Now;
                Log.Info($"Casi «{word.Name}»: {score:0.00} (DTW {dtw:0.00}, clasificador {cls:0.00}; umbral {threshold:0.00})");
            }
        }
    }

    /// <summary>Keeps recording until the speaker goes quiet, then hands everything to phase 2.</summary>
    private void Trigger(WakeWord word, double score)
    {
        var preroll = new float[_ringFull ? _ring.Length : _ringPos];
        if (_ringFull)
        {
            Array.Copy(_ring, _ringPos, preroll, 0, _ring.Length - _ringPos);
            Array.Copy(_ring, 0, preroll, _ring.Length - _ringPos, _ringPos);
        }
        else Array.Copy(_ring, preroll, _ringPos);

        // Parakeet loads (if it was freed) while the rest of the order is still being said.
        _voice.SpeechToText.Preload();
        var after = new List<float>(MicCapture.SampleRate * 4);
        var vad = new VoiceActivity(TimeSpan.FromMilliseconds(700));
        _after = after;
        _afterVad = vad;
        _quietUntil = DateTime.MaxValue;

        Task.Run(async () =>
        {
            var watch = Stopwatch.StartNew();
            try
            {
                // «KDesktop» alone: a short pause ends it. «KDesktop, abre Firefox»: until the order ends.
                while (watch.Elapsed < MaxAfter)
                {
                    await Task.Delay(50).ConfigureAwait(false);
                    bool heard, ended;
                    lock (after) (heard, ended) = (vad.HeardSpeech, vad.Ended);
                    if (ended || (!heard && watch.Elapsed > NothingAfter)) break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error tras detectar la palabra de activación", ex);
            }
            float[] audio;
            lock (after)
            {
                _after = null;
                audio = new float[preroll.Length + after.Count];
                preroll.CopyTo(audio, 0);
                after.CopyTo(audio, preroll.Length);
                vad.Dispose();
            }
            _clearRecent = true;
            _quietUntil = DateTime.Now + PauseAfterTrigger;
            await _voice.HandleWakeAsync(audio, word, score).ConfigureAwait(false);
            _quietUntil = DateTime.Now + TimeSpan.FromSeconds(1);
        });
    }

    public void Dispose()
    {
        _voice.WakeWords.Changed -= Refresh;
        lock (_gate) StopLocked();
        _mic.Dispose();
    }
}

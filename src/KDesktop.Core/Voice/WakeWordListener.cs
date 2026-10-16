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

    /// <summary>After a trigger: everything heard since, until the order is over.</summary>
    private WakeCapture? _capture;

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
                // Another microphone chosen, or the echo cancellation switched: start again with it.
                if (wanted && IsRunning && (_micId != s.VoiceMicId || _echoCancel != s.VoiceEchoCancel)) StopLocked();
                if (wanted && !IsRunning) StartLocked(s.VoiceMicId, s.VoiceEchoCancel);
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
    private bool _echoCancel;

    private void StartLocked(string? micId, bool echoCancel)
    {
        _stream = new OwwStream(_voice.Features);
        _recent.Clear();
        _ringPos = 0;
        _ringFull = false;
        _queue = Channel.CreateBounded<float[]>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        var queue = _queue;
        _worker = Task.Factory.StartNew(() => Work(queue), TaskCreationOptions.LongRunning);
        if (Environment.GetEnvironmentVariable("KDESKTOP_VOICE_FAKE_STREAM") is { Length: > 0 } fake) StartFake(File.Exists(fake) ? fake : null, echoCancel);
        else if (Environment.GetEnvironmentVariable("KDESKTOP_VOICE_FAKE") is { Length: > 0 }) StartFake(null, false); // tests: never the real microphone
        else
        {
            _mic.Samples += OnSamples;
            _mic.EchoCancel = echoCancel;
            _mic.Start(micId);
        }
        _micId = micId;
        _echoCancel = echoCancel;
        IsRunning = true;
        Log.Info("Escuchando las palabras de activación: " + string.Join(", ", _voice.WakeWords.Active.Where(w => w.Detector is not null).Select(w => $"«{w.Name}» ({w.DetectorThreshold:0.00})")));
    }

    private CancellationTokenSource? _fake;

    /// <summary>
    /// Tests only (<c>KDESKTOP_VOICE_FAKE_STREAM</c> = a .wav, or <c>KDESKTOP_VOICE_FAKE</c> set): plays that file in real
    /// time instead of the microphone, after a second of silence, then silence (only silence without a file). With
    /// <c>KDESKTOP_VOICE_FAKE_RENDER</c> = a .wav of what "the PC played" meanwhile (in step with the first one), the
    /// echo canceller takes it out as it would with the speakers.
    /// </summary>
    private void StartFake(string? path, bool echoCancel)
    {
        var audio = path is null ? [] : VoiceDiagnostics.ReadWav(path);
        var cts = _fake = new CancellationTokenSource();
        EchoStage? echo = null;
        if (echoCancel && Environment.GetEnvironmentVariable("KDESKTOP_VOICE_FAKE_RENDER") is { Length: > 0 } render && File.Exists(render) &&
            EchoCanceller.TryCreate() is { } canceller)
            echo = new EchoStage(canceller, new FileReference(VoiceDiagnostics.ReadWav(render)));
        var cleaned = new List<float>();
        Log.Info("Escucha simulada con " + (path ?? "silencio") + (echo is not null ? " y cancelación de eco" : ""));
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
                var start = position;
                for (var i = 0; i < chunk.Length; i++, position++)
                    chunk[i] = position >= 0 && position < audio.Length ? audio[position] : 0;
                sent += due;
                if (echo is null)
                {
                    OnSamples(chunk);
                    continue;
                }
                cleaned.Clear();
                echo.Process(chunk, start, start, cleaned);
                if (cleaned.Count > 0) OnSamples(cleaned.ToArray());
            }
            echo?.Dispose();
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
        _capture?.Add(samples);
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
        if (embeddings.Count == 0 || _capture is not null || _voice.IsListening || DateTime.Now < _quietUntil) return;

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

    /// <summary>
    /// Keeps recording, without a gap, into a <see cref="WakeCapture"/> and hands it to phase 2, which checks the word
    /// and then waits for the order (however long the pause after the word).
    /// </summary>
    private void Trigger(WakeWord word, double score)
    {
        var preroll = new float[_ringFull ? _ring.Length : _ringPos];
        if (_ringFull)
        {
            Array.Copy(_ring, _ringPos, preroll, 0, _ring.Length - _ringPos);
            Array.Copy(_ring, 0, preroll, _ring.Length - _ringPos, _ringPos);
        }
        else Array.Copy(_ring, preroll, _ringPos);

        // Parakeet loads (if it was freed) while the user goes on talking.
        _voice.SpeechToText.Preload();
        var capture = new WakeCapture(preroll, TimeSpan.FromMilliseconds(800));
        _capture = capture;
        _quietUntil = DateTime.MaxValue;

        Task.Run(async () =>
        {
            try
            {
                await _voice.HandleWakeAsync(word, score, capture).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error("Error tras detectar la palabra de activación", ex);
            }
            finally
            {
                _capture = null;
                capture.Dispose();
                _clearRecent = true;
                _quietUntil = DateTime.Now + TimeSpan.FromSeconds(1);
            }
        });
    }

    public void Dispose()
    {
        _voice.WakeWords.Changed -= Refresh;
        lock (_gate) StopLocked();
        _mic.Dispose();
    }
}

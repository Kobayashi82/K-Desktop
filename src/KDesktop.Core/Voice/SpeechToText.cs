using System.Diagnostics;
using SherpaOnnx;

namespace KDesktop.Core.Voice;

/// <summary>
/// Speech to text with Parakeet (sherpa-onnx offline recognizer). The model (~670 MB) is loaded on first use, or
/// ahead with <see cref="Preload"/>, and released after <see cref="IdleUnload"/> without use so an idle K-Desktop
/// stays small.
/// </summary>
public sealed class SpeechToText : IDisposable
{
    private readonly object _gate = new();
    private readonly Timer _unloadTimer;
    private OfflineRecognizer? _recognizer;

    public SpeechToText() => _unloadTimer = new Timer(_ => { if (!_pinned) Unload(); }, null, Timeout.Infinite, Timeout.Infinite);

    /// <summary>Time without transcribing after which the model leaves memory.</summary>
    public TimeSpan IdleUnload { get; set; } = TimeSpan.FromMinutes(10);

    private volatile bool _pinned;

    /// <summary>
    /// Kept in memory whatever <see cref="IdleUnload"/> says (5.0.200: while the wake words are listened for, so a word is
    /// checked at once and nothing shows before it is). Unpinned, the usual countdown starts again.
    /// </summary>
    public bool Pinned
    {
        get => _pinned;
        set
        {
            if (_pinned == value) return;
            _pinned = value;
            if (value) Preload();
            else if (IsLoaded) _unloadTimer.Change(IdleUnload, Timeout.InfiniteTimeSpan);
        }
    }

    public bool IsLoaded => _recognizer is not null;

    /// <summary>Loads the model in the background (e.g. as soon as the user starts speaking).</summary>
    public void Preload() => Task.Run(() =>
    {
        try { lock (_gate) EnsureLoaded(); }
        catch (Exception ex) { Log.Error("No se pudo cargar el modelo de voz", ex); }
    });

    /// <summary>Transcribes 16 kHz mono samples. Thread-safe; blocks while the model loads.</summary>
    public string Transcribe(float[] samples)
    {
        lock (_gate)
        {
            var recognizer = EnsureLoaded();
            var watch = Stopwatch.StartNew();
            using var stream = recognizer.CreateStream();
            // A little silence after the speech helps the last word come out.
            stream.AcceptWaveform(MicCapture.SampleRate, samples);
            stream.AcceptWaveform(MicCapture.SampleRate, new float[MicCapture.SampleRate / 3]);
            recognizer.Decode(stream);
            var text = stream.Result.Text.Trim();
            Log.Info($"Voz: «{text}» ({samples.Length / (double)MicCapture.SampleRate:0.0} s de audio, {watch.ElapsedMilliseconds} ms)");
            _unloadTimer.Change(IdleUnload, Timeout.InfiniteTimeSpan);
            return text;
        }
    }

    private OfflineRecognizer EnsureLoaded()
    {
        if (_recognizer is { } loaded) return loaded;
        if (!VoiceModels.HasSpeechToText) throw new FileNotFoundException("Faltan los archivos del modelo Parakeet", VoiceModels.ParakeetDir);
        var watch = Stopwatch.StartNew();
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = MicCapture.SampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = VoiceModels.ParakeetEncoder;
        config.ModelConfig.Transducer.Decoder = VoiceModels.ParakeetDecoder;
        config.ModelConfig.Transducer.Joiner = VoiceModels.ParakeetJoiner;
        config.ModelConfig.Tokens = VoiceModels.ParakeetTokens;
        config.ModelConfig.ModelType = "nemo_transducer";
        config.ModelConfig.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";
        _recognizer = new OfflineRecognizer(config);
        Log.Info($"Modelo de voz cargado en {watch.ElapsedMilliseconds} ms");
        _unloadTimer.Change(IdleUnload, Timeout.InfiniteTimeSpan);
        return _recognizer;
    }

    /// <summary>Frees the model (it loads again on the next use).</summary>
    public void Unload()
    {
        lock (_gate)
        {
            if (_recognizer is null) return;
            _recognizer.Dispose();
            _recognizer = null;
            Log.Info("Modelo de voz descargado de la memoria (sin uso)");
        }
        GC.Collect(2, GCCollectionMode.Optimized, false);
    }

    public void Dispose()
    {
        _unloadTimer.Dispose();
        Unload();
    }
}

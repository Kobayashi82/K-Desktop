using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Model;
using NAudio.Wave;

namespace KDesktop.Core.Voice;

/// <summary>
/// One wake word recorded with the user's voice. Lives in <c>Voice\WakeWords\&lt;id&gt;\</c> of the data folder:
/// <c>wakeword.json</c> (this) plus the recordings (<c>word_1.wav</c>…, <c>silence.wav</c>, <c>speech.wav</c>), which
/// the always-on detector (stage 4) learns from.
/// </summary>
public partial class WakeWord : ObservableObject
{
    public string Id { get; set; } = Ids.New();

    /// <summary>What the user calls it («KDesktop», «Oye ordenador»).</summary>
    [ObservableProperty] private string _name = string.Empty;

    [ObservableProperty] private bool _enabled = true;

    /// <summary>0 (strict, fewer false alarms) … 1 (lenient, catches it more easily). Moves <see cref="Threshold"/>.</summary>
    [ObservableProperty] private double _sensitivity = 0.5;

    /// <summary>How Parakeet wrote the word in each recording (distinct), the reference for the verification.</summary>
    public List<string> Variants { get; set; } = [];

    /// <summary>Verification score needed at the middle sensitivity, calibrated with the user's normal speech.</summary>
    public double Threshold { get; set; } = WakeWordVerifier.DefaultThreshold;

    /// <summary>Best score the normal speech reached (how close ordinary talk gets to the word).</summary>
    public double NegativeScore { get; set; }

    /// <summary>Background noise level (RMS) measured during the silence step.</summary>
    public double NoiseLevel { get; set; }

    /// <summary>Recordings of the word kept for the detector.</summary>
    public List<string> Recordings { get; set; } = [];

    public DateTime Created { get; set; } = DateTime.Now;
    public string? Microphone { get; set; }

    /// <summary>Threshold actually used, after the sensitivity.</summary>
    [JsonIgnore] public double EffectiveThreshold => Math.Clamp(Threshold + (0.5 - Sensitivity) * 0.3, 0.5, 0.97);

    /// <summary>Keys of everything this word may sound like: its variants and its own name.</summary>
    [JsonIgnore] public IReadOnlyList<string> Keys =>
        Variants.Append(Name).Select(PhoneticKey.Of).Where(k => k.Length > 0).Distinct().ToList();

    [JsonIgnore] public string Folder => Path.Combine(WakeWordStore.Root, Id);

    /// <summary>The always-on detector learnt from the recordings (null until trained).</summary>
    [JsonIgnore] public WakeDetector? Detector { get; set; }

    /// <summary>Phase-1 score needed, after the sensitivity.</summary>
    [JsonIgnore] public double DetectorThreshold => Detector?.Threshold(Sensitivity) ?? 1;

    /// <summary>One line for the list in Settings: recordings, how Parakeet wrote it, threshold in use.</summary>
    [JsonIgnore] public string Summary => Localization.Loc.T("settings.wakeInfo", Recordings.Count,
        string.Join(", ", Variants.Take(3).Select(v => $"«{v}»")) + (Variants.Count > 3 ? "…" : ""),
        EffectiveThreshold.ToString("0.00", Localization.Loc.Instance.Culture));

    partial void OnSensitivityChanged(double value)
    {
        OnPropertyChanged(nameof(EffectiveThreshold));
        OnPropertyChanged(nameof(DetectorThreshold));
        OnPropertyChanged(nameof(Summary));
    }
}

/// <summary>The wake words on disk. Changes made through the UI (name, on/off, sensitivity) are saved by themselves.</summary>
public sealed class WakeWordStore : IDisposable
{
    public static string Root => Path.Combine(AppPaths.DataDir, "Voice", "WakeWords");
    private const string FileName = "wakeword.json";

    private readonly Timer _saveTimer;
    private readonly HashSet<WakeWord> _dirty = [];

    public WakeWordStore()
    {
        _saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        ReloadWords();
    }

    /// <summary>Lives on the UI thread's side: change it only from there.</summary>
    public ObservableCollection<WakeWord> Words { get; } = [];

    /// <summary>The ones in use (not the <see cref="Asleep"/> ones).</summary>
    public IEnumerable<WakeWord> Active => Words.Where(w => w.Enabled && w.Variants.Count > 0 && !_asleep.Contains(w.Id));

    private volatile IReadOnlySet<string> _asleep = new HashSet<string>();

    /// <summary>
    /// Words switched on but not listened to now: an assistant's own word while it is not on the desktop. Set it (any
    /// thread) when that changes; listening starts again with the new ones.
    /// </summary>
    public IReadOnlySet<string> Asleep
    {
        get => _asleep;
        set
        {
            if (_asleep.SetEquals(value)) return;
            _asleep = value;
            RaiseChanged();
        }
    }

    /// <summary>Raised (any thread) when words are added, removed, switched on/off or (re)trained.</summary>
    public event Action? Changed;

    private void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception ex) { Log.Error("Error avisando de un cambio en las palabras de activación", ex); }
    }

    // ── detector ─────────────────────────────────────────────────────────

    private const string DetectorFile = "detector.json";

    private static string DetectorPath(WakeWord word) => Path.Combine(word.Folder, DetectorFile);

    /// <summary>Reads the saved detector of <paramref name="word"/> (null if missing, broken or from an older version).</summary>
    private static WakeDetector? LoadDetector(WakeWord word)
    {
        try
        {
            var path = DetectorPath(word);
            if (!File.Exists(path)) return null;
            var detector = Json.Deserialize<WakeDetector>(File.ReadAllText(path));
            return detector is { Version: WakeDetector.CurrentVersion, Templates.Count: > 0 } ? detector : null;
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo leer el detector de «{word.Name}»: {ex.Message}");
            return null;
        }
    }

    /// <summary>The recordings of a word: the takes, the silence and the normal speech (those that exist).</summary>
    public static (List<float[]> Takes, float[]? Silence, float[]? Speech) ReadRecordings(WakeWord word)
    {
        float[]? Read(string name)
        {
            var path = Path.Combine(word.Folder, name);
            return File.Exists(path) ? VoiceDiagnostics.ReadWav(path) : null;
        }
        var takes = word.Recordings.Select(Read).OfType<float[]>().ToList();
        return (takes, Read("silence.wav"), Read("speech.wav"));
    }

    /// <summary>Trains the detector of <paramref name="word"/> from its recordings and saves it (slow: call off the UI thread).</summary>
    public WakeDetector.Report? Train(WakeWord word, OwwFeatures features)
    {
        var (takes, silence, speech) = ReadRecordings(word);
        if (takes.Count == 0) return null;
        var (detector, report) = WakeDetector.Train(features, takes, silence, speech);
        Save(word, detector);
        Log.Info($"Detector de «{word.Name}» entrenado en {report.Milliseconds} ms: {report.Positives} positivos, {report.Negatives} negativos; " +
                 $"tomas ≥ {report.PositiveScore:0.00}, habla normal ≤ {report.NegativeScore:0.00} → umbral {detector.Threshold(word.Sensitivity):0.00}");
        return report;
    }

    /// <summary>Stores an already trained detector (the set-up trains it while you try the word).</summary>
    public void Save(WakeWord word, WakeDetector detector)
    {
        word.Detector = detector;
        try
        {
            Directory.CreateDirectory(word.Folder);
            File.WriteAllText(DetectorPath(word), Json.Serialize(detector));
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo guardar el detector de la palabra de activación", ex);
        }
        RaiseChanged();
    }

    /// <summary>Reads every wake word again (after an import, for example).</summary>
    public void Reload()
    {
        ReloadWords();
        RaiseChanged();
    }

    private void ReloadWords()
    {
        Flush();
        foreach (var w in Words) w.PropertyChanged -= OnWordChanged;
        Words.Clear();
        if (!Directory.Exists(Root)) return;
        foreach (var dir in Directory.GetDirectories(Root).Order())
        {
            var file = Path.Combine(dir, FileName);
            if (!File.Exists(file)) continue;
            try
            {
                if (Json.Deserialize<WakeWord>(File.ReadAllText(file)) is not { } word) continue;
                word.Id = Path.GetFileName(dir);
                word.Detector = LoadDetector(word);
                Add(word);
            }
            catch (Exception ex)
            {
                Log.Error($"No se pudo leer la palabra de activación {dir}", ex);
            }
        }
    }

    private void Add(WakeWord word)
    {
        word.PropertyChanged += OnWordChanged;
        Words.Add(word);
    }

    /// <summary>Saves a new or re-recorded word (its folder must already hold the recordings).</summary>
    public void Save(WakeWord word)
    {
        Write(word);
        var existing = Words.FirstOrDefault(w => w.Id == word.Id);
        if (existing is null) Add(word);
        else
        {
            existing.PropertyChanged -= OnWordChanged;
            Words[Words.IndexOf(existing)] = word;
            word.PropertyChanged += OnWordChanged;
        }
        RaiseChanged();
    }

    public void Delete(WakeWord word)
    {
        word.PropertyChanged -= OnWordChanged;
        Words.Remove(word);
        lock (_dirty) _dirty.Remove(word);
        try
        {
            if (Directory.Exists(word.Folder)) Directory.Delete(word.Folder, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo borrar la palabra de activación", ex);
        }
        RaiseChanged();
    }

    private void OnWordChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not WakeWord word || e.PropertyName is nameof(WakeWord.EffectiveThreshold) or nameof(WakeWord.DetectorThreshold) or nameof(WakeWord.Summary)) return;
        lock (_dirty) _dirty.Add(word);
        _saveTimer.Change(500, Timeout.Infinite);
        if (e.PropertyName == nameof(WakeWord.Enabled)) RaiseChanged();
    }

    public void Flush()
    {
        WakeWord[] words;
        lock (_dirty)
        {
            words = [.. _dirty];
            _dirty.Clear();
        }
        foreach (var word in words) Write(word);
    }

    private static void Write(WakeWord word)
    {
        try
        {
            Directory.CreateDirectory(word.Folder);
            File.WriteAllText(Path.Combine(word.Folder, FileName), Json.Serialize(word));
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo guardar la palabra de activación", ex);
        }
    }

    /// <summary>Writes 16 kHz mono samples as a 16-bit .wav.</summary>
    public static void WriteWav(string path, float[] samples)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new WaveFileWriter(path, new WaveFormat(MicCapture.SampleRate, 16, 1));
        writer.WriteSamples(samples, 0, samples.Length);
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Flush();
    }
}

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

    /// <summary>One line for the list in Settings: recordings, how Parakeet wrote it, threshold in use.</summary>
    [JsonIgnore] public string Summary => Localization.Loc.T("settings.wakeInfo", Recordings.Count,
        string.Join(", ", Variants.Take(3).Select(v => $"«{v}»")) + (Variants.Count > 3 ? "…" : ""),
        EffectiveThreshold.ToString("0.00", Localization.Loc.Instance.Culture));

    partial void OnSensitivityChanged(double value)
    {
        OnPropertyChanged(nameof(EffectiveThreshold));
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
        Reload();
    }

    /// <summary>Lives on the UI thread's side: change it only from there.</summary>
    public ObservableCollection<WakeWord> Words { get; } = [];

    /// <summary>The ones in use.</summary>
    public IEnumerable<WakeWord> Active => Words.Where(w => w.Enabled && w.Variants.Count > 0);

    /// <summary>Reads every wake word again (after an import, for example).</summary>
    public void Reload()
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
    }

    private void OnWordChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not WakeWord word || e.PropertyName is nameof(WakeWord.EffectiveThreshold) or nameof(WakeWord.Summary)) return;
        lock (_dirty) _dirty.Add(word);
        _saveTimer.Change(500, Timeout.Infinite);
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

using System.Diagnostics;

namespace KDesktop.Core.Voice;

/// <summary>Why a recording of the wake word is not kept.</summary>
public enum TakeIssue { None, NoSpeech, TooShort, TooLong, TooQuiet, Clipped, Odd }

/// <summary>A recording of the wake word: the speech (silence trimmed) and what is wrong with it, if anything.</summary>
public sealed record WakeTake(float[] Speech, TakeIssue Issue, double Seconds, double Peak, double Rms)
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Recordings for the wake word set-up: a fixed stretch (silence, normal speech) or one utterance that ends by
/// itself after a short silence. Opens the microphone only while recording.
/// </summary>
public sealed class WakeWordRecorder : IDisposable
{
    public const double MinSeconds = 0.25, MaxSeconds = 2.5;

    private readonly MicCapture _mic = new();

    /// <summary>
    /// Tests only: with the environment variable <c>KDESKTOP_VOICE_FAKE</c> set to a folder, every recording returns
    /// the next .wav of that folder (alphabetical order) instead of opening the microphone.
    /// </summary>
    private static readonly Queue<string>? Fake = Environment.GetEnvironmentVariable("KDESKTOP_VOICE_FAKE") is { Length: > 0 } dir && Directory.Exists(dir)
        ? new Queue<string>(Directory.GetFiles(dir, "*.wav").Order())
        : null;

    private static async Task<float[]?> FakeAsync(CancellationToken cancel)
    {
        if (Fake is null) return null;
        await Task.Delay(300, cancel).ConfigureAwait(false);
        lock (Fake) return Fake.Count > 0 ? VoiceDiagnostics.ReadWav(Fake.Dequeue()) : [];
    }

    /// <summary>Microphone level, 0–1, while recording (any thread).</summary>
    public float Level => _mic.Level;

    /// <summary>Records <paramref name="duration"/> of audio (silence, normal speech).</summary>
    public async Task<float[]> RecordAsync(string? micId, TimeSpan duration, CancellationToken cancel)
    {
        if (await FakeAsync(cancel).ConfigureAwait(false) is { } fake) return fake;
        var audio = new List<float>(MicCapture.SampleRate * (int)Math.Ceiling(duration.TotalSeconds));
        void OnSamples(float[] s) { lock (audio) audio.AddRange(s); }
        _mic.Samples += OnSamples;
        try
        {
            _mic.Start(micId);
            await Task.Delay(duration, cancel).ConfigureAwait(false);
        }
        finally
        {
            _mic.Samples -= OnSamples;
            _mic.Stop();
        }
        lock (audio) return audio.ToArray();
    }

    /// <summary>
    /// Records one utterance: from the first speech until 0.6 s of silence (or <paramref name="max"/>). Returns all of
    /// it; <see cref="Check"/> trims and judges it.
    /// </summary>
    public async Task<float[]> RecordUtteranceAsync(string? micId, TimeSpan max, CancellationToken cancel)
    {
        if (await FakeAsync(cancel).ConfigureAwait(false) is { } fake) return fake;
        var audio = new List<float>(MicCapture.SampleRate * 4);
        using var vad = new VoiceActivity(TimeSpan.FromMilliseconds(600));
        void OnSamples(float[] s)
        {
            lock (audio)
            {
                audio.AddRange(s);
                vad.Accept(s);
            }
        }
        _mic.Samples += OnSamples;
        try
        {
            _mic.Start(micId);
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < max)
            {
                await Task.Delay(40, cancel).ConfigureAwait(false);
                lock (audio) if (vad.Ended) break;
            }
        }
        finally
        {
            _mic.Samples -= OnSamples;
            _mic.Stop();
        }
        lock (audio) return audio.ToArray();
    }

    /// <summary>Speech segments of a whole recording (Silero VAD), as (start, length) in samples.</summary>
    public static List<(int Start, int Length)> Segments(float[] audio, double endSilence = 0.3)
    {
        using var vad = new VoiceActivity(TimeSpan.FromSeconds(endSilence));
        for (var i = 0; i < audio.Length; i += 512) vad.Accept(audio[i..Math.Min(audio.Length, i + 512)]);
        vad.Flush();
        return vad.Segments;
    }

    /// <summary>
    /// Trims a take of the wake word to its speech and judges it: too short or long, too quiet next to the background
    /// noise, saturated, or far longer/shorter than the takes already kept (<paramref name="keptSeconds"/>).
    /// </summary>
    public static WakeTake Check(float[] audio, double noiseRms, IReadOnlyCollection<double> keptSeconds)
    {
        var segments = Segments(audio);
        if (segments.Count == 0) return new WakeTake([], TakeIssue.NoSpeech, 0, 0, 0);

        var pad = MicCapture.SampleRate / 8;
        var start = Math.Max(0, segments[0].Start - pad);
        var end = Math.Min(audio.Length, segments[^1].Start + segments[^1].Length + pad);
        var speech = audio[start..end];
        var seconds = (segments[^1].Start + segments[^1].Length - segments[0].Start) / (double)MicCapture.SampleRate;

        double peak = 0, sum = 0;
        var clipped = 0;
        foreach (var s in speech)
        {
            var a = Math.Abs(s);
            peak = Math.Max(peak, a);
            sum += s * s;
            if (a >= 0.99) clipped++;
        }
        var rms = Math.Sqrt(sum / Math.Max(1, speech.Length));

        var issue =
            seconds < MinSeconds ? TakeIssue.TooShort
            : seconds > MaxSeconds ? TakeIssue.TooLong
            : clipped > speech.Length / 500 ? TakeIssue.Clipped
            : peak < 0.03 || rms < Math.Max(0.004, noiseRms * 2.5) ? TakeIssue.TooQuiet
            : IsOdd(seconds, keptSeconds) ? TakeIssue.Odd
            : TakeIssue.None;
        return new WakeTake(speech, issue, seconds, peak, rms);
    }

    /// <summary>
    /// Far from the length of the other takes (half or nearly double the median). The always-on detector (stage 4)
    /// will also compare how they sound.
    /// </summary>
    private static bool IsOdd(double seconds, IReadOnlyCollection<double> kept)
    {
        if (kept.Count < 2) return false;
        var sorted = kept.Order().ToList();
        var median = sorted[sorted.Count / 2];
        return seconds < median * 0.5 || seconds > median * 1.8;
    }

    /// <summary>RMS of a recording (background noise).</summary>
    public static double Rms(float[] audio)
    {
        double sum = 0;
        foreach (var s in audio) sum += s * s;
        return audio.Length == 0 ? 0 : Math.Sqrt(sum / audio.Length);
    }

    /// <summary>The speech parts of a long recording (normal talk), each to be transcribed on its own.</summary>
    public static IEnumerable<float[]> Utterances(float[] audio)
    {
        var pad = MicCapture.SampleRate / 10;
        foreach (var (start, length) in Segments(audio, endSilence: 0.4))
        {
            var from = Math.Max(0, start - pad);
            var to = Math.Min(audio.Length, start + length + pad);
            yield return audio[from..to];
        }
    }

    public void Dispose() => _mic.Dispose();
}

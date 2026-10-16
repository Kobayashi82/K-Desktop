using System.Diagnostics;

namespace KDesktop.Core.Voice;

/// <summary>
/// What the microphone hears after a wake word fired, without any gap: the listener keeps feeding it while
/// <see cref="VoiceService.HandleWakeAsync"/> checks the word and then waits for the order. Thread-safe.
/// </summary>
public sealed class WakeCapture : IDisposable
{
    private readonly object _gate = new();
    private readonly List<float> _audio = new(MicCapture.SampleRate * 6);
    private readonly VoiceActivity _vad;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private bool _stopped;

    /// <param name="preroll">The audio before the trigger (the word and whatever came before it).</param>
    public WakeCapture(float[] preroll, TimeSpan endSilence)
    {
        Preroll = preroll;
        _vad = new VoiceActivity(endSilence);
    }

    public float[] Preroll { get; }

    /// <summary>Peak level of the last block, 0–1 (for the meter).</summary>
    public float Level { get; private set; }

    public TimeSpan Elapsed => _watch.Elapsed;

    public bool HeardSpeech
    {
        get { lock (_gate) return _vad.HeardSpeech; }
    }

    /// <summary>Speech heard and then the end silence: the order is complete.</summary>
    public bool Ended
    {
        get { lock (_gate) return _vad.Ended; }
    }

    /// <summary>Speech going on right now.</summary>
    public bool Speaking
    {
        get { lock (_gate) return _vad.Speaking; }
    }

    /// <summary>How many stretches of speech have finished (each one followed by the end silence).</summary>
    public int SegmentCount
    {
        get { lock (_gate) return _vad.Segments.Count; }
    }

    /// <summary>What came after the trigger so far and its finished speech segments, still recording.</summary>
    public (float[] Audio, List<(int Start, int Length)> Segments) Snapshot()
    {
        lock (_gate) return (_audio.ToArray(), [.. _vad.Segments]);
    }

    public void Add(float[] samples)
    {
        lock (_gate)
        {
            if (_stopped) return;
            _audio.AddRange(samples);
            _vad.Accept(samples);
        }
        var peak = 0f;
        foreach (var s in samples) peak = Math.Max(peak, Math.Abs(s));
        Level = Math.Min(1, peak);
    }

    /// <summary>Stops taking audio; returns what came after the trigger and its speech segments.</summary>
    public (float[] Audio, List<(int Start, int Length)> Segments) Stop()
    {
        lock (_gate)
        {
            if (!_stopped)
            {
                _stopped = true;
                _vad.Flush();
            }
            return (_audio.ToArray(), [.. _vad.Segments]);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stopped = true;
            _vad.Dispose();
        }
    }
}

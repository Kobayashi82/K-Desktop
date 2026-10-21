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
    /// <summary>The same speech cut at every short pause: what has been said so far can be transcribed before the end silence.</summary>
    private readonly VoiceActivity _pauses;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private bool _stopped;

    /// <summary>A pause this long may be the end of the order: it is transcribed meanwhile (stage 5.7).</summary>
    public static readonly TimeSpan PauseSilence = TimeSpan.FromMilliseconds(250);

    /// <param name="preroll">The audio before the trigger (the word and whatever came before it).</param>
    public WakeCapture(float[] preroll, TimeSpan endSilence)
    {
        Preroll = preroll;
        _vad = new VoiceActivity(endSilence);
        _pauses = new VoiceActivity(PauseSilence);
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

    /// <summary>How many short pauses there have been (a new one: <see cref="Guess"/> may have changed).</summary>
    public int PauseCount
    {
        get { lock (_gate) return _pauses.Segments.Count; }
    }

    /// <summary>
    /// What the segments would be if the speech ended at the last short pause: the finished ones and the one still open
    /// (whose silence has not lasted long enough yet). Null when nothing was said after the last finished one.
    /// </summary>
    public (float[] Audio, List<(int Start, int Length)> Segments)? Guess()
    {
        lock (_gate)
        {
            var finished = _vad.Segments;
            var after = finished.Count > 0 ? finished[^1].Start + finished[^1].Length : 0;
            var open = _pauses.Segments.Where(p => p.Start + p.Length > after).ToList();
            if (open.Count == 0) return null;
            var start = Math.Max(open[0].Start, after);
            var end = open[^1].Start + open[^1].Length;
            return (_audio.ToArray(), [.. finished, (start, end - start)]);
        }
    }

    /// <summary>
    /// Whether the segments of a <see cref="Guess"/> are the ones that finished (same pieces, ends within a tenth of a
    /// second): what was transcribed from it can be used.
    /// </summary>
    public static bool SameSpeech(List<(int Start, int Length)> guessed, List<(int Start, int Length)> finished)
    {
        const int near = MicCapture.SampleRate / 10;
        if (guessed.Count != finished.Count) return false;
        for (var i = 0; i < guessed.Count; i++)
            if (Math.Abs(guessed[i].Start - finished[i].Start) > near ||
                Math.Abs(guessed[i].Start + guessed[i].Length - (finished[i].Start + finished[i].Length)) > near)
                return false;
        return true;
    }

    public void Add(float[] samples)
    {
        lock (_gate)
        {
            if (_stopped) return;
            _audio.AddRange(samples);
            _vad.Accept(samples);
            _pauses.Accept(samples);
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
            _pauses.Dispose();
        }
    }
}

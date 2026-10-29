using SherpaOnnx;

namespace KDesktop.Core.Voice;

/// <summary>
/// Silero VAD (sherpa-onnx): finds where speech starts and ends in the microphone stream, so a spoken order ends
/// by itself after a short silence. One instance per listening session (it keeps state).
/// </summary>
public sealed class VoiceActivity : IDisposable
{
    private readonly VoiceActivityDetector _vad;
    private readonly bool _keepSpeech;

    /// <param name="endSilence">Silence after speech that ends the utterance.</param>
    /// <param name="keepSpeech">Each finished segment's samples go to <see cref="Speech"/> too (a file to transcribe, D5).</param>
    public VoiceActivity(TimeSpan endSilence, bool keepSpeech = false)
    {
        _keepSpeech = keepSpeech;
        var config = new VadModelConfig();
        config.SileroVad.Model = VoiceModels.VadFile;
        config.SileroVad.Threshold = 0.5f;
        config.SileroVad.MinSilenceDuration = (float)endSilence.TotalSeconds;
        config.SileroVad.MinSpeechDuration = 0.2f;
        config.SileroVad.MaxSpeechDuration = 20;
        config.SileroVad.WindowSize = 512;
        config.SampleRate = MicCapture.SampleRate;
        config.NumThreads = 1;
        config.Provider = "cpu";
        _vad = new VoiceActivityDetector(config, 30);
    }

    /// <summary>True once some speech has been heard.</summary>
    public bool HeardSpeech { get; private set; }

    /// <summary>Speech going on right now (a segment open).</summary>
    public bool Speaking => _vad.IsSpeechDetected();

    /// <summary>True when speech was heard and then the end silence passed: the utterance is complete.</summary>
    public bool Ended { get; private set; }

    /// <summary>The finished speech segments (silence trimmed), in order; Start is the sample where each begins.</summary>
    public List<(int Start, int Length)> Segments { get; } = [];

    /// <summary>With <c>keepSpeech</c>: the finished segments with their samples, to be taken.</summary>
    public Queue<(long Start, float[] Samples)> Speech { get; } = new();

    public void Accept(float[] samples)
    {
        _vad.AcceptWaveform(samples);
        if (_vad.IsSpeechDetected()) HeardSpeech = true;
        while (!_vad.IsEmpty())
        {
            Segments.Add(Take(_vad.Front()));
            _vad.Pop();
            HeardSpeech = true;
            Ended = true;
        }
    }

    /// <summary>Closes a segment still open (the user stopped the listening while speaking).</summary>
    public void Flush()
    {
        _vad.Flush();
        while (!_vad.IsEmpty())
        {
            Segments.Add(Take(_vad.Front()));
            _vad.Pop();
        }
    }

    private (int, int) Take(SpeechSegment segment)
    {
        if (_keepSpeech) Speech.Enqueue((segment.Start, segment.Samples));
        return (segment.Start, segment.Samples.Length);
    }

    public void Dispose() => _vad.Dispose();
}

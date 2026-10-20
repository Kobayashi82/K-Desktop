using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Host.Audio;

/// <summary>A loaded sound: float samples (−1…1), interleaved when stereo, at their own rate.</summary>
internal sealed class HostSound : ISound
{
    public HostSound(float[] samples, int sampleRate, int channels)
    {
        if (sampleRate is < 1000 or > 384000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(channels), "Solo mono o estéreo");
        Samples = samples;
        SampleRate = sampleRate;
        Channels = channels;
    }

    public float[] Samples { get; }

    public int SampleRate { get; }

    public int Channels { get; }

    public int Frames => Samples.Length / Channels;

    public TimeSpan Duration => TimeSpan.FromSeconds(Frames / (double)SampleRate);
}

/// <summary>
/// Mixes what the assistants play into stereo frames at <see cref="Rate"/>: one-shot sounds and loops with live gain,
/// pitch and pan, under the user's volume for assistants. Pure arithmetic (the output thread drives it), safe to feed
/// from any thread.
/// </summary>
internal sealed class Mixer
{
    public const int Rate = 48000;

    /// <summary>One-shot sounds at once; more are ignored (a burst of the same sound adds nothing).</summary>
    public const int MaxVoices = 32;

    private readonly Lock _gate = new();
    private readonly List<Voice> _voices = [];
    private readonly List<HostLoop> _loops = [];
    private volatile float _master = 1;
    private volatile bool _muted;

    /// <summary>The user switched the sound off: nothing is mixed (loops keep their settings for when it comes back).</summary>
    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (value) Silence();
        }
    }

    /// <summary>The user's volume for assistants (0…1), applied with a curve that follows the ear.</summary>
    public double Volume
    {
        get => Math.Sqrt(_master);
        set => _master = (float)(Math.Clamp(value, 0, 1) * Math.Clamp(value, 0, 1));
    }

    public void Play(HostSound sound, double gain, double pan)
    {
        if (gain <= 0 || _muted) return;
        lock (_gate)
        {
            if (_voices.Count < MaxVoices) _voices.Add(new Voice(sound, (float)gain, (float)Math.Clamp(pan, -1, 1)));
        }
    }

    public HostLoop Loop(HostSound sound)
    {
        var loop = new HostLoop(this, sound);
        lock (_gate) _loops.Add(loop);
        return loop;
    }

    internal void Remove(HostLoop loop)
    {
        lock (_gate) _loops.Remove(loop);
    }

    /// <summary>Forgets the one-shot sounds playing (the sound was switched off).</summary>
    public void Silence()
    {
        lock (_gate) _voices.Clear();
    }

    /// <summary>Whether anything would be heard (a one-shot sound, or a loop that is up or still fading out).</summary>
    public bool HasSound
    {
        get
        {
            if (_muted) return false;
            lock (_gate)
            {
                if (_voices.Count > 0) return true;
                foreach (var loop in _loops)
                    if (loop.Audible) return true;
                return false;
            }
        }
    }

    /// <summary>Mixes the next <c>output.Length / 2</c> stereo frames (interleaved) into <paramref name="output"/>.</summary>
    public void Mix(Span<float> output)
    {
        output.Clear();
        if (_muted) return;
        var master = _master;
        lock (_gate)
        {
            for (var i = _voices.Count - 1; i >= 0; i--)
            {
                var voice = _voices[i];
                if (!voice.Render(output, master)) _voices.RemoveAt(i);
            }
            foreach (var loop in _loops) loop.Render(output, master);
        }
    }

    /// <summary>Equal-power pan: −1 all left, 0 both at full, 1 all right.</summary>
    internal static (float Left, float Right) Pan(float pan)
    {
        var angle = (pan + 1) * MathF.PI / 4;
        const float sqrt2 = 1.41421356f;
        return (MathF.Cos(angle) * sqrt2, MathF.Sin(angle) * sqrt2);
    }

    /// <summary>
    /// Reads <paramref name="sound"/> from <paramref name="position"/> (in its frames) with steps of
    /// <paramref name="step"/>, adding it to <paramref name="output"/> with the gain going from <paramref name="from"/> to
    /// <paramref name="to"/> across the block (no clicks). Returns the new position; past the end it stops, unless
    /// <paramref name="loop"/>.
    /// </summary>
    internal static double Render(HostSound sound, double position, double step, bool loop, Span<float> output, float from, float to, float pan)
    {
        var data = sound.Samples;
        var frames = sound.Frames;
        var count = output.Length / 2;
        if (frames == 0) return loop ? 0 : 1;
        var (left, right) = Pan(pan);
        for (var i = 0; i < count; i++)
        {
            if (position >= frames)
            {
                if (!loop) return position;
                position %= frames;
            }
            var gain = from + (to - from) * (i / (float)count);
            var at = (int)position;
            var frac = (float)(position - at);
            var next = at + 1 < frames ? at + 1 : (loop ? 0 : at);
            if (sound.Channels == 1)
            {
                var s = (data[at] + (data[next] - data[at]) * frac) * gain;
                output[2 * i] += s * left;
                output[2 * i + 1] += s * right;
            }
            else
            {
                var l = data[2 * at] + (data[2 * next] - data[2 * at]) * frac;
                var r = data[2 * at + 1] + (data[2 * next + 1] - data[2 * at + 1]) * frac;
                // A stereo sound keeps its image; the pan only leans it (balance).
                output[2 * i] += l * gain * Math.Min(1, 1 - pan);
                output[2 * i + 1] += r * gain * Math.Min(1, 1 + pan);
            }
            position += step;
        }
        return position;
    }

    private sealed class Voice(HostSound sound, float gain, float pan)
    {
        private double _position;

        /// <summary>Adds its next block; false once it has ended.</summary>
        public bool Render(Span<float> output, float master)
        {
            _position = Mixer.Render(sound, _position, sound.SampleRate / (double)Rate, false, output, gain * master, gain * master, pan);
            return _position < sound.Frames;
        }
    }
}

/// <summary>A sound that repeats until disposed (<see cref="ILoop"/>). Gain changes glide over one block.</summary>
internal sealed class HostLoop(Mixer mixer, HostSound sound) : ILoop
{
    private double _position;
    private float _applied;
    private volatile float _gain;
    private volatile float _pitch = 1;
    private volatile float _pan;

    public double Gain { get => _gain; set => _gain = (float)Math.Clamp(value, 0, 4); }

    public double Pitch { get => _pitch; set => _pitch = (float)Math.Clamp(value, 0.1, 8); }

    public double Pan { get => _pan; set => _pan = (float)Math.Clamp(value, -1, 1); }

    /// <summary>Up, or still gliding down to silence.</summary>
    internal bool Audible => _gain > 0.0005f || _applied > 0.0005f;

    internal void Render(Span<float> output, float master)
    {
        var target = _gain * master;
        if (!Audible)
        {
            _applied = 0;
            return;
        }
        _position = Mixer.Render(sound, _position, sound.SampleRate / (double)Mixer.Rate * _pitch, true, output, _applied, target, _pan);
        _applied = target;
    }

    public void Dispose() => mixer.Remove(this);
}

namespace KDesktop.Assistant.Voice.Effects;

/// <summary>
/// One effect of a voice's chain (plan §10.2). It takes a sentence piece by piece as the voice makes it, keeping its state
/// between pieces (so nothing clicks where they join); some change the length (speed). One instance per sentence.
/// </summary>
public interface IVoiceEffect
{
    /// <summary>The next piece through it.</summary>
    float[] Process(float[] input);

    /// <summary>The sentence is over: what it still holds (the tail of an echo, the beep of a radio).</summary>
    float[] Finish() => [];
}

/// <summary>
/// Higher or lower without changing how long it lasts: two taps that read a short delay line at another speed, each
/// fading in as the other fades out. Moves the whole voice (pitch and «mouth» together), like a cartoon.
/// </summary>
public sealed class PitchShift(double semitones, int sampleRate) : IVoiceEffect
{
    private readonly double _ratio = Math.Pow(2, semitones / 12);
    private readonly int _window = sampleRate * 40 / 1000;
    private readonly float[] _ring = new float[sampleRate / 4];
    private int _write;
    private double _phase;

    public float[] Process(float[] input)
    {
        if (Math.Abs(_ratio - 1) < 1e-4) return input;
        var output = new float[input.Length];
        var step = (1 - _ratio) / _window;
        for (var i = 0; i < input.Length; i++)
        {
            _ring[_write] = input[i];
            double sum = 0;
            for (var tap = 0; tap < 2; tap++)
            {
                var phase = _phase + tap * 0.5;
                phase -= Math.Floor(phase);
                var delay = phase * _window + 2;
                var read = _write - delay;
                if (read < 0) read += _ring.Length;
                var at = (int)read;
                var frac = read - at;
                var a = _ring[at % _ring.Length];
                var b = _ring[(at + 1) % _ring.Length];
                var gain = Math.Sin(Math.PI * phase);
                sum += (a + (b - a) * frac) * gain * gain;
            }
            output[i] = (float)sum;
            _phase += step;
            _phase -= Math.Floor(_phase);
            _write = (_write + 1) % _ring.Length;
        }
        return output;
    }
}

/// <summary>
/// Faster or slower without changing the pitch: the voice read faster (shorter, higher) and then brought back down to its
/// own pitch. For the voices that have no speed of their own (Pocket TTS).
/// </summary>
public sealed class Speed(double speed, int sampleRate) : IVoiceEffect
{
    private readonly double _speed = Math.Clamp(speed, 0.5, 2);
    private readonly PitchShift _back = new(-12 * Math.Log2(Math.Clamp(speed, 0.5, 2)), sampleRate);
    private double _position;
    private float _last;

    public float[] Process(float[] input)
    {
        if (Math.Abs(_speed - 1) < 0.01) return input;
        var output = new List<float>((int)(input.Length / _speed) + 2);
        // _position runs over [previous last sample, input…]: index 0 is _last.
        while (_position < input.Length)
        {
            var at = (int)_position;
            var frac = (float)(_position - at);
            var a = at == 0 ? _last : input[at - 1];
            var b = input[at];
            output.Add(a + (b - a) * frac);
            _position += _speed;
        }
        _position -= input.Length;
        if (input.Length > 0) _last = input[^1];
        return _back.Process([.. output]);
    }
}

/// <summary>
/// The «size of the mouth» without the pitch: the outline of the spectrum (the formants) stretched or squeezed, each frame
/// of 21 ms. Above 1, smaller (a child, a little robot); below, bigger.
/// </summary>
public sealed class FormantShift(double shift, int sampleRate) : IVoiceEffect
{
    private const int Size = 512;
    private const int Hop = Size / 4;
    private readonly double _shift = Math.Clamp(shift, 0.5, 2);
    private readonly double[] _window = Enumerable.Range(0, Size).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Size)).ToArray();
    private readonly List<float> _input = [];
    private readonly double[] _overlap = new double[Size];
    private bool _primed;

    // Hann² summed every quarter of a frame gives 1.5: divided back out.
    private const double Normal = 1.5;

    public float[] Process(float[] input)
    {
        if (Math.Abs(_shift - 1) < 0.01) return input;
        if (!_primed)
        {
            // The frames are a frame late: a frame of silence first keeps the start.
            _input.AddRange(new float[Size - Hop]);
            _primed = true;
        }
        _input.AddRange(input);
        var output = new List<float>(input.Length + Hop);
        while (_input.Count >= Size)
        {
            output.AddRange(Frame());
            _input.RemoveRange(0, Hop);
        }
        return [.. output];
    }

    public float[] Finish()
    {
        if (Math.Abs(_shift - 1) < 0.01) return [];
        _input.AddRange(new float[Size]);
        var output = new List<float>();
        while (_input.Count >= Size)
        {
            output.AddRange(Frame());
            _input.RemoveRange(0, Hop);
        }
        return [.. output];
    }

    /// <summary>One frame: its envelope moved, added to the overlap; the first hop of it is done.</summary>
    private float[] Frame()
    {
        var re = new double[Size];
        var im = new double[Size];
        for (var i = 0; i < Size; i++) re[i] = _input[i] * _window[i];
        Fft.Transform(re, im, inverse: false);
        var half = Size / 2;
        var magnitude = new double[half + 1];
        for (var k = 0; k <= half; k++) magnitude[k] = Math.Sqrt(re[k] * re[k] + im[k] * im[k]) + 1e-9;
        var envelope = Envelope(magnitude);
        for (var k = 0; k <= half; k++)
        {
            var from = k / _shift;
            var lo = (int)from;
            var env = lo + 1 <= half ? envelope[lo] + (envelope[lo + 1] - envelope[lo]) * (from - lo) : envelope[half] * 1e-3;
            var gain = Math.Clamp(env / envelope[k], 0, 8);
            re[k] *= gain;
            im[k] *= gain;
            if (k > 0 && k < half)
            {
                re[Size - k] = re[k];
                im[Size - k] = -im[k];
            }
        }
        Fft.Transform(re, im, inverse: true);
        for (var i = 0; i < Size; i++) _overlap[i] += re[i] * _window[i] / Normal;
        var done = new float[Hop];
        for (var i = 0; i < Hop; i++) done[i] = (float)_overlap[i];
        Array.Copy(_overlap, Hop, _overlap, 0, Size - Hop);
        Array.Clear(_overlap, Size - Hop, Hop);
        return done;
    }

    /// <summary>The outline of a spectrum: its magnitude averaged over a band around each bin (about 280 Hz).</summary>
    private double[] Envelope(double[] magnitude)
    {
        var width = Math.Max(2, (int)(280.0 / sampleRate * Size));
        var envelope = new double[magnitude.Length];
        for (var k = 0; k < magnitude.Length; k++)
        {
            double sum = 0;
            var n = 0;
            for (var j = Math.Max(0, k - width); j <= Math.Min(magnitude.Length - 1, k + width); j++, n++) sum += magnitude[j];
            envelope[k] = sum / n + 1e-9;
        }
        return envelope;
    }
}

/// <summary>The classic robot: the voice multiplied by a tone (it rings, metallic).</summary>
public sealed class RingModulator(double frequency, double mix, int sampleRate) : IVoiceEffect
{
    private double _phase;

    public float[] Process(float[] input)
    {
        var output = new float[input.Length];
        var step = 2 * Math.PI * frequency / sampleRate;
        var m = Math.Clamp(mix, 0, 1);
        for (var i = 0; i < input.Length; i++)
        {
            output[i] = (float)(input[i] * (1 - m + m * Math.Sin(_phase)));
            _phase += step;
            if (_phase > 2 * Math.PI) _phase -= 2 * Math.PI;
        }
        return output;
    }
}

/// <summary>The robot of 8 bits: fewer levels and fewer samples per second.</summary>
public sealed class Bitcrusher(double bits, double rate, int sampleRate) : IVoiceEffect
{
    private double _counter;
    private float _held;

    public float[] Process(float[] input)
    {
        var output = new float[input.Length];
        var levels = Math.Pow(2, Math.Clamp(bits, 2, 16) - 1);
        var hold = sampleRate / Math.Clamp(rate, 1000, sampleRate);
        for (var i = 0; i < input.Length; i++)
        {
            if ((_counter += 1) >= hold)
            {
                _counter -= hold;
                _held = (float)(Math.Round(input[i] * levels) / levels);
            }
            output[i] = _held;
        }
        return output;
    }
}

/// <summary>A voice over the radio (mission control): a narrow band, a little overdrive, hiss, and a beep at the end.</summary>
public sealed class Radio(double bandwidth, double noise, bool beep, int sampleRate, int seed) : IVoiceEffect
{
    private readonly Biquad _low = Biquad.HighPass(320, sampleRate);
    private readonly Biquad _low2 = Biquad.HighPass(320, sampleRate);
    private readonly Biquad _high = Biquad.LowPass(Math.Clamp(bandwidth, 800, 8000), sampleRate);
    private readonly Biquad _high2 = Biquad.LowPass(Math.Clamp(bandwidth, 800, 8000), sampleRate);
    private readonly Random _random = new(seed);
    private bool _started;

    public float[] Process(float[] input)
    {
        var output = new List<float>(input.Length + sampleRate / 10);
        // The squelch opening: a burst of hiss before the first word.
        if (!_started)
        {
            _started = true;
            for (var i = 0; i < sampleRate * 6 / 100; i++) output.Add(Hiss() * 6);
        }
        foreach (var x in input)
        {
            var y = _high2.Next(_high.Next(_low2.Next(_low.Next(x))));
            output.Add((float)(Math.Tanh(y * 2.2) / Math.Tanh(2.2)) + Hiss());
        }
        return [.. output];
    }

    public float[] Finish()
    {
        var output = new List<float>();
        if (beep)
        {
            // «Over»: a short tone after the last word.
            for (var i = 0; i < sampleRate * 4 / 100; i++) output.Add(Hiss());
            var length = sampleRate * 12 / 100;
            for (var i = 0; i < length; i++)
                output.Add((float)(Math.Sin(2 * Math.PI * 1250 * i / sampleRate) * 0.28 * Math.Min(1, Math.Min(i, length - i) / (sampleRate * 0.005))));
        }
        for (var i = 0; i < sampleRate * 5 / 100; i++) output.Add(Hiss() * 4 * (1 - i / (sampleRate * 0.05f)));
        return [.. output];
    }

    private float Hiss() => (float)((_random.NextDouble() * 2 - 1) * Math.Clamp(noise, 0, 0.5));
}

/// <summary>A voice that wavers, metallic (a Martian): the voice mixed with itself through a short delay that moves.</summary>
public sealed class Chorus(double rate, double depth, int sampleRate) : IVoiceEffect
{
    private readonly float[] _ring = new float[sampleRate / 10];
    private int _write;
    private double _phase;

    public float[] Process(float[] input)
    {
        var output = new float[input.Length];
        var baseDelay = sampleRate * 0.012;
        var swing = sampleRate * 0.006 * Math.Clamp(depth, 0, 1);
        var step = 2 * Math.PI * Math.Clamp(rate, 0.05, 12) / sampleRate;
        for (var i = 0; i < input.Length; i++)
        {
            _ring[_write] = input[i];
            var delay = baseDelay + swing * (1 + Math.Sin(_phase)) / 2;
            var read = _write - delay;
            if (read < 0) read += _ring.Length;
            var at = (int)read;
            var frac = read - at;
            var wet = _ring[at % _ring.Length] + (_ring[(at + 1) % _ring.Length] - _ring[at % _ring.Length]) * frac;
            output[i] = (float)((input[i] + wet) * 0.6);
            _phase += step;
            _write = (_write + 1) % _ring.Length;
        }
        return output;
    }
}

/// <summary>A cabin or space: the voice comes back, fainter each time.</summary>
public sealed class Echo(double milliseconds, double feedback, double mix, int sampleRate) : IVoiceEffect
{
    private readonly float[] _line = new float[Math.Max(1, (int)(sampleRate * Math.Clamp(milliseconds, 5, 1000) / 1000))];
    private int _at;

    public float[] Process(float[] input)
    {
        var output = new float[input.Length];
        for (var i = 0; i < input.Length; i++) output[i] = Step(input[i]);
        return output;
    }

    /// <summary>The tail: until it has faded (a second at most).</summary>
    public float[] Finish()
    {
        var fb = Math.Clamp(feedback, 0, 0.9);
        var repeats = fb <= 0.01 ? 1 : (int)Math.Ceiling(Math.Log(0.01) / Math.Log(fb));
        var length = Math.Min(sampleRate, _line.Length * repeats);
        var output = new float[length];
        for (var i = 0; i < length; i++) output[i] = Step(0);
        return output;
    }

    private float Step(float x)
    {
        var delayed = _line[_at];
        _line[_at] = (float)(x + delayed * Math.Clamp(feedback, 0, 0.9));
        _at = (_at + 1) % _line.Length;
        return (float)(x * (1 - Math.Clamp(mix, 0, 1) * 0.5) + delayed * Math.Clamp(mix, 0, 1));
    }
}

/// <summary>Something has gone wrong (a crash): every bit may stutter or drop out for a moment.</summary>
public sealed class Glitch(double chance, int sampleRate, int seed) : IVoiceEffect
{
    private readonly Random _random = new(seed);
    private readonly List<float> _block = [];
    private readonly int _size = sampleRate * 7 / 100;

    public float[] Process(float[] input)
    {
        var output = new List<float>(input.Length * 2);
        foreach (var x in input)
        {
            _block.Add(x);
            if (_block.Count < _size) continue;
            Emit(output);
        }
        return [.. output];
    }

    public float[] Finish()
    {
        var output = new List<float>();
        if (_block.Count > 0) output.AddRange(_block);
        _block.Clear();
        return [.. output];
    }

    private void Emit(List<float> output)
    {
        var roll = _random.NextDouble();
        var p = Math.Clamp(chance, 0, 1);
        if (roll < p * 0.6)
        {
            // A stutter: the same bit two or three times.
            var times = 2 + _random.Next(2);
            for (var t = 0; t < times; t++) output.AddRange(_block);
        }
        else if (roll < p)
        {
            // A drop: half of it gone.
            output.AddRange(_block.Take(_block.Count / 2));
            output.AddRange(new float[_block.Count / 2]);
        }
        else output.AddRange(_block);
        _block.Clear();
    }
}

/// <summary>
/// The synthesizer robot: the voice's bands shape a buzzing tone of one note (it speaks in monotone). A little of the
/// voice itself stays so it is understood.
/// </summary>
public sealed class Vocoder : IVoiceEffect
{
    private readonly Biquad[] _voice;
    private readonly Biquad[] _carrier;
    private readonly double[] _envelope;
    private readonly double _step;
    private readonly double _attack;
    private readonly double _release;
    private double _phase;

    public Vocoder(double note, int bands, int sampleRate)
    {
        var count = Math.Clamp(bands, 6, 32);
        var frequency = 440 * Math.Pow(2, (Math.Clamp(note, 24, 84) - 69) / 12);
        _step = frequency / sampleRate;
        _voice = new Biquad[count];
        _carrier = new Biquad[count];
        _envelope = new double[count];
        for (var b = 0; b < count; b++)
        {
            var center = 150 * Math.Pow(5000.0 / 150, (b + 0.5) / count);
            _voice[b] = Biquad.BandPass(center, sampleRate, 6);
            _carrier[b] = Biquad.BandPass(center, sampleRate, 6);
        }
        _attack = 1 - Math.Exp(-1 / (0.004 * sampleRate));
        _release = 1 - Math.Exp(-1 / (0.03 * sampleRate));
    }

    public float[] Process(float[] input)
    {
        var output = new float[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            // A sawtooth with a little noise: every band has something to shape.
            var saw = 2 * _phase - 1;
            _phase += _step;
            if (_phase >= 1) _phase -= 1;
            double sum = 0;
            for (var b = 0; b < _voice.Length; b++)
            {
                var level = Math.Abs(_voice[b].Next(input[i]));
                _envelope[b] += (level - _envelope[b]) * (level > _envelope[b] ? _attack : _release);
                sum += _carrier[b].Next((float)saw) * _envelope[b];
            }
            output[i] = (float)(sum * 6 + input[i] * 0.45);
        }
        return output;
    }
}

/// <summary>
/// Keeps every voice at the same loudness whatever the effects did (a vocoder, a ring), with a soft ceiling: the gain
/// follows the loudness slowly, faster when it must go down.
/// </summary>
public sealed class Leveler(int sampleRate) : IVoiceEffect
{
    private const double Target = 0.16;
    private readonly double _down = 1 - Math.Exp(-1 / (0.01 * sampleRate));
    private readonly double _up = 1 - Math.Exp(-1 / (0.4 * sampleRate));
    private double _level = Target;

    public float[] Process(float[] input)
    {
        var output = new float[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            var x = Math.Abs(input[i]);
            // The level of what has been loud: silence does not raise the gain.
            if (x > _level) _level += (x - _level) * _down;
            else if (x > 0.02) _level += (x - _level) * _up;
            var gain = Math.Clamp(Target * 3 / Math.Max(_level, 1e-3), 0.4, 4);
            output[i] = (float)Math.Tanh(input[i] * gain * 0.9);
        }
        return output;
    }
}

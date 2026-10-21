namespace KDesktop.Assistant.Voice.Effects;

/// <summary>A second-order filter (RBJ's cookbook), for a stream of samples.</summary>
public sealed class Biquad
{
    private double _b0, _b1, _b2, _a1, _a2;
    private double _x1, _x2, _y1, _y2;

    private Biquad() { }

    public static Biquad LowPass(double frequency, int sampleRate, double q = 0.707) => Make(frequency, sampleRate, q, 0);

    public static Biquad HighPass(double frequency, int sampleRate, double q = 0.707) => Make(frequency, sampleRate, q, 1);

    public static Biquad BandPass(double frequency, int sampleRate, double q) => Make(frequency, sampleRate, q, 2);

    private static Biquad Make(double frequency, int sampleRate, double q, int kind)
    {
        frequency = Math.Clamp(frequency, 10, sampleRate * 0.45);
        var w = 2 * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(w);
        var alpha = Math.Sin(w) / (2 * q);
        var a0 = 1 + alpha;
        var f = new Biquad();
        switch (kind)
        {
            case 0:
                f._b0 = (1 - cos) / 2 / a0; f._b1 = (1 - cos) / a0; f._b2 = (1 - cos) / 2 / a0;
                break;
            case 1:
                f._b0 = (1 + cos) / 2 / a0; f._b1 = -(1 + cos) / a0; f._b2 = (1 + cos) / 2 / a0;
                break;
            default:
                f._b0 = alpha / a0; f._b1 = 0; f._b2 = -alpha / a0;
                break;
        }
        f._a1 = -2 * cos / a0;
        f._a2 = (1 - alpha) / a0;
        return f;
    }

    public float Next(float x)
    {
        var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1; _x1 = x;
        _y2 = _y1; _y1 = y;
        return (float)y;
    }
}

/// <summary>An in-place radix-2 FFT (the length a power of two).</summary>
public static class Fft
{
    public static void Transform(double[] re, double[] im, bool inverse)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = 2 * Math.PI / len * (inverse ? 1 : -1);
            var wr = Math.Cos(angle);
            var wi = Math.Sin(angle);
            for (var i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    var ur = re[i + k];
                    var ui = im[i + k];
                    var vr = re[i + k + len / 2] * cr - im[i + k + len / 2] * ci;
                    var vi = re[i + k + len / 2] * ci + im[i + k + len / 2] * cr;
                    re[i + k] = ur + vr;
                    im[i + k] = ui + vi;
                    re[i + k + len / 2] = ur - vr;
                    im[i + k + len / 2] = ui - vi;
                    (cr, ci) = (cr * wr - ci * wi, cr * wi + ci * wr);
                }
            }
        }
        if (!inverse) return;
        for (var i = 0; i < n; i++)
        {
            re[i] /= n;
            im[i] /= n;
        }
    }
}

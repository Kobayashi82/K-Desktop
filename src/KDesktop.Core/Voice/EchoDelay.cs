using System.Numerics;

namespace KDesktop.Core.Voice;

/// <summary>
/// Finds how late the echo of the speakers reaches the microphone compared with where the clock says it should:
/// devices that don't report their whole latency (Bluetooth, a TV over HDMI) can be 100–300 ms late, more than the
/// echo canceller's filter covers. Cross-correlation of the microphone with what was played (GCC-PHAT: only the
/// phase, so the peak is sharp even with music), searched from −50 to +400 ms.
/// </summary>
internal static class EchoDelay
{
    /// <summary>Microphone audio compared each time: 1.5 s.</summary>
    public const int Window = MicCapture.SampleRate * 3 / 2;

    /// <summary>Latest echo looked for (400 ms) and how early it may come (50 ms, stamps a little off).</summary>
    public const int MaxLate = MicCapture.SampleRate * 2 / 5, MaxEarly = MicCapture.SampleRate / 20;

    private const int Size = 1 << 16;

    /// <summary>
    /// The delay (in samples) at which <paramref name="reference"/> best explains <paramref name="mic"/>, or null when
    /// there is no clear echo (too quiet, nothing playing, or no single peak). <paramref name="reference"/> starts
    /// <see cref="MaxLate"/> samples before <paramref name="mic"/> and ends <see cref="MaxEarly"/> after it.
    /// </summary>
    public static int? Estimate(ReadOnlySpan<float> mic, ReadOnlySpan<float> reference)
    {
        if (mic.Length != Window || reference.Length != Window + MaxLate + MaxEarly) throw new ArgumentException("window");
        if (Rms(mic) < 1e-4 || Rms(reference) < 1e-3) return null;

        var a = new Complex[Size];
        var b = new Complex[Size];
        for (var i = 0; i < mic.Length; i++) a[i] = mic[i];
        for (var i = 0; i < reference.Length; i++) b[i] = reference[i];
        Fft(a, inverse: false);
        Fft(b, inverse: false);
        for (var i = 0; i < Size; i++)
        {
            var cross = Complex.Conjugate(a[i]) * b[i];
            var magnitude = cross.Magnitude;
            a[i] = magnitude > 1e-12 ? cross / magnitude : Complex.Zero;
        }
        Fft(a, inverse: true);

        // a[k] = Σ mic[n]·reference[n + k]; reference[j] is MaxLate before mic[j], so k = MaxLate − delay.
        int best = -1, second = -1;
        double bestValue = double.MinValue, secondValue = double.MinValue, sum = 0;
        var span = MaxLate + MaxEarly;
        for (var k = 0; k <= span; k++)
        {
            var v = a[k].Real;
            sum += Math.Abs(v);
            if (v > bestValue)
            {
                if (best < 0 || Math.Abs(k - best) > 40) (second, secondValue) = (best, bestValue);
                (best, bestValue) = (k, v);
            }
            else if (v > secondValue && Math.Abs(k - best) > 40) (second, secondValue) = (k, v);
        }
        var mean = sum / (span + 1);
        // A clear echo: a peak well above the rest and above any other one.
        if (bestValue < mean * 10 || (second >= 0 && bestValue < secondValue * 1.5)) return null;
        return MaxLate - best;
    }

    private static double Rms(ReadOnlySpan<float> x)
    {
        double sum = 0;
        foreach (var v in x) sum += v * v;
        return Math.Sqrt(sum / Math.Max(1, x.Length));
    }

    /// <summary>In-place radix-2 FFT (length a power of two); the inverse is scaled by 1/n.</summary>
    private static void Fft(Complex[] data, bool inverse)
    {
        var n = data.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }
        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = 2 * Math.PI / length * (inverse ? 1 : -1);
            var step = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var i = 0; i < n; i += length)
            {
                var w = Complex.One;
                for (var k = 0; k < length / 2; k++)
                {
                    var u = data[i + k];
                    var v = data[i + k + length / 2] * w;
                    data[i + k] = u + v;
                    data[i + k + length / 2] = u - v;
                    w *= step;
                }
            }
        }
        if (inverse)
            for (var i = 0; i < n; i++) data[i] /= n;
    }
}

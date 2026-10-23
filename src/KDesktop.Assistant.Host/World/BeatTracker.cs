namespace KDesktop.Assistant.Host.World;

/// <summary>
/// Finds the rhythm of the music from the output's peak level (the WASAPI meter, read about a hundred times a second;
/// from Kobii). It takes the hits (sudden rises of the level), looks every few tenths for the clearest periodicity
/// between 70 and 180 beats per minute (autocorrelation of the hits of the last seconds) and the phase with a comb of
/// pulses, and follows it smoothly like a PLL: the beat does not jump, so an assistant can dance on it. A sound without
/// rhythm (someone talking, noise) is not music.
/// </summary>
internal sealed class BeatTracker
{
    /// <summary>Samples per second it works with (the readings that come are put on this grid).</summary>
    public const double Rate = 100;

    private const int Window = 600;
    private const double MinBpm = 70;
    private const double MaxBpm = 180;
    private const double EstimateEvery = 0.25;
    private const double SilenceLevel = 0.015;
    private const double StartSeconds = 2.5;
    private const double StopSeconds = 1.8;

    private readonly double[] _onsets = new double[Window];
    private int _head;
    private int _count;
    private double _clock = double.NaN;
    private double _level;
    private double _fast;
    private double _slow;
    private double _loud;
    private double _recent;
    private double _nextEstimate;
    private double _confidence;
    private double _period;
    private double _origin;
    private double _rhythmic;
    private double _silent;
    private bool _playing;

    /// <summary>Music with a beat is sounding.</summary>
    public bool Playing => _playing;

    /// <summary>Beats per minute (0 without a rhythm).</summary>
    public double Bpm => _period > 0 ? 60 / _period : 0;

    /// <summary>How clear the rhythm is (0–1).</summary>
    public double Confidence => _confidence;

    /// <summary>Recent average level (0–1).</summary>
    public double Level => _loud;

    /// <summary>Beats since the reference at <paramref name="time"/> (the fraction is the phase).</summary>
    public double Beat(double time) => _period > 0 ? (time - _origin) / _period : 0;

    /// <summary>Adds a reading of the meter (peak level, 0–1) taken at <paramref name="time"/> seconds.</summary>
    public void Add(double time, double peak)
    {
        if (double.IsNaN(_clock) || time - _clock > 1)
        {
            // First reading (or it had stopped): from scratch.
            Reset();
            _clock = time;
        }

        _level = Math.Clamp(peak, 0, 1);
        while (_clock + 1 / Rate <= time)
        {
            _clock += 1 / Rate;
            Sample(_clock);
        }
    }

    /// <summary>Forgets everything (no music).</summary>
    public void Reset()
    {
        Array.Clear(_onsets);
        _head = 0;
        _count = 0;
        _clock = double.NaN;
        _fast = _slow = _loud = _recent = 0;
        _confidence = 0;
        _period = 0;
        _rhythmic = 0;
        _silent = 0;
        _playing = false;
        _nextEstimate = 0;
    }

    private void Sample(double time)
    {
        const double dt = 1 / Rate;

        // Compressed envelope (hearing is logarithmic) and its hits: what rises above the slow average.
        var e = Math.Log10(1 + 60 * _level);
        _fast += (e - _fast) * (1 - Math.Exp(-dt * 35));
        _slow += (e - _slow) * (1 - Math.Exp(-dt * 4));
        _loud += (_level - _loud) * (1 - Math.Exp(-dt * 1.5));
        _recent += (_level - _recent) * (1 - Math.Exp(-dt * 6));
        var onset = Math.Max(0, _fast - _slow * 1.02);
        _onsets[_head] = onset;
        _head = (_head + 1) % Window;
        _count = Math.Min(_count + 1, Window);

        if (time >= _nextEstimate)
        {
            _nextEstimate = time + EstimateEvery;
            Estimate(time);
        }

        // Music starts after a few seconds with a clear rhythm, and stops after a while of silence.
        var quiet = _recent < SilenceLevel;
        _silent = quiet ? _silent + dt : 0;
        _rhythmic = !quiet && _confidence > 0.3 && _period > 0 ? _rhythmic + dt : Math.Max(0, _rhythmic - dt * 0.5);
        if (!_playing && _rhythmic > StartSeconds) _playing = true;
        else if (_playing && (_silent > StopSeconds || (_confidence < 0.12 && _rhythmic <= 0))) _playing = false;
    }

    /// <summary>The hit of <paramref name="ago"/> samples ago.</summary>
    private double Onset(int ago) => _onsets[((_head - 1 - ago) % Window + Window) % Window];

    private void Estimate(double time)
    {
        if (_count < Rate * 3) return;

        // The hits without their mean: so the autocorrelation really measures whether they repeat (a coefficient from
        // -1 to 1), not just whether there is sound.
        var n = _count;
        var centered = new double[n];
        var mean = 0.0;
        for (var i = 0; i < n; i++)
        {
            centered[i] = Onset(i);
            mean += centered[i];
        }

        mean /= n;
        var energy = 0.0;
        for (var i = 0; i < n; i++)
        {
            centered[i] -= mean;
            energy += centered[i] * centered[i];
        }

        if (energy < 1e-6)
        {
            _confidence *= 0.5;
            return;
        }

        // Periodicity: autocorrelation of the hits, preferring the usual tempos (around 120).
        var minLag = (int)(Rate * 60 / MaxBpm);
        var maxLag = (int)(Rate * 60 / MinBpm);
        var scores = new double[maxLag + 2];
        var bestLag = 0;
        var best = 0.0;
        for (var lag = minLag; lag <= maxLag; lag++)
        {
            var sum = 0.0;
            for (var i = 0; i + lag < n; i++) sum += centered[i] * centered[i + lag];

            var raw = sum / energy * n / (n - lag);
            scores[lag] = raw;
            var bpm = Rate * 60 / lag;
            var octave = Math.Log2(bpm / 120);
            var weighted = raw * Math.Exp(-octave * octave / (2 * 0.6 * 0.6));
            if (weighted > best)
            {
                best = weighted;
                bestLag = lag;
            }
        }

        if (bestLag == 0) return;

        // Refines the period between samples (parabolic interpolation).
        var lagValue = (double)bestLag;
        if (bestLag > minLag && bestLag < maxLag)
        {
            var (a, b, c) = (scores[bestLag - 1], scores[bestLag], scores[bestLag + 1]);
            var denominator = a - 2 * b + c;
            if (Math.Abs(denominator) > 1e-9) lagValue += Math.Clamp(0.5 * (a - c) / denominator, -0.5, 0.5);
        }

        var confidence = Math.Clamp(scores[bestLag], 0, 1);
        _confidence += (confidence - _confidence) * 0.5;
        var period = lagValue / Rate;
        if (_period <= 0 || Math.Abs(period - _period) / _period > 0.08)
        {
            // A new tempo: taken if it is clear (if not, the one there was stays).
            if (_period <= 0 || confidence > 0.35)
            {
                _period = period;
                _origin = time;
            }
        }
        else
        {
            _period += (period - _period) * 0.3;
        }

        // Phase: the shift whose comb of pulses collects the most hits (over the last seconds).
        var steps = (int)Math.Round(_period * Rate);
        var bestShift = 0;
        var bestComb = -1.0;
        for (var shift = 0; shift < steps; shift++)
        {
            var comb = 0.0;
            for (var k = 0; shift + k * steps < n; k++)
                comb += Onset(shift + k * steps) + 0.5 * (Onset(Math.Max(0, shift + k * steps - 1)) + Onset(shift + k * steps + 1));

            if (comb > bestComb)
            {
                bestComb = comb;
                bestShift = shift;
            }
        }

        // Like a PLL: corrects little by little towards the measured beat (no jumps).
        var lastBeat = time - bestShift / Rate;
        var measured = Beat(lastBeat);
        var error = measured - Math.Round(measured);
        _origin += error * _period * 0.45;
    }
}

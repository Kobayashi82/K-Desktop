using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.Rendering;

/// <summary>
/// Keeps the engine in step with the screen (from Kobii): each frame is drawn right after the desktop composed the
/// previous one, so the compositor gets exactly one per refresh (or one every two on 120 Hz screens or faster) and
/// movement is smooth, without repeated or dropped frames.
/// </summary>
internal sealed class FramePacer
{
    /// <summary>Margin after the composition (so the frame is in good time for the next one).</summary>
    private const double Margin = 0.0015;

    private const double QueryInterval = 1;

    private double _period = 1.0 / 60;
    private double _vblank;
    private double _nextQuery;
    private bool _available = true;

    /// <summary>Refresh rate of the desktop (Hz).</summary>
    public double RefreshRate => 1 / _period;

    /// <summary>
    /// Interval for smooth movement: the refresh of the screen or, above 90 Hz, half (or a quarter) of it, so as not
    /// to spend more than needed; always about 60 frames per second or more.
    /// </summary>
    public double SmoothInterval => _period * Math.Max(1, Math.Floor(RefreshRate / 60 + 0.25));

    /// <param name="now">Engine time (seconds).</param>
    public void Update(double now)
    {
        if (!_available || now < _nextQuery) return;
        _nextQuery = now + QueryInterval;
        try
        {
            if (DCompositionGetFrameStatistics(out var stats) != 0 || stats.timeFrequency <= 0) return;
            if (stats.rateDenominator > 0)
            {
                var rate = (double)stats.rateNumerator / stats.rateDenominator;
                if (rate is > 20 and < 500) _period = 1 / rate;
            }
            _vblank = now - (stats.currentTime - stats.lastFrameTime) / (double)stats.timeFrequency;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            _available = false;
        }
    }

    /// <summary>
    /// The time for the next frame: the closest to <paramref name="target"/> that falls right after a composition.
    /// Long waits (nothing moving) need not keep in step.
    /// </summary>
    public double Align(double target)
    {
        if (target - _vblank > 1e6 || _period <= 0) return target;
        var k = Math.Round((target - _vblank - Margin) / _period);
        return _vblank + k * _period + Margin;
    }
}

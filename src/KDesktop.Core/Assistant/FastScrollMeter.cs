namespace KDesktop.Core.Assistant;

/// <summary>
/// Tells a hard scroll from an ordinary one (the <c>fastScroll</c> message, the Lander's gust of wind): every notch of
/// the wheel adds energy that fades away by itself; when enough piles up in a short time it fires, and then rests a
/// moment. Not thread-safe: it is fed from the hook thread only.
/// </summary>
public sealed class FastScrollMeter
{
    /// <summary>Notches piled up (fading) that make a hard scroll: about four quick ones.</summary>
    public const double Threshold = 3.5;

    /// <summary>Notches for full strength.</summary>
    public const double Full = 12;

    /// <summary>How fast the energy fades (per second).</summary>
    private const double Decay = 2.5;

    /// <summary>Seconds after firing before it can fire again.</summary>
    public const double Rest = 1.2;

    private double _energy;
    private double _down;
    private double _last = double.NegativeInfinity;
    private double _restUntil = double.NegativeInfinity;

    /// <summary>
    /// One turn of the wheel at <paramref name="now"/> (seconds) of <paramref name="delta"/> (120 per notch, positive is
    /// up or to the right). Returns the strength of a hard scroll (0…1, positive downwards) when this turn makes one.
    /// </summary>
    public double? Add(double now, int delta, bool horizontal)
    {
        var fade = Math.Exp(-Math.Max(0, now - _last) * Decay);
        _energy *= fade;
        _down *= fade;
        _last = now;
        var notches = Math.Abs(delta) / 120.0;
        _energy += notches;
        if (!horizontal) _down -= delta / 120.0;
        if (_energy < Threshold || now < _restUntil) return null;

        var strength = Math.Min(_energy, Full) / Full * (_down < 0 ? -1 : 1);
        _energy = _down = 0;
        _restUntil = now + Rest;
        return strength;
    }
}

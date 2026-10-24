using System.Windows;
using System.Windows.Media;

namespace KDesktop.Assistant.Sdk;

/// <summary>
/// A rope (cable, hook line, leash) simulated with Verlet integration, in screen pixels like everything else.
/// The start is always held (by the assistant); the end hangs free or is held too (stuck in a window, on a projectile).
/// It only resists stretching, so it sags and goes slack, and it never gets longer than <see cref="MaxLength"/> while
/// its end is free. It steps itself at a fixed 120 Hz whatever the frame rate, so it behaves the same on any screen.
/// </summary>
/// <example>
/// <code>
/// var rope = new Rope(nose, hook, length: 300) { MaxLength = 420 };
/// // every tick:
/// rope.Start = nose; rope.PinEnd(hook);   // or rope.ReleaseEnd() to let it fall
/// rope.Step(tick.Elapsed);
/// rope.Draw(dc, pen);                      // inside a surface's Present
/// if (rope.IsTaut) PullWindow(rope.Pull); // what towing does with it
/// </code>
/// </example>
public sealed class Rope
{
    /// <summary>The fixed step of the simulation (s).</summary>
    public const double TimeStep = 1.0 / 120;

    /// <summary>Most steps taken per call: a long pause does not make it catch up for seconds.</summary>
    private const int MaxSteps = 12;

    private readonly Point[] _points;
    private readonly Point[] _previous;
    private double _length;
    private double _maxLength = double.PositiveInfinity;
    private double _carry;
    private bool _endPinned;

    /// <param name="start">Where it starts (held).</param>
    /// <param name="end">Where its end is now; the rope is laid straight between both.</param>
    /// <param name="length">Its length when relaxed, in pixels.</param>
    /// <param name="segments">How many pieces it is made of (more: rounder, a little more work). 4–64.</param>
    public Rope(Point start, Point end, double length, int segments = 20)
    {
        Segments = Math.Clamp(segments, 4, 64);
        _points = new Point[Segments + 1];
        _previous = new Point[Segments + 1];
        _length = Math.Max(1, length);
        for (var i = 0; i <= Segments; i++)
        {
            var t = i / (double)Segments;
            _points[i] = _previous[i] = new Point(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
        }
    }

    public int Segments { get; }

    /// <summary>The points along it, from the start to the end.</summary>
    public IReadOnlyList<Point> Points => _points;

    /// <summary>Where it starts: held there (the assistant moves it every tick).</summary>
    public Point Start
    {
        get => _points[0];
        set => _points[0] = _previous[0] = value;
    }

    /// <summary>Where its end is.</summary>
    public Point End => _points[Segments];

    /// <summary>Its end is held (<see cref="PinEnd"/>) rather than hanging.</summary>
    public bool EndPinned => _endPinned;

    /// <summary>Its length when relaxed (reeling it in or out changes it), never over <see cref="MaxLength"/>.</summary>
    public double Length
    {
        get => _length;
        set => _length = Math.Clamp(value, 1, _maxLength);
    }

    /// <summary>The longest it can ever be (its end free). Infinite by default.</summary>
    public double MaxLength
    {
        get => _maxLength;
        set
        {
            _maxLength = Math.Max(1, value);
            _length = Math.Min(_length, _maxLength);
        }
    }

    /// <summary>Gravity, in pixels per second squared (down is +Y). About 1000 looks like a light cable.</summary>
    public Vector Gravity { get; set; } = new(0, 1000);

    /// <summary>How fast it loses its movement: the fraction of speed left after one second is e^(−Damping). 0: none.</summary>
    public double Damping { get; set; } = 1.5;

    /// <summary>How many times per step the pieces are pulled back to their length: more, stiffer. 4–64.</summary>
    public int Iterations { get; set; } = 16;

    /// <summary>The straight distance between its start and its end.</summary>
    public double Span => (End - Start).Length;

    /// <summary>How much further apart its ends are than its length (both held and pulled apart): 0 when it is not.</summary>
    public double Overstretch => Math.Max(0, Span - _length);

    /// <summary>It is straight and pulling (its ends at least 98 % of its length apart).</summary>
    public bool IsTaut => Span >= _length * 0.98;

    /// <summary>
    /// What it pulls the end with, towards the start: the direction from the end to the start times
    /// <see cref="Overstretch"/>. Zero while it is slack. A window being towed follows this with a spring.
    /// </summary>
    public Vector Pull
    {
        get
        {
            var span = Start - End;
            var length = span.Length;
            return length < 1e-6 || length <= _length ? default : span / length * (length - _length);
        }
    }

    /// <summary>Holds its end at <paramref name="point"/> (a hook stuck in a window, a projectile carrying it).</summary>
    public void PinEnd(Point point)
    {
        _endPinned = true;
        _points[Segments] = _previous[Segments] = point;
    }

    /// <summary>Lets its end hang, moving at <paramref name="velocity"/> (pixels per second) as it is let go.</summary>
    public void ReleaseEnd(Vector velocity = default)
    {
        _endPinned = false;
        _previous[Segments] = _points[Segments] - velocity * TimeStep;
    }

    /// <summary>Moves it all to a new place at once, laid straight (after a jump: a new monitor, a respawn).</summary>
    public void Reset(Point start, Point end)
    {
        for (var i = 0; i <= Segments; i++)
        {
            var t = i / (double)Segments;
            _points[i] = _previous[i] = new Point(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
        }
    }

    /// <summary>Advances it <paramref name="seconds"/> (the tick's elapsed time), in fixed steps.</summary>
    public void Step(double seconds)
    {
        if (!(seconds > 0))
        {
            return;
        }

        _carry = Math.Min(_carry + seconds, MaxSteps * TimeStep);
        while (_carry >= TimeStep)
        {
            _carry -= TimeStep;
            StepOnce();
        }
    }

    private void StepOnce()
    {
        var keep = Math.Exp(-Math.Max(0, Damping) * TimeStep);
        var fall = Gravity * (TimeStep * TimeStep);
        var last = _endPinned ? Segments - 1 : Segments;
        for (var i = 1; i <= last; i++)
        {
            var point = _points[i];
            var velocity = (point - _previous[i]) * keep;
            _previous[i] = point;
            _points[i] = point + velocity + fall;
        }

        // Each piece pulled back to its length (only when stretched: a rope does not push).
        var rest = _length / Segments;
        var iterations = Math.Clamp(Iterations, 4, 64);
        for (var k = 0; k < iterations; k++)
        {
            for (var i = 0; i < Segments; i++)
            {
                var a = _points[i];
                var b = _points[i + 1];
                var delta = b - a;
                var distance = delta.Length;
                if (distance <= rest || distance < 1e-9)
                {
                    continue;
                }

                var correction = delta * ((distance - rest) / distance);
                var aFixed = i == 0;
                var bFixed = i + 1 == Segments && _endPinned;
                if (aFixed && bFixed)
                {
                    continue;
                }

                if (aFixed)
                {
                    _points[i + 1] = b - correction;
                }
                else if (bFixed)
                {
                    _points[i] = a + correction;
                }
                else
                {
                    _points[i] = a + correction / 2;
                    _points[i + 1] = b - correction / 2;
                }
            }
        }

        // Never longer than its maximum: with its end free, each piece is kept within its share from the start (what
        // the iterations leave stretched, this takes away). Moving a point here also takes its speed into account.
        if (!_endPinned && !double.IsInfinity(_maxLength))
        {
            var most = _maxLength / Segments;
            for (var i = 1; i <= Segments; i++)
            {
                var delta = _points[i] - _points[i - 1];
                var distance = delta.Length;
                if (distance > most)
                {
                    var moved = _points[i - 1] + delta * (most / distance);
                    _previous[i] += moved - _points[i];
                    _points[i] = moved;
                }
            }
        }
    }

    /// <summary>The smallest rectangle around it (to size a surface), grown by <paramref name="margin"/> pixels.</summary>
    public Rect Bounds(double margin = 0)
    {
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (var p in _points)
        {
            left = Math.Min(left, p.X);
            top = Math.Min(top, p.Y);
            right = Math.Max(right, p.X);
            bottom = Math.Max(bottom, p.Y);
        }

        return new Rect(left - margin, top - margin, right - left + margin * 2, bottom - top + margin * 2);
    }

    /// <summary>Its shape as a smooth curve through the middle of its pieces (frozen: ready to draw).</summary>
    public StreamGeometry ToGeometry()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(_points[0], isFilled: false, isClosed: false);
            for (var i = 1; i < Segments; i++)
            {
                var middle = new Point((_points[i].X + _points[i + 1].X) / 2, (_points[i].Y + _points[i + 1].Y) / 2);
                context.QuadraticBezierTo(_points[i], middle, isStroked: true, isSmoothJoin: true);
            }

            context.LineTo(_points[Segments], isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Draws it with <paramref name="pen"/> (in a surface's <see cref="ISurface.Present"/>, which uses screen pixels).</summary>
    public void Draw(DrawingContext dc, Pen pen)
    {
        ArgumentNullException.ThrowIfNull(dc);
        dc.DrawGeometry(null, pen, ToGeometry());
    }
}

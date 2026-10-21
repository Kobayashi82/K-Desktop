using System.Windows;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 7.1: the SDK's Verlet rope (gravity, damping, maximum length) and how it is drawn.</summary>
public class RopeTests
{
    private static void Run(Rope rope, double seconds, double frame = 1.0 / 60)
    {
        for (var t = 0.0; t < seconds; t += frame)
        {
            rope.Step(frame);
        }
    }

    private static bool Finite(Rope rope) => rope.Points.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y));

    /// <summary>The sum of its pieces (how long it really is now).</summary>
    private static double Measured(Rope rope)
    {
        var total = 0.0;
        for (var i = 1; i < rope.Points.Count; i++)
        {
            total += (rope.Points[i] - rope.Points[i - 1]).Length;
        }

        return total;
    }

    [Fact]
    public void ItHangsStraightDownAndSettles()
    {
        // Laid out sideways, it swings down and comes to rest under its start, about its length below.
        var rope = new Rope(new Point(500, 100), new Point(800, 100), length: 300);
        Run(rope, 8);
        Assert.True(Finite(rope));
        Assert.InRange(rope.End.X, 495, 505);
        Assert.InRange(rope.End.Y, 100 + 300 * 0.97, 100 + 300 * 1.05);

        // At rest: one more second barely moves it.
        var before = rope.End;
        Run(rope, 1);
        Assert.True((rope.End - before).Length < 1.5);
    }

    [Fact]
    public void WithoutDampingItKeepsSwingingAndWithItItStops()
    {
        double SwingAfter(double damping)
        {
            var rope = new Rope(new Point(0, 0), new Point(200, 0), length: 200) { Damping = damping };
            Run(rope, 6);
            var lowest = rope.End;
            var widest = 0.0;
            for (var i = 0; i < 120; i++)
            {
                rope.Step(1.0 / 60);
                widest = Math.Max(widest, Math.Abs(rope.End.X - lowest.X));
            }

            return widest;
        }

        Assert.True(SwingAfter(0) > 60, "Sin amortiguación debería seguir columpiándose");
        Assert.True(SwingAfter(3) < 5, "Con amortiguación debería pararse");
    }

    [Fact]
    public void ItNeverGrowsPastItsMaximumLength()
    {
        // Thrown hard with its end free, and with a strong gravity: the end never goes past the maximum from the start.
        var rope = new Rope(new Point(300, 300), new Point(320, 300), length: 150) { MaxLength = 180, Gravity = new Vector(0, 4000), Iterations = 4 };
        rope.ReleaseEnd(new Vector(6000, -3000));
        var farthest = 0.0;
        for (var i = 0; i < 600; i++)
        {
            // The start moving away fast too, as when the lander pulls.
            rope.Start = new Point(300 - i * 3.0, 300);
            rope.Step(1.0 / 60);
            Assert.True(Finite(rope));
            farthest = Math.Max(farthest, rope.Span);
            Assert.True(Measured(rope) <= 180 + 0.5, $"Mide {Measured(rope):0.0}");
        }

        Assert.True(farthest <= 180 + 0.5, $"Llegó a {farthest:0.0}");
        Assert.True(farthest > 140, "Debería haberse estirado hasta casi su largo");
    }

    [Fact]
    public void ItsLengthCannotBeSetOverTheMaximum()
    {
        var rope = new Rope(new Point(0, 0), new Point(100, 0), length: 100) { MaxLength = 120 };
        rope.Length = 500;
        Assert.Equal(120, rope.Length);
        rope.MaxLength = 80;
        Assert.Equal(80, rope.Length);
    }

    [Fact]
    public void HeldAtBothEndsItSagsWhenSlackAndPullsWhenStretched()
    {
        var rope = new Rope(new Point(0, 0), new Point(200, 0), length: 300);
        rope.PinEnd(new Point(200, 0));
        Run(rope, 4);
        Assert.False(rope.IsTaut);
        Assert.Equal(default, rope.Pull);
        Assert.True(rope.Points[rope.Segments / 2].Y > 60, "Floja, debería combarse");

        // Pulled apart beyond its length: straight, and it pulls the end towards the start with the excess.
        rope.PinEnd(new Point(400, 0));
        Run(rope, 1);
        Assert.True(rope.IsTaut);
        Assert.Equal(100, rope.Overstretch, 3);
        Assert.Equal(-100, rope.Pull.X, 3);
        Assert.Equal(0, rope.Pull.Y, 3);
        Assert.True(rope.Points.All(p => Math.Abs(p.Y) < 3), "Tensa, debería ir recta");
        Assert.True(Finite(rope));
    }

    [Fact]
    public void ReelingInShortensItSmoothly()
    {
        var rope = new Rope(new Point(500, 0), new Point(500, 400), length: 400);
        Run(rope, 3);
        for (var i = 0; i < 120; i++)
        {
            rope.Length -= 2.5;
            rope.Step(1.0 / 60);
            Assert.True(Finite(rope));
        }

        Run(rope, 3);
        Assert.InRange(rope.Span, 100 * 0.97, 100 * 1.05);
    }

    [Fact]
    public void ItBehavesTheSameAtAnyFrameRate()
    {
        Point After(double frame)
        {
            var rope = new Rope(new Point(0, 0), new Point(250, 0), length: 250);
            Run(rope, 0.75, frame);
            return rope.End;
        }

        // Fixed steps inside: 30, 60 and 144 frames per second end in (almost) the same place.
        var at60 = After(1.0 / 60);
        Assert.True((After(1.0 / 30) - at60).Length < 25);
        Assert.True((After(1.0 / 144) - at60).Length < 25);
    }

    [Fact]
    public void ALongPauseDoesNotMakeItRunForSeconds()
    {
        var rope = new Rope(new Point(0, 0), new Point(250, 0), length: 250);
        rope.Step(30);
        Assert.True(rope.End.Y < 120, "Tras una pausa larga solo debería avanzar unos pasos");
        Assert.True(Finite(rope));
        rope.Step(double.NaN);
        rope.Step(-1);
        Assert.True(Finite(rope));
    }

    [Fact]
    public void ItDrawsAsOneSmoothCurveAndKnowsItsBounds()
    {
        var rope = new Rope(new Point(10, 20), new Point(310, 20), length: 400);
        Run(rope, 2);
        var geometry = rope.ToGeometry();
        Assert.True(geometry.IsFrozen);
        var bounds = rope.Bounds(4);
        var drawn = geometry.GetRenderBounds(new Pen(Brushes.Black, 2));
        Assert.True(bounds.Contains(drawn.TopLeft) && bounds.Contains(drawn.BottomRight), $"{drawn} fuera de {bounds}");

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            rope.Draw(dc, new Pen(Brushes.Black, 2));
        }

        Assert.False(visual.ContentBounds.IsEmpty);
    }
}

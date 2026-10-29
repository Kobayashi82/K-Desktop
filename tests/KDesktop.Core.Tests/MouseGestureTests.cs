using KDesktop.Core.Input;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using Xunit.Abstractions;

namespace KDesktop.Core.Tests;

public class MouseGestureTests(ITestOutputHelper output)
{
    /// <summary>
    /// A drawing as a hand makes it: its path stretched (each side on its own), a little turned, shaky and at an uneven
    /// speed, in screen pixels.
    /// </summary>
    private static List<GesturePoint> Hand(IReadOnlyList<GesturePoint> path, Random random, bool backwards = false)
    {
        var size = 120 + random.NextDouble() * 220;
        double width = size * (0.7 + random.NextDouble() * 0.6), height = size * (0.7 + random.NextDouble() * 0.6);
        var angle = (random.NextDouble() - 0.5) * 2 * 12 * Math.PI / 180;
        // A slow wobble (the hand) and a pixel of jitter (the mouse), sampled every few pixels at an uneven speed.
        var wobble = Math.Max(width, height) * 0.04;
        double fx = 1 + random.NextDouble() * 2, fy = 1 + random.NextDouble() * 2, px = random.NextDouble() * 6.3, py = random.NextDouble() * 6.3;
        var corners = (backwards ? path.Reverse() : path).Select(p => new GesturePoint(p.X * width, p.Y * height)).ToList();
        double total = 0;
        for (var i = 1; i < corners.Count; i++) total += Math.Sqrt(Math.Pow(corners[i].X - corners[i - 1].X, 2) + Math.Pow(corners[i].Y - corners[i - 1].Y, 2));
        var result = new List<GesturePoint>();
        void Add(double x, double y, double along)
        {
            var t = along / total;
            x += wobble * Math.Sin(2 * Math.PI * fx * t + px) + random.NextDouble() - 0.5;
            y += wobble * Math.Sin(2 * Math.PI * fy * t + py) + random.NextDouble() - 0.5;
            result.Add(new GesturePoint(500 + x * Math.Cos(angle) - y * Math.Sin(angle), 400 + x * Math.Sin(angle) + y * Math.Cos(angle)));
        }
        double done = 0;
        for (var i = 1; i < corners.Count; i++)
        {
            var (a, b) = (corners[i - 1], corners[i]);
            var length = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            for (double d = 0; d < length; d += 3 + random.NextDouble() * 9)
                Add(a.X + (b.X - a.X) * d / length, a.Y + (b.Y - a.Y) * d / length, done + d);
            done += length;
        }
        Add(corners[^1].X, corners[^1].Y, total);
        return result;
    }

    [Fact]
    public void EveryDrawingIsRecognisedAsAHandMakesIt()
    {
        var random = new Random(82);
        var failures = new List<string>();
        foreach (var id in MouseGestures.BuiltIn)
        {
            var line = id is "Left" or "Right" or "Up" or "Down";
            var path = MouseGestures.Path(id);
            var right = 0;
            const int tries = 60;
            for (var i = 0; i < tries; i++)
            {
                var stroke = Hand(path, random, backwards: !line && i % 2 == 1);
                var got = MouseGestures.Recognize(stroke, null, out _);
                if (got == id) right++;
                else if (got is not null) failures.Add($"{id} → {got}");
            }
            output.WriteLine($"{id}: {right}/{tries} " + string.Join(", ", failures.Where(f => f.StartsWith(id + " ", StringComparison.Ordinal)).Distinct()));
            Assert.True(right >= tries * 0.9, $"{id}: solo {right}/{tries}");
        }
        output.WriteLine(string.Join(", ", failures.GroupBy(f => f).Select(g => $"{g.Key} ×{g.Count()}")));
        // Never the wrong drawing (better none than another command).
        Assert.True(failures.Count <= 3, string.Join(", ", failures));
    }

    [Fact]
    public void ScribblesAreNoDrawing()
    {
        var random = new Random(7);
        var accepted = 0;
        const int tries = 300;
        for (var i = 0; i < tries; i++)
        {
            var points = new List<GesturePoint>();
            double x = 500, y = 500, heading = random.NextDouble() * Math.PI * 2;
            for (var s = 0; s < 80; s++)
            {
                heading += (random.NextDouble() - 0.5) * 1.6;
                x += Math.Cos(heading) * 8;
                y += Math.Sin(heading) * 8;
                points.Add(new GesturePoint(x, y));
            }
            if (MouseGestures.Recognize(points, null, out _) is not null) accepted++;
        }
        output.WriteLine($"garabatos aceptados: {accepted}/{tries}");
        Assert.True(accepted <= tries * 0.12, $"{accepted}/{tries}");
    }

    [Fact]
    public void AClickWithALittleMovementIsNoDrawing()
    {
        Assert.Null(MouseGestures.Recognize([new(100, 100), new(110, 104), new(115, 100)], null, out _));
        Assert.Null(MouseGestures.Normalize([new(100, 100)]));
    }

    [Fact]
    public void ACTurnedIsAU()
    {
        var random = new Random(3);
        Assert.Equal("C", MouseGestures.Recognize(Hand(MouseGestures.Path("C"), random), null, out _));
        Assert.Equal("U", MouseGestures.Recognize(Hand(MouseGestures.Path("U"), random), null, out _));
        Assert.Equal("Left", MouseGestures.Recognize([new(300, 100), new(200, 104), new(100, 98)], null, out _));
        Assert.Equal("Right", MouseGestures.Recognize([new(100, 100), new(200, 96), new(300, 102)], null, out _));
    }

    [Fact]
    public void ADrawingOfTheUserIsRecognised()
    {
        // A triangle, which is not built in, taught with three strokes.
        GesturePoint[] triangle = [new(0.5, 0), new(1, 1), new(0, 1), new(0.5, 0)];
        var random = new Random(11);
        var gesture = new CustomGesture { Name = "Triángulo" };
        for (var i = 0; i < 3; i++) gesture.Samples.Add(MouseGestures.ToFlat(MouseGestures.Normalize(Hand(triangle, random))!));
        var right = 0;
        for (var i = 0; i < 20; i++)
            if (MouseGestures.Recognize(Hand(triangle, random), [gesture], out _) == "Triángulo") right++;
        Assert.True(right >= 17, $"{right}/20");
        // And the built-in ones still are.
        Assert.True(Enumerable.Range(0, 10).Count(i => MouseGestures.Recognize(Hand(MouseGestures.Path("Z"), random), [gesture], out _) == "Z") >= 8);
    }

    [Fact]
    public void HotkeysAreShownInTheLanguage()
    {
        Assert.True(Hotkey.IsGesture(Hotkey.Normalize("Gesture C")));
        Assert.True(Hotkey.IsCorner(Hotkey.Normalize(HotCorners.Hotkey(ScreenCorner.TopLeft))));
        Assert.Equal("Corner TopLeft", Hotkey.Normalize("Corner TopLeft"));
        Assert.Equal("Gesture Left", Hotkey.Normalize("Gesture Left"));
        Assert.Contains("↖", Hotkey.Display("Corner TopLeft"), StringComparison.Ordinal);
        Assert.Contains("←", Hotkey.Display("Gesture Left"), StringComparison.Ordinal);
        Assert.Equal("CTRL + A", Hotkey.Display("CTRL + A"));
    }

    // ── corners ──────────────────────────────────────────────────────────

    [Fact]
    public void OneScreenHasFourCorners()
    {
        ScreenRect[] one = [new(0, 0, 1920, 1080)];
        Assert.Equal(ScreenCorner.TopLeft, HotCorners.At(0, 0, one));
        Assert.Equal(ScreenCorner.TopRight, HotCorners.At(1919, 1, one));
        Assert.Equal(ScreenCorner.BottomLeft, HotCorners.At(2, 1079, one));
        Assert.Equal(ScreenCorner.BottomRight, HotCorners.At(1919, 1079, one));
        Assert.Null(HotCorners.At(10, 0, one));
        Assert.Null(HotCorners.At(960, 540, one));
    }

    [Fact]
    public void WhereTwoScreensMeetIsNoCornerButALooseOneIs()
    {
        // 1080 high on the left, 1440 high on the right, tops aligned.
        ScreenRect[] two = [new(0, 0, 1920, 1080), new(1920, 0, 2560, 1440)];
        Assert.Null(HotCorners.At(1919, 0, two));      // the top where they meet: the cursor goes on
        Assert.Null(HotCorners.At(1920, 0, two));
        Assert.Null(HotCorners.At(1919, 1079, two));   // the left one's bottom right: to the right there is the other one
        Assert.Equal(ScreenCorner.BottomLeft, HotCorners.At(1920, 1439, two)); // loose: nothing to the left down there
        Assert.Equal(ScreenCorner.TopLeft, HotCorners.At(0, 0, two));
        Assert.Equal(ScreenCorner.BottomRight, HotCorners.At(4479, 1439, two));
    }

    [Fact]
    public void ShiftedScreensKeepTheirLooseCorners()
    {
        // The right one 300 pixels lower than the left one, both 1080 high.
        ScreenRect[] two = [new(0, 0, 1920, 1080), new(1920, 300, 1920, 1080)];
        Assert.Equal(ScreenCorner.TopRight, HotCorners.At(1919, 0, two));     // above the right one there is nothing
        Assert.Equal(ScreenCorner.BottomLeft, HotCorners.At(1920, 1379, two)); // below the left one there is nothing
        Assert.Null(HotCorners.At(1920, 300, two));                            // the left one is just there
    }

    [Fact]
    public void TheIndexKnowsCornersAndGestures()
    {
        var corner = new Command { Name = "esquina" };
        corner.Hotkeys.Add("Corner TopLeft");
        var gesture = new Command { Name = "gesto" };
        gesture.Hotkeys.Add("Gesture C");
        var index = new HotkeyIndex([corner, gesture], new AppSettings());
        Assert.True(index.HasCorners);
        Assert.True(index.HasGestures);
        Assert.Equal(["esquina"], index.Resolve("Corner TopLeft", "notepad", out _).Select(c => c.Name));
        Assert.False(new HotkeyIndex([corner, gesture], new AppSettings { MouseGestures = false }).HasGestures);
        Assert.False(new HotkeyIndex([corner], new AppSettings()).HasGestures);
        Assert.False(new HotkeyIndex([corner, gesture], new AppSettings(), paused: true).HasCorners);
    }
}

using KDesktop.Core.Model;

namespace KDesktop.Core.Input;

/// <summary>A point of a stroke, in screen pixels (or in any unit: only its shape counts).</summary>
public readonly record struct GesturePoint(double X, double Y);

/// <summary>
/// Gestos del ratón (E4): drawings of one stroke (C, N, Z, L, arrows…) made holding a mouse button. The recogniser is a $1
/// unistroke one without rotation (a C turned is a U): the stroke is resampled to <see cref="Samples"/> points, scaled to a
/// square (a line, only along its length) and centred, then compared point by point with each drawing. Below
/// <see cref="Accept"/>, or too close to another drawing, it is none: better nothing than the wrong command.
/// </summary>
public static class MouseGestures
{
    public const int Samples = 64;

    /// <summary>Smaller than this (pixels, its longer side) is a twitch, not a drawing.</summary>
    public const double MinSize = 30;

    /// <summary>The score (0–1) a stroke needs to be a drawing.</summary>
    public const double Accept = 0.79;

    /// <summary>What the best drawing has to win by over the next different one.</summary>
    private const double Margin = 0.025;

    /// <summary>A stroke thinner than this (short side / long side) is a line: scaled along its length only.</summary>
    private const double LineRatio = 0.3;

    private static readonly double HalfDiagonal = 0.5 * Math.Sqrt(2);

    /// <summary>The built-in drawings, in the order the picker shows them.</summary>
    public static readonly IReadOnlyList<string> BuiltIn = ["Left", "Right", "Up", "Down", "C", "L", "N", "S", "U", "V", "Caret", "W", "M", "Z", "O"];

    private static readonly Lazy<(string Id, GesturePoint[] Shape)[]> Templates = new(BuildTemplates);

    /// <summary>How a drawing is written in a hotkey and shown: an arrow for the lines, Λ for the caret.</summary>
    public static string Symbol(string id) => id switch
    {
        "Left" => "←",
        "Right" => "→",
        "Up" => "↑",
        "Down" => "↓",
        "Caret" => "Λ",
        _ => id,
    };

    public static bool IsBuiltIn(string id) => BuiltIn.Contains(id, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The drawing of a stroke (a built-in id or the name of one of <paramref name="custom"/>), or null when it is not one
    /// clearly. <paramref name="score"/>: how sure (0–1).
    /// </summary>
    public static string? Recognize(IReadOnlyList<GesturePoint> stroke, IEnumerable<CustomGesture>? custom, out double score)
    {
        score = 0;
        if (Normalize(stroke) is not { } shape) return null;
        string? best = null, second = null;
        double bestScore = 0, secondScore = 0;
        void Try(string id, GesturePoint[] template)
        {
            var s = Likeness(shape, template);
            if (s > bestScore)
            {
                if (!string.Equals(best, id, StringComparison.OrdinalIgnoreCase)) (second, secondScore) = (best, bestScore);
                (best, bestScore) = (id, s);
            }
            else if (s > secondScore && !string.Equals(best, id, StringComparison.OrdinalIgnoreCase)) (second, secondScore) = (id, s);
        }
        foreach (var (id, template) in Templates.Value) Try(id, template);
        foreach (var gesture in custom ?? [])
            foreach (var sample in gesture.Samples)
                if (FromFlat(sample) is { } template) Try(gesture.Name, template);
        score = bestScore;
        if (best is null || bestScore < Accept) return null;
        return second is not null && bestScore - secondScore < Margin ? null : best;
    }

    /// <summary>
    /// The stroke resampled, scaled and centred, ready to compare; null when it is too short to be a drawing (a click with
    /// a little movement).
    /// </summary>
    public static GesturePoint[]? Normalize(IReadOnlyList<GesturePoint> stroke)
    {
        if (stroke.Count < 2) return null;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in stroke)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        double width = maxX - minX, height = maxY - minY, size = Math.Max(width, height);
        if (size < MinSize) return null;
        var points = Resample(stroke);
        // A line keeps its proportions (so a slanted line stays a line); anything else fills the square.
        var line = Math.Min(width, height) / size < LineRatio;
        double sx = line ? size : Math.Max(width, 1e-6), sy = line ? size : Math.Max(height, 1e-6);
        double cx = 0, cy = 0;
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = new GesturePoint((points[i].X - minX) / sx, (points[i].Y - minY) / sy);
            cx += points[i].X;
            cy += points[i].Y;
        }
        cx /= points.Length;
        cy /= points.Length;
        for (var i = 0; i < points.Length; i++) points[i] = new GesturePoint(points[i].X - cx, points[i].Y - cy);
        return points;
    }

    /// <summary>A normalised stroke as numbers (x, y, x, y…) to keep it in the settings.</summary>
    public static double[] ToFlat(GesturePoint[] shape)
    {
        var flat = new double[shape.Length * 2];
        for (var i = 0; i < shape.Length; i++)
        {
            flat[2 * i] = Math.Round(shape[i].X, 4);
            flat[2 * i + 1] = Math.Round(shape[i].Y, 4);
        }
        return flat;
    }

    /// <summary>A stroke kept by <see cref="ToFlat"/>, back as points (null when it is not one).</summary>
    public static GesturePoint[]? FromFlat(double[] flat)
    {
        if (flat.Length != Samples * 2) return null;
        var shape = new GesturePoint[Samples];
        for (var i = 0; i < Samples; i++) shape[i] = new GesturePoint(flat[2 * i], flat[2 * i + 1]);
        return shape;
    }

    /// <summary>How alike two normalised strokes are: 1 the same, 0 nothing alike.</summary>
    public static double Likeness(GesturePoint[] a, GesturePoint[] b)
    {
        double sum = 0;
        for (var i = 0; i < Samples; i++) sum += Math.Sqrt((a[i].X - b[i].X) * (a[i].X - b[i].X) + (a[i].Y - b[i].Y) * (a[i].Y - b[i].Y));
        return Math.Max(0, 1 - sum / Samples / HalfDiagonal);
    }

    /// <summary><see cref="Samples"/> points evenly spaced along the stroke.</summary>
    private static GesturePoint[] Resample(IReadOnlyList<GesturePoint> stroke)
    {
        double length = 0;
        for (var i = 1; i < stroke.Count; i++) length += Distance(stroke[i - 1], stroke[i]);
        var step = length / (Samples - 1);
        var result = new GesturePoint[Samples];
        result[0] = stroke[0];
        var count = 1;
        double carried = 0;
        var previous = stroke[0];
        for (var i = 1; i < stroke.Count && count < Samples; i++)
        {
            var current = stroke[i];
            var d = Distance(previous, current);
            while (d > 0 && carried + d >= step && count < Samples)
            {
                var t = (step - carried) / d;
                var point = new GesturePoint(previous.X + t * (current.X - previous.X), previous.Y + t * (current.Y - previous.Y));
                result[count++] = point;
                d = Distance(point, current);
                previous = point;
                carried = 0;
            }
            carried += d;
            previous = current;
        }
        while (count < Samples) result[count++] = stroke[^1];
        return result;
    }

    private static double Distance(GesturePoint a, GesturePoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>
    /// The path of a built-in drawing in a square of side 1 (y down), as it is drawn: the picker draws it and the
    /// recogniser compares with it.
    /// </summary>
    public static IReadOnlyList<GesturePoint> Path(string id) => id switch
    {
        "Left" => [new(1, 0.5), new(0, 0.5)],
        "Right" => [new(0, 0.5), new(1, 0.5)],
        "Up" => [new(0.5, 1), new(0.5, 0)],
        "Down" => [new(0.5, 0), new(0.5, 1)],
        "L" => [new(0.15, 0), new(0.15, 1), new(0.85, 1)],
        "N" => [new(0, 1), new(0, 0), new(1, 1), new(1, 0)],
        "V" => [new(0, 0), new(0.5, 1), new(1, 0)],
        "Caret" => [new(0, 1), new(0.5, 0), new(1, 1)],
        "W" => [new(0, 0), new(0.25, 1), new(0.5, 0.35), new(0.75, 1), new(1, 0)],
        "M" => [new(0, 1), new(0, 0), new(0.5, 0.65), new(1, 0), new(1, 1)],
        "Z" => [new(0, 0), new(1, 0), new(0, 1), new(1, 1)],
        // From the top right, round by the left to the bottom right.
        "C" => Arc(0.5, 0.5, 0.5, 0.5, -55, -305),
        // From the top, anticlockwise all the way round.
        "O" => Arc(0.5, 0.5, 0.5, 0.5, -90, -450),
        "U" => [new(0, 0), .. Arc(0.5, 0.5, 0.5, 0.5, 180, 0), new(1, 0)],
        // The top half anticlockwise to the middle, the bottom half clockwise to the bottom left.
        "S" => [.. Arc(0.5, 0.25, 0.4, 0.25, -20, -270), .. Arc(0.5, 0.75, 0.4, 0.25, -90, 160)],
        _ => [],
    };

    /// <summary>Points of an ellipse from one angle to another (degrees; y down, so positive angles go down).</summary>
    private static GesturePoint[] Arc(double cx, double cy, double rx, double ry, double from, double to)
    {
        const int steps = 32;
        var points = new GesturePoint[steps + 1];
        for (var i = 0; i <= steps; i++)
        {
            var angle = (from + (to - from) * i / steps) * Math.PI / 180;
            points[i] = new GesturePoint(cx + rx * Math.Cos(angle), cy + ry * Math.Sin(angle));
        }
        return points;
    }

    /// <summary>Each drawing and, for the letters, the same drawn backwards (where to start a C is up to each one).</summary>
    private static (string, GesturePoint[])[] BuildTemplates()
    {
        var result = new List<(string, GesturePoint[])>();
        foreach (var id in BuiltIn)
        {
            // In pixels, so that the minimum size never leaves one out.
            var path = Path(id).Select(p => new GesturePoint(p.X * 200, p.Y * 200)).ToList();
            if (Normalize(path) is { } forward) result.Add((id, forward));
            if (id is "Left" or "Right" or "Up" or "Down") continue;
            path.Reverse();
            if (Normalize(path) is { } backward) result.Add((id, backward));
        }
        return [.. result];
    }
}

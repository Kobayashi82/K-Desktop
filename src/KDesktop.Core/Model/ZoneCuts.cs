namespace KDesktop.Core.Model;

/// <summary>The templates to cut a monitor with («2 columnas», «Cuadrícula»…).</summary>
public enum ZoneTemplate { Full, Columns2, Columns3, Grid, BigAndTwo, Rows2 }

/// <summary>
/// A line between zones that can be dragged (E1b): where it is (x for a vertical one, y for a horizontal one), from where to
/// where it goes, and the zones on each side. With one zone on each side and the same span, the two can be joined.
/// </summary>
public sealed record ZoneLine(bool Vertical, double At, double From, double To, IReadOnlyList<ArrangedZone> Before, IReadOnlyList<ArrangedZone> After)
{
    public bool CanJoin => Before.Count == 1 && After.Count == 1 &&
        (Vertical
            ? Math.Abs(Before[0].Top - After[0].Top) < ZoneCuts.Near && Math.Abs(Before[0].Height - After[0].Height) < ZoneCuts.Near
            : Math.Abs(Before[0].Left - After[0].Left) < ZoneCuts.Near && Math.Abs(Before[0].Width - After[0].Width) < ZoneCuts.Near);
}

/// <summary>
/// How the zones of a monitor are cut (E1b), without a window: templates, cutting a zone in two, dragging the lines between
/// zones and joining two. Everything in fractions of the monitor's work area.
/// </summary>
public static class ZoneCuts
{
    /// <summary>Two edges this close are the same line.</summary>
    public const double Near = 0.003;

    /// <summary>The smallest a zone gets (in fraction of the monitor).</summary>
    public const double Smallest = 0.05;

    /// <summary>The rectangles of a template.</summary>
    public static IReadOnlyList<(double Left, double Top, double Width, double Height)> Rects(ZoneTemplate template)
    {
        const double third = 1d / 3, twoThirds = 2d / 3;
        return template switch
        {
            ZoneTemplate.Columns2 => [(0, 0, 0.5, 1), (0.5, 0, 0.5, 1)],
            ZoneTemplate.Columns3 => [(0, 0, third, 1), (third, 0, third, 1), (twoThirds, 0, 1 - twoThirds, 1)],
            ZoneTemplate.Grid => [(0, 0, 0.5, 0.5), (0.5, 0, 0.5, 0.5), (0, 0.5, 0.5, 0.5), (0.5, 0.5, 0.5, 0.5)],
            ZoneTemplate.BigAndTwo => [(0, 0, twoThirds, 1), (twoThirds, 0, 1 - twoThirds, 0.5), (twoThirds, 0.5, 1 - twoThirds, 0.5)],
            ZoneTemplate.Rows2 => [(0, 0, 1, 0.5), (0, 0.5, 1, 0.5)],
            _ => [(0, 0, 1, 1)],
        };
    }

    /// <summary>
    /// Cuts a monitor with a template: its zones (<paramref name="old"/>, out of <paramref name="zones"/>) are replaced, and the programs each had go to the new zone where its middle
    /// falls, so choosing another template does not lose them.
    /// </summary>
    public static List<ArrangedZone> Apply(ZoneTemplate template, IList<ArrangedZone> zones, IReadOnlyList<ArrangedZone> old, string monitorName, string monitorDevice)
    {
        var fresh = Rects(template).Select(r =>
        {
            var zone = new ArrangedZone { MonitorName = monitorName, MonitorDevice = monitorDevice };
            zone.SetRect(r.Left, r.Top, r.Width, r.Height);
            return zone;
        }).ToList();
        foreach (var zone in old)
        {
            var (x, y) = (zone.Left + zone.Width / 2, zone.Top + zone.Height / 2);
            var target = fresh.FirstOrDefault(z => x >= z.Left && x <= z.Right && y >= z.Top && y <= z.Bottom) ?? fresh[0];
            foreach (var program in zone.Programs)
                if (!target.Programs.Any(p => p.Process == program.Process && p.Title == program.Title)) target.Programs.Add(program);
            zones.Remove(zone);
        }
        foreach (var zone in fresh) zones.Add(zone);
        return fresh;
    }

    /// <summary>
    /// Cuts a zone in two at <paramref name="at"/> (a fraction of the monitor: x when <paramref name="vertical"/>, else y):
    /// it keeps the left or top part and its programs; the other part is a new zone, added after it. Null when one part
    /// would be too small.
    /// </summary>
    public static ArrangedZone? Split(IList<ArrangedZone> zones, ArrangedZone zone, bool vertical, double at)
    {
        at = Math.Round(at, 4);
        var (start, size) = vertical ? (zone.Left, zone.Width) : (zone.Top, zone.Height);
        if (at - start < Smallest || start + size - at < Smallest) return null;
        var other = new ArrangedZone { MonitorName = zone.MonitorName, MonitorDevice = zone.MonitorDevice };
        if (vertical)
        {
            other.SetRect(at, zone.Top, zone.Right - at, zone.Height);
            zone.Width = Math.Round(at - zone.Left, 4);
        }
        else
        {
            other.SetRect(zone.Left, at, zone.Width, zone.Bottom - at);
            zone.Height = Math.Round(at - zone.Top, 4);
        }
        zones.Insert(zones.IndexOf(zone) + 1, other);
        return other;
    }

    /// <summary>
    /// The lines between the zones of a monitor: where the right (or bottom) edge of some zones meets the left (or top)
    /// edge of others. Zones that only touch at a corner make separate lines (each half of a grid moves on its own).
    /// </summary>
    public static List<ZoneLine> Lines(IReadOnlyList<ArrangedZone> zones)
    {
        var lines = new List<ZoneLine>();
        Collect(true);
        Collect(false);
        return lines;

        void Collect(bool vertical)
        {
            double End(ArrangedZone z) => vertical ? z.Right : z.Bottom;
            double Start(ArrangedZone z) => vertical ? z.Left : z.Top;
            double SpanFrom(ArrangedZone z) => vertical ? z.Top : z.Left;
            double SpanTo(ArrangedZone z) => vertical ? z.Bottom : z.Right;
            var places = zones.Select(End).Where(x => x > Near && x < 1 - Near).OrderBy(x => x).ToList();
            var seen = new List<double>();
            foreach (var at in places)
            {
                if (seen.Any(s => Math.Abs(s - at) < Near)) continue;
                seen.Add(at);
                var before = zones.Where(z => Math.Abs(End(z) - at) < Near).ToList();
                var after = zones.Where(z => Math.Abs(Start(z) - at) < Near).ToList();
                if (after.Count == 0) continue;
                // Groups: zones joined by overlapping spans across the line.
                var left = before.Concat(after).ToList();
                while (left.Count > 0)
                {
                    var group = new List<ArrangedZone> { left[0] };
                    left.RemoveAt(0);
                    for (var grew = true; grew;)
                    {
                        grew = false;
                        for (var i = left.Count - 1; i >= 0; i--)
                        {
                            var z = left[i];
                            var isBefore = before.Contains(z);
                            // Across the line (one before, one after) with a real overlap.
                            if (!group.Any(g => before.Contains(g) != isBefore && SpanFrom(g) < SpanTo(z) - Near && SpanFrom(z) < SpanTo(g) - Near)) continue;
                            group.Add(z);
                            left.RemoveAt(i);
                            grew = true;
                        }
                    }
                    var b = group.Where(before.Contains).ToList();
                    var a = group.Where(after.Contains).ToList();
                    if (b.Count == 0 || a.Count == 0) continue;
                    lines.Add(new ZoneLine(vertical, at, group.Min(SpanFrom), group.Max(SpanTo), b, a));
                }
            }
        }
    }

    /// <summary>How far a line can go, so no zone on either side gets smaller than <see cref="Smallest"/>.</summary>
    public static (double Min, double Max) Range(ZoneLine line) => line.Vertical
        ? (line.Before.Max(z => z.Left) + Smallest, line.After.Min(z => z.Right) - Smallest)
        : (line.Before.Max(z => z.Top) + Smallest, line.After.Min(z => z.Bottom) - Smallest);

    /// <summary>Moves a line (all the zones on both sides) to <paramref name="at"/>, kept in its <see cref="Range"/>; the line as it is now.</summary>
    public static ZoneLine Move(ZoneLine line, double at)
    {
        var (min, max) = Range(line);
        at = Math.Round(Math.Clamp(at, min, Math.Max(min, max)), 4);
        foreach (var z in line.Before)
            if (line.Vertical) z.Width = Math.Round(at - z.Left, 4);
            else z.Height = Math.Round(at - z.Top, 4);
        foreach (var z in line.After)
            if (line.Vertical)
            {
                var right = z.Right;
                z.Left = at;
                z.Width = Math.Round(right - at, 4);
            }
            else
            {
                var bottom = z.Bottom;
                z.Top = at;
                z.Height = Math.Round(bottom - at, 4);
            }
        return line with { At = at };
    }

    /// <summary>A free strip at least this wide (of the monitor) next to the windows becomes an empty zone; a narrower one, part of its neighbour.</summary>
    public const double EmptyStrip = 0.15;

    /// <summary>Two windows are cut apart only if neither loses more than this part of itself; else they share a zone.</summary>
    public const double MostLost = 0.3;

    /// <summary>The zones of a monitor cover it whole without one over another (as the cuts make them).</summary>
    public static bool IsCut(IReadOnlyList<ArrangedZone> zones)
    {
        if (zones.Count == 0) return true;
        if (Math.Abs(zones.Sum(z => z.Width * z.Height) - 1) > 0.01) return false;
        for (var i = 0; i < zones.Count; i++)
            for (var j = i + 1; j < zones.Count; j++)
                if (Overlap(zones[i], zones[j]) > Near) return false;
        return zones.All(z => z.Left >= -Near && z.Top >= -Near && z.Right <= 1 + Near && z.Bottom <= 1 + Near);
    }

    private static double Overlap(ArrangedZone a, ArrangedZone b) =>
        Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left)) * Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top));

    /// <summary>
    /// Cuts a monitor out of rectangles that may overlap or leave gaps (the windows open now, or the zones of 5.0.375), so
    /// the zones cover it whole without one over another: each cut goes between windows (half way where they overlap a
    /// little); windows that cannot be cut apart without losing much of one share a zone; a wide free strip is an empty
    /// zone, a narrow one goes to its neighbour. The programs go with their window, in order.
    /// </summary>
    public static List<ArrangedZone> Tile(IReadOnlyList<ArrangedZone> pieces, string monitorName, string monitorDevice)
    {
        var result = new List<ArrangedZone>();
        if (pieces.Count > 0) Cut(0, 0, 1, 1, [.. pieces]);
        return result;

        void Cut(double l, double t, double r, double b, List<ArrangedZone> inside)
        {
            if (inside.Count == 0)
            {
                Add(l, t, r, b, inside);
                return;
            }
            // A wide free strip next to the windows: an empty zone (the widest first).
            var (bl, bt, br, bb) = (Math.Max(l, inside.Min(p => p.Left)), Math.Max(t, inside.Min(p => p.Top)),
                Math.Min(r, inside.Max(p => p.Right)), Math.Min(b, inside.Max(p => p.Bottom)));
            var gaps = new[] { (Gap: bl - l, Vertical: true, At: bl), (Gap: r - br, Vertical: true, At: br), (Gap: bt - t, Vertical: false, At: bt), (Gap: b - bb, Vertical: false, At: bb) };
            var gap = gaps.MaxBy(g => g.Gap);
            if (gap.Gap >= EmptyStrip)
            {
                var at = Snap(gap.At, gap.Vertical ? l : t, gap.Vertical ? r : b);
                Split2(gap.Vertical, at);
                return;
            }
            if (inside.Count > 1 && BestCut(l, t, r, b, inside) is { } cut)
            {
                Split2(cut.Vertical, cut.At);
                return;
            }
            Add(l, t, r, b, inside);

            void Split2(bool vertical, double at)
            {
                bool Before(ArrangedZone p) => (vertical ? p.Left + p.Width / 2 : p.Top + p.Height / 2) < at;
                if (vertical)
                {
                    Cut(l, t, at, b, [.. inside.Where(Before)]);
                    Cut(at, t, r, b, [.. inside.Where(p => !Before(p))]);
                }
                else
                {
                    Cut(l, t, r, at, [.. inside.Where(Before)]);
                    Cut(l, at, r, b, [.. inside.Where(p => !Before(p))]);
                }
            }
        }

        void Add(double l, double t, double r, double b, List<ArrangedZone> inside)
        {
            var zone = new ArrangedZone { MonitorName = monitorName, MonitorDevice = monitorDevice };
            zone.SetRect(l, t, r - l, b - t);
            foreach (var program in inside.SelectMany(p => p.Programs))
                if (!zone.Programs.Any(p => p.Process == program.Process && p.Title == program.Title)) zone.Programs.Add(program.Clone());
            result.Add(zone);
        }
    }

    /// <summary>The cut that parts the windows losing the least of any of them (null when every one loses too much).</summary>
    private static (bool Vertical, double At)? BestCut(double l, double t, double r, double b, List<ArrangedZone> inside)
    {
        (bool Vertical, double At, double Lost)? best = null;
        foreach (var vertical in new[] { true, false })
        {
            double Mid(ArrangedZone p) => vertical ? p.Left + p.Width / 2 : p.Top + p.Height / 2;
            double Start(ArrangedZone p) => Math.Max(vertical ? l : t, vertical ? p.Left : p.Top);
            double End(ArrangedZone p) => Math.Min(vertical ? r : b, vertical ? p.Right : p.Bottom);
            var (from, to) = vertical ? (l, r) : (t, b);
            var sorted = inside.OrderBy(Mid).ToList();
            for (var i = 1; i < sorted.Count; i++)
            {
                if (Mid(sorted[i]) - Mid(sorted[i - 1]) < Near) continue;
                var before = sorted.Take(i).ToList();
                var after = sorted.Skip(i).ToList();
                // Half way between the windows on each side (in the gap, or in the middle of where they overlap).
                var at = Math.Clamp((before.Max(End) + after.Min(Start)) / 2, Mid(sorted[i - 1]), Mid(sorted[i]));
                at = Snap(at, from, to);
                if (at - from < Smallest || to - at < Smallest) continue;
                double LostOf(ArrangedZone p, bool isBefore)
                {
                    var size = Math.Max(Near, End(p) - Start(p));
                    return Math.Clamp(isBefore ? End(p) - at : at - Start(p), 0, size) / size;
                }
                var lost = Math.Max(before.Max(p => LostOf(p, true)), after.Max(p => LostOf(p, false)));
                if (lost <= MostLost && (best is null || lost < best.Value.Lost)) best = (vertical, at, lost);
            }
        }
        return best is { } found ? (found.Vertical, found.At) : null;
    }

    /// <summary>A cut near ½, ⅓, ¼, ⅔ or ¾ of the monitor goes there (as the magnet when dragging), kept inside its zone.</summary>
    private static double Snap(double at, double from, double to)
    {
        foreach (var nice in (double[])[0.5, 1d / 3, 2d / 3, 0.25, 0.75])
            if (Math.Abs(at - nice) < 0.02 && nice - from >= Smallest && to - nice >= Smallest) return Math.Round(nice, 4);
        return Math.Round(Math.Clamp(at, from + Smallest, Math.Max(from + Smallest, to - Smallest)), 4);
    }

    /// <summary>
    /// Mends the zones of each monitor that do not cover it whole or have one over another (saved by 5.0.376), with
    /// <see cref="Tile"/>. Returns true when something changed.
    /// </summary>
    public static bool Mend(IList<ArrangedZone> zones)
    {
        var changed = false;
        foreach (var monitor in zones.GroupBy(z => (Name: z.MonitorName.ToUpperInvariant(), Device: z.MonitorDevice.ToUpperInvariant())).ToList())
        {
            var mine = monitor.ToList();
            if (IsCut(mine)) continue;
            var at = zones.IndexOf(mine[0]);
            foreach (var zone in mine) zones.Remove(zone);
            foreach (var zone in Tile(mine, mine[0].MonitorName, mine[0].MonitorDevice)) zones.Insert(Math.Min(at++, zones.Count), zone);
            changed = true;
        }
        return changed;
    }

    /// <summary>Joins the two zones of a line (<see cref="ZoneLine.CanJoin"/>): the first one grows over the other and takes its programs.</summary>
    public static ArrangedZone? Join(IList<ArrangedZone> zones, ZoneLine line)
    {
        if (!line.CanJoin) return null;
        var (keep, gone) = (line.Before[0], line.After[0]);
        if (line.Vertical) keep.Width = Math.Round(gone.Right - keep.Left, 4);
        else keep.Height = Math.Round(gone.Bottom - keep.Top, 4);
        foreach (var program in gone.Programs) keep.Programs.Add(program);
        zones.Remove(gone);
        return keep;
    }
}

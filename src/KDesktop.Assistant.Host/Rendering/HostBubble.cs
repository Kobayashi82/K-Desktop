using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Host.Rendering;

/// <summary>
/// The module's speech bubble (stage 3.6, plan §11.1: the bubble is the host's): what an assistant says, over it,
/// pointing to its <see cref="IAssistant.BubbleAnchor"/> and following it while it moves, for as long as it takes to
/// read. Rounded, with a tail, in the theme of K-Desktop; it pops in and fades out. Below the character when there is no
/// room above, and always inside the work area of its monitor. A long answer is cut (the whole of it is in the chat).
/// Engine thread.
/// </summary>
internal sealed class HostBubble : IDisposable
{
    /// <summary>Width of the text, in DIPs at 100 %.</summary>
    private const double TextWidth = 300;

    private const double FontSize = 14;
    private const double Padding = 12;
    private const double Corner = 12;
    private const double Tail = 9;
    private const int MaxLines = 7;
    private const double PopIn = 0.22;
    private const double FadeOut = 0.25;

    private static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private readonly ISurface _surface;
    private readonly string _text;
    private readonly bool _dark;
    private readonly double _start;
    private double _end;
    private (Point Anchor, double Alpha, double Grow, double Scale) _drawn = (default, -1, -1, 0);

    public HostBubble(ISurface surface, string text, bool dark, double now, double seconds)
    {
        _surface = surface;
        _text = text;
        _dark = dark;
        _start = now;
        _end = now + seconds;
    }

    /// <summary>Gone (faded out).</summary>
    public bool IsOver(double now) => now >= _end + FadeOut;

    /// <summary>Starts fading out now (the order was replaced or cancelled, or the gesture ended early).</summary>
    public void Close(double now) => _end = Math.Min(_end, now);

    /// <summary>Stays at least until then (its voice is still saying it).</summary>
    public void KeepUntil(double end) => _end = Math.Max(_end, end);

    /// <summary>
    /// Draws it again if it moved or is animating, and returns the seconds until it needs another look: every frame
    /// while it animates, a few times per second otherwise (to follow the character, whose own ticks move it).
    /// </summary>
    public double Update(double now, Point? anchor, MonitorInfo? monitor, double frame)
    {
        if (anchor is not { } at || monitor is null)
        {
            _surface.Hide();
            _drawn = (default, -1, -1, 0);
            return 0.1;
        }
        var age = now - _start;
        var grow = EaseOutBack(Math.Clamp(age / PopIn, 0, 1));
        var alpha = Math.Clamp(age / 0.12, 0, 1) * Math.Clamp((_end + FadeOut - now) / FadeOut, 0, 1);
        var scale = monitor.DpiScale;
        var animating = age < PopIn || now > _end;
        if (Math.Abs(at.X - _drawn.Anchor.X) >= 0.5 || Math.Abs(at.Y - _drawn.Anchor.Y) >= 0.5 || Math.Abs(alpha - _drawn.Alpha) > 0.004
            || Math.Abs(grow - _drawn.Grow) > 0.004 || scale != _drawn.Scale)
        {
            Draw(at, monitor.WorkArea, scale, alpha, grow);
            _drawn = (at, alpha, grow, scale);
        }
        return animating ? frame : Math.Min(0.1, Math.Max(frame, _end - now));
    }

    private void Draw(Point anchor, PixelRect work, double scale, double alpha, double grow)
    {
        var ink = _dark ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x1B, 0x1B, 0x1F);
        var text = new FormattedText(_text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, FontSize * scale, Brush(ink), scale)
        {
            MaxTextWidth = TextWidth * scale,
            MaxLineCount = MaxLines,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Left,
        };
        var pad = Padding * scale;
        var tail = Tail * scale;
        var width = Math.Ceiling(text.WidthIncludingTrailingWhitespace + 2 * pad);
        var height = Math.Ceiling(text.Height + 2 * pad);
        var gap = 4 * scale;

        // Above the anchor if it fits, below it otherwise; sideways inside the work area, the tail still on the anchor.
        var above = anchor.Y - gap - tail - height >= work.Top;
        var top = above ? anchor.Y - gap - tail - height : anchor.Y + gap + tail;
        var left = Math.Clamp(anchor.X - width / 2, work.Left + gap, Math.Max(work.Left + gap, work.Right - gap - width));
        var tip = new Point(Math.Clamp(anchor.X, left + Corner * scale + tail, left + width - Corner * scale - tail), above ? anchor.Y - gap : anchor.Y + gap);
        var body = new Rect(left, top, width, height);

        var shadowPad = (int)Math.Ceiling(10 * scale);
        var area = new PixelRect((int)Math.Floor(left) - shadowPad, (int)Math.Floor(Math.Min(top, tip.Y)) - shadowPad,
            (int)Math.Ceiling(left + width) + shadowPad, (int)Math.Ceiling(Math.Max(top + height, tip.Y)) + shadowPad);
        var frame = Frame(body, tip, above, Corner * scale, tail);
        var fill = _dark ? Color.FromArgb(246, 0x2B, 0x2B, 0x2E) : Color.FromArgb(250, 0xFF, 0xFF, 0xFF);
        var stroke = new Pen(Brush(_dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0)), Math.Max(1, scale));
        stroke.Freeze();
        var shadow = frame.Clone();
        shadow.Transform = new TranslateTransform(0, 2 * scale);
        shadow.Freeze();

        _surface.Present(area, dc =>
        {
            // It grows from the tip.
            dc.PushTransform(new ScaleTransform(0.6 + 0.4 * grow, 0.6 + 0.4 * grow, tip.X, tip.Y));
            dc.PushOpacity(alpha);
            dc.DrawGeometry(Brush(Color.FromArgb(_dark ? (byte)90 : (byte)40, 0, 0, 0)), null, shadow);
            dc.DrawGeometry(Brush(fill), stroke, frame);
            dc.DrawText(text, new Point(left + pad, top + pad));
            dc.Pop();
            dc.Pop();
        });
    }

    /// <summary>The rounded body and its tail as one shape.</summary>
    private static Geometry Frame(Rect body, Point tip, bool above, double corner, double tail)
    {
        var baseY = above ? body.Bottom - 1 : body.Top + 1;
        var tailShape = new StreamGeometry();
        using (var g = tailShape.Open())
        {
            g.BeginFigure(new Point(tip.X - tail, baseY), true, true);
            g.LineTo(tip, true, true);
            g.LineTo(new Point(tip.X + tail * 0.35, baseY), true, true);
        }
        var shape = new CombinedGeometry(GeometryCombineMode.Union, new RectangleGeometry(body, corner, corner), tailShape).GetFlattenedPathGeometry();
        shape.Freeze();
        return shape;
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    public void Dispose() => _surface.Dispose();
}

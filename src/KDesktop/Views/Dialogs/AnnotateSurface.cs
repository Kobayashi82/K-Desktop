using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Core.Platform;

namespace KDesktop.Views.Dialogs;

/// <summary>A tool of the capture editor (F1).</summary>
/// <summary>A tool of the capture editor (F1); «Copiar texto» draws nothing: it reads the zone dragged (or everything).</summary>
internal enum AnnotateTool { Arrow, Box, Text, Highlighter, Blur, Crop, CopyText }

/// <summary>Something drawn on the capture, in the picture's own pixels.</summary>
internal abstract record AnnotateMark
{
    public abstract void Draw(DrawingContext dc);

    protected static Pen PenOf(Color color, double width, PenLineJoin join = PenLineJoin.Round)
    {
        var pen = new Pen(new SolidColorBrush(color), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = join };
        pen.Freeze();
        return pen;
    }
}

internal sealed record ArrowMark(Point From, Point To, Color Color, double Width) : AnnotateMark
{
    public override void Draw(DrawingContext dc)
    {
        var v = To - From;
        if (v.Length < 1) return;
        v.Normalize();
        // The head: a triangle as long as 4.5 widths (at least 12 px), the line stopping inside it.
        var head = Math.Max(12, Width * 4.5);
        var side = new Vector(-v.Y, v.X) * head * 0.55;
        var baseCenter = To - v * head;
        var brush = new SolidColorBrush(Color);
        brush.Freeze();
        dc.DrawLine(PenOf(Color, Width), From, baseCenter + v * (head * 0.3));
        var tip = new StreamGeometry();
        using (var g = tip.Open())
        {
            g.BeginFigure(To, isFilled: true, isClosed: true);
            g.LineTo(baseCenter + side, true, true);
            g.LineTo(baseCenter - side, true, true);
        }
        tip.Freeze();
        dc.DrawGeometry(brush, PenOf(Color, Math.Max(1, Width / 3)), tip);
    }
}

internal sealed record BoxMark(Rect Area, Color Color, double Width) : AnnotateMark
{
    public override void Draw(DrawingContext dc) => dc.DrawRoundedRectangle(null, PenOf(Color, Width), Area, Width, Width);
}

internal sealed record HighlightMark(IReadOnlyList<Point> Points, Color Color, double Width) : AnnotateMark
{
    public override void Draw(DrawingContext dc)
    {
        if (Points.Count == 0) return;
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(Points[0], isFilled: false, isClosed: false);
            if (Points.Count == 1) g.LineTo(Points[0] + new Vector(0.1, 0), true, true);
            else g.PolyLineTo([.. Points.Skip(1)], true, true);
        }
        line.Freeze();
        // A marker: see-through, with square-ish ends.
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(110, Color.R, Color.G, Color.B)), Width)
        {
            StartLineCap = PenLineCap.Square, EndLineCap = PenLineCap.Square, LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);
    }
}

internal sealed record TextMark(Point At, string Text, Color Color, double Size) : AnnotateMark
{
    public static FormattedText Format(string text, Color color, double size) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            size, new SolidColorBrush(color), 1.0);

    public override void Draw(DrawingContext dc)
    {
        dc.DrawText(Format(Text, Color, Size), At);
    }
}

/// <summary>«Difuminar»: the blurred pixels of its rectangle (computed once, from the capture).</summary>
internal sealed record BlurMark(Int32Rect Area, BitmapSource Patch) : AnnotateMark
{
    public override void Draw(DrawingContext dc) => dc.DrawImage(Patch, new Rect(Area.X, Area.Y, Area.Width, Area.Height));
}

/// <summary>«Recortar»: what is kept (the last one counts).</summary>
internal sealed record CropMark(Int32Rect Area) : AnnotateMark
{
    public override void Draw(DrawingContext dc) { }
}

/// <summary>
/// The picture of the capture editor with what is drawn on it, fitted to the space (never enlarged), and the final picture
/// at its own size. While cropping it shows the whole capture with what is left out darkened; otherwise only what is kept.
/// </summary>
internal sealed class AnnotateSurface : FrameworkElement
{
    private BitmapSource? _picture;

    public BgraImage? Source { get; private set; }

    public List<AnnotateMark> Marks { get; } = [];

    /// <summary>What is being drawn now (shown, not yet in <see cref="Marks"/>).</summary>
    public AnnotateMark? Drawing { get; set; }

    /// <summary>The whole capture, to crop it.</summary>
    public bool ShowWhole { get; set; }

    public void Load(BgraImage image)
    {
        Source = image;
        _picture = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Stride);
        _picture.Freeze();
        InvalidateVisual();
    }

    /// <summary>What is kept: the last crop, or the whole capture.</summary>
    public Int32Rect Crop =>
        Marks.OfType<CropMark>().LastOrDefault()?.Area ?? new Int32Rect(0, 0, Source?.Width ?? 0, Source?.Height ?? 0);

    private Int32Rect Shown => ShowWhole ? new Int32Rect(0, 0, Source?.Width ?? 0, Source?.Height ?? 0) : Crop;

    /// <summary>Picture pixels per view unit (≤ 1) and where the shown part starts in the view.</summary>
    public (double Scale, Vector Offset) Fit()
    {
        var shown = Shown;
        if (shown.Width <= 0 || shown.Height <= 0 || ActualWidth <= 0 || ActualHeight <= 0) return (1, default);
        const double Margin = 12;
        var scale = Math.Min(1, Math.Min((ActualWidth - Margin * 2) / shown.Width, (ActualHeight - Margin * 2) / shown.Height));
        scale = Math.Max(scale, 0.02);
        var offset = new Vector((ActualWidth - shown.Width * scale) / 2 - shown.X * scale, (ActualHeight - shown.Height * scale) / 2 - shown.Y * scale);
        return (scale, offset);
    }

    public Point ToPicture(Point view)
    {
        var (scale, offset) = Fit();
        return new Point((view.X - offset.X) / scale, (view.Y - offset.Y) / scale);
    }

    public Point ToView(Point picture)
    {
        var (scale, offset) = Fit();
        return new Point(picture.X * scale + offset.X, picture.Y * scale + offset.Y);
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_picture is null) return;
        var (scale, offset) = Fit();
        var shown = Shown;
        dc.PushTransform(new TranslateTransform(offset.X, offset.Y));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.PushClip(new RectangleGeometry(new Rect(shown.X, shown.Y, shown.Width, shown.Height)));
        DrawContent(dc);
        dc.Pop();
        if (ShowWhole)
        {
            // Darken what the crop leaves out, and frame what it keeps.
            var crop = Drawing is CropMark drawn ? drawn.Area : Crop;
            var keep = new Rect(crop.X, crop.Y, crop.Width, crop.Height);
            var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, Source!.Width, Source.Height)), new RectangleGeometry(keep));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, outside);
            var frame = new Pen(Brushes.White, 1.5 / scale) { DashStyle = new DashStyle([4, 3], 0) };
            dc.DrawRectangle(null, frame, keep);
        }
        dc.Pop();
        dc.Pop();
    }

    /// <summary>The capture and every mark over it, in picture pixels.</summary>
    private void DrawContent(DrawingContext dc)
    {
        dc.DrawImage(_picture, new Rect(0, 0, _picture!.PixelWidth, _picture.PixelHeight));
        foreach (var mark in Marks) mark.Draw(dc);
        if (Drawing is not CropMark) Drawing?.Draw(dc);
    }

    /// <summary>The finished picture: what is kept, at its own size.</summary>
    public BgraImage Render()
    {
        var crop = Crop;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-crop.X, -crop.Y));
            var drawing = Drawing;
            Drawing = null;
            DrawContent(dc);
            Drawing = drawing;
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(crop.Width, crop.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[crop.Width * crop.Height * 4];
        bitmap.CopyPixels(pixels, crop.Width * 4, 0);
        return new BgraImage(crop.Width, crop.Height, pixels);
    }
}

/// <summary>The rectangle of «Difuminar» while it is dragged.</summary>
internal sealed record BlurPreview(Rect Area) : AnnotateMark
{
    public override void Draw(DrawingContext dc)
    {
        var fill = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1) { DashStyle = new DashStyle([4, 3], 0) };
        dc.DrawRectangle(fill, pen, Area);
    }
}

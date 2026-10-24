using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpGen.Runtime;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using D2DArc = Vortice.Direct2D1.ArcSegment;
using D2DArcSize = Vortice.Direct2D1.ArcSize;
using D2DBezier = Vortice.Direct2D1.BezierSegment;
using D2DQuadratic = Vortice.Direct2D1.QuadraticBezierSegment;
using D2DSweep = Vortice.Direct2D1.SweepDirection;
using WpfArc = System.Windows.Media.ArcSegment;
using WpfBezier = System.Windows.Media.BezierSegment;
using WpfQuadratic = System.Windows.Media.QuadraticBezierSegment;
using Rect = System.Windows.Rect;
using PathSegment = System.Windows.Media.PathSegment;
using RawRectF = Vortice.RawRectF;

namespace KDesktop.Assistant.Host.Rendering;

/// <summary>
/// Plays with Direct2D a drawing recorded with WPF's API (<see cref="DrawingGroup"/>), as Lander does: assistants
/// write their vector art with the API they know and the GPU rasterizes it with hardware antialiasing.
/// </summary>
/// <remarks>
/// The Direct2D geometries, brushes and bitmaps are cached by their WPF object (fixed, frozen parts are converted
/// only once). What goes unused for a few frames is released.
/// </remarks>
internal sealed class DrawingReplayer : IDisposable
{
    private const int KeepFrames = 3;

    private static readonly RawRectF Infinite = new(-1e7f, -1e7f, 1e7f, 1e7f);

    private readonly ID2D1Factory1 _factory;
    private readonly Dictionary<object, CacheEntry> _cache = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(PenLineCap, PenLineCap, PenLineJoin), ID2D1StrokeStyle> _strokes = [];
    private readonly Dictionary<string, CacheEntry> _text = new(StringComparer.Ordinal);
    private readonly List<object> _expired = [];
    private ID2D1DeviceContext _context = null!;
    private readonly Dictionary<uint, CacheEntry> _solids = [];
    private readonly Dictionary<string, CacheEntry> _brushes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CacheEntry> _stops = new(StringComparer.Ordinal);
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<GradientBrush, string> _contentKeys = [];
    private long _frame;

    /// <summary>Primitives drawn (diagnostics).</summary>
    public long Primitives { get; set; }

    public DrawingReplayer(ID2D1Factory1 factory)
    {
        _factory = factory;
    }

    /// <summary>Draws <paramref name="drawing"/> on <paramref name="context"/> with the given transform.</summary>
    public void Replay(ID2D1DeviceContext context, Drawing drawing, Matrix3x2 transform)
    {
        // Each frame may come with another target, but brushes and geometries belong to the device (the same while
        // this object lives) and stay valid.
        _context = context;

        _frame++;
        Draw(drawing, transform);
        context.Transform = Matrix3x2.Identity;
        if (_frame % 30 == 0)
        {
            Trim();
        }
    }

    public void Dispose()
    {
        ReleaseDeviceResources();
        foreach (var stroke in _strokes.Values)
        {
            stroke.Dispose();
        }

        _strokes.Clear();
    }

    /// <summary>Releases everything that depends on the device (when it is lost).</summary>
    public void ReleaseDeviceResources()
    {
        foreach (var entry in _cache.Values)
        {
            entry.Dispose();
        }

        foreach (var entry in _text.Values)
        {
            entry.Dispose();
        }

        _cache.Clear();
        _text.Clear();
        foreach (var entry in _solids.Values.Concat(_brushes.Values).Concat(_stops.Values))
        {
            entry.Dispose();
        }

        _solids.Clear();
        _brushes.Clear();
        _stops.Clear();
    }

    private void Draw(Drawing drawing, Matrix3x2 world)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                DrawGroup(group, world);
                break;

            case GeometryDrawing geometry:
                Primitives++;
                RenderStats.Primitive();
                DrawGeometry(geometry.Geometry, geometry.Brush, geometry.Pen, world);
                break;

            case GlyphRunDrawing text when text.GlyphRun is not null && text.ForegroundBrush is not null:
                DrawText(text, world);
                break;

            case ImageDrawing image when image.ImageSource is BitmapSource source:
                DrawImage(source, image.Rect, world);
                break;
        }
    }

    private void DrawGroup(DrawingGroup group, Matrix3x2 world)
    {
        if (group.Transform is { } transform && !transform.Value.IsIdentity)
        {
            world = ToMatrix(transform.Value) * world;
        }

        var opacity = group.Opacity;
        if (opacity <= 0.002)
        {
            return;
        }

        var layers = 0;
        if (opacity < 0.998)
        {
            _context.Transform = world;
            PushLayer(null, (float)opacity);
            layers++;
        }

        if (group.ClipGeometry is { } clip)
        {
            var clipWorld = clip.Transform is { } clipTransform && !clipTransform.Value.IsIdentity
                ? ToMatrix(clipTransform.Value) * world
                : world;
            _context.Transform = clipWorld;
            PushLayer(GetGeometry(clip).Geometry, 1);
            layers++;
        }

        foreach (var child in group.Children)
        {
            Draw(child, world);
        }

        for (var i = 0; i < layers; i++)
        {
            _context.PopLayer();
        }
    }

    private void PushLayer(ID2D1Geometry? mask, float opacity)
    {
        var parameters = new LayerParameters1
        {
            ContentBounds = Infinite,
            GeometricMask = mask!,
            MaskAntialiasMode = AntialiasMode.PerPrimitive,
            MaskTransform = Matrix3x2.Identity,
            Opacity = opacity,
            LayerOptions = LayerOptions1.None,
        };
        _context.PushLayer(ref parameters, null);
        RenderStats.Layer();
    }

    private void DrawGeometry(Geometry? geometry, System.Windows.Media.Brush? fill, System.Windows.Media.Pen? pen, Matrix3x2 world)
    {
        if (geometry is null || (fill is null && pen?.Brush is null))
        {
            return;
        }

        if (geometry.Transform is { } transform && !transform.Value.IsIdentity && geometry is EllipseGeometry or RectangleGeometry or LineGeometry)
        {
            world = ToMatrix(transform.Value) * world;
        }

        _context.Transform = world;
        var stroke = pen?.Brush is null ? null : pen;
        var thickness = (float)(stroke?.Thickness ?? 0);
        switch (geometry)
        {
            case EllipseGeometry ellipse:
            {
                var center = ellipse.Center;
                var shape = new Ellipse(new Vector2((float)center.X, (float)center.Y), (float)ellipse.RadiusX, (float)ellipse.RadiusY);
                var bounds = new Rect(center.X - ellipse.RadiusX, center.Y - ellipse.RadiusY, ellipse.RadiusX * 2, ellipse.RadiusY * 2);
                if (fill is GradientBrush { MappingMode: BrushMappingMode.RelativeToBoundingBox } relative && bounds.Width > 0 && bounds.Height > 0)
                {
                    // Halos and particles: drawn in a unit space, so the brush is the same wherever the
                    // ellipse is and whatever its size.
                    _context.Transform = new Matrix3x2((float)bounds.Width, 0, 0, (float)bounds.Height, (float)bounds.X, (float)bounds.Y) * world;
                    if (GetBrush(relative, new Rect(0, 0, 1, 1)) is { } unitBrush)
                    {
                        _context.FillEllipse(new Ellipse(new Vector2(0.5f, 0.5f), 0.5f, 0.5f), unitBrush);
                    }

                    _context.Transform = world;
                }
                else if (fill is not null && GetBrush(fill, bounds) is { } brush)
                {
                    _context.FillEllipse(shape, brush);
                }

                if (stroke is not null && GetBrush(stroke.Brush, bounds) is { } outline)
                {
                    _context.DrawEllipse(shape, outline, thickness, GetStroke(stroke));
                }

                return;
            }

            case RectangleGeometry rectangle:
            {
                var rect = rectangle.Rect;
                if (rect.IsEmpty)
                {
                    return;
                }

                var raw = new RawRectF((float)rect.Left, (float)rect.Top, (float)rect.Right, (float)rect.Bottom);
                var rounded = rectangle.RadiusX > 0 && rectangle.RadiusY > 0;
                var shape = new RoundedRectangle(raw, (float)rectangle.RadiusX, (float)rectangle.RadiusY);
                if (fill is not null && GetBrush(fill, rect) is { } brush)
                {
                    if (rounded)
                    {
                        _context.FillRoundedRectangle(ref shape, brush);
                    }
                    else
                    {
                        _context.FillRectangle(raw, brush);
                    }
                }

                if (stroke is not null && GetBrush(stroke.Brush, rect) is { } outline)
                {
                    if (rounded)
                    {
                        _context.DrawRoundedRectangle(ref shape, outline, thickness, GetStroke(stroke));
                    }
                    else
                    {
                        _context.DrawRectangle(raw, outline, thickness, GetStroke(stroke));
                    }
                }

                return;
            }

            case LineGeometry line:
            {
                if (stroke is null)
                {
                    return;
                }

                var bounds = new Rect(line.StartPoint, line.EndPoint);
                if (GetBrush(stroke.Brush, bounds) is { } outline)
                {
                    _context.DrawLine(ToVector(line.StartPoint), ToVector(line.EndPoint), outline, thickness, GetStroke(stroke));
                }

                return;
            }

            default:
            {
                var entry = GetGeometry(geometry);
                if (entry.Geometry is null)
                {
                    return;
                }

                if (entry.Transform is { } local)
                {
                    _context.Transform = local * world;
                }

                if (fill is not null && GetBrush(fill, entry) is { } brush)
                {
                    _context.FillGeometry(entry.Geometry, brush);
                }

                if (stroke is not null && GetBrush(stroke.Brush, entry) is { } outline)
                {
                    _context.DrawGeometry(entry.Geometry, outline, thickness, GetStroke(stroke));
                }

                return;
            }
        }
    }

    private void DrawText(GlyphRunDrawing drawing, Matrix3x2 world)
    {
        var run = drawing.GlyphRun;
        var key = TextKey(run);
        if (!_text.TryGetValue(key, out var entry))
        {
            var geometry = run.BuildGeometry();
            entry = BuildGeometryEntry(geometry);
            _text[key] = entry;
        }

        entry.Frame = _frame;
        if (entry.Geometry is null)
        {
            return;
        }

        _context.Transform = entry.Transform is { } local ? local * world : world;
        if (GetBrush(drawing.ForegroundBrush, entry) is { } brush)
        {
            _context.FillGeometry(entry.Geometry, brush);
        }
    }

    private static string TextKey(GlyphRun run)
    {
        var builder = new System.Text.StringBuilder(64);
        builder.Append(run.GlyphTypeface.FontUri).Append('|').Append(run.FontRenderingEmSize.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
            .Append('|').Append(run.BaselineOrigin.X.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
            .Append(',').Append(run.BaselineOrigin.Y.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append('|');
        foreach (var glyph in run.GlyphIndices)
        {
            builder.Append(glyph).Append(',');
        }

        return builder.ToString();
    }

    private void DrawImage(BitmapSource source, Rect rect, Matrix3x2 world)
    {
        if (!_cache.TryGetValue(source, out var entry))
        {
            entry = new CacheEntry { Resource = CreateBitmap(source) };
            _cache[source] = entry;
        }

        entry.Frame = _frame;
        if (entry.Resource is not ID2D1Bitmap bitmap)
        {
            return;
        }

        _context.Transform = world;
        var destination = new RawRectF((float)rect.Left, (float)rect.Top, (float)rect.Right, (float)rect.Bottom);
        _context.DrawBitmap(bitmap, destination, 1, Vortice.Direct2D1.BitmapInterpolationMode.Linear, null);
    }

    private unsafe ID2D1Bitmap? CreateBitmap(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Pbgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);
        fixed (byte* data = pixels)
        {
            var properties = new BitmapProperties(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied));
            return _context.CreateBitmap(new SizeI(width, height), (nint)data, (uint)stride, properties);
        }
    }

    // ── brushes ──────────────────────────────────────────────────────────

    /// <summary>The brush for a cached geometry: its bounds are only worked out for a relative gradient (they cost).</summary>
    private ID2D1Brush? GetBrush(System.Windows.Media.Brush brush, CacheEntry entry) =>
        GetBrush(brush, brush is GradientBrush { MappingMode: BrushMappingMode.RelativeToBoundingBox } ? entry.Bounds : Rect.Empty);

    /// <summary>
    /// The equivalent Direct2D brush, fitted to <paramref name="bounds"/> for a relative gradient.
    /// </summary>
    /// <remarks>
    /// Brushes are never changed once used: Direct2D batches the figures, and changing a brush already in the batch
    /// flushes it (one GPU call per figure). So there is one per colour and one per placed gradient, cached.
    /// </remarks>
    private ID2D1Brush? GetBrush(System.Windows.Media.Brush brush, Rect bounds)
    {
        switch (brush)
        {
            case SolidColorBrush solid:
            {
                var color = solid.Color;
                var alpha = (int)Math.Round(color.A * Math.Clamp(solid.Opacity, 0, 1));
                if (alpha <= 0)
                {
                    return null;
                }

                var key = (uint)(alpha << 24 | color.R << 16 | color.G << 8 | color.B);
                if (!_solids.TryGetValue(key, out var entry))
                {
                    entry = new CacheEntry
                    {
                        Resource = _context.CreateSolidColorBrush(new Color4(color.R / 255f, color.G / 255f, color.B / 255f, alpha / 255f)),
                        KeepLonger = true,
                    };
                    _solids[key] = entry;
                }

                entry.Frame = _frame;
                return (ID2D1Brush)entry.Resource!;
            }

            case LinearGradientBrush linear:
            {
                var start = linear.StartPoint;
                var end = linear.EndPoint;
                if (linear.MappingMode == BrushMappingMode.RelativeToBoundingBox)
                {
                    start = Relative(start, bounds);
                    end = Relative(end, bounds);
                }

                var key = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{ContentKey(linear)}@{start.X:0.##},{start.Y:0.##},{end.X:0.##},{end.Y:0.##},{linear.Opacity:0.###}");
                return GetPlacedGradient(key, linear, collection =>
                    _context.CreateLinearGradientBrush(
                        new LinearGradientBrushProperties(ToVector(start), ToVector(end)),
                        new BrushProperties((float)linear.Opacity, Matrix3x2.Identity),
                        collection));
            }

            case RadialGradientBrush radial:
            {
                var center = radial.Center;
                var origin = radial.GradientOrigin;
                var radiusX = radial.RadiusX;
                var radiusY = radial.RadiusY;
                if (radial.MappingMode == BrushMappingMode.RelativeToBoundingBox)
                {
                    center = Relative(center, bounds);
                    origin = Relative(origin, bounds);
                    radiusX *= bounds.Width;
                    radiusY *= bounds.Height;
                }

                var key = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{ContentKey(radial)}@{center.X:0.##},{center.Y:0.##},{origin.X:0.##},{origin.Y:0.##},{radiusX:0.##},{radiusY:0.##},{radial.Opacity:0.###}");
                var offset = new Vector2((float)(origin.X - center.X), (float)(origin.Y - center.Y));
                return GetPlacedGradient(key, radial, collection =>
                    _context.CreateRadialGradientBrush(
                        new RadialGradientBrushProperties(ToVector(center), offset, (float)radiusX, (float)radiusY),
                        new BrushProperties((float)radial.Opacity, Matrix3x2.Identity),
                        collection));
            }

            default:
                return null;
        }
    }

    /// <summary>A placed gradient (its points resolved), created once and reused.</summary>
    private ID2D1Brush GetPlacedGradient(string key, GradientBrush brush, Func<ID2D1GradientStopCollection, ID2D1Brush> create)
    {
        if (!_brushes.TryGetValue(key, out var entry))
        {
            entry = new CacheEntry { Resource = create(GetStops(brush)) };
            _brushes[key] = entry;
        }

        entry.Frame = _frame;
        return (ID2D1Brush)entry.Resource!;
    }

    /// <summary>Gradient stops by content (shared by every brush with the same colours).</summary>
    private ID2D1GradientStopCollection GetStops(GradientBrush brush)
    {
        var key = ContentKey(brush);
        if (_stops.TryGetValue(key, out var entry) && entry.Resource is ID2D1GradientStopCollection cached)
        {
            entry.Frame = _frame;
            return cached;
        }

        var stops = new Vortice.Direct2D1.GradientStop[brush.GradientStops.Count];
        for (var i = 0; i < stops.Length; i++)
        {
            var stop = brush.GradientStops[i];
            var c = stop.Color;
            stops[i] = new Vortice.Direct2D1.GradientStop((float)Math.Clamp(stop.Offset, 0, 1), new Color4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f));
        }

        var extend = brush.SpreadMethod switch
        {
            GradientSpreadMethod.Reflect => ExtendMode.Mirror,
            GradientSpreadMethod.Repeat => ExtendMode.Wrap,
            _ => ExtendMode.Clamp,
        };

        var collection = _context.CreateGradientStopCollection(stops, Gamma.StandardRgb, extend);
        _stops[key] = new CacheEntry { Resource = collection, Frame = _frame, KeepLonger = true };
        return collection;
    }

    /// <summary>Key of a gradient's content (kind, stops and spread), computed once per brush.</summary>
    private string ContentKey(GradientBrush brush)
    {
        if (brush.IsFrozen && _contentKeys.TryGetValue(brush, out var cached))
        {
            return cached;
        }

        var builder = new System.Text.StringBuilder(brush is RadialGradientBrush ? "R" : "L", 64);
        builder.Append((int)brush.SpreadMethod);
        foreach (var stop in brush.GradientStops)
        {
            var c = stop.Color;
            builder.Append('|').Append(Math.Round(stop.Offset, 4).ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(':').Append((uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B));
        }

        var key = builder.ToString();
        if (brush.IsFrozen)
        {
            _contentKeys.AddOrUpdate(brush, key);
        }

        return key;
    }

    private static System.Windows.Point Relative(System.Windows.Point point, Rect bounds) =>
        new(bounds.X + point.X * bounds.Width, bounds.Y + point.Y * bounds.Height);

    private ID2D1StrokeStyle GetStroke(System.Windows.Media.Pen pen)
    {
        var key = (pen.StartLineCap, pen.EndLineCap, pen.LineJoin);
        if (!_strokes.TryGetValue(key, out var style))
        {
            style = _factory.CreateStrokeStyle(new StrokeStyleProperties1
            {
                StartCap = ToCap(pen.StartLineCap),
                EndCap = ToCap(pen.EndLineCap),
                DashCap = ToCap(pen.DashCap),
                LineJoin = pen.LineJoin switch
                {
                    PenLineJoin.Bevel => Vortice.Direct2D1.LineJoin.Bevel,
                    PenLineJoin.Round => Vortice.Direct2D1.LineJoin.Round,
                    _ => Vortice.Direct2D1.LineJoin.MiterOrBevel,
                },
                MiterLimit = (float)Math.Max(1, pen.MiterLimit),
                DashStyle = Vortice.Direct2D1.DashStyle.Solid,
                TransformType = StrokeTransformType.Normal,
            });
            _strokes[key] = style;
        }

        return style;
    }

    private static CapStyle ToCap(PenLineCap cap) => cap switch
    {
        PenLineCap.Round => CapStyle.Round,
        PenLineCap.Square => CapStyle.Square,
        PenLineCap.Triangle => CapStyle.Triangle,
        _ => CapStyle.Flat,
    };

    // ── geometries ───────────────────────────────────────────────────────

    private CacheEntry GetGeometry(Geometry geometry)
    {
        if (!_cache.TryGetValue(geometry, out var entry))
        {
            entry = geometry switch
            {
                EllipseGeometry ellipse => new CacheEntry
                {
                    Resource = _factory.CreateEllipseGeometry(new Ellipse(ToVector(ellipse.Center), (float)ellipse.RadiusX, (float)ellipse.RadiusY)),
                    Transform = LocalTransform(geometry),
                },
                RectangleGeometry rectangle when rectangle.RadiusX <= 0 || rectangle.RadiusY <= 0 => new CacheEntry
                {
                    Resource = _factory.CreateRectangleGeometry(ToRect(rectangle.Rect)),
                    Transform = LocalTransform(geometry),
                },
                RectangleGeometry rounded => new CacheEntry
                {
                    Resource = _factory.CreateRoundedRectangleGeometry(new RoundedRectangle(ToRect(rounded.Rect), (float)rounded.RadiusX, (float)rounded.RadiusY)),
                    Transform = LocalTransform(geometry),
                },
                _ => BuildGeometryEntry(geometry),
            };

            _cache[geometry] = entry;
        }

        entry.Frame = _frame;
        return entry;
    }

    private static Matrix3x2? LocalTransform(Geometry geometry) =>
        geometry.Transform is { } transform && !transform.Value.IsIdentity ? ToMatrix(transform.Value) : null;

    /// <summary>Turns any WPF geometry into a Direct2D path (curves included).</summary>
    private CacheEntry BuildGeometryEntry(Geometry geometry)
    {
        var path = geometry as PathGeometry ?? PathGeometry.CreateFromGeometry(geometry);
        // The bounds are only worked out if a relative gradient asks for them (they are a third of the conversion).
        var entry = new CacheEntry { Source = path, Transform = LocalTransform(path) };
        if (path.Figures.Count == 0)
        {
            return entry;
        }

        RenderStats.Geometry();
        var d2d = _factory.CreatePathGeometry();
        using (var sink = d2d.Open())
        {
            sink.SetFillMode(path.FillRule == FillRule.EvenOdd ? FillMode.Alternate : FillMode.Winding);
            foreach (var figure in path.Figures)
            {
                sink.BeginFigure(ToVector(figure.StartPoint), figure.IsFilled ? FigureBegin.Filled : FigureBegin.Hollow);
                foreach (var segment in figure.Segments)
                {
                    AddSegment(sink, segment);
                }

                sink.EndFigure(figure.IsClosed ? FigureEnd.Closed : FigureEnd.Open);
            }

            sink.Close();
        }

        entry.Resource = d2d;
        return entry;
    }

    private static void AddSegment(ID2D1GeometrySink sink, PathSegment segment)
    {
        switch (segment)
        {
            case LineSegment line:
                sink.AddLine(ToVector(line.Point));
                break;

            case PolyLineSegment poly:
            {
                // All at once: each call crosses into Direct2D, and a contour has a hundred points or more.
                var points = poly.Points;
                var lines = new Vector2[points.Count];
                for (var i = 0; i < lines.Length; i++)
                {
                    lines[i] = ToVector(points[i]);
                }

                sink.AddLines(lines);
                break;
            }

            case WpfBezier bezier:
                sink.AddBezier(new D2DBezier { Point1 = ToVector(bezier.Point1), Point2 = ToVector(bezier.Point2), Point3 = ToVector(bezier.Point3) });
                break;

            case PolyBezierSegment poly:
            {
                var points = poly.Points;
                var curves = new D2DBezier[points.Count / 3];
                for (int i = 0, p = 0; i < curves.Length; i++, p += 3)
                {
                    curves[i] = new D2DBezier { Point1 = ToVector(points[p]), Point2 = ToVector(points[p + 1]), Point3 = ToVector(points[p + 2]) };
                }

                sink.AddBeziers(curves);
                break;
            }

            case WpfQuadratic quadratic:
                sink.AddQuadraticBezier(new D2DQuadratic { Point1 = ToVector(quadratic.Point1), Point2 = ToVector(quadratic.Point2) });
                break;

            case PolyQuadraticBezierSegment poly:
                for (var i = 0; i + 1 < poly.Points.Count; i += 2)
                {
                    sink.AddQuadraticBezier(new D2DQuadratic { Point1 = ToVector(poly.Points[i]), Point2 = ToVector(poly.Points[i + 1]) });
                }

                break;

            case WpfArc arc:
                sink.AddArc(new D2DArc
                {
                    Point = ToVector(arc.Point),
                    Size = new Vortice.Mathematics.Size((float)arc.Size.Width, (float)arc.Size.Height),
                    RotationAngle = (float)arc.RotationAngle,
                    SweepDirection = arc.SweepDirection == System.Windows.Media.SweepDirection.Clockwise ? D2DSweep.Clockwise : D2DSweep.CounterClockwise,
                    ArcSize = arc.IsLargeArc ? D2DArcSize.Large : D2DArcSize.Small,
                });
                break;
        }
    }

    // ── cache ────────────────────────────────────────────────────────────

    private void Trim()
    {
        // Geometries of a single frame go at once; icons and gradients (which repeat a lot) last longer.
        var limit = _frame - KeepFrames;
        var longLimit = _frame - 600;
        foreach (var (key, entry) in _cache)
        {
            if (entry.Frame < (key is BitmapSource ? longLimit : limit))
            {
                _expired.Add(key);
            }
        }

        foreach (var key in _expired)
        {
            if (_cache.Remove(key, out var entry))
            {
                entry.Dispose();
            }
        }

        _expired.Clear();
        Expire(_text, limit, longLimit);
        Expire(_brushes, _frame - 120, longLimit);
        Expire(_stops, limit, longLimit);
        foreach (var key in _solids.Where(p => p.Value.Frame < longLimit).Select(p => p.Key).ToList())
        {
            if (_solids.Remove(key, out var entry))
            {
                entry.Dispose();
            }
        }
    }

    private static void Expire(Dictionary<string, CacheEntry> cache, long limit, long longLimit)
    {
        foreach (var key in cache.Where(p => p.Value.Frame < (p.Value.KeepLonger ? longLimit : limit)).Select(p => p.Key).ToList())
        {
            if (cache.Remove(key, out var entry))
            {
                entry.Dispose();
            }
        }
    }

    private static Vector2 ToVector(System.Windows.Point point) => new((float)point.X, (float)point.Y);

    private static RawRectF ToRect(Rect rect) => new((float)rect.Left, (float)rect.Top, (float)rect.Right, (float)rect.Bottom);

    private static Matrix3x2 ToMatrix(System.Windows.Media.Matrix m) =>
        new((float)m.M11, (float)m.M12, (float)m.M21, (float)m.M22, (float)m.OffsetX, (float)m.OffsetY);

    private sealed class CacheEntry : IDisposable
    {
        public ComObject? Resource { get; set; }

        public ID2D1Geometry? Geometry => Resource as ID2D1Geometry;

        private Rect? _bounds;

        /// <summary>The WPF path it was made from (for <see cref="Bounds"/>).</summary>
        public PathGeometry? Source { get; init; }

        /// <summary>Its bounds without its own transform (applied when drawing), worked out the first time they are asked for.</summary>
        public Rect Bounds
        {
            get
            {
                if (_bounds is { } bounds)
                {
                    return bounds;
                }

                var path = Source;
                if (path?.Transform is { } transform && !transform.Value.IsIdentity)
                {
                    path = path.Clone();
                    path.Transform = null;
                }

                _bounds = path?.Bounds ?? Rect.Empty;
                return _bounds.Value;
            }
        }

        public Matrix3x2? Transform { get; set; }

        public long Frame { get; set; }

        public bool KeepLonger { get; init; }

        public void Dispose()
        {
            Resource?.Dispose();
            Resource = null;
        }
    }
}

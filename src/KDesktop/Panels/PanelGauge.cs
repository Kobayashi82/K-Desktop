using System.Windows;
using System.Windows.Media;
using KDesktop.Core.Model;

namespace KDesktop.Panels;

/// <summary>
/// The drawing under a figure of a panel: a thin bar (<see cref="PanelItemLook.Bar"/>) or the last figures as a small
/// area (<see cref="PanelItemLook.Graph"/>). Drawn by hand: nothing to lay out when it changes.
/// </summary>
internal sealed class PanelGauge : FrameworkElement
{
    private double[] _points = [];
    private double _top = 100;
    private Brush? _softOf, _soft;

    public static readonly DependencyProperty BrushProperty =
        DependencyProperty.Register(nameof(Brush), typeof(Brush), typeof(PanelGauge), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Its colour (that of the names: the theme's accent unless the panel has its own).</summary>
    public Brush Brush
    {
        get => (Brush)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    public PanelItemLook Look { get; init; }

    public PanelGauge() => IsHitTestVisible = false;

    /// <summary>The colour, faded: the empty part of the bar and the area of the graph.</summary>
    private Brush Soft()
    {
        if (ReferenceEquals(_softOf, Brush) && _soft is not null) return _soft;
        var soft = Brush.CloneCurrentValue();
        soft.Opacity = 0.22;
        soft.Freeze();
        (_softOf, _soft) = (Brush, soft);
        return soft;
    }

    /// <summary>The figures (the last one for a bar) and the top of the scale.</summary>
    public void Show(double[] points, double top)
    {
        _points = points;
        _top = top <= 0 ? 1 : top;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        if (Look == PanelItemLook.Bar)
        {
            dc.DrawRoundedRectangle(Soft(), null, new Rect(0, 0, width, height), height / 2, height / 2);
            var share = _points.Length == 0 ? 0 : Math.Clamp(_points[^1] / _top, 0, 1);
            if (share > 0) dc.DrawRoundedRectangle(Brush, null, new Rect(0, 0, Math.Max(height, width * share), height), height / 2, height / 2);
            return;
        }
        if (_points.Length < 2) return;
        // The newest on the right; the whole width is the history, so it fills in from the right at first.
        var step = width / (Core.Platform.PanelSampler.HistoryLength - 1);
        var start = width - step * (_points.Length - 1);
        var area = new StreamGeometry();
        var line = new StreamGeometry();
        using (var fill = area.Open())
        using (var stroke = line.Open())
        {
            fill.BeginFigure(new Point(start, height), true, true);
            for (var i = 0; i < _points.Length; i++)
            {
                var point = new Point(start + step * i, height - Math.Clamp(_points[i] / _top, 0, 1) * (height - 1));
                fill.LineTo(point, false, false);
                if (i == 0) stroke.BeginFigure(point, false, false);
                else stroke.LineTo(point, true, true);
            }
            fill.LineTo(new Point(width, height), false, false);
        }
        area.Freeze();
        line.Freeze();
        dc.DrawGeometry(Soft(), null, area);
        var pen = new Pen(Brush, 1) { LineJoin = PenLineJoin.Round };
        pen.Freeze();
        dc.DrawGeometry(null, pen, line);
    }
}

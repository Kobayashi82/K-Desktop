using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views;

/// <summary>
/// The drawing of a monitor cut into zones («Disposiciones», E1b), in its proportions, with the colours of K-Board's
/// keyboard: each zone a key with its number and its programs. A click marks a zone; another click on the marked one cuts
/// it in two where the dotted line shows (vertical; with Shift, horizontal). The lines between zones are dragged to change
/// their size, and the ✕ in the middle of one joins its two zones. A monitor without zones: a click makes one of all of it;
/// with zones, a click outside them unmarks the marked one. Inside the marked zone, dashed, the saved places of its programs
/// (where they go when it is put, 5.0.379).
/// </summary>
public sealed class ZoneSurface : Border
{
    private const double Gap = 6;
    private const double MaxDrawWidth = 720;
    private const double MaxDrawHeight = 360;
    private const double LineHit = 12;

    private readonly Canvas _canvas = new() { ClipToBounds = true, Background = Brushes.Transparent };
    private readonly Line _preview = new() { StrokeThickness = 1.5, StrokeDashArray = [4, 3], IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock _empty = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 12, IsHitTestVisible = false };
    private readonly Dictionary<ArrangedZone, (Border Box, TextBlock Number, TextBlock Label, TextBlock Size)> _boxes = [];
    private readonly List<(ZoneLine Line, Border Hit, Border Grip, Border? Join)> _lines = [];
    private readonly List<(SavedPlace Place, Rectangle Shape)> _places = [];

    private IList<ArrangedZone> _all = [];
    private Func<ArrangedZone, bool> _mine = _ => false;
    private (string Name, string Device) _monitor;
    private double _aspect = 16d / 9;
    private ArrangedZone? _selected;
    private ArrangedZone? _hover;
    private (ZoneLine Line, Border Hit, Border Grip)? _drag;

    /// <summary>A zone was marked (null: none).</summary>
    public event Action<ArrangedZone?>? ZoneSelected;

    public ArrangedZone? Selected => _selected;

    public ZoneSurface()
    {
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(Gap / 2);
        HorizontalAlignment = HorizontalAlignment.Center;
        SetResourceReference(BackgroundProperty, "Kd.Subtle");
        SetResourceReference(BorderBrushProperty, "CardStrokeColorDefaultBrush");
        _preview.SetResourceReference(Shape.StrokeProperty, "Kd.Accent");
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        _empty.Text = Loc.T("arrangement.monitorEmpty");
        Child = _canvas;
        _canvas.SizeChanged += (_, _) => Layout();
        _canvas.MouseLeftButtonDown += OnEmptyClick;
        MouseMove += (_, e) => UpdatePreview(e.GetPosition(_canvas));
        MouseLeave += (_, _) =>
        {
            _hover = null;
            _preview.Visibility = Visibility.Collapsed;
            Paint();
        };
        // Shift pressed or let go with the mouse still: the dotted line turns at once.
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is { } window)
            {
                window.PreviewKeyDown += OnShift;
                window.PreviewKeyUp += OnShift;
            }
        };
        Unloaded += (_, _) =>
        {
            if (Window.GetWindow(this) is { } window)
            {
                window.PreviewKeyDown -= OnShift;
                window.PreviewKeyUp -= OnShift;
            }
        };
    }

    /// <summary>
    /// Shows a monitor: <paramref name="all"/> are the zones of the disposición (cuts and joins change it),
    /// <paramref name="mine"/> says which are on this monitor, and its size gives the proportions.
    /// </summary>
    public void Show(IList<ArrangedZone> all, Func<ArrangedZone, bool> mine, string name, string device, double width, double height, ArrangedZone? selected)
    {
        _all = all;
        _mine = mine;
        _monitor = (name, device);
        _aspect = width > 0 && height > 0 ? width / height : 16d / 9;
        _selected = selected is not null && mine(selected) ? selected : null;
        InvalidateMeasure();
        Build();
    }

    /// <summary>No zone marked (a click outside the zones, here or on the page).</summary>
    public void Unmark()
    {
        if (_selected is null) return;
        _selected = null;
        Build();
        ZoneSelected?.Invoke(null);
    }

    /// <summary>Draws it again (after a template, or a zone removed from outside).</summary>
    public void Refresh(ArrangedZone? selected)
    {
        _selected = selected is not null && _mine(selected) ? selected : null;
        Build();
    }

    private List<ArrangedZone> Zones() => [.. _all.Where(_mine)];

    // ── size: its monitor's proportions, as wide as fits ─────────────────

    protected override Size MeasureOverride(Size constraint)
    {
        var width = Math.Min(double.IsInfinity(constraint.Width) ? MaxDrawWidth : constraint.Width, MaxDrawWidth);
        var height = width / _aspect;
        if (height > MaxDrawHeight)
        {
            height = MaxDrawHeight;
            width = height * _aspect;
        }
        base.MeasureOverride(new Size(width, height));
        return new Size(width, height);
    }

    // ── building ─────────────────────────────────────────────────────────

    private void Build()
    {
        _canvas.Children.Clear();
        _boxes.Clear();
        _lines.Clear();
        _places.Clear();
        _drag = null;
        var zones = Zones();
        for (var i = 0; i < zones.Count; i++) _boxes[zones[i]] = MakeBox(zones[i], i + 1);
        // The saved places of the marked zone's programs, over the zones and under the lines.
        foreach (var place in _selected?.Programs.Select(p => p.Place).OfType<SavedPlace>() ?? [])
        {
            var shape = new Rectangle { StrokeThickness = 1.5, StrokeDashArray = [4, 3], RadiusX = 4, RadiusY = 4, IsHitTestVisible = false };
            shape.SetResourceReference(Shape.StrokeProperty, "Kd.Accent");
            _places.Add((place, shape));
            _canvas.Children.Add(shape);
        }
        foreach (var line in ZoneCuts.Lines(zones)) _lines.Add(MakeLine(line));
        _empty.Visibility = zones.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _canvas.Children.Add(_empty);
        _canvas.Children.Add(_preview);
        Layout();
        Paint();
    }

    private (Border, TextBlock, TextBlock, TextBlock) MakeBox(ArrangedZone zone, int number)
    {
        var numberText = new TextBlock
        {
            Text = number.ToString(Loc.Instance.Culture), FontSize = 12, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(8, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        var label = new TextBlock
        {
            Text = zone.Label, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(8, 0, 8, 0),
        };
        var size = new TextBlock { Text = zone.Size, FontSize = 12, TextAlignment = TextAlignment.Center, Margin = new Thickness(8, 2, 8, 0) };
        numberText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        size.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { label, size } };
        var box = new Border
        {
            CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand, Tag = zone,
            Child = new Grid { Children = { middle, numberText } },
            ToolTip = zone.Programs.Count > 0 ? string.Join("\n", zone.Programs.Select(p => p.Tip)) : null,
        };
        box.MouseEnter += (_, _) =>
        {
            _hover = zone;
            Paint();
        };
        box.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            OnZoneClick(zone, e.GetPosition(_canvas));
        };
        _canvas.Children.Add(box);
        return (box, numberText, label, size);
    }

    private (ZoneLine, Border, Border, Border?) MakeLine(ZoneLine line)
    {
        var grip = new Border { CornerRadius = new CornerRadius(2), Visibility = Visibility.Hidden, IsHitTestVisible = false };
        grip.SetResourceReference(BackgroundProperty, "Kd.Accent");
        var hit = new Border
        {
            Background = Brushes.Transparent, Cursor = line.Vertical ? Cursors.SizeWE : Cursors.SizeNS,
            Child = grip, ToolTip = null,
        };
        Border? join = null;
        if (line.CanJoin)
        {
            join = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand, Visibility = Visibility.Hidden,
                Child = new SymbolIcon { Symbol = SymbolRegular.Dismiss24, FontSize = 10 },
                ToolTip = Loc.T("arrangement.joinZones"),
            };
            System.Windows.Automation.AutomationProperties.SetName(join, Loc.T("arrangement.joinZones"));
            join.SetResourceReference(BackgroundProperty, "Kd.Accent");
            ((SymbolIcon)join.Child).SetResourceReference(SymbolIcon.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
            join.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                var kept = ZoneCuts.Join(_all, line);
                _selected = kept ?? _selected;
                Build();
                ZoneSelected?.Invoke(_selected);
            };
            join.MouseEnter += (_, _) => ShowGrip(grip, join, true);
            join.MouseLeave += (_, _) => ShowGrip(grip, join, hit.IsMouseOver || _drag is not null);
        }
        hit.MouseEnter += (_, _) => ShowGrip(grip, join, true);
        hit.MouseLeave += (_, _) => ShowGrip(grip, join, _drag is not null || join?.IsMouseOver == true);
        hit.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _drag = (line, hit, grip);
            _preview.Visibility = Visibility.Collapsed;
            if (join is not null) join.Visibility = Visibility.Hidden;
            hit.CaptureMouse();
        };
        hit.MouseMove += (_, e) =>
        {
            if (_drag is not { } drag || !ReferenceEquals(drag.Hit, hit)) return;
            var p = e.GetPosition(_canvas);
            var at = line.Vertical ? p.X / Math.Max(1, _canvas.ActualWidth) : p.Y / Math.Max(1, _canvas.ActualHeight);
            _drag = (ZoneCuts.Move(drag.Line, Snap(at)), hit, grip);
            Layout();
        };
        hit.MouseLeftButtonUp += (_, _) =>
        {
            if (_drag is null) return;
            hit.ReleaseMouseCapture();
            // The lines are worked out again: moving one changes where the others begin and end.
            Build();
            ZoneSelected?.Invoke(_selected);
        };
        _canvas.Children.Add(hit);
        if (join is not null) _canvas.Children.Add(join);
        return (line, hit, grip, join);
    }

    private static void ShowGrip(Border grip, Border? join, bool on)
    {
        grip.Visibility = on ? Visibility.Visible : Visibility.Hidden;
        if (join is not null) join.Visibility = on ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>A line dragged close to a half, a third or a quarter stays there; else in steps of half a percent.</summary>
    private static double Snap(double at)
    {
        foreach (var mark in (double[])[0.25, 1d / 3, 0.5, 2d / 3, 0.75])
            if (Math.Abs(at - mark) < 0.012) return mark;
        return Math.Round(at * 200) / 200;
    }

    // ── placing ──────────────────────────────────────────────────────────

    private Rect PixelsOf(double left, double top, double width, double height)
    {
        var (w, h) = (_canvas.ActualWidth, _canvas.ActualHeight);
        return new Rect(left * w + Gap / 2, top * h + Gap / 2, Math.Max(0, width * w - Gap), Math.Max(0, height * h - Gap));
    }

    private void Layout()
    {
        var (w, h) = (_canvas.ActualWidth, _canvas.ActualHeight);
        if (w <= 0 || h <= 0) return;
        foreach (var (zone, (box, _, label, size)) in _boxes)
        {
            var r = PixelsOf(zone.Left, zone.Top, zone.Width, zone.Height);
            Canvas.SetLeft(box, r.X);
            Canvas.SetTop(box, r.Y);
            box.Width = r.Width;
            box.Height = r.Height;
            size.Text = zone.Size;
            // A small zone shows only its programs (or only its size).
            size.Visibility = r.Height > 64 || zone.Programs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            label.Visibility = zone.Programs.Count > 0 && r.Height > 30 ? Visibility.Visible : Visibility.Collapsed;
        }
        foreach (var (place, shape) in _places)
        {
            var r = place.Maximized ? PixelsOf(0, 0, 1, 1) : PixelsOf(place.Left, place.Top, place.Width, place.Height);
            Canvas.SetLeft(shape, r.X + 2);
            Canvas.SetTop(shape, r.Y + 2);
            shape.Width = Math.Max(0, r.Width - 4);
            shape.Height = Math.Max(0, r.Height - 4);
        }
        foreach (var (line, hit, grip, join) in _lines)
        {
            var current = _drag is { } drag && ReferenceEquals(drag.Hit, hit) ? drag.Line : line;
            // Not the one dragged: hidden until the drag ends (it is worked out again then).
            hit.Visibility = _drag is null || ReferenceEquals(_drag.Value.Hit, hit) ? Visibility.Visible : Visibility.Hidden;
            if (line.Vertical)
            {
                Canvas.SetLeft(hit, current.At * w - LineHit / 2);
                Canvas.SetTop(hit, current.From * h + Gap);
                hit.Width = LineHit;
                hit.Height = Math.Max(0, (current.To - current.From) * h - Gap * 2);
                grip.Width = 4;
                grip.Height = Math.Min(28, hit.Height);
            }
            else
            {
                Canvas.SetLeft(hit, current.From * w + Gap);
                Canvas.SetTop(hit, current.At * h - LineHit / 2);
                hit.Width = Math.Max(0, (current.To - current.From) * w - Gap * 2);
                hit.Height = LineHit;
                grip.Width = Math.Min(28, hit.Width);
                grip.Height = 4;
            }
            if (join is not null)
            {
                // The ✕ in the middle, a little past the grip so both can be reached.
                var (x, y) = line.Vertical
                    ? (current.At * w, (current.From + current.To) / 2 * h + 26)
                    : ((current.From + current.To) / 2 * w + 26, current.At * h);
                Canvas.SetLeft(join, x - 10);
                Canvas.SetTop(join, y - 10);
            }
        }
        Canvas.SetLeft(_empty, 16);
        _empty.Width = Math.Max(0, w - 32);
        _empty.Measure(new Size(_empty.Width, double.PositiveInfinity));
        Canvas.SetTop(_empty, Math.Max(0, (h - _empty.DesiredSize.Height) / 2));
    }

    /// <summary>The colours: like K-Board's keys; the marked one as a pill chosen.</summary>
    private void Paint()
    {
        foreach (var (zone, (box, number, _, _)) in _boxes)
        {
            var selected = ReferenceEquals(zone, _selected);
            box.BorderThickness = new Thickness(selected ? 2 : 1);
            box.SetResourceReference(BackgroundProperty, selected ? "Kd.AccentSoft"
                : ReferenceEquals(zone, _hover) ? "ControlFillColorSecondaryBrush" : "ControlFillColorDefaultBrush");
            box.SetResourceReference(BorderBrushProperty, selected ? "Kd.Accent" : "ControlStrokeColorDefaultBrush");
            number.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Kd.AccentText" : "TextFillColorSecondaryBrush");
        }
    }

    // ── cutting ──────────────────────────────────────────────────────────

    private static bool Horizontal => (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

    private void OnShift(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift && IsMouseOver) UpdatePreview(Mouse.GetPosition(_canvas));
    }

    /// <summary>The dotted line where a click would cut: only on the marked zone.</summary>
    private void UpdatePreview(Point p)
    {
        if (_drag is not null || _selected is not { } zone || !_boxes.TryGetValue(zone, out var parts) || !parts.Box.IsMouseOver)
        {
            _preview.Visibility = Visibility.Collapsed;
            return;
        }
        var r = PixelsOf(zone.Left, zone.Top, zone.Width, zone.Height);
        var (w, h) = (Math.Max(1, _canvas.ActualWidth), Math.Max(1, _canvas.ActualHeight));
        var horizontal = Horizontal;
        var at = horizontal ? Snap(p.Y / h) : Snap(p.X / w);
        var (start, size) = horizontal ? (zone.Top, zone.Height) : (zone.Left, zone.Width);
        if (at - start < ZoneCuts.Smallest || start + size - at < ZoneCuts.Smallest)
        {
            _preview.Visibility = Visibility.Collapsed;
            return;
        }
        if (horizontal)
        {
            (_preview.X1, _preview.X2, _preview.Y1, _preview.Y2) = (r.Left + 6, r.Right - 6, at * h, at * h);
        }
        else
        {
            (_preview.X1, _preview.X2, _preview.Y1, _preview.Y2) = (at * w, at * w, r.Top + 6, r.Bottom - 6);
        }
        _preview.Visibility = Visibility.Visible;
    }

    private void OnZoneClick(ArrangedZone zone, Point p)
    {
        if (!ReferenceEquals(zone, _selected))
        {
            _selected = zone;
            Build();
            UpdatePreview(p);
            ZoneSelected?.Invoke(zone);
            return;
        }
        var horizontal = Horizontal;
        var at = horizontal ? Snap(p.Y / Math.Max(1, _canvas.ActualHeight)) : Snap(p.X / Math.Max(1, _canvas.ActualWidth));
        if (ZoneCuts.Split(_all, zone, !horizontal, at) is null) return;
        Build();
        UpdatePreview(p);
        ZoneSelected?.Invoke(_selected);
    }

    /// <summary>A monitor without zones: a click makes one of all of it, marked. With zones, a click between them unmarks.</summary>
    private void OnEmptyClick(object sender, MouseButtonEventArgs e)
    {
        if (_boxes.Count > 0)
        {
            if (_drag is null) Unmark();
            return;
        }
        var zone = new ArrangedZone { MonitorName = _monitor.Name, MonitorDevice = _monitor.Device };
        _all.Add(zone);
        _selected = zone;
        Build();
        ZoneSelected?.Invoke(zone);
        e.Handled = true;
    }

    /// <summary>Its size as «2560 × 1440» (for the pills' tooltips).</summary>
    public static string Resolution(int width, int height) => string.Format(CultureInfo.InvariantCulture, "{0} × {1}", width, height);
}

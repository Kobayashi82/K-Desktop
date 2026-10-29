using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Dialogs;

/// <summary>
/// Chooses the mouse gesture of a command (E4): the built-in drawings and the user's as tiles, a box to try them with the
/// left button (it marks the one it recognises), and «Dibujar el tuyo»: a name and the drawing made <see cref="Teach"/>
/// times in the box, each like the first, and not like any other drawing.
/// </summary>
public partial class GestureDialog : FluentWindow
{
    private const int Teach = 3;

    private readonly AppSettings _settings = App.Current.Config.Settings;
    private readonly List<RadioButton> _tiles = [];
    private readonly List<GesturePoint> _stroke = [];
    private Polyline? _ink;

    // Drawing one of the user's: the strokes drawn so far (as they were, and normalised).
    private bool _teaching;
    private readonly List<List<GesturePoint>> _taught = [];
    private readonly List<GesturePoint[]> _taughtShapes = [];

    /// <summary>The gesture chosen (<c>Gesture C</c>); null when cancelled.</summary>
    public string? Chosen { get; private set; }

    private GestureDialog()
    {
        InitializeComponent();
        IntroText.Text = Loc.T("gesture.intro", Loc.T(_settings.GestureButton switch
        {
            KDesktop.Core.Input.MouseButton.Middle => "gesture.buttonMiddle",
            KDesktop.Core.Input.MouseButton.X1 => "gesture.buttonX1",
            KDesktop.Core.Input.MouseButton.X2 => "gesture.buttonX2",
            _ => "gesture.buttonRight",
        }));
        foreach (var id in MouseGestures.BuiltIn) BuiltInTiles.Children.Add(Tile(id, ShapeName(id), MouseGestures.Path(id)));
        FillMine();
    }

    public static string? Pick(Window? owner)
    {
        var dialog = new GestureDialog { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Chosen : null;
    }

    /// <summary>The name of a built-in drawing: «Izquierda», «Círculo», «Letra C»…</summary>
    private static string ShapeName(string id) => id.Length == 1 && id != "O" ? Loc.T("gesture.letter", id) : Loc.T("gesture.shape." + id);

    private void FillMine()
    {
        foreach (var tile in MineTiles.Children.OfType<RadioButton>().ToList())
        {
            MineTiles.Children.Remove(tile);
            _tiles.Remove(tile);
        }
        var at = 0;
        foreach (var gesture in _settings.CustomGestures)
            if (gesture.Samples.Count > 0 && MouseGestures.FromFlat(gesture.Samples[0]) is { } shape)
                MineTiles.Children.Insert(at++, Tile(gesture.Name, gesture.Name, shape, removable: true));
    }

    /// <summary>A tile: the drawing (a dot where it starts) and its name; chosen, it is the gesture.</summary>
    private RadioButton Tile(string id, string name, IReadOnlyList<GesturePoint> path, bool removable = false)
    {
        var grid = new Grid { RowDefinitions = { new RowDefinition(), new RowDefinition { Height = GridLength.Auto } } };
        var drawing = Draw(path, 56, 36, 2.5);
        drawing.HorizontalAlignment = HorizontalAlignment.Center;
        drawing.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(drawing);
        var label = new System.Windows.Controls.TextBlock { Text = name, Style = (Style)FindResource("TileLabel") };
        Grid.SetRow(label, 1);
        grid.Children.Add(label);
        if (removable)
        {
            var remove = new Wpf.Ui.Controls.Button
            {
                Style = (Style)FindResource("IconButton"), Padding = new Thickness(3), Margin = new Thickness(0, -6, -2, 0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, ToolTip = Loc.T("common.delete"),
                Content = new SymbolIcon { Symbol = SymbolRegular.Delete24, FontSize = 12 },
            };
            // Grey, red under the mouse; on the button too, or the tile (a radio button) passes its own colour on to the icon.
            void Tint(string key)
            {
                remove.SetResourceReference(ForegroundProperty, key);
                ((SymbolIcon)remove.Content).SetResourceReference(SymbolIcon.ForegroundProperty, key);
            }
            Tint("TextFillColorSecondaryBrush");
            remove.MouseEnter += (_, _) => Tint("Kd.Danger");
            remove.MouseLeave += (_, _) => Tint("TextFillColorSecondaryBrush");
            System.Windows.Automation.AutomationProperties.SetName(remove, Loc.T("common.delete") + " " + name);
            remove.Click += (_, e) =>
            {
                e.Handled = true;
                Remove(id);
            };
            grid.Children.Add(remove);
        }
        var tile = new RadioButton { Style = (Style)FindResource("GestureTile"), GroupName = "gesture", Tag = id, Content = grid, ToolTip = name };
        System.Windows.Automation.AutomationProperties.SetName(tile, name);
        tile.Checked += (_, _) => AcceptButton.IsEnabled = !_teaching;
        tile.MouseDoubleClick += (_, _) => OnAccept(tile, new RoutedEventArgs());
        _tiles.Add(tile);
        return tile;
    }

    /// <summary>
    /// A stroke drawn in a box of <paramref name="width"/> × <paramref name="height"/>, in the colour of the tile it is in:
    /// a line keeps its proportions, the rest fills the box.
    /// </summary>
    private static Canvas Draw(IReadOnlyList<GesturePoint> path, double width, double height, double thickness)
    {
        var canvas = new Canvas { Width = width, Height = height };
        if (path.Count < 2) return canvas;
        double minX = path.Min(p => p.X), maxX = path.Max(p => p.X), minY = path.Min(p => p.Y), maxY = path.Max(p => p.Y);
        double w = Math.Max(maxX - minX, 1e-6), h = Math.Max(maxY - minY, 1e-6);
        var scale = Math.Min(width / w, height / h);
        double ox = (width - w * scale) / 2, oy = (height - h * scale) / 2;
        Point At(GesturePoint p) => new(ox + (p.X - minX) * scale, oy + (p.Y - minY) * scale);
        var line = new Polyline { StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        foreach (var p in path) line.Points.Add(At(p));
        var start = At(path[0]);
        var dot = new Ellipse { Width = thickness * 2.6, Height = thickness * 2.6 };
        Canvas.SetLeft(dot, start.X - dot.Width / 2);
        Canvas.SetTop(dot, start.Y - dot.Height / 2);
        var colour = new Binding(nameof(Foreground)) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(RadioButton), 1) };
        line.SetBinding(Shape.StrokeProperty, colour);
        dot.SetBinding(Shape.FillProperty, colour);
        canvas.Children.Add(line);
        canvas.Children.Add(dot);
        return canvas;
    }

    private void Remove(string name)
    {
        if (_settings.CustomGestures.FirstOrDefault(g => g.Name == name) is not { } gesture) return;
        _settings.CustomGestures.Remove(gesture);
        // The commands that used it lose that hotkey (no drawing would ever match it again).
        var hotkey = Hotkey.Gesture(name);
        foreach (var command in App.Current.Config.Commands)
            foreach (var h in command.Hotkeys.Where(h => Hotkey.Comparer.Equals(h, hotkey)).ToList())
                command.Hotkeys.Remove(h);
        FillMine();
        AcceptButton.IsEnabled = _tiles.Any(t => t.IsChecked == true);
    }

    // ── the box ──────────────────────────────────────────────────────────

    private void OnPadDown(object sender, MouseButtonEventArgs e)
    {
        _stroke.Clear();
        PadCanvas.Children.Clear();
        PadHint.Visibility = Visibility.Collapsed;
        _ink = new Polyline { StrokeThickness = 4, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _ink.SetResourceReference(Shape.StrokeProperty, "Kd.Accent");
        PadCanvas.Children.Add(_ink);
        Pad.CaptureMouse();
        AddPoint(e.GetPosition(PadCanvas));
    }

    private void OnPadMove(object sender, MouseEventArgs e)
    {
        if (_ink is not null && Pad.IsMouseCaptured) AddPoint(e.GetPosition(PadCanvas));
    }

    private void AddPoint(Point p)
    {
        _stroke.Add(new GesturePoint(p.X, p.Y));
        _ink?.Points.Add(p);
    }

    private void OnPadUp(object sender, MouseButtonEventArgs e)
    {
        if (_ink is null) return;
        Pad.ReleaseMouseCapture();
    }

    /// <summary>The stroke ends when the button is let go (or the capture is lost some other way).</summary>
    private void OnPadLost(object sender, MouseEventArgs e)
    {
        if (_ink is null) return;
        _ink.Opacity = 0.55;
        _ink = null;
        var stroke = _stroke.ToList();
        if (_teaching) Learn(stroke);
        else TryStroke(stroke);
    }

    private void TryStroke(List<GesturePoint> stroke)
    {
        var id = MouseGestures.Recognize(stroke, _settings.CustomGestures, out _);
        var tile = id is null ? null : _tiles.FirstOrDefault(t => string.Equals((string)t.Tag, id, StringComparison.OrdinalIgnoreCase));
        if (tile is null)
        {
            ResultText.Text = Loc.T("gesture.none");
            return;
        }
        tile.IsChecked = true;
        tile.BringIntoView();
        ResultText.Text = "✓ " + tile.ToolTip;
    }

    // ── drawing one of the user's ────────────────────────────────────────

    private void OnNew(object sender, RoutedEventArgs e)
    {
        _teaching = !_teaching;
        _taught.Clear();
        _taughtShapes.Clear();
        PadCanvas.Children.Clear();
        PadHint.Visibility = Visibility.Visible;
        ResultText.Text = string.Empty;
        TeachPanel.Visibility = _teaching ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.Visibility = _teaching ? Visibility.Visible : Visibility.Collapsed;
        PadHint.Text = Loc.T(_teaching ? "gesture.teach" : "gesture.pad", Teach);
        TeachText.Text = Loc.T("gesture.teach", Teach);
        AcceptButton.IsEnabled = !_teaching && _tiles.Any(t => t.IsChecked == true);
        if (_teaching)
        {
            NameBox.Text = string.Empty;
            NameBox.Focus();
        }
        UpdateTeach();
    }

    private void Learn(List<GesturePoint> stroke)
    {
        if (MouseGestures.Normalize(stroke) is not { } shape)
        {
            ResultText.Text = Loc.T("gesture.none");
            return;
        }
        if (_taughtShapes.Count > 0 && MouseGestures.Likeness(_taughtShapes[0], shape) < MouseGestures.Accept)
        {
            ResultText.Text = Loc.T("gesture.notSame");
            return;
        }
        if (_taught.Count >= Teach)
        {
            _taught.RemoveAt(0);
            _taughtShapes.RemoveAt(0);
        }
        _taught.Add(stroke);
        _taughtShapes.Add(shape);
        ResultText.Text = string.Empty;
        UpdateTeach();
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e) => UpdateTeach();

    private void UpdateTeach()
    {
        TeachCount.Text = Loc.T("gesture.teachCount", _taught.Count, Teach);
        var name = NameBox.Text.Trim();
        var taken = name.Length > 0 && !NameIsFree(name);
        if (taken) ResultText.Text = Loc.T("gesture.nameTaken");
        else if (ResultText.Text == Loc.T("gesture.nameTaken")) ResultText.Text = string.Empty;
        SaveButton.IsEnabled = _teaching && name.Length > 0 && !taken && _taught.Count >= Teach;
    }

    /// <summary>A name not of a built-in drawing (nor its arrow) nor of another of the user's, and that fits in a hotkey.</summary>
    private bool NameIsFree(string name) =>
        !name.Contains('+', StringComparison.Ordinal) && !MouseGestures.IsBuiltIn(name) &&
        !MouseGestures.BuiltIn.Any(id => MouseGestures.Symbol(id) == name) &&
        !_settings.CustomGestures.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        // Not like a drawing that already exists: it would take its commands, or lose to it.
        foreach (var stroke in _taught)
            if (MouseGestures.Recognize(stroke, _settings.CustomGestures, out _) is { } like)
            {
                ResultText.Text = Loc.T("gesture.tooLike", MouseGestures.IsBuiltIn(like) ? ShapeName(like) : like);
                return;
            }
        _settings.CustomGestures.Add(new CustomGesture { Name = name, Samples = [.. _taughtShapes.Select(MouseGestures.ToFlat)] });
        OnNew(sender, e); // back to choosing
        FillMine();
        if (_tiles.FirstOrDefault(t => (string)t.Tag == name) is { } tile) tile.IsChecked = true;
    }

    // ── footer ───────────────────────────────────────────────────────────

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (_teaching || _tiles.FirstOrDefault(t => t.IsChecked == true) is not { Tag: string id }) return;
        Chosen = Hotkey.Gesture(id);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

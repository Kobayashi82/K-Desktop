using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using KDesktop.Core.Input;
using KDesktop.Core.Platform;
using KDesktop.Native;

namespace KDesktop.Osd;

/// <summary>
/// The stroke of a mouse gesture while it is drawn (E4): the line in the accent colour over every monitor and, next to the
/// cursor, the command it will run (with the surface of the menus, <c>Kd.MenuTint</c> and <c>Kd.MenuBorder</c>; nothing
/// while it is no drawing of a command). Like the zones of «Encajar», one window per monitor that never takes the focus
/// nor the clicks; it follows the stroke at the screen's pace and fades out when the button is let go.
/// </summary>
public sealed class GestureTrail
{
    private readonly HotkeyRouter _router;
    private readonly DispatcherTimer _timer;
    private readonly List<Layer> _layers = [];
    private int _drawn;
    private int _recognisedAt;

    public GestureTrail(HotkeyRouter router)
    {
        _router = router;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Follow();
    }

    /// <summary>A press became a drawing: the layers come up and follow it.</summary>
    public void Begin()
    {
        Close();
        foreach (var monitor in ScreenCapture.Monitors()) _layers.Add(new Layer(monitor));
        _drawn = 0;
        _recognisedAt = 0;
        _timer.Start();
        Follow();
    }

    /// <summary>The button was let go (or the drawing cancelled): the stroke fades out.</summary>
    public void End()
    {
        _timer.Stop();
        var layers = _layers.ToList();
        _layers.Clear();
        foreach (var layer in layers) layer.FadeOut();
    }

    private void Close()
    {
        _timer.Stop();
        foreach (var layer in _layers) layer.Close();
        _layers.Clear();
    }

    private void Follow()
    {
        if (_router.GestureTrail() is not { } trail)
        {
            // Ended (perhaps before the layers came up): nothing stays on the screen.
            if (_layers.Count > 0) End();
            return;
        }
        var (points, front) = trail;
        if (points.Length == _drawn && _layers.TrueForAll(l => l.Ready)) return;
        _drawn = points.Length;
        // What it will run, asked again every few points (cheap, but not on every frame).
        string? label = null;
        var ask = points.Length - _recognisedAt >= 6;
        if (ask)
        {
            _recognisedAt = points.Length;
            var (_, commands) = _router.GestureOf(points, front);
            label = commands.Count == 0 ? string.Empty : string.Join("  ·  ", commands.Select(c => c.Name));
        }
        var tip = points[^1];
        foreach (var layer in _layers) layer.Show(points, label, (int)tip.X, (int)tip.Y);
    }

    /// <summary>The window over one monitor.</summary>
    private sealed class Layer
    {
        private readonly Window _window;
        private readonly Canvas _canvas = new();
        // A stroke of light (5.0.387): a blurred halo, the stroke thin at the tail and thick at the tip in pieces, a light
        // core, and a spark at the tip.
        private const int Pieces = 14;
        private const double Tail = 1.5, Tip = 7;
        private readonly Polyline _halo = Stroke(16, 0.5);
        private readonly Canvas _pieces = new();
        private readonly Polyline _core = Stroke(1.6, 0.85);
        private readonly Ellipse _glow = new() { Width = 30, Height = 30 };
        private readonly Ellipse _spark = new() { Width = 7, Height = 7, Fill = Brushes.White };
        private readonly Brush _accent;
        private readonly Border _label;
        private readonly TextBlock _labelText = new() { FontSize = 14, FontWeight = FontWeights.SemiBold };
        private readonly ScreenRect _area;

        /// <summary>Laid out: it knows its size in DIPs.</summary>
        public bool Ready => _canvas.ActualWidth > 0;

        public Layer(ScreenRect area)
        {
            _area = area;
            _accent = Application.Current.TryFindResource("Kd.Accent") as Brush ?? Brushes.Goldenrod;
            var colour = (_accent as SolidColorBrush)?.Color ?? Colors.Goldenrod;
            _halo.Stroke = _accent;
            _halo.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 14 };
            _core.Stroke = Brushes.White;
            _glow.Fill = new RadialGradientBrush(Color.FromArgb(230, colour.R, colour.G, colour.B), Color.FromArgb(0, colour.R, colour.G, colour.B));
            _glow.Fill.Freeze();
            _spark.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 2 };
            _labelText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            _label = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 6, 12, 6), Child = _labelText, Visibility = Visibility.Collapsed };
            _label.SetResourceReference(Border.BackgroundProperty, "Kd.MenuTint");
            _label.SetResourceReference(Border.BorderBrushProperty, "Kd.MenuBorder");
            TextElementFont(_label);
            _canvas.Children.Add(_halo);
            _canvas.Children.Add(_pieces);
            _canvas.Children.Add(_core);
            _canvas.Children.Add(_glow);
            _canvas.Children.Add(_spark);
            Paint([]); // the spark out of sight until there is a stroke
            _canvas.Children.Add(_label);
            _window = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, ShowInTaskbar = false,
                Topmost = true, ShowActivated = false, Focusable = false, IsHitTestVisible = false, Title = "K-Desktop",
                Background = Brushes.Transparent, Left = 0, Top = 0, Width = 10, Height = 10, Content = _canvas,
            };
            _window.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(_window).Handle;
                UiNative.AddExStyle(hwnd, toolWindow: true, noActivate: true, clickThrough: true);
                // Physical placement, whatever the monitor's scale.
                SetWindowPos(hwnd, new IntPtr(-1), area.Left, area.Top, area.Width, area.Height, 0x0010 /* NOACTIVATE */ | 0x0040 /* SHOWWINDOW */);
            };
            _window.Show();
        }

        private static void TextElementFont(Border border) =>
            System.Windows.Documents.TextElement.SetFontFamily(border, new FontFamily("Segoe UI Variable Text, Segoe UI"));

        /// <summary>The stroke (screen pixels) and, when <paramref name="label"/> is given, what it will run (empty: nothing).</summary>
        public void Show(GesturePoint[] points, string? label, int tipX, int tipY)
        {
            // Physical pixels of the monitor → DIPs of this window.
            double sx = _canvas.ActualWidth / Math.Max(1, _area.Width), sy = _canvas.ActualHeight / Math.Max(1, _area.Height);
            if (sx <= 0 || sy <= 0) return;
            var line = Smooth(points.Select(p => new Point((p.X - _area.Left) * sx, (p.Y - _area.Top) * sy)));
            Paint(line);
            if (label is not null)
            {
                _labelText.Text = label;
                _label.Visibility = label.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            var inside = tipX >= _area.Left && tipX < _area.Right && tipY >= _area.Top && tipY < _area.Bottom;
            if (!inside)
            {
                _label.Visibility = Visibility.Collapsed;
                return;
            }
            if (_label.Visibility != Visibility.Visible) return;
            _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var x = (tipX - _area.Left) * sx + 22;
            var y = (tipY - _area.Top) * sy + 18;
            // Kept inside the monitor.
            x = Math.Min(x, _canvas.ActualWidth - _label.DesiredSize.Width - 8);
            y = Math.Min(y, _canvas.ActualHeight - _label.DesiredSize.Height - 8);
            Canvas.SetLeft(_label, Math.Max(8, x));
            Canvas.SetTop(_label, Math.Max(8, y));
        }

        private static Polyline Stroke(double thickness, double opacity) => new()
        {
            StrokeThickness = thickness, Opacity = opacity, StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };

        /// <summary>The points without the ones a pixel apart, rounded twice (Chaikin) so the stroke has no corners of the mouse's steps.</summary>
        private static List<Point> Smooth(IEnumerable<Point> raw)
        {
            var points = new List<Point>();
            foreach (var p in raw)
                if (points.Count == 0 || (p - points[^1]).Length >= 1.5) points.Add(p);
            for (var pass = 0; pass < 2 && points.Count > 2; pass++)
            {
                var next = new List<Point>(points.Count * 2) { points[0] };
                for (var i = 0; i < points.Count - 1; i++)
                {
                    var (a, b) = (points[i], points[i + 1]);
                    next.Add(new Point(0.75 * a.X + 0.25 * b.X, 0.75 * a.Y + 0.25 * b.Y));
                    next.Add(new Point(0.25 * a.X + 0.75 * b.X, 0.25 * a.Y + 0.75 * b.Y));
                }
                next.Add(points[^1]);
                points = next;
            }
            return points;
        }

        private void Paint(List<Point> line)
        {
            var all = new PointCollection(line);
            all.Freeze();
            _halo.Points = all;
            _core.Points = all;
            // Thicker towards the tip: the stroke in pieces, each a little thicker than the one before.
            _pieces.Children.Clear();
            var count = Math.Min(Pieces, Math.Max(1, line.Count / 3));
            for (var k = 0; k < count; k++)
            {
                int from = k * (line.Count - 1) / count, to = (k + 1) * (line.Count - 1) / count;
                var piece = new PointCollection(line.Skip(from).Take(to - from + 1));
                piece.Freeze();
                var t = (k + 1) / (double)count;
                var stroke = Stroke(Tail + (Tip - Tail) * Math.Pow(t, 0.7), 1);
                stroke.Stroke = _accent;
                stroke.Points = piece;
                _pieces.Children.Add(stroke);
            }
            var tip = line.Count > 0 ? line[^1] : new Point(-100, -100);
            Canvas.SetLeft(_glow, tip.X - _glow.Width / 2);
            Canvas.SetTop(_glow, tip.Y - _glow.Height / 2);
            Canvas.SetLeft(_spark, tip.X - _spark.Width / 2);
            Canvas.SetTop(_spark, tip.Y - _spark.Height / 2);
        }

        public void FadeOut()
        {
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, _) => Close();
            _window.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        public void Close() => _window.Close();
    }

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

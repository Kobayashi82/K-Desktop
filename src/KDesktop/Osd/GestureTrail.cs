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
        private readonly Polyline _line = new()
        {
            StrokeThickness = 5, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.45 },
        };
        private readonly Border _label;
        private readonly TextBlock _labelText = new() { FontSize = 14, FontWeight = FontWeights.SemiBold };
        private readonly ScreenRect _area;

        /// <summary>Laid out: it knows its size in DIPs.</summary>
        public bool Ready => _canvas.ActualWidth > 0;

        public Layer(ScreenRect area)
        {
            _area = area;
            _line.SetResourceReference(Shape.StrokeProperty, "Kd.Accent");
            _labelText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            _label = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 6, 12, 6), Child = _labelText, Visibility = Visibility.Collapsed };
            _label.SetResourceReference(Border.BackgroundProperty, "Kd.MenuTint");
            _label.SetResourceReference(Border.BorderBrushProperty, "Kd.MenuBorder");
            TextElementFont(_label);
            _canvas.Children.Add(_line);
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
            var line = new PointCollection(points.Length);
            foreach (var p in points) line.Add(new Point((p.X - _area.Left) * sx, (p.Y - _area.Top) * sy));
            line.Freeze();
            _line.Points = line;
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

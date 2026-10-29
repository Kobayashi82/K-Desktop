using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Osd;

/// <summary>
/// «Cuentagotas» and «Regla» (E3): the screen as it was, frozen (not dimmed: the colours must be the real ones), one
/// window per monitor as in <see cref="RegionSelector"/>. The arrows move the pointer one pixel; Esc or the right button
/// cancels. Everything is measured in physical pixels, whatever each monitor's scale.
/// </summary>
public static class ScreenPicker
{
    /// <summary>Pixels across the magnifier, from the wheel (odd: one in the middle).</summary>
    private static readonly int[] Zooms = [7, 11, 15, 21, 31];

    /// <summary>Its width in DIPs, more or less (each pixel is a whole number of DIPs, so the grid stays sharp).</summary>
    private const double LoupeSize = 165;

    private static readonly Brush LabelBack = Frozen(Color.FromArgb(0xD0, 0x20, 0x20, 0x20));

    // ── Cuentagotas ──────────────────────────────────────────────────────

    public static Task<PixelColor?> PickColorAsync(BgraImage desktop, ScreenRect bounds, ColorFormat format)
    {
        var zoom = 2; // index in Zooms, shared by the monitors
        return Run<PixelColor>(desktop, bounds, Loc.T("capture.colorHint"), (surface, finish) =>
        {
            var pixels = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(pixels, BitmapScalingMode.NearestNeighbor);
            var grid = new Path { Stroke = Frozen(Color.FromArgb(0x40, 0x80, 0x80, 0x80)), StrokeThickness = 1, SnapsToDevicePixels = true };
            var centre = new Rectangle { Stroke = Brushes.White, StrokeThickness = 2, SnapsToDevicePixels = true };
            var centreDark = new Rectangle { Stroke = Brushes.Black, StrokeThickness = 1, SnapsToDevicePixels = true };
            var lens = new Grid { ClipToBounds = true };
            lens.Children.Add(pixels);
            lens.Children.Add(grid);
            lens.Children.Add(centreDark);
            lens.Children.Add(centre);
            var swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), BorderBrush = Brushes.White, BorderThickness = new Thickness(1) };
            var value = new TextBlock { Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontFamily = UiFont };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 8, 2, 2) };
            row.Children.Add(swatch);
            row.Children.Add(value);
            var panel = new StackPanel();
            panel.Children.Add(new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = lens });
            panel.Children.Add(row);
            var loupe = new Border { Background = LabelBack, CornerRadius = new CornerRadius(8), Padding = new Thickness(6), IsHitTestVisible = false, Visibility = Visibility.Collapsed, Child = panel };
            surface.Canvas.Children.Add(loupe);
            surface.Canvas.Cursor = Cursors.Cross;

            WriteableBitmap? bitmap = null;
            void Update()
            {
                if (!surface.PointerHere(out var x, out var y))
                {
                    loupe.Visibility = Visibility.Collapsed;
                    return;
                }
                var n = Zooms[zoom];
                var cell = Math.Max(3, Math.Round(LoupeSize / n));
                var side = n * cell;
                if (bitmap is null || bitmap.PixelWidth != n)
                {
                    bitmap = new WriteableBitmap(n, n, 96, 96, PixelFormats.Bgra32, null);
                    pixels.Source = bitmap;
                    lens.Width = lens.Height = pixels.Width = pixels.Height = side;
                    var lines = new StreamGeometry();
                    using (var g = lines.Open())
                        for (var i = 1; i < n; i++)
                        {
                            g.BeginFigure(new Point(i * cell + 0.5, 0), false, false);
                            g.LineTo(new Point(i * cell + 0.5, side), true, false);
                            g.BeginFigure(new Point(0, i * cell + 0.5), false, false);
                            g.LineTo(new Point(side, i * cell + 0.5), true, false);
                        }
                    lines.Freeze();
                    grid.Data = lines;
                    var middle = n / 2 * cell;
                    foreach (var r in new[] { centre, centreDark })
                    {
                        r.Width = r.Height = cell + 2;
                        r.HorizontalAlignment = HorizontalAlignment.Left;
                        r.VerticalAlignment = VerticalAlignment.Top;
                        r.Margin = new Thickness(middle - 1, middle - 1, 0, 0);
                    }
                    centreDark.Width = centreDark.Height = cell + 4;
                    centreDark.Margin = new Thickness(middle - 2, middle - 2, 0, 0);
                }
                var buffer = new byte[n * n * 4];
                var ix = x - bounds.Left;
                var iy = y - bounds.Top;
                for (var row2 = 0; row2 < n; row2++)
                    for (var col = 0; col < n; col++)
                    {
                        int sx = ix + col - n / 2, sy = iy + row2 - n / 2, o = (row2 * n + col) * 4;
                        if (sx < 0 || sy < 0 || sx >= desktop.Width || sy >= desktop.Height) continue; // off the desktop: transparent
                        var i = sy * desktop.Stride + sx * 4;
                        buffer[o] = desktop.Pixels[i];
                        buffer[o + 1] = desktop.Pixels[i + 1];
                        buffer[o + 2] = desktop.Pixels[i + 2];
                        buffer[o + 3] = 255;
                    }
                bitmap.WritePixels(new Int32Rect(0, 0, n, n), buffer, n * 4, 0);
                var color = PixelColor.At(desktop, ix, iy);
                swatch.Background = Frozen(Color.FromRgb(color.R, color.G, color.B));
                value.Text = color.Format(format);
                loupe.Visibility = Visibility.Visible;
                surface.Place(loupe, x, y);
            }

            surface.Moved += Update;
            surface.Canvas.MouseLeftButtonUp += (_, _) => Pick();
            surface.Canvas.MouseWheel += (_, e) =>
            {
                zoom = Math.Clamp(zoom + (e.Delta > 0 ? -1 : 1), 0, Zooms.Length - 1);
                Update();
            };
            surface.Window.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Space) Pick();
            };

            void Pick()
            {
                GetCursorPos(out var p);
                finish(PixelColor.At(desktop, p.X - bounds.Left, p.Y - bounds.Top));
            }
        });
    }

    // ── Regla ────────────────────────────────────────────────────────────

    public static Task<(int Width, int Height)?> MeasureAsync(BgraImage desktop, ScreenRect bounds)
    {
        (int Width, int Height) measured = (0, 0); // the last measure, on whichever monitor (Enter may go to another one's window)
        return Run<(int Width, int Height)>(desktop, bounds, Loc.T("capture.rulerHint"), (surface, finish) =>
        {
            var accent = (Brush?)Application.Current.TryFindResource("Kd.Accent") ?? Brushes.Gold;
            var lines = new Path { Stroke = accent, StrokeThickness = 1.5, IsHitTestVisible = false, SnapsToDevicePixels = true };
            var frame = new Rectangle { Stroke = accent, StrokeThickness = 2, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            var label = new Border
            {
                Background = LabelBack, CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 3, 8, 3), IsHitTestVisible = false, Visibility = Visibility.Collapsed,
                Child = new TextBlock { Foreground = Brushes.White, FontSize = 13, FontFamily = UiFont },
            };
            surface.Canvas.Children.Add(lines);
            surface.Canvas.Children.Add(frame);
            surface.Canvas.Children.Add(label);
            surface.Canvas.Cursor = Cursors.Cross;

            (int X, int Y)? start = null; // physical, while dragging
            var monitor = surface.Monitor;
            var limit = new ScreenRect(monitor.Left - bounds.Left, monitor.Top - bounds.Top, monitor.Width, monitor.Height);

            void Update()
            {
                if (!surface.PointerHere(out var x, out var y))
                {
                    lines.Data = null;
                    label.Visibility = frame.Visibility = Visibility.Collapsed;
                    return;
                }
                if (start is { } s)
                {
                    // A box: from where the button went down to the pointer, both pixels inside.
                    int left = Math.Min(s.X, x), top = Math.Min(s.Y, y), right = Math.Max(s.X, x), bottom = Math.Max(s.Y, y);
                    measured = (right - left + 1, bottom - top + 1);
                    lines.Data = null;
                    var a = surface.ToDip(left, top);
                    var b = surface.ToDip(right + 1, bottom + 1);
                    frame.Width = b.X - a.X;
                    frame.Height = b.Y - a.Y;
                    Canvas.SetLeft(frame, a.X);
                    Canvas.SetTop(frame, a.Y);
                    frame.Visibility = Visibility.Visible;
                }
                else
                {
                    // Between the edges around the pointer: a line across and one down, with a tick at each end.
                    var (l, t, r, btm) = ScreenMeasure.Spacing(desktop, x - bounds.Left, y - bounds.Top, limit);
                    measured = (r - l + 1, btm - t + 1);
                    var p = surface.ToDip(x - bounds.Left, y - bounds.Top, image: true);
                    var from = surface.ToDip(l, t, image: true);
                    var to = surface.ToDip(r + 1, btm + 1, image: true);
                    double cx = p.X + surface.Pixel.X / 2, cy = p.Y + surface.Pixel.Y / 2;
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        void Line(double x1, double y1, double x2, double y2)
                        {
                            c.BeginFigure(new Point(x1, y1), false, false);
                            c.LineTo(new Point(x2, y2), true, false);
                        }
                        Line(from.X, cy, to.X, cy);
                        Line(cx, from.Y, cx, to.Y);
                        Line(from.X, cy - 5, from.X, cy + 5);
                        Line(to.X, cy - 5, to.X, cy + 5);
                        Line(cx - 5, from.Y, cx + 5, from.Y);
                        Line(cx - 5, to.Y, cx + 5, to.Y);
                    }
                    g.Freeze();
                    lines.Data = g;
                    frame.Visibility = Visibility.Collapsed;
                }
                ((TextBlock)label.Child).Text = ScreenMeasure.Text(measured.Width, measured.Height);
                label.Visibility = Visibility.Visible;
                surface.Place(label, x, y);
            }

            surface.Moved += Update;
            surface.Canvas.MouseLeftButtonDown += (_, _) =>
            {
                GetCursorPos(out var p);
                start = (p.X, p.Y);
                surface.Canvas.CaptureMouse();
                Update();
            };
            surface.Canvas.MouseLeftButtonUp += (_, _) =>
            {
                if (start is not { } s) return;
                surface.Canvas.ReleaseMouseCapture();
                GetCursorPos(out var p);
                start = null;
                // A click without dragging copies the distance between the edges.
                if (Math.Abs(p.X - s.X) < 3 && Math.Abs(p.Y - s.Y) < 3) Update();
                finish(measured);
            };
            surface.Window.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Space) finish(measured);
            };
        });
    }

    // ── the frozen screen ────────────────────────────────────────────────

    private static readonly FontFamily UiFont = new("Segoe UI Variable Text, Segoe UI");

    /// <summary>One monitor's window: its picture, where the pointer is and how to turn screen pixels into its DIPs.</summary>
    private sealed class Surface(Window window, Canvas canvas, ScreenRect monitor, ScreenRect bounds)
    {
        public Window Window { get; } = window;
        public Canvas Canvas { get; } = canvas;
        public ScreenRect Monitor { get; } = monitor;

        /// <summary>The pointer moved (or the zoom changed): redraw.</summary>
        public event Action? Moved;

        public void RaiseMoved() => Moved?.Invoke();

        /// <summary>One screen pixel in DIPs of this window.</summary>
        public Point Pixel => new(Canvas.ActualWidth / Math.Max(1, Monitor.Width), Canvas.ActualHeight / Math.Max(1, Monitor.Height));

        /// <summary>The pointer, in screen pixels, if it is on this monitor.</summary>
        public bool PointerHere(out int x, out int y)
        {
            GetCursorPos(out var p);
            (x, y) = (p.X, p.Y);
            return x >= Monitor.Left && x < Monitor.Right && y >= Monitor.Top && y < Monitor.Bottom;
        }

        /// <summary>A screen pixel (or, with <paramref name="image"/>, a pixel of the desktop picture) in DIPs of this window.</summary>
        public Point ToDip(int x, int y, bool image = false)
        {
            if (image) (x, y) = (x + bounds.Left, y + bounds.Top);
            var pixel = Pixel;
            return new Point((x - Monitor.Left) * pixel.X, (y - Monitor.Top) * pixel.Y);
        }

        /// <summary>Puts <paramref name="element"/> down and right of the pointer, or on the other side near an edge.</summary>
        public void Place(FrameworkElement element, int x, int y)
        {
            element.UpdateLayout();
            var p = ToDip(x, y);
            double w = element.ActualWidth, h = element.ActualHeight;
            var left = p.X + 22 + w > Canvas.ActualWidth ? p.X - 22 - w : p.X + 22;
            var top = p.Y + 22 + h > Canvas.ActualHeight ? p.Y - 22 - h : p.Y + 22;
            Canvas.SetLeft(element, Math.Max(0, left));
            Canvas.SetTop(element, Math.Max(0, top));
        }
    }

    private static Task<T?> Run<T>(BgraImage desktop, ScreenRect bounds, string hintText, Action<Surface, Action<T?>> setup) where T : struct
    {
        var done = new TaskCompletionSource<T?>();
        var windows = new List<Window>();

        void Finish(T? result)
        {
            if (done.Task.IsCompleted) return;
            done.TrySetResult(result);
            foreach (var w in windows) w.Close();
        }

        foreach (var monitor in ScreenCapture.Monitors())
        {
            var part = desktop.Crop(monitor.Left - bounds.Left, monitor.Top - bounds.Top, monitor.Width, monitor.Height);
            var surface = Create<T>(part, monitor, bounds, hintText, Finish);
            setup(surface, Finish);
            windows.Add(surface.Window);
        }
        foreach (var w in windows) w.Show();
        windows.FirstOrDefault()?.Activate();
        return done.Task;
    }

    private static Surface Create<T>(BgraImage picture, ScreenRect monitor, ScreenRect bounds, string hintText, Action<T?> finish) where T : struct
    {
        var bitmap = BitmapSource.Create(picture.Width, picture.Height, 96, 96, PixelFormats.Bgra32, null, picture.Pixels, picture.Stride);
        bitmap.Freeze();

        var canvas = new Canvas { Background = Brushes.Transparent, ClipToBounds = true };
        var image = new Image { Source = bitmap, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var hint = new Border
        {
            Background = LabelBack, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 14, 8), IsHitTestVisible = false,
            Child = new TextBlock { Foreground = Brushes.White, FontSize = 14, Text = hintText },
        };
        canvas.Children.Add(image);
        canvas.Children.Add(hint);

        var window = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
            ShowActivated = true, Background = Brushes.Black, Content = canvas, Title = "K-Desktop",
            Left = 0, Top = 0, Width = 100, Height = 100,
        };
        var surface = new Surface(window, canvas, monitor, bounds);

        // Physical placement: covers the monitor exactly, whatever its scale.
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            SetWindowPos(hwnd, new IntPtr(-1), monitor.Left, monitor.Top, monitor.Width, monitor.Height, 0x0040 /* SHOWWINDOW */);
        };

        void Layout()
        {
            image.Width = canvas.ActualWidth;
            image.Height = canvas.ActualHeight;
            Canvas.SetLeft(hint, (canvas.ActualWidth - hint.ActualWidth) / 2);
            Canvas.SetTop(hint, 28);
        }
        canvas.SizeChanged += (_, _) => Layout();
        hint.SizeChanged += (_, _) => Layout();

        canvas.MouseMove += (_, e) =>
        {
            // The hint goes away while the pointer is near it (it could hide what is under it).
            var p = e.GetPosition(canvas);
            var near = new Rect(Canvas.GetLeft(hint) - 40, Canvas.GetTop(hint) - 40, hint.ActualWidth + 80, hint.ActualHeight + 80);
            hint.Visibility = near.Contains(p) ? Visibility.Hidden : Visibility.Visible;
            surface.RaiseMoved();
        };
        canvas.MouseLeave += (_, _) => surface.RaiseMoved();
        window.ContentRendered += (_, _) => surface.RaiseMoved(); // with the pointer still: the magnifier from the start
        canvas.MouseRightButtonUp += (_, _) => finish(null);
        window.KeyDown += (_, e) =>
        {
            // One pixel at a time with the arrows (the pointer moves, so every monitor redraws itself).
            var (dx, dy) = e.Key switch { Key.Left => (-1, 0), Key.Right => (1, 0), Key.Up => (0, -1), Key.Down => (0, 1), _ => (0, 0) };
            if (dx != 0 || dy != 0)
            {
                GetCursorPos(out var c);
                SetCursorPos(c.X + dx, c.Y + dy);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) finish(null);
        };
        window.Closed += (_, _) => finish(null);
        return surface;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

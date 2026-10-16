using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using KDesktop.Core.Platform;

namespace KDesktop.Osd;

/// <summary>
/// «Captura de pantalla › Región»: the screen as it was, frozen and dimmed, one window per monitor; drag a rectangle
/// (its size shows in pixels). Esc or the right button cancels. Works in physical pixels, whatever each monitor's scale.
/// </summary>
public static class RegionSelector
{
    public static Task<ScreenRect?> SelectAsync(BgraImage desktop, ScreenRect bounds)
    {
        var done = new TaskCompletionSource<ScreenRect?>();
        var windows = new List<Window>();

        void Finish(ScreenRect? result)
        {
            if (done.Task.IsCompleted) return;
            done.TrySetResult(result);
            foreach (var w in windows) w.Close();
        }

        foreach (var monitor in ScreenCapture.Monitors())
        {
            var part = desktop.Crop(monitor.Left - bounds.Left, monitor.Top - bounds.Top, monitor.Width, monitor.Height);
            windows.Add(Create(part, monitor, Finish));
        }
        foreach (var w in windows) w.Show();
        windows.FirstOrDefault()?.Activate();
        return done.Task;
    }

    private static Window Create(BgraImage picture, ScreenRect monitor, Action<ScreenRect?> finish)
    {
        var bitmap = BitmapSource.Create(picture.Width, picture.Height, 96, 96, PixelFormats.Bgra32, null, picture.Pixels, picture.Stride);
        bitmap.Freeze();

        var canvas = new Canvas { Background = Brushes.Transparent, Cursor = Cursors.Cross, ClipToBounds = true };
        var image = new Image { Source = bitmap, Stretch = Stretch.Fill };
        var dim = new Path { Fill = new SolidColorBrush(Color.FromArgb(0x78, 0, 0, 0)), IsHitTestVisible = false };
        var frame = new Rectangle
        {
            Stroke = (Brush?)Application.Current.TryFindResource("Kd.Accent") ?? Brushes.Gold,
            StrokeThickness = 2, Visibility = Visibility.Collapsed, IsHitTestVisible = false,
        };
        var size = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x20, 0x20, 0x20)),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 3, 8, 3), Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            Child = new TextBlock { Foreground = Brushes.White, FontSize = 13, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") },
        };
        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x20, 0x20, 0x20)),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 14, 8), IsHitTestVisible = false,
            Child = new TextBlock { Foreground = Brushes.White, FontSize = 14, Text = Core.Localization.Loc.T("capture.regionHint") },
        };
        canvas.Children.Add(image);
        canvas.Children.Add(dim);
        canvas.Children.Add(frame);
        canvas.Children.Add(size);
        canvas.Children.Add(hint);

        var window = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true,
            ShowActivated = true, Background = Brushes.Black, Content = canvas, Title = "K-Desktop",
            Left = 0, Top = 0, Width = 100, Height = 100,
        };

        // Physical placement: covers the monitor exactly, whatever its scale.
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            SetWindowPos(hwnd, new IntPtr(-1), monitor.Left, monitor.Top, monitor.Width, monitor.Height, 0x0040 /* SHOWWINDOW */);
        };

        Point? start = null;
        void Layout()
        {
            var w = canvas.ActualWidth;
            var h = canvas.ActualHeight;
            image.Width = w;
            image.Height = h;
            Canvas.SetLeft(hint, (w - hint.ActualWidth) / 2);
            Canvas.SetTop(hint, 28);
            dim.Data = new RectangleGeometry(new Rect(0, 0, w, h));
        }
        canvas.SizeChanged += (_, _) => Layout();
        hint.SizeChanged += (_, _) => Layout();

        // DIPs of this window → physical pixels of the screen.
        ScreenRect ToPhysical(Rect r)
        {
            double sx = monitor.Width / Math.Max(1, canvas.ActualWidth), sy = monitor.Height / Math.Max(1, canvas.ActualHeight);
            var left = (int)Math.Round(r.Left * sx);
            var top = (int)Math.Round(r.Top * sy);
            return new ScreenRect(monitor.Left + left, monitor.Top + top, (int)Math.Round(r.Right * sx) - left, (int)Math.Round(r.Bottom * sy) - top);
        }

        void Update(Point p)
        {
            if (start is not { } s) return;
            var rect = new Rect(s, p);
            dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight)), new RectangleGeometry(rect));
            frame.Visibility = Visibility.Visible;
            frame.Width = rect.Width;
            frame.Height = rect.Height;
            Canvas.SetLeft(frame, rect.Left);
            Canvas.SetTop(frame, rect.Top);
            var physical = ToPhysical(rect);
            ((TextBlock)size.Child).Text = $"{physical.Width} × {physical.Height}";
            size.Visibility = Visibility.Visible;
            Canvas.SetLeft(size, rect.Left);
            Canvas.SetTop(size, rect.Top >= 34 ? rect.Top - 32 : rect.Bottom + 6);
            hint.Visibility = Visibility.Collapsed;
        }

        canvas.MouseLeftButtonDown += (_, e) =>
        {
            start = e.GetPosition(canvas);
            canvas.CaptureMouse();
        };
        canvas.MouseMove += (_, e) => Update(e.GetPosition(canvas));
        canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (start is not { } s) return;
            canvas.ReleaseMouseCapture();
            var rect = new Rect(s, e.GetPosition(canvas));
            start = null;
            // A click without dragging takes the whole monitor.
            finish(rect.Width < 3 && rect.Height < 3 ? monitor : ToPhysical(rect));
        };
        canvas.MouseRightButtonUp += (_, _) => finish(null);
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) finish(null);
        };
        window.Deactivated += (_, _) => { /* other monitors' windows take the focus: not a cancel */ };
        window.Closed += (_, _) => finish(null);
        return window;
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

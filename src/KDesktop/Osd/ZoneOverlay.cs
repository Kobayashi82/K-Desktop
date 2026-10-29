using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using KDesktop.Native;

namespace KDesktop.Osd;

/// <summary>
/// The zones of a monitor over its work area while a window is dragged with Shift (E1b): with the surfaces of the menus
/// (<c>Kd.MenuTint</c>, <c>Kd.MenuBorder</c>), each one with its number and its programs as in the drawing of
/// «Disposiciones»; the one under the cursor marked as the marked zone there (<c>Kd.AccentSoft</c>, <c>Kd.Accent</c>). It
/// fades in like the launcher and never takes the focus nor the clicks.
/// </summary>
public sealed class ZoneOverlay
{
    private const double Gap = 8;

    private readonly Window _window;
    private readonly Canvas _canvas = new();
    private readonly ScreenRect _area;
    private readonly List<(SnapZone Zone, Border Box, TextBlock Number)> _boxes = [];
    private SnapZone? _hover;

    public ZoneOverlay(ScreenRect area, IReadOnlyList<SnapZone> zones)
    {
        _area = area;
        foreach (var zone in zones) _boxes.Add(MakeBox(zone));
        _window = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, ShowInTaskbar = false,
            Topmost = true, ShowActivated = false, Focusable = false, IsHitTestVisible = false, Title = "K-Desktop",
            Background = Brushes.Transparent, Opacity = 0, Left = 0, Top = 0, Width = 10, Height = 10, Content = _canvas,
        };
        _window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(_window).Handle;
            UiNative.AddExStyle(hwnd, toolWindow: true, noActivate: true, clickThrough: true);
            // Physical placement, whatever the monitor's scale.
            SetWindowPos(hwnd, new IntPtr(-1), area.Left, area.Top, area.Width, area.Height, 0x0010 /* NOACTIVATE */ | 0x0040 /* SHOWWINDOW */);
        };
        _canvas.SizeChanged += (_, _) => Layout();
    }

    public void Show()
    {
        _window.Show();
        _window.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    public void Close() => _window.Close();

    /// <summary>The zone under the cursor (null: none on this monitor).</summary>
    public void Mark(SnapZone? zone)
    {
        if (ReferenceEquals(zone, _hover)) return;
        _hover = zone;
        Paint();
    }

    private (SnapZone, Border, TextBlock) MakeBox(SnapZone zone)
    {
        var number = new TextBlock
        {
            Text = zone.Number.ToString(Loc.Instance.Culture), FontSize = 16, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(14, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        var label = new TextBlock
        {
            Text = zone.Zone.Label, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center,
            Visibility = zone.Zone.Programs.Count > 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var box = new Border
        {
            CornerRadius = new CornerRadius(8),
            Child = new Grid { Children = { label, number } },
        };
        System.Windows.Documents.TextElement.SetFontFamily(box, new FontFamily("Segoe UI Variable Text, Segoe UI"));
        _canvas.Children.Add(box);
        return (zone, box, number);
    }

    private void Layout()
    {
        // Physical pixels of the work area → DIPs of this window.
        double sx = _canvas.ActualWidth / Math.Max(1, _area.Width), sy = _canvas.ActualHeight / Math.Max(1, _area.Height);
        foreach (var (zone, box, _) in _boxes)
        {
            Canvas.SetLeft(box, (zone.Rect.Left - _area.Left) * sx + Gap / 2);
            Canvas.SetTop(box, (zone.Rect.Top - _area.Top) * sy + Gap / 2);
            box.Width = Math.Max(0, zone.Rect.Width * sx - Gap);
            box.Height = Math.Max(0, zone.Rect.Height * sy - Gap);
        }
        Paint();
    }

    private void Paint()
    {
        foreach (var (zone, box, number) in _boxes)
        {
            var marked = ReferenceEquals(zone, _hover);
            box.BorderThickness = new Thickness(marked ? 2 : 1);
            box.SetResourceReference(Border.BackgroundProperty, marked ? "Kd.AccentSoft" : "Kd.MenuTint");
            box.SetResourceReference(Border.BorderBrushProperty, marked ? "Kd.Accent" : "Kd.MenuBorder");
            number.SetResourceReference(TextBlock.ForegroundProperty, marked ? "Kd.AccentText" : "TextFillColorSecondaryBrush");
        }
    }

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KDesktop.Core.Platform;

namespace KDesktop.Osd;

/// <summary>
/// «Captura de pantalla» with the flash of Settings › Capturas: a white veil over what was taken that fades away. It
/// never takes the focus nor the clicks, and it comes after the picture (it is never in it).
/// </summary>
public static class CaptureFlash
{
    public static void Show(ScreenRect area)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, ShowInTaskbar = false,
            Topmost = true, ShowActivated = false, Focusable = false, IsHitTestVisible = false, Title = "K-Desktop",
            Background = Brushes.White, Opacity = 0, Left = 0, Top = 0, Width = 10, Height = 10,
        };
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            // Physical placement, whatever the monitor's scale.
            SetWindowPos(hwnd, new IntPtr(-1), area.Left, area.Top, area.Width, area.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        };
        var fade = new DoubleAnimation(0.6, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        fade.Completed += (_, _) => window.Close();
        window.Show();
        window.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

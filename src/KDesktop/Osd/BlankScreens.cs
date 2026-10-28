using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using KDesktop.Core;
using KDesktop.Core.Platform;

namespace KDesktop.Osd;

/// <summary>
/// «Apagar pantallas» of some monitors only: Windows turns off all of them or none, so the chosen ones are covered in
/// black (on OLED screens the pixels are off). They come back with a click on them or with the action again; the mouse
/// and the keys go on working on the others (a film on one, the other off).
/// </summary>
public static class BlankScreens
{
    private static readonly List<Window> Open = [];

    /// <summary>Covers those monitors, or uncovers them all when some are covered.</summary>
    public static void Toggle(IReadOnlyCollection<string> devices)
    {
        if (Open.Count > 0)
        {
            CloseAll();
            return;
        }
        foreach (var device in devices)
        {
            if (DisplayTools.Bounds(device) is not { } bounds)
            {
                Log.Warn($"Apagar pantallas: {device} no está encendido");
                continue;
            }
            var window = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = false,
                Background = Brushes.Black,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                Cursor = Cursors.None,
                Title = "K-Desktop",
                Left = -32000,
                Top = -32000,
                Width = 10,
                Height = 10,
            };
            window.SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                // Out of Alt + Tab, and on the monitor in its own pixels (whatever its scale).
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);
                SetWindowPos(hwnd, HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            };
            window.MouseDown += (_, _) => CloseAll();
            window.Closed += (_, _) => Open.Remove(window);
            Open.Add(window);
            window.Show();
        }
    }

    public static void CloseAll()
    {
        foreach (var window in Open.ToList()) window.Close();
        Open.Clear();
    }

    private const int GWL_EXSTYLE = -20;
    private const nint WS_EX_TOOLWINDOW = 0x80;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(IntPtr hwnd, int index, nint value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

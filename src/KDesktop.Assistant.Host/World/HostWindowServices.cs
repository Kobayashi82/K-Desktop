using KDesktop.Assistant.Sdk;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.World;

/// <summary>
/// Moving and reading windows from the assistant process (<see cref="IWindowServices"/>). Windows of elevated
/// processes are out of reach of a normal-user process: <see cref="TryMove"/> says no and K-Desktop places them.
/// </summary>
internal sealed unsafe class HostWindowServices(WindowScanner scanner) : IWindowServices
{
    public bool TryMove(long handle, PixelRect bounds)
    {
        var hwnd = (nint)handle;
        if (bounds.IsEmpty || !IsWindow(hwnd) || IsZoomed(hwnd) || scanner.ProcessOf(hwnd).Elevated) return false;
        // The bounds are DWM's (no invisible borders); SetWindowPos wants the window rectangle, borders included.
        if (!GetWindowRect(hwnd, out var outer) || !WindowScanner.TryGetBounds(hwnd, out var frame)) return false;
        int left = frame.Left - outer.Left, top = frame.Top - outer.Top, right = outer.Right - frame.Right, bottom = outer.Bottom - frame.Bottom;
        return SetWindowPos(hwnd, 0, bounds.Left - left, bounds.Top - top, bounds.Width + left + right, bounds.Height + top + bottom,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// From DWM (<c>DWMWA_CAPTION_BUTTON_BOUNDS</c>, relative to the window rectangle): works with standard windows
    /// and with most that draw their own title bar. The area is split in three: minimize, maximize, close.
    /// </summary>
    public CaptionButtons? GetCaptionButtons(long handle)
    {
        var hwnd = (nint)handle;
        RECT area;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CAPTION_BUTTON_BOUNDS, &area, (uint)sizeof(RECT)) != 0 || !GetWindowRect(hwnd, out var outer))
            return null;
        var width = area.Right - area.Left;
        if (width < 30 || area.Bottom - area.Top < 8) return null;
        int top = outer.Top + area.Top, bottom = outer.Top + area.Bottom, left = outer.Left + area.Left, third = width / 3;
        return new CaptionButtons(
            new PixelRect(left, top, left + third, bottom),
            new PixelRect(left + third, top, left + 2 * third, bottom),
            new PixelRect(left + 2 * third, top, outer.Left + area.Right, bottom));
    }

    // Pictures of windows (ghosts of elevated windows, effects) come with the gestures that use them (phases 6–7).
    public System.Windows.Media.ImageSource? Thumbnail(long handle, int maxSize) => null;

    public System.Windows.Media.ImageSource? AppIcon(long handle) => null;
}

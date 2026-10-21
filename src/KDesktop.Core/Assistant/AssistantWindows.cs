using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Native;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Assistant;

/// <summary>
/// The windows K-Desktop changes for an assistant (stage 6.5, <c>window</c>): only top-level windows of other programs
/// (never K-Desktop's own nor the desktop), and only move, minimize, maximize, restore or bring to the front.
/// </summary>
public static class AssistantWindows
{
    /// <summary>Does what <paramref name="request"/> asks; false when it is not allowed or could not be done.</summary>
    public static bool Apply(WindowMessage request)
    {
        var hwnd = (IntPtr)request.Hwnd;
        if (!Allowed(hwnd)) return false;
        try
        {
            switch (request.Op)
            {
                case "move" when request.Rect is { W: > 0, H: > 0 } r:
                    WindowLayout.MoveTo(hwnd, new ScreenRect(r.X, r.Y, r.W, r.H));
                    return true;
                case "minimize":
                    WindowLayout.Show(hwnd, Win32.SW_MINIMIZE);
                    return true;
                case "maximize":
                    WindowLayout.Show(hwnd, Win32.SW_SHOWMAXIMIZED);
                    return true;
                case "restore":
                    WindowLayout.Show(hwnd, Win32.SW_RESTORE);
                    return true;
                case "activate":
                    if (Win32.IsIconic(hwnd)) WindowLayout.Show(hwnd, Win32.SW_RESTORE);
                    WindowTools.ForceForeground(hwnd);
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Asistente: no se ha podido cambiar la ventana ({request.Op}): {ex.Message}");
            return false;
        }
    }

    /// <summary>A visible top-level window of another program, not the desktop's.</summary>
    public static bool Allowed(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd) || Win32.GetAncestor(hwnd, Win32.GA_ROOT) != hwnd || hwnd == Win32.GetShellWindow()) return false;
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        return pid != (uint)Environment.ProcessId;
    }
}

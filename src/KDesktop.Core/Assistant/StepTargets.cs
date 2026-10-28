using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Input;
using KDesktop.Core.Model;
using KDesktop.Core.Native;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Assistant;

/// <summary>
/// The place of each step of a choreographed run (stage 6.2): the window an action acts on, found as the engine will find
/// it, and where it ends up, with the same calculations the engine then applies (a «Colocar» or a move to another monitor
/// to the pixel; restoring or centring a maximised window, to a pixel or two). Actions without a place give null.
/// </summary>
public static class StepTargets
{
    /// <summary>Where <paramref name="action"/> happens if it runs now, with <paramref name="foreground"/> in front.</summary>
    public static StepTarget? Of(CommandAction action, ForegroundWindow foreground)
    {
        try
        {
            return action switch
            {
                WindowAction w => Window(w, foreground),
                AppControlAction { Mode: AppControlMode.Activate or AppControlMode.Close } a =>
                    Describe(WindowLayout.Find(WindowTarget.Process, a.ProcessName, IntPtr.Zero), null),
                RunAppAction r when Path.GetExtension(r.Path).Equals(".exe", StringComparison.OrdinalIgnoreCase) =>
                    new StepTarget(0, ProcessNames.Normalize(Path.GetFileName(r.Path)), string.Empty, "normal", false),
                TopMostAction or SendInputAction { Mode: SendInputMode.Keys or SendInputMode.Text }
                    or ScreenshotAction { Area: CaptureArea.ActiveWindow } or ScreenTextAction { Area: CaptureArea.ActiveWindow } => InFront(foreground),
                _ => null,
            };
        }
        catch (Exception ex)
        {
            Log.Warn($"Asistente: no se ha podido saber dónde ocurre «{action.Summary}»: {ex.Message}");
            return null;
        }
    }

    private static StepTarget? Window(WindowAction a, ForegroundWindow foreground)
    {
        var hwnd = a.FindWindow(foreground.RootHandle);
        if (hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd) || hwnd == Win32.GetShellWindow()) return null;
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId) return null; // the engine never touches K-Desktop's own windows
        var maximized = WindowLayout.IsMaximized(hwnd);
        var minimized = Win32.IsIconic(hwnd);
        var area = WindowLayout.WorkAreaOf(hwnd);
        ScreenRect? result = a.Operation switch
        {
            WindowOperation.Maximize => area,
            WindowOperation.Minimize or WindowOperation.Close => null,
            WindowOperation.Restore => WindowLayout.RestoredBounds(hwnd),
            WindowOperation.ToggleMaximize => maximized ? WindowLayout.RestoredBounds(hwnd) : area,
            WindowOperation.Snap => a.Place ?? WindowLayout.SnapRect(area, a.Snap, a.X, a.Y, a.Width, a.Height),
            WindowOperation.Center => WindowLayout.Centered(WindowLayout.RestoredBounds(hwnd), area),
            WindowOperation.MoveToMonitor => WindowLayout.MonitorTarget(hwnd, a.Monitor, a.MonitorNumber) is var (from, to)
                ? maximized ? to : WindowLayout.MapRect(WindowLayout.RestoredBounds(hwnd), from, to)
                : null,
            _ => minimized ? null : WindowLayout.VisibleBounds(hwnd),
        };
        return Describe(hwnd, result, maximized, minimized);
    }

    /// <summary>
    /// The window in front when the command started: the one the engine fixes on top, types into or captures (never
    /// K-Desktop's own, nor the desktop).
    /// </summary>
    private static StepTarget? InFront(ForegroundWindow foreground)
    {
        var hwnd = foreground.RootHandle;
        if (hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd) || hwnd == Win32.GetShellWindow() || foreground.ProcessId == (uint)Environment.ProcessId) return null;
        return Describe(hwnd, Win32.IsIconic(hwnd) ? null : WindowLayout.VisibleBounds(hwnd));
    }

    /// <summary>A window as a step's target (null for none).</summary>
    private static StepTarget? Describe(IntPtr hwnd, ScreenRect? result) =>
        hwnd == IntPtr.Zero ? null : Describe(hwnd, result, WindowLayout.IsMaximized(hwnd), Win32.IsIconic(hwnd));

    private static StepTarget Describe(IntPtr hwnd, ScreenRect? result, bool maximized, bool minimized)
    {
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        return new StepTarget(hwnd.ToInt64(), ProcessNames.Get(pid), Win32.GetWindowTitle(hwnd),
            minimized ? "minimized" : maximized ? "maximized" : "normal", WindowTools.IsElevated(hwnd),
            minimized ? null : Rect(WindowLayout.VisibleBounds(hwnd)), result is { } r ? Rect(r) : null);
    }

    private static StepRect Rect(ScreenRect r) => new(r.Left, r.Top, r.Width, r.Height);
}

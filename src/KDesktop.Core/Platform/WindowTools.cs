using System.Diagnostics;
using KDesktop.Core.Input;
using KDesktop.Core.Native;

namespace KDesktop.Core.Platform;

public static class WindowTools
{
    public static bool IsTopMost(IntPtr hwnd) =>
        (Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64() & Win32.WS_EX_TOPMOST) != 0;

    public static void SetTopMost(IntPtr hwnd, bool topMost) =>
        Win32.SetWindowPos(hwnd, topMost ? Win32.HWND_TOPMOST : Win32.HWND_NOTOPMOST, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);

    /// <summary>Toggles "always on top" and returns the new state.</summary>
    public static bool ToggleTopMost(IntPtr hwnd)
    {
        var state = !IsTopMost(hwnd);
        SetTopMost(hwnd, state);
        return state;
    }

    /// <summary>Brings a window to the foreground, working around the foreground lock.</summary>
    public static bool ForceForeground(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd)) return false;
        if (Win32.IsIconic(hwnd)) Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
        if (Win32.GetForegroundWindow() == hwnd) return true;

        // Synthetic input releases the foreground lock for the calling process.
        InputSimulator.SendMenuMask();
        if (Win32.SetForegroundWindow(hwnd)) return true;

        var foreground = Win32.GetForegroundWindow();
        var fgThread = Win32.GetWindowThreadProcessId(foreground, out _);
        var current = Win32.GetCurrentThreadId();
        var attached = fgThread != 0 && fgThread != current && Win32.AttachThreadInput(current, fgThread, true);
        try
        {
            Win32.BringWindowToTop(hwnd);
            Win32.ShowWindow(hwnd, Win32.SW_SHOW);
            return Win32.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) Win32.AttachThreadInput(current, fgThread, false);
        }
    }

    /// <summary>Visible, unowned top-level windows that belong to the given process ids.</summary>
    public static List<IntPtr> TopLevelWindows(ICollection<uint> pids)
    {
        var result = new List<IntPtr>();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (!Win32.IsWindowVisible(hwnd) || Win32.GetWindow(hwnd, Win32.GW_OWNER) != IntPtr.Zero) return true;
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pids.Contains(pid) && Win32.GetWindowTextLength(hwnd) > 0) result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static Process[] FindProcesses(string name)
    {
        var n = ProcessNames.Normalize(name);
        return n.Length == 0 ? [] : Process.GetProcessesByName(n);
    }

    /// <summary>Restores and activates the main window of a process. Returns false when it is not running.</summary>
    public static bool Activate(string processName)
    {
        var processes = FindProcesses(processName);
        try
        {
            if (processes.Length == 0) return false;
            var windows = TopLevelWindows(processes.Select(p => (uint)p.Id).ToHashSet());
            var target = windows.FirstOrDefault();
            if (target == IntPtr.Zero) target = processes.Select(p => p.MainWindowHandle).FirstOrDefault(h => h != IntPtr.Zero);
            return target != IntPtr.Zero && ForceForeground(target);
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    /// <summary>Closes (politely or by force) every process with that name.</summary>
    public static async Task<bool> CloseAsync(string processName, bool force, CancellationToken token)
    {
        var processes = FindProcesses(processName);
        if (processes.Length == 0) return false;
        try
        {
            if (force)
            {
                foreach (var p in processes)
                {
                    try { p.Kill(); } catch (Exception ex) { Log.Warn($"No se pudo terminar {p.ProcessName}: {ex.Message}"); }
                }
            }
            else
            {
                var pids = processes.Select(p => (uint)p.Id).ToHashSet();
                foreach (var hwnd in TopLevelWindows(pids)) Win32.PostMessage(hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                foreach (var p in processes)
                {
                    try { p.CloseMainWindow(); } catch { /* ignore */ }
                }
            }
            // Give them a moment to exit.
            for (var i = 0; i < 30 && FindProcesses(processName) is { Length: > 0 } left; i++)
            {
                foreach (var l in left) l.Dispose();
                await Task.Delay(100, token);
            }
            return true;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }
}

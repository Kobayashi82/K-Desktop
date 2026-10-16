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

    /// <summary>Full path of a process's exe. Works across elevation levels (Process.MainModule does not).</summary>
    public static string? ProcessPath(int pid) => Win32.GetProcessPath((uint)pid);

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
                QuitPlayers(pids);
                foreach (var hwnd in TopLevelWindows(pids)) Win32.PostMessage(hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                foreach (var p in processes)
                {
                    try { p.CloseMainWindow(); } catch { /* ignore */ }
                }
            }
            // Give them a moment to exit.
            for (var i = 0; i < 30; i++)
            {
                var left = FindProcesses(processName);
                foreach (var l in left) l.Dispose();
                if (left.Length == 0) return true;
                await Task.Delay(100, token);
            }
            return false;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    private const int WM_COMMAND = 0x0111;
    private const int WM_AIMP_COMMAND = Win32.WM_USER + 0x75, AIMP_RA_CMD_QUIT = 21;
    private const int WINAMP_FILE_QUIT = 40001;

    /// <summary>
    /// Music players live in the tray with every window hidden, where WM_CLOSE reaches nothing (and closing the main
    /// window often just hides it): the ones with a remote control are asked to quit through it — AIMP's own, and
    /// Winamp's, which AIMP, Winamp and others answer.
    /// </summary>
    private static void QuitPlayers(ICollection<uint> pids)
    {
        IntPtr aimp = IntPtr.Zero, winamp = IntPtr.Zero;
        Win32.EnumWindows((hwnd, _) =>
        {
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            if (!pids.Contains(pid)) return true;
            switch (Win32.GetClassName(hwnd))
            {
                case "AIMP2_RemoteInfo": aimp = hwnd; break;
                case "Winamp v1.x": winamp = hwnd; break;
            }
            return true;
        }, IntPtr.Zero);
        if (aimp != IntPtr.Zero) Win32.PostMessage(aimp, WM_AIMP_COMMAND, (IntPtr)AIMP_RA_CMD_QUIT, IntPtr.Zero);
        else if (winamp != IntPtr.Zero) Win32.PostMessage(winamp, WM_COMMAND, (IntPtr)WINAMP_FILE_QUIT, IntPtr.Zero);
    }
}

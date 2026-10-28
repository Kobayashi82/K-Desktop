using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using KDesktop.Core.Model;
using KDesktop.Core.Native;

namespace KDesktop.Core.Platform;

/// <summary>Everything needed to start a program, serialisable so the non-elevated helper can run it.</summary>
public sealed record LaunchSpec(
    string FileName,
    string Arguments = "",
    string? WorkingDirectory = null,
    WindowMode Window = WindowMode.Normal,
    ProcessPriorityLevel Priority = ProcessPriorityLevel.Normal,
    bool TopMost = false,
    bool Wait = false);

/// <summary>
/// Starts processes either elevated (in-process) or as the normal, non-elevated user.
/// K-Desktop runs elevated (needed so its hooks also work over elevated windows); to start something
/// without elevation it re-launches its own executable in "helper mode" with the Explorer token
/// (<c>CreateProcessWithTokenW</c>), and that helper performs a regular ShellExecute.
/// </summary>
public static class ProcessLauncher
{
    public const string HelperArgument = "--kd-launch";

    public static bool IsElevated { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Launches <paramref name="spec"/>. Never throws; returns false on failure.</summary>
    public static bool Launch(LaunchSpec spec, bool asAdmin)
    {
        try
        {
            if (asAdmin || !IsElevated) return LaunchDirect(spec);
            if (LaunchAsUser(spec)) return true;
            Log.Warn($"No se pudo lanzar sin elevación, se lanza elevado: {spec.FileName}");
            return LaunchDirect(spec);
        }
        catch (Exception ex)
        {
            Log.Error($"Error lanzando {spec.FileName}", ex);
            return false;
        }
    }

    /// <summary>
    /// Like <see cref="Launch"/>, and waits until the program ends. Without elevation the helper starts it and waits for
    /// it, and this waits for the helper (an elevated process cannot get the handle of what the helper starts).
    /// </summary>
    public static async Task LaunchAndWaitAsync(LaunchSpec spec, bool asAdmin, CancellationToken token)
    {
        Process? process = null;
        try
        {
            if (!asAdmin && IsElevated)
            {
                var exe = AppPaths.ExecutablePath;
                process = StartAsUser(exe, $"{HelperArgument} {Encode(spec with { Wait = true })}");
                if (process is null) Log.Warn($"No se pudo lanzar sin elevación, se lanza elevado: {spec.FileName}");
            }
            if (process is null && !Start(spec, out process)) return;
            if (process is not null) await process.WaitForExitAsync(token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error($"Error lanzando {spec.FileName}", ex);
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>Starts the process from the current process (same elevation); with <see cref="LaunchSpec.Wait"/>, until it ends.</summary>
    public static bool LaunchDirect(LaunchSpec spec)
    {
        if (!Start(spec, out var process)) return false;
        using (process)
        {
            if (spec.Wait) process?.WaitForExit();
        }
        return true;
    }

    /// <summary>Starts it and returns the process when there is one to watch (not for folders or documents Windows hands over).</summary>
    private static bool Start(LaunchSpec spec, out Process? started)
    {
        started = null;
        var file = Environment.ExpandEnvironmentVariables(spec.FileName.Trim().Trim('"'));
        var args = Environment.ExpandEnvironmentVariables(spec.Arguments ?? string.Empty);
        if (file.Length == 0) return false;

        var psi = new ProcessStartInfo { UseShellExecute = true, FileName = file, Arguments = args };

        // K-Desktop 4 compatibility: "cmd" + parameters runs them with /K (visible) or /C (hidden).
        if (file.Equals("cmd", StringComparison.OrdinalIgnoreCase) && args.Length > 0 &&
            !args.TrimStart().StartsWith('/'))
            psi.Arguments = (spec.Window == WindowMode.Hidden ? "/C " : "/K ") + args;

        var isDirectory = Directory.Exists(file);
        if (isDirectory)
        {
            psi.FileName = "explorer.exe";
            psi.Arguments = $"\"{file}\"";
        }
        else
        {
            var wd = Environment.ExpandEnvironmentVariables(spec.WorkingDirectory ?? string.Empty);
            if (Directory.Exists(wd)) psi.WorkingDirectory = wd;
            else if (Path.IsPathRooted(file) && Path.GetDirectoryName(file) is { } dir && Directory.Exists(dir)) psi.WorkingDirectory = dir;
        }

        psi.WindowStyle = spec.Window switch
        {
            WindowMode.Minimized => ProcessWindowStyle.Minimized,
            WindowMode.Maximized => ProcessWindowStyle.Maximized,
            WindowMode.Hidden => ProcessWindowStyle.Hidden,
            _ => ProcessWindowStyle.Normal,
        };
        if (spec.Window == WindowMode.Hidden) psi.CreateNoWindow = true;

        // Before starting it: the windows the program already has, so its new one can be told apart.
        var exeName = ExeName(file);
        var topMost = spec.TopMost && spec.Window != WindowMode.Hidden && !isDirectory;
        var before = topMost ? WindowsOf(null, exeName) : null;

        Win32.AllowSetForegroundWindow(Win32.ASFW_ANY);
        var process = Process.Start(psi);
        if (process is null || isDirectory)
        {
            process?.Dispose();
            if (before is not null && exeName is not null) MakeTopMost(null, exeName, before);
            return true;
        }
        started = process;

        if (spec.Priority != ProcessPriorityLevel.Normal)
        {
            try
            {
                process.PriorityClass = spec.Priority switch
                {
                    ProcessPriorityLevel.Idle => ProcessPriorityClass.Idle,
                    ProcessPriorityLevel.BelowNormal => ProcessPriorityClass.BelowNormal,
                    ProcessPriorityLevel.AboveNormal => ProcessPriorityClass.AboveNormal,
                    ProcessPriorityLevel.High => ProcessPriorityClass.High,
                    ProcessPriorityLevel.RealTime => ProcessPriorityClass.RealTime,
                    _ => ProcessPriorityClass.Normal,
                };
            }
            catch (Exception ex) { Log.Warn($"No se pudo cambiar la prioridad de {file}: {ex.Message}"); }
        }

        if (before is not null) MakeTopMost(process, exeName, before);
        return true;
    }

    /// <summary>«notepad» for notepad.exe or «notepad»; null for documents, shortcuts and the like.</summary>
    private static string? ExeName(string file)
    {
        var ext = Path.GetExtension(file);
        return ext.Length == 0 || ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(file) : null;
    }

    /// <summary>The windows of a process and of every process with that program's name.</summary>
    private static HashSet<IntPtr> WindowsOf(Process? process, string? exeName)
    {
        var pids = new HashSet<uint>();
        try
        {
            if (process is { HasExited: false }) pids.Add((uint)process.Id);
        }
        catch { /* protected */ }
        if (exeName is not null)
            foreach (var p in WindowTools.FindProcesses(exeName))
            {
                pids.Add((uint)p.Id);
                p.Dispose();
            }
        return pids.Count == 0 ? [] : [.. WindowTools.TopLevelWindows(pids)];
    }

    /// <summary>
    /// «Siempre visible» for the window it opens, within 5 s. Not always the started process's own: Windows 11's Notepad
    /// and other Store apps start through an alias that hands over to another process, so a new window of a process with
    /// the program's name counts too. When none appears (it opened a tab in a window it had), the program's window in
    /// front gets it.
    /// </summary>
    private static void MakeTopMost(Process? process, string? exeName, HashSet<IntPtr> before)
    {
        for (var i = 0; i < 100; i++)
        {
            var window = WindowsOf(process, exeName).FirstOrDefault(h => !before.Contains(h));
            if (window != IntPtr.Zero)
            {
                WindowTools.SetTopMost(window, true);
                return;
            }
            Thread.Sleep(50);
        }
        var front = Win32.GetForegroundWindow();
        if (front != IntPtr.Zero && WindowsOf(process, exeName).Contains(front)) WindowTools.SetTopMost(front, true);
        else Log.Info($"Siempre visible: no apareció ninguna ventana de {exeName ?? "lo lanzado"}");
    }

    // ── non-elevated launch through the helper ───────────────────────────

    private static bool LaunchAsUser(LaunchSpec spec)
    {
        var exe = AppPaths.ExecutablePath;
        return CreateProcessAsShellUser(exe, $"\"{exe}\" {HelperArgument} {Encode(spec)}") is not null;
    }

    private static string Encode(LaunchSpec spec) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(Json.Serialize(spec))).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// Starts <paramref name="application"/> itself (no helper) as the normal user, from an elevated process, and
    /// returns it to be watched (the assistant process). Null when it could not be started that way.
    /// </summary>
    public static Process? StartAsUser(string application, string arguments)
    {
        try
        {
            var pid = CreateProcessAsShellUser(application, $"\"{application}\" {arguments}", keepHandle: true, out var handle);
            if (pid is null) return null;
            try { return Process.GetProcessById((int)pid.Value); }
            finally { Win32.CloseHandle(handle); } // held until Process has its own, so the id cannot be reused meanwhile
        }
        catch (Exception ex)
        {
            Log.Error($"Error lanzando {application} sin elevación", ex);
            return null;
        }
    }

    /// <summary>Entry point of the helper process (called from Program.Main). Returns the exit code.</summary>
    public static int RunHelper(string payload)
    {
        try
        {
            var b64 = payload.Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            var spec = Json.Deserialize<LaunchSpec>(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
            return spec is not null && LaunchDirect(spec) ? 0 : 1;
        }
        catch (Exception ex)
        {
            Log.Error("Error en el proceso auxiliar de lanzamiento", ex);
            return 2;
        }
    }

    private static uint? CreateProcessAsShellUser(string application, string commandLine) =>
        CreateProcessAsShellUser(application, commandLine, keepHandle: false, out _);

    /// <summary>
    /// Starts a process with the token of the desktop shell (Explorer), i.e. not elevated. Returns its id, or null.
    /// With <paramref name="keepHandle"/> the process handle is left open in <paramref name="process"/> (close it).
    /// </summary>
    private static uint? CreateProcessAsShellUser(string application, string commandLine, bool keepHandle, out IntPtr process)
    {
        process = IntPtr.Zero;
        Win32.EnablePrivilege("SeIncreaseQuotaPrivilege");

        var shell = Win32.GetShellWindow();
        if (shell == IntPtr.Zero) return null;
        Win32.GetWindowThreadProcessId(shell, out var shellPid);
        if (shellPid == 0) return null;

        IntPtr hProcess = IntPtr.Zero, hShellToken = IntPtr.Zero, hToken = IntPtr.Zero, env = IntPtr.Zero;
        try
        {
            hProcess = Win32.OpenProcess(Win32.PROCESS_QUERY_INFORMATION, false, shellPid);
            if (hProcess == IntPtr.Zero) return Fail("OpenProcess");
            if (!Win32.OpenProcessToken(hProcess, Win32.TOKEN_DUPLICATE, out hShellToken)) return Fail("OpenProcessToken");

            const uint rights = Win32.TOKEN_QUERY | Win32.TOKEN_ASSIGN_PRIMARY | Win32.TOKEN_DUPLICATE |
                                Win32.TOKEN_ADJUST_DEFAULT | Win32.TOKEN_ADJUST_SESSIONID;
            if (!Win32.DuplicateTokenEx(hShellToken, rights, IntPtr.Zero, Win32.SecurityImpersonation, Win32.TokenPrimary, out hToken))
                return Fail("DuplicateTokenEx");

            Win32.CreateEnvironmentBlock(out env, hToken, false);

            var si = new Win32.STARTUPINFO { cb = Marshal.SizeOf<Win32.STARTUPINFO>() };
            // Writable and null-terminated: CreateProcessWithTokenW may change the command line in place.
            var cmd = new char[Math.Max(commandLine.Length + 1, 1024)];
            commandLine.CopyTo(cmd);
            if (!Win32.CreateProcessWithTokenW(hToken, 0, application, cmd, Win32.CREATE_UNICODE_ENVIRONMENT, env,
                    Path.GetDirectoryName(application), ref si, out var pi))
                return Fail("CreateProcessWithTokenW");

            Win32.AllowSetForegroundWindow((int)pi.dwProcessId);
            Win32.CloseHandle(pi.hThread);
            if (keepHandle) process = pi.hProcess;
            else Win32.CloseHandle(pi.hProcess);
            return pi.dwProcessId;
        }
        finally
        {
            if (env != IntPtr.Zero) Win32.DestroyEnvironmentBlock(env);
            if (hToken != IntPtr.Zero) Win32.CloseHandle(hToken);
            if (hShellToken != IntPtr.Zero) Win32.CloseHandle(hShellToken);
            if (hProcess != IntPtr.Zero) Win32.CloseHandle(hProcess);
        }

        static uint? Fail(string what)
        {
            Log.Warn($"{what} falló: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            return null;
        }
    }
}

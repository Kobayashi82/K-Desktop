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
    bool TopMost = false);

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

    /// <summary>Starts the process from the current process (same elevation).</summary>
    public static bool LaunchDirect(LaunchSpec spec)
    {
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

        Win32.AllowSetForegroundWindow(Win32.ASFW_ANY);
        using var process = Process.Start(psi);
        if (process is null || isDirectory) return true;

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

        if (spec.TopMost && spec.Window != WindowMode.Hidden)
        {
            try
            {
                for (var i = 0; i < 100 && process.MainWindowHandle == IntPtr.Zero && !process.HasExited; i++)
                {
                    Thread.Sleep(50);
                    process.Refresh();
                }
                if (process.MainWindowHandle != IntPtr.Zero) WindowTools.SetTopMost(process.MainWindowHandle, true);
            }
            catch { /* the process may have exited or be protected */ }
        }
        return true;
    }

    // ── non-elevated launch through the helper ───────────────────────────

    private static bool LaunchAsUser(LaunchSpec spec)
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(Json.Serialize(spec)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var exe = AppPaths.ExecutablePath;
        return CreateProcessAsShellUser(exe, $"\"{exe}\" {HelperArgument} {payload}");
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

    /// <summary>Starts a process with the token of the desktop shell (Explorer), i.e. not elevated.</summary>
    private static bool CreateProcessAsShellUser(string application, string commandLine)
    {
        Win32.EnablePrivilege("SeIncreaseQuotaPrivilege");

        var shell = Win32.GetShellWindow();
        if (shell == IntPtr.Zero) return false;
        Win32.GetWindowThreadProcessId(shell, out var shellPid);
        if (shellPid == 0) return false;

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
            var cmd = new StringBuilder(commandLine, Math.Max(commandLine.Length + 1, 1024));
            if (!Win32.CreateProcessWithTokenW(hToken, 0, application, cmd, Win32.CREATE_UNICODE_ENVIRONMENT, env,
                    Path.GetDirectoryName(application), ref si, out var pi))
                return Fail("CreateProcessWithTokenW");

            Win32.AllowSetForegroundWindow((int)pi.dwProcessId);
            Win32.CloseHandle(pi.hThread);
            Win32.CloseHandle(pi.hProcess);
            return true;
        }
        finally
        {
            if (env != IntPtr.Zero) Win32.DestroyEnvironmentBlock(env);
            if (hToken != IntPtr.Zero) Win32.CloseHandle(hToken);
            if (hShellToken != IntPtr.Zero) Win32.CloseHandle(hShellToken);
            if (hProcess != IntPtr.Zero) Win32.CloseHandle(hProcess);
        }

        static bool Fail(string what)
        {
            Log.Warn($"{what} falló: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            return false;
        }
    }
}

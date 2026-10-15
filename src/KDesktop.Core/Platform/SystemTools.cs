using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using KDesktop.Core.Input;
using KDesktop.Core.Model;
using KDesktop.Core.Native;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace KDesktop.Core.Platform;

/// <summary>Master volume of the default playback device.</summary>
public static class AudioTools
{
    private static MMDevice DefaultDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    public static int GetVolume()
    {
        using var device = DefaultDevice();
        return (int)Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
    }

    public static int SetVolume(int percent)
    {
        using var device = DefaultDevice();
        var v = Math.Clamp(percent, 0, 100);
        device.AudioEndpointVolume.MasterVolumeLevelScalar = v / 100f;
        if (v > 0 && device.AudioEndpointVolume.Mute) device.AudioEndpointVolume.Mute = false;
        return v;
    }

    public static int StepVolume(int delta) => SetVolume(GetVolume() + delta);

    /// <summary>Toggles mute and returns the new mute state.</summary>
    public static bool ToggleMute()
    {
        using var device = DefaultDevice();
        var muted = !device.AudioEndpointVolume.Mute;
        device.AudioEndpointVolume.Mute = muted;
        return muted;
    }
}

/// <summary>
/// Plays a sound by name or any .wav file. Names come from the sounds built into the assembly plus the .wav files in
/// %APPDATA%\K-Desktop 5\Sounds; a user file with the same name as a built-in sound replaces it.
/// </summary>
public static class Sounds
{
    private const string EmbeddedFolder = "Sounds";

    /// <summary>Built-in sounds loaded so far. The arrays are pinned and never freed: PlaySound reads them while it plays.</summary>
    private static readonly ConcurrentDictionary<string, byte[]> Loaded = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Available()
    {
        var names = new HashSet<string>(EmbeddedAssets.List(EmbeddedFolder).Select(f => Path.GetFileNameWithoutExtension(f)), StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(AppPaths.SoundsDir))
                names.UnionWith(Directory.GetFiles(AppPaths.SoundsDir, "*.wav").Select(f => Path.GetFileNameWithoutExtension(f)));
        }
        catch { }
        return names.Order(StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static void Play(string? sound) => Play(sound, Win32.SND_ASYNC);

    /// <summary>Plays the sound and completes when it ends.</summary>
    public static Task PlayAndWaitAsync(string? sound, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(sound)) return Task.CompletedTask;
        using var reg = token.Register(() => Win32.PlaySound(null, IntPtr.Zero, Win32.SND_PURGE));
        return Task.Run(() => Play(sound, Win32.SND_SYNC), token);
    }

    private static void Play(string? sound, uint mode)
    {
        if (string.IsNullOrWhiteSpace(sound)) return;
        var flags = mode | Win32.SND_NODEFAULT;
        if (FindFile(sound) is { } file)
            Win32.PlaySound(file, IntPtr.Zero, flags | Win32.SND_FILENAME);
        else if (BuiltIn(sound) is { } wav)
            Win32.PlaySound(Marshal.UnsafeAddrOfPinnedArrayElement(wav, 0), IntPtr.Zero, flags | Win32.SND_MEMORY);
    }

    /// <summary>A .wav path, or a name in the user's Sounds folder.</summary>
    private static string? FindFile(string sound)
    {
        if (File.Exists(sound)) return sound;
        var user = Path.Combine(AppPaths.SoundsDir, sound + ".wav");
        return File.Exists(user) ? user : null;
    }

    private static byte[]? BuiltIn(string name)
    {
        if (Loaded.TryGetValue(name, out var wav)) return wav;
        return EmbeddedAssets.ReadBytes(EmbeddedFolder, name + ".wav", pinned: true) is { } bytes ? Loaded.GetOrAdd(name, bytes) : null;
    }
}

public static class PowerTools
{
    private const uint ReasonPlanned = 0x80000000;

    public static void Execute(PowerMode mode)
    {
        switch (mode)
        {
            case PowerMode.Lock:
                Win32.LockWorkStation();
                return;
            case PowerMode.SwitchUser:
                // Same as "Switch user" in the Start menu: the session stays open and the sign-in screen appears.
                if (!Win32.WTSDisconnectSession(IntPtr.Zero, Win32.WTS_CURRENT_SESSION, false))
                {
                    Log.Warn($"WTSDisconnectSession falló (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}); se bloquea el equipo");
                    Win32.LockWorkStation();
                }
                return;
            case PowerMode.Sleep:
            case PowerMode.Hibernate:
                Win32.EnablePrivilege("SeShutdownPrivilege");
                Win32.SetSuspendState(mode == PowerMode.Hibernate, false, false);
                return;
        }
        Win32.EnablePrivilege("SeShutdownPrivilege");
        var flags = mode switch
        {
            PowerMode.Restart => Win32.EWX_REBOOT,
            PowerMode.LogOff => Win32.EWX_LOGOFF,
            _ => Win32.EWX_POWEROFF,
        };
        if (!Win32.ExitWindowsEx(flags | Win32.EWX_FORCEIFHUNG, ReasonPlanned))
            Log.Warn($"ExitWindowsEx falló para {mode}");
    }
}

public static class ShellTools
{
    /// <summary>Opens a special location; returns false for unknown keys.</summary>
    public static bool OpenSpecial(string key)
    {
        LaunchSpec Explorer(string target) => new("explorer.exe", target);
        bool User(LaunchSpec spec) => ProcessLauncher.Launch(spec, asAdmin: false);
        bool Admin(LaunchSpec spec) => ProcessLauncher.Launch(spec, asAdmin: true);

        switch (key)
        {
            case "ThisPC": return User(Explorer("shell:MyComputerFolder"));
            case "Desktop": return User(Explorer("shell:Desktop"));
            case "Documents": return User(Explorer("shell:Personal"));
            case "Downloads": return User(Explorer("shell:Downloads"));
            case "Pictures": return User(Explorer("shell:My Pictures"));
            case "Music": return User(Explorer("shell:My Music"));
            case "Videos": return User(Explorer("shell:My Video"));
            case "UserProfile": return User(Explorer("shell:UsersFilesFolder"));
            case "RecycleBin": return User(Explorer("shell:RecycleBinFolder"));
            case "EmptyRecycleBin":
                Win32.SHEmptyRecycleBin(IntPtr.Zero, null, Win32.SHERB_NOCONFIRMATION);
                return true;
            case "ControlPanel": return User(new LaunchSpec("control.exe"));
            case "Settings": return User(new LaunchSpec("ms-settings:"));
            case "VolumeMixer": return User(new LaunchSpec("sndvol.exe"));
            case "SoundSettings": return User(new LaunchSpec("control.exe", "mmsys.cpl"));
            case "NetworkConnections": return User(new LaunchSpec("control.exe", "ncpa.cpl"));
            case "Fonts": return User(Explorer("shell:Fonts"));
            case "StartMenu": return User(Explorer("shell:Start Menu"));
            case "CommonStartMenu": return User(Explorer("shell:Common Start Menu"));
            case "WindowsApps": return User(Explorer("shell:AppsFolder"));
            case "GodMode": return User(Explorer("shell:::{ED7BA470-8E54-465E-825C-99712043E01C}"));
            case "TaskScheduler": return Admin(new LaunchSpec("taskschd.msc"));
            case "GroupPolicy": return Admin(new LaunchSpec("gpedit.msc"));
            case "Registry": return Admin(new LaunchSpec("regedit.exe"));
            case "OnScreenKeyboard": return User(new LaunchSpec("osk.exe"));
            case "PowerShell": return User(new LaunchSpec("powershell.exe"));
            case "TaskView": return InputSimulator.SendCombo("WIN + Tab");
            case "KDesktopData": return User(Explorer($"\"{AppPaths.DataDir}\""));
            default: return false;
        }
    }

    public static bool HiddenFilesVisible()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        return key?.GetValue("Hidden") is int v && v == 1;
    }

    /// <summary>Toggles "show hidden files" and refreshes Explorer. Returns true when hidden files are now visible.</summary>
    public static bool ToggleHiddenFiles()
    {
        var show = !HiddenFilesVisible();
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
            key.SetValue("Hidden", show ? 1 : 2, RegistryValueKind.DWord);
        RefreshExplorer();
        return show;
    }

    public static void RefreshExplorer()
    {
        Win32.SHChangeNotify(Win32.SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
        // Desktop: F5 to the SHELLDLL_DefView window.
        var progman = Win32.FindWindow("Progman", "Program Manager");
        var defView = Win32.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView != IntPtr.Zero) Win32.PostMessage(defView, 0x0100, new IntPtr(0x74), new IntPtr(3));
        // Open Explorer windows.
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return;
            dynamic shell = Activator.CreateInstance(type)!;
            foreach (var window in shell.Windows())
            {
                try { window.Refresh(); } catch { /* not an Explorer window */ }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudieron refrescar las ventanas del Explorador: " + ex.Message);
        }
    }
}

/// <summary>"Start with Windows" through a scheduled task (starts elevated without a UAC prompt).</summary>
public static class StartupTask
{
    public const string TaskName = "K-Desktop 5";

    public static bool IsRegistered() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    public static bool Register()
    {
        var exe = AppPaths.ExecutablePath;
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Inicia K-Desktop al iniciar sesión.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{System.Security.SecurityElement.Escape(user)}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{System.Security.SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>4</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>"{System.Security.SecurityElement.Escape(exe)}"</Command><Arguments>--startup</Arguments></Exec></Actions>
            </Task>
            """;
        var file = Path.Combine(AppPaths.TempDir, "startup-task.xml");
        Directory.CreateDirectory(AppPaths.TempDir);
        File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
        try
        {
            var ok = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F") == 0;
            if (!ok) Log.Warn("No se pudo registrar la tarea de inicio");
            return ok;
        }
        finally
        {
            try { File.Delete(file); } catch { /* ignore */ }
        }
    }

    public static bool Unregister() => !IsRegistered() || RunSchtasks($"/Delete /TN \"{TaskName}\" /F") == 0;

    public static void Apply(bool enabled)
    {
        try
        {
            if (enabled) Register(); else Unregister();
        }
        catch (Exception ex)
        {
            Log.Error("Error configurando el inicio con Windows", ex);
        }
    }

    internal static int RunSchtasks(string args) => Run("schtasks.exe", args);

    internal static int Run(string file, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        if (p is null) return -1;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(10_000);
        return p.HasExited ? p.ExitCode : -1;
    }
}

/// <summary>Inbound firewall rule so the UDP server is reachable from the LAN.</summary>
public static class FirewallRule
{
    private const string RuleName = "K-Desktop 5";

    public static void Ensure()
    {
        try
        {
            if (StartupTask.Run("netsh", $"advfirewall firewall show rule name=\"{RuleName}\"") == 0) return;
            var exe = AppPaths.ExecutablePath;
            var code = StartupTask.Run("netsh",
                $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{exe}\" protocol=UDP enable=yes profile=any");
            if (code != 0) Log.Warn("No se pudo crear la regla del firewall");
        }
        catch (Exception ex)
        {
            Log.Warn("Error creando la regla del firewall: " + ex.Message);
        }
    }
}

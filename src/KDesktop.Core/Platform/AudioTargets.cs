using System.Runtime.InteropServices;
using KDesktop.Core.Input;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace KDesktop.Core.Platform;

/// <summary>The result of changing a volume: the new level (0–100) and whether it is muted.</summary>
public readonly record struct LevelState(int Level, bool Muted);

/// <summary>One speaker or microphone by its Windows id (the «Volumen» action with a device chosen).</summary>
public static class DeviceVolume
{
    /// <summary>Applies <paramref name="change"/> to that device; null when it is not connected or does not exist.</summary>
    public static LevelState? Change(string id, Func<LevelState, LevelState> change)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(id);
            if (device.State != DeviceState.Active) return null;
            var volume = device.AudioEndpointVolume;
            var now = new LevelState((int)Math.Round(volume.MasterVolumeLevelScalar * 100), volume.Mute);
            var next = change(now);
            next = next with { Level = Math.Clamp(next.Level, 0, 100) };
            if (next.Level != now.Level) volume.MasterVolumeLevelScalar = next.Level / 100f;
            volume.Mute = next.Muted;
            return next;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return null; // gone
        }
    }
}

/// <summary>
/// The default microphone: its level and its mute, like the recording tab of the sound settings. The default device
/// for communications (calls) is changed too when it is a different one.
/// </summary>
public static class MicrophoneTools
{
    /// <summary>Applies <paramref name="change"/> to the default microphones; null when there is none.</summary>
    public static LevelState? Change(Func<LevelState, LevelState> change)
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = new List<MMDevice>();
        try
        {
            foreach (var role in new[] { Role.Multimedia, Role.Communications })
            {
                if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, role)) continue;
                var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, role);
                if (devices.Any(d => d.ID == device.ID)) device.Dispose();
                else devices.Add(device);
            }
            if (devices.Count == 0) return null;
            var first = devices[0].AudioEndpointVolume;
            var now = new LevelState((int)Math.Round(first.MasterVolumeLevelScalar * 100), first.Mute);
            var next = change(now);
            next = next with { Level = Math.Clamp(next.Level, 0, 100) };
            foreach (var device in devices)
            {
                var volume = device.AudioEndpointVolume;
                if (next.Level != now.Level) volume.MasterVolumeLevelScalar = next.Level / 100f;
                volume.Mute = next.Muted;
            }
            return next;
        }
        finally
        {
            foreach (var d in devices) d.Dispose();
        }
    }
}

/// <summary>
/// The volume of one program, as the volume mixer shows it: every audio session of its processes, on every output.
/// </summary>
public static class AppVolume
{
    /// <summary>
    /// Applies <paramref name="change"/> to the sessions of the processes named <paramref name="processName"/>; null when
    /// the program has no sound open.
    /// </summary>
    public static LevelState? Change(string processName, Func<LevelState, LevelState> change)
    {
        var name = ProcessNames.Normalize(processName);
        if (name.Length == 0) return null;
        var processes = WindowTools.FindProcesses(name);
        var pids = processes.Select(p => (uint)p.Id).ToHashSet();
        foreach (var p in processes) p.Dispose();
        if (pids.Count == 0) return null;

        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
        try
        {
            var sessions = new List<AudioSessionControl>();
            foreach (var device in devices)
            {
                try
                {
                    var manager = device.AudioSessionManager;
                    manager.RefreshSessions();
                    var list = manager.Sessions;
                    for (var i = 0; i < list.Count; i++)
                    {
                        var session = list[i];
                        if (session.State != AudioSessionState.AudioSessionStateExpired && pids.Contains(session.GetProcessID)) sessions.Add(session);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"No se pudieron leer las sesiones de sonido de {device.FriendlyName}: {ex.Message}");
                }
            }
            if (sessions.Count == 0) return null;
            // The one sounding (or the loudest) tells the current level.
            var reference = sessions.OrderByDescending(s => s.State == AudioSessionState.AudioSessionStateActive).ThenByDescending(s => s.SimpleAudioVolume.Volume).First();
            var now = new LevelState((int)Math.Round(reference.SimpleAudioVolume.Volume * 100), reference.SimpleAudioVolume.Mute);
            var next = change(now);
            next = next with { Level = Math.Clamp(next.Level, 0, 100) };
            foreach (var session in sessions)
            {
                var volume = session.SimpleAudioVolume;
                if (next.Level != now.Level) volume.Volume = next.Level / 100f;
                volume.Mute = next.Muted;
            }
            return next;
        }
        finally
        {
            foreach (var d in devices) d.Dispose();
        }
    }

    /// <summary>
    /// The programs with sound open on the default output, as the volume mixer shows them: one per program (its
    /// processes together), the ones sounding first. The sounds of Windows itself are left out.
    /// </summary>
    public static IReadOnlyList<AppLevel> List()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var manager = device.AudioSessionManager;
        manager.RefreshSessions();
        var list = manager.Sessions;
        var found = new Dictionary<string, AppLevel>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < list.Count; i++)
        {
            var session = list[i];
            try
            {
                if (session.State == AudioSessionState.AudioSessionStateExpired || session.IsSystemSoundsSession) continue;
                var pid = (int)session.GetProcessID;
                if (pid == 0) continue;
                var path = WindowTools.ProcessPath(pid);
                var process = ProcessNames.Normalize(path is null ? string.Empty : Path.GetFileName(path));
                if (process.Length == 0) continue;
                var level = new AppLevel(process, NameOf(session, (uint)pid, process), path,
                    (int)Math.Round(session.SimpleAudioVolume.Volume * 100), session.SimpleAudioVolume.Mute,
                    session.State == AudioSessionState.AudioSessionStateActive);
                // Several processes of one program (a browser): the one sounding tells its level.
                if (!found.TryGetValue(process, out var known) || (level.Active && !known.Active)) found[process] = level;
            }
            catch (Exception ex)
            {
                Log.Warn("No se pudo leer una sesión de sonido: " + ex.Message);
            }
        }
        return [.. found.Values.OrderByDescending(a => a.Active).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static string NameOf(AudioSessionControl session, uint pid, string process)
    {
        var display = session.DisplayName;
        if (!string.IsNullOrWhiteSpace(display) && !display.StartsWith('@')) return display.Trim();
        return AppFinder.DescriptionOf(pid) is { Length: > 0 } description ? description : process;
    }
}

/// <summary>The volume of one program in the mixer. <c>Process</c>: «spotify», what <see cref="AppVolume.Change"/> takes.</summary>
public sealed record AppLevel(string Process, string Name, string? Path, int Level, bool Muted, bool Active);

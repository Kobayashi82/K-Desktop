using KDesktop.Core.Input;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace KDesktop.Core.Platform;

/// <summary>The result of changing a volume: the new level (0–100) and whether it is muted.</summary>
public readonly record struct LevelState(int Level, bool Muted);

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
}

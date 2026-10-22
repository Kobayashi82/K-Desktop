using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace KDesktop.Core.Platform;

/// <summary>
/// Turns down what the other programs are playing while a voice order is being said (each program's own volume, as
/// the volume mixer does), and puts it back afterwards; like a smart speaker does when it hears its name. A program
/// whose volume the user changed meanwhile is left as the user set it. Every output device (music may be on the
/// headphones and a film on the TV); never K-Desktop itself nor the assistants' process, whose voice answers meanwhile.
/// </summary>
public sealed class AudioDucker : IDisposable
{
    private sealed record Ducked(AudioSessionControl Session, float Original, float Lowered);

    private readonly List<Ducked> _ducked = [];
    private readonly MMDeviceEnumerator? _enumerator;
    private readonly List<MMDevice> _devices = [];
    private int _restored;

    private AudioDucker(float level)
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                _devices.Add(device);
                try
                {
                    var manager = device.AudioSessionManager;
                    manager.RefreshSessions();
                    var sessions = manager.Sessions;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        var session = sessions[i];
                        if (session.State != AudioSessionState.AudioSessionStateActive || session.IsSystemSoundsSession
                            || ProgramOf(session.GetProcessID) is not { } name || IsOurs(name))
                        {
                            session.Dispose();
                            continue;
                        }
                        var volume = session.SimpleAudioVolume;
                        var original = volume.Volume;
                        var lowered = original * level;
                        volume.Volume = lowered;
                        _ducked.Add(new Ducked(session, original, lowered));
                        names.Add(name);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"No se pudo bajar el volumen en «{device.FriendlyName}»: {ex.Message}");
                }
            }
            if (_ducked.Count > 0) Log.Info($"Volumen bajado al {level:P0} mientras se habla: {string.Join(", ", names)}");
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo bajar el volumen de los programas: " + ex.Message);
        }
    }

    /// <summary>Lowers every program that is sounding to <paramref name="level"/> (0–1) of its volume until disposed.</summary>
    public static AudioDucker Duck(double level) => new((float)Math.Clamp(level, 0, 1));

    /// <summary>Puts the volumes back (gently, in a moment).</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _restored, 1) != 0) return;
        if (_ducked.Count == 0)
        {
            Release();
            return;
        }
        // A short ramp up: less abrupt than jumping straight back.
        Task.Run(async () =>
        {
            try
            {
                const int steps = 6;
                for (var step = 1; step <= steps; step++)
                {
                    foreach (var d in _ducked)
                    {
                        try
                        {
                            var volume = d.Session.SimpleAudioVolume;
                            var expected = d.Lowered + (d.Original - d.Lowered) * (step - 1) / steps;
                            if (Math.Abs(volume.Volume - expected) > 0.02f) continue; // the user moved it: theirs now
                            volume.Volume = d.Lowered + (d.Original - d.Lowered) * step / steps;
                        }
                        catch
                        {
                            // the program closed meanwhile
                        }
                    }
                    await Task.Delay(40).ConfigureAwait(false);
                }
            }
            finally
            {
                Release();
            }
        });
    }

    private void Release()
    {
        foreach (var d in _ducked) d.Session.Dispose();
        foreach (var device in _devices) device.Dispose();
        _enumerator?.Dispose();
    }

    private static readonly string OwnName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "KDesktop");

    /// <summary>K-Desktop, the assistants' process (the same exe) or a test instance.</summary>
    private static bool IsOurs(string name) =>
        name.Equals(OwnName, StringComparison.OrdinalIgnoreCase) || name.Equals("KDesktop", StringComparison.OrdinalIgnoreCase);

    /// <summary>The program behind a session, or null when it is gone.</summary>
    private static string? ProgramOf(uint pid)
    {
        if (pid == 0) return null;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }
}

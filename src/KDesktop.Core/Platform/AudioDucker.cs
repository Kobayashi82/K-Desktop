using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace KDesktop.Core.Platform;

/// <summary>
/// Turns down what the other programs are playing while a voice order is being said (each program's own volume, as
/// the volume mixer does), and puts it back afterwards; like a smart speaker does when it hears its name. A program
/// whose volume the user changed meanwhile is left as the user set it.
/// </summary>
public sealed class AudioDucker : IDisposable
{
    private sealed record Ducked(AudioSessionControl Session, float Original, float Lowered);

    private readonly List<Ducked> _ducked = [];
    private readonly MMDeviceEnumerator? _enumerator;
    private readonly MMDevice? _device;
    private int _restored;

    private AudioDucker(float level)
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var manager = _device.AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;
            var own = (uint)Environment.ProcessId;
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.State != AudioSessionState.AudioSessionStateActive || session.GetProcessID == own || session.IsSystemSoundsSession) continue;
                var volume = session.SimpleAudioVolume;
                var original = volume.Volume;
                var lowered = original * level;
                volume.Volume = lowered;
                _ducked.Add(new Ducked(session, original, lowered));
            }
            if (_ducked.Count > 0) Log.Info($"Volumen de {_ducked.Count} programa(s) bajado al {level:P0} mientras se habla");
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
        _device?.Dispose();
        _enumerator?.Dispose();
    }
}

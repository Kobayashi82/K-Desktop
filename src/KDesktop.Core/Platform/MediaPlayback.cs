using System.Diagnostics;
using KDesktop.Core.Input;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace KDesktop.Core.Platform;

/// <summary>
/// Playback of whatever player is in use, through the media keys (every player and browser answers them). Play and
/// pause are the same key, so <see cref="Pause"/> and <see cref="Resume"/> first look whether something is sounding:
/// «pause» never starts the music and «resume» never stops it.
/// </summary>
public static class MediaPlayback
{
    private const int PlayPauseKey = 0xB3;
    private const int NextKey = 0xB0;
    private const int PreviousKey = 0xB1;

    /// <summary>Peak level above which a session counts as sounding (silence is exactly 0, noise floors are tiny).</summary>
    private const float Audible = 0.0005f;

    public static void PlayPause() => InputSimulator.KeyPress(PlayPauseKey);

    public static void Next() => InputSimulator.KeyPress(NextKey);

    public static void Previous() => InputSimulator.KeyPress(PreviousKey);

    /// <summary>Pauses what is sounding; false when nothing was (then nothing is sent).</summary>
    public static bool Pause()
    {
        if (IsPlaying() == false) return false;
        PlayPause();
        return true;
    }

    /// <summary>Resumes playback; false when something is sounding already (then nothing is sent).</summary>
    public static bool Resume()
    {
        if (IsPlaying() == true) return false;
        PlayPause();
        return true;
    }

    /// <summary>
    /// Whether another program is playing sound on the default output, watched for a moment (a quiet passage of a song
    /// still moves the meter within it). Null when Windows could not tell.
    /// </summary>
    public static bool? IsPlaying(int milliseconds = 240)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var manager = device.AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;
            var own = (uint)Environment.ProcessId;
            var active = new List<AudioSessionControl>();
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.State == AudioSessionState.AudioSessionStateActive && session.GetProcessID != own) active.Add(session);
            }
            if (active.Count == 0) return false;
            var watch = Stopwatch.StartNew();
            while (true)
            {
                if (active.Any(s => s.AudioMeterInformation.MasterPeakValue > Audible)) return true;
                if (watch.ElapsedMilliseconds >= milliseconds) return false;
                Thread.Sleep(30);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo saber si suena algo: " + ex.Message);
            return null;
        }
    }
}

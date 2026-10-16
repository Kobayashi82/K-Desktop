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

    private const int WM_APPCOMMAND = 0x0319;
    private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;

    /// <summary>
    /// Play / pause sent to one program only (WM_APPCOMMAND to its window), so another player that is sounding is not
    /// touched. False when it has no window to send it to.
    /// </summary>
    public static bool PlayPauseIn(string processName)
    {
        var processes = WindowTools.FindProcesses(processName);
        try
        {
            if (processes.Length == 0) return false;
            var window = WindowTools.TopLevelWindows(processes.Select(p => (uint)p.Id).ToHashSet()).FirstOrDefault();
            if (window == IntPtr.Zero) window = processes.Select(p => p.MainWindowHandle).FirstOrDefault(h => h != IntPtr.Zero);
            if (window == IntPtr.Zero) return false;
            Native.Win32.PostMessage(window, WM_APPCOMMAND, window, (IntPtr)(APPCOMMAND_MEDIA_PLAY_PAUSE << 16));
            return true;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    /// <summary>Whether a program named <paramref name="processName"/> is sounding (see <see cref="IsPlaying(int)"/>).</summary>
    public static bool? IsPlaying(string processName, int milliseconds = 240)
    {
        var processes = WindowTools.FindProcesses(processName);
        var pids = processes.Select(p => (uint)p.Id).ToHashSet();
        foreach (var p in processes) p.Dispose();
        return pids.Count == 0 ? false : IsPlaying(milliseconds, pids.Contains);
    }

    /// <summary>
    /// Whether another program is playing sound on the default output, watched for a moment (a quiet passage of a song
    /// still moves the meter within it). Null when Windows could not tell.
    /// </summary>
    public static bool? IsPlaying(int milliseconds = 240) => IsPlaying(milliseconds, null);

    private static bool? IsPlaying(int milliseconds, Func<uint, bool>? process)
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
                if (session.State == AudioSessionState.AudioSessionStateActive && session.GetProcessID != own &&
                    (process is null || process(session.GetProcessID)))
                    active.Add(session);
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

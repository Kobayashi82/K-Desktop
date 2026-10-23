using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.World;

/// <summary>
/// Follows the beat of what the PC plays (<see cref="IWorld.Music"/>): on a thread of its own it reads the peak meter
/// of each audio session (the bars of Windows' volume mixer: nothing is recorded) and gives the loudest to the
/// <see cref="BeatTracker"/>, a hundred times a second while something sounds and four while it is quiet. The sessions
/// of K-Desktop are left out (this process, and K-Desktop's own, whose echo reference repeats everything that sounds), so
/// assistants never dance to their own steps or voice. It only runs while some assistant listens: stopped, it costs
/// nothing.
/// </summary>
internal sealed class MusicWatcher(Func<double> clock) : IDisposable
{
    private const double SampleSeconds = 0.01;
    private const double QuietSampleSeconds = 0.25;
    /// <summary>Quiet for this long, it reads slowly (the tracker forgets after a second without readings anyway).</summary>
    private const double QuietAfter = 3;
    private const double RefreshSeconds = 2;
    private const float Audible = 0.001f;

    private readonly BeatTracker _tracker = new();
    private readonly Lock _gate = new();
    private Thread? _thread;
    private volatile bool _running;

    public bool IsRunning => _thread is not null;

    /// <summary>The beat now (not playing while stopped).</summary>
    public MusicBeat Read(double now)
    {
        if (_thread is null) return default;
        lock (_gate) return new MusicBeat(_tracker.Playing, _tracker.Bpm, _tracker.Beat(now), now);
    }

    public void SetActive(bool active)
    {
        if (active == (_thread is not null)) return;
        if (!active)
        {
            Stop();
            return;
        }
        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "Asistente: música", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public void Dispose() => Stop();

    private void Stop()
    {
        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(1));
        _thread = null;
        lock (_gate) _tracker.Reset();
    }

    private void Run()
    {
        var timer = CreateWaitableTimerEx(0, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
        if (timer == 0) timer = CreateWaitableTimerEx(0, null, 0, TIMER_ALL_ACCESS);
        var meters = new Meters();
        try
        {
            var refresh = double.NegativeInfinity;
            var lastSound = double.NegativeInfinity;
            var wasPlaying = false;
            while (_running)
            {
                var now = clock();
                if (now >= refresh)
                {
                    refresh = now + RefreshSeconds;
                    meters.Refresh();
                }

                var peak = meters.Peak();
                if (peak > Audible) lastSound = now;
                var quiet = now - lastSound > QuietAfter;
                // Quiet, the readings stop: the tracker starts over when sound comes back.
                bool playing;
                lock (_gate)
                {
                    if (!quiet) _tracker.Add(now, peak);
                    else if (_tracker.Playing) _tracker.Reset();
                    playing = _tracker.Playing;
                }
                if (playing != wasPlaying)
                {
                    wasPlaying = playing;
                    Log.Info(playing ? $"Asistente: suena música ({_tracker.Bpm:0} ppm)" : "Asistente: ya no suena música");
                }

                var wait = quiet ? QuietSampleSeconds : SampleSeconds;
                if (timer != 0)
                {
                    var due = -(long)(wait * 10_000_000);
                    SetWaitableTimer(timer, in due, 0, 0, 0, false);
                    _ = WaitForSingleObject(timer, 500);
                }
                else Thread.Sleep(TimeSpan.FromSeconds(wait));
            }
        }
        catch (Exception ex) { Log.Warn("Asistente: el medidor de la música se ha parado: " + ex.Message); }
        finally
        {
            meters.Dispose();
            if (timer != 0) CloseHandle(timer);
        }
    }

    /// <summary>The meters of the active sessions of the default output that are not K-Desktop's.</summary>
    private sealed class Meters : IDisposable
    {
        private readonly MMDeviceEnumerator _enumerator = new();
        private readonly List<AudioSessionControl> _sessions = [];
        private MMDevice? _device;
        private bool _warned;

        public void Refresh()
        {
            Clear();
            try
            {
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var manager = _device.AudioSessionManager;
                manager.RefreshSessions();
                var sessions = manager.Sessions;
                var own = OwnProcesses();
                for (var i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (session.State == AudioSessionState.AudioSessionStateActive && !own.Contains(session.GetProcessID)) _sessions.Add(session);
                    else session.Dispose();
                }
            }
            catch (Exception ex)
            {
                // No output device (or it is changing): nothing sounds until the next look.
                if (!_warned) Log.Warn("Asistente: no se pueden leer las sesiones de audio: " + ex.Message);
                _warned = true;
            }
        }

        /// <summary>This process and every K-Desktop (the one that launched it records what sounds for its echo canceller).</summary>
        private static HashSet<uint> OwnProcesses()
        {
            var own = new HashSet<uint> { (uint)Environment.ProcessId };
            using var current = System.Diagnostics.Process.GetCurrentProcess();
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
            {
                own.Add((uint)process.Id);
                process.Dispose();
            }
            return own;
        }

        public float Peak()
        {
            var peak = 0f;
            foreach (var session in _sessions)
            {
                try { peak = Math.Max(peak, session.AudioMeterInformation.MasterPeakValue); }
                catch (Exception) { /* the session went away: forgotten at the next refresh */ }
            }
            return peak;
        }

        private void Clear()
        {
            foreach (var session in _sessions) session.Dispose();
            _sessions.Clear();
            _device?.Dispose();
            _device = null;
        }

        public void Dispose()
        {
            Clear();
            _enumerator.Dispose();
        }
    }
}

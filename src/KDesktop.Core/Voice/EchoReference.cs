using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace KDesktop.Core.Voice;

/// <summary>
/// What the PC plays, on the microphone's clock: Windows stamps every packet of both with the performance counter
/// (QPC), so a moment of the microphone can be matched with what was sounding then. Positions are 16 kHz ticks of
/// that clock (<see cref="AudioClock"/>).
/// </summary>
public interface IEchoReference
{
    /// <summary>
    /// Fills <paramref name="destination"/> with what was played from <paramref name="tick"/> on (zeros where nothing
    /// was). False when part of that stretch is still to arrive: try again a little later.
    /// </summary>
    bool Read(long tick, Span<float> destination);
}

/// <summary>The performance counter in 16 kHz ticks: one tick per sample of the voice audio.</summary>
public static class AudioClock
{
    /// <summary>100 ns units (what WASAPI stamps packets with) per tick: 10⁷ / 16000.</summary>
    public const long HundredNsPerTick = 10_000_000 / MicCapture.SampleRate;

    public static long Now => (long)(Stopwatch.GetTimestamp() * (10_000_000.0 / Stopwatch.Frequency)) / HundredNsPerTick;

    /// <summary>A WASAPI packet stamp (QPC in 100 ns units) as a tick.</summary>
    public static long FromQpc(long qpc) => qpc / HundredNsPerTick;
}

/// <summary>
/// Places the samples of a stream on the clock: one after another while the packets come when expected, so the
/// jitter of each stamp doesn't move them; anew after a gap (nothing was playing) or a glitch.
/// </summary>
internal sealed class StreamClock
{
    /// <summary>Beyond this difference between the stamp and where the samples were expected, they follow the stamp (20 ms).</summary>
    private const long Tolerance = MicCapture.SampleRate / 50;

    private long _next = long.MinValue;

    /// <summary>Tick of the first of <paramref name="count"/> samples of a packet stamped at <paramref name="tick"/>.</summary>
    public long Place(long tick, int count, bool discontinuity)
    {
        if (_next == long.MinValue || discontinuity || Math.Abs(tick - _next) > Tolerance) _next = tick;
        var start = _next;
        _next += count;
        return start;
    }

    public void Reset() => _next = long.MinValue;
}

/// <summary>
/// Pairs each 10 ms frame of the microphone with what the PC played at that moment and runs the echo canceller on
/// it. A frame whose reference hasn't arrived yet waits (a few ms) for it. Every few seconds, while something plays,
/// it checks how late the echo really comes (<see cref="EchoDelay"/>) and moves the reference to match.
/// </summary>
internal sealed class EchoStage : IDisposable
{
    /// <summary>
    /// Where the echo should start in the canceller's filter (30 ms): the reference goes this much ahead of the
    /// microphone, so the echo always comes after it, as the canceller needs, even when the device stamps are a little
    /// off. The filter covers the rest (the room's reverberation).
    /// </summary>
    private const long Lead = MicCapture.SampleRate * 30 / 1000;

    /// <summary>Longest a frame waits for its reference (100 ms); then it goes with what there is.</summary>
    private const long MaxWait = MicCapture.SampleRate / 10;

    /// <summary>How often the delay is checked (2 s of audio).</summary>
    private const int CheckEvery = MicCapture.SampleRate * 2;

    /// <summary>
    /// Where in the filter the echo may start without moving anything (10–100 ms): moving the reference makes the
    /// canceller learn the echo again, so it is only done when the echo would fall too early or too late in it.
    /// </summary>
    private const long EarliestTap = MicCapture.SampleRate / 100, LatestTap = MicCapture.SampleRate / 10;

    private const int Frame = EchoCanceller.Frame;

    private readonly EchoCanceller _canceller;
    private readonly IEchoReference _reference;
    private readonly float[] _render = new float[Frame];
    private float[] _pending = new float[Frame * 16];
    private int _count;
    private long _pendingTick;

    // How late the echo comes beyond what the clock says, and the last moments of the microphone (before cancelling)
    // to measure it with.
    private long _delay;
    private readonly float[] _history = new float[EchoDelay.Window];
    private int _historyPos;
    private bool _historyFull;
    private long _historyEnd = long.MinValue;
    private int _sinceCheck;
    private int _checking;
    private int? _candidate;
    private int _measured = int.MinValue;

    private readonly bool _inline;

    /// <param name="inline">Measure the delay right away instead of in the background (tests on recordings, faster than real time).</param>
    public EchoStage(EchoCanceller canceller, IEchoReference reference, bool inline = false)
    {
        _canceller = canceller;
        _reference = reference;
        _inline = inline;
    }

    /// <summary>
    /// Takes <paramref name="samples"/> (the first at <paramref name="tick"/>) and adds to <paramref name="output"/>
    /// every frame ready, cleaned. <paramref name="now"/>: the clock now (frames older than that may not wait).
    /// </summary>
    public void Process(ReadOnlySpan<float> samples, long tick, long now, List<float> output)
    {
        ApplyMeasuredDelay();
        if (_count > 0 && tick != _pendingTick + _count)
        {
            // The stream jumped (a glitch): what was waiting goes out now, as it is.
            Drain(now, output, force: true);
            _count = 0;
        }
        if (_count == 0) _pendingTick = tick;
        if (_pending.Length < _count + samples.Length) Array.Resize(ref _pending, Math.Max(_pending.Length * 2, _count + samples.Length));
        samples.CopyTo(_pending.AsSpan(_count));
        _count += samples.Length;
        Drain(now, output, force: false);
    }

    private void Drain(long now, List<float> output, bool force)
    {
        var done = 0;
        while (_count - done >= Frame)
        {
            var frameTick = _pendingTick + done;
            var complete = _reference.Read(frameTick + Lead - _delay, _render);
            if (!complete && !force && now - frameTick < MaxWait) break;
            var frame = _pending.AsSpan(done, Frame);
            Remember(frame, frameTick);
            _canceller.Cancel(frame, _render);
            foreach (var s in frame) output.Add(s);
            done += Frame;
        }
        if (force && _count - done > 0)
        {
            // Less than a frame left: it goes uncleaned rather than lost.
            foreach (var s in _pending.AsSpan(done, _count - done)) output.Add(s);
            done = _count;
        }
        if (done == 0) return;
        _pending.AsSpan(done, _count - done).CopyTo(_pending);
        _count -= done;
        _pendingTick += done;
    }

    /// <summary>Keeps the raw frame for the delay check, and starts a check (in the background) every few seconds.</summary>
    private void Remember(ReadOnlySpan<float> frame, long tick)
    {
        if (tick != _historyEnd)
        {
            // The first frame, or a jump: the history starts over.
            _historyPos = 0;
            _historyFull = false;
        }
        foreach (var s in frame)
        {
            _history[_historyPos++] = s;
            if (_historyPos == _history.Length)
            {
                _historyPos = 0;
                _historyFull = true;
            }
        }
        _historyEnd = tick + frame.Length;
        _sinceCheck += frame.Length;
        if (!_historyFull || _sinceCheck < CheckEvery || Volatile.Read(ref _checking) != 0) return;
        _sinceCheck = 0;

        var mic = new float[EchoDelay.Window];
        _history.AsSpan(_historyPos).CopyTo(mic);
        _history.AsSpan(0, _historyPos).CopyTo(mic.AsSpan(_history.Length - _historyPos));
        var reference = new float[EchoDelay.Window + EchoDelay.MaxLate + EchoDelay.MaxEarly];
        if (!_reference.Read(_historyEnd - EchoDelay.Window - EchoDelay.MaxLate, reference)) return;
        _checking = 1;
        if (_inline)
        {
            if (EchoDelay.Estimate(mic, reference) is { } found) _measured = found;
            _checking = 0;
            return;
        }
        Task.Run(() =>
        {
            try
            {
                if (EchoDelay.Estimate(mic, reference) is { } delay) Interlocked.Exchange(ref _measured, delay);
            }
            catch (Exception ex)
            {
                Log.Warn("Error midiendo el retardo del eco: " + ex.Message);
            }
            finally
            {
                Volatile.Write(ref _checking, 0);
            }
        });
    }

    /// <summary>Two measurements in a row that agree, and an echo out of place in the filter, move the reference.</summary>
    private void ApplyMeasuredDelay()
    {
        var measured = Interlocked.Exchange(ref _measured, int.MinValue);
        if (measured == int.MinValue) return;
        if (_candidate is { } previous && Math.Abs(previous - measured) <= MicCapture.SampleRate * 3 / 1000)
        {
            _candidate = null;
            var tap = Lead + measured - _delay;
            if (tap >= EarliestTap && tap <= LatestTap) return;
            Log.Info($"Cancelación de eco: el sonido de los altavoces llega {measured * 1000 / MicCapture.SampleRate} ms más tarde de lo que dice Windows; se ajusta");
            _delay = measured;
            _canceller.Reset();
        }
        else _candidate = measured;
    }

    public void Dispose() => _canceller.Dispose();
}

/// <summary>
/// What the speakers (the Windows default output) play, captured as loopback and kept for 3 s on the clock. Shared:
/// every microphone in use takes it with <see cref="Acquire"/> and gives it back with <see cref="Release"/>; it only
/// records while someone uses it. Follows the default output when it changes.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Shared instance: Release stops its timer when the last user gives it back.")]
public sealed class PlaybackReference : IEchoReference
{
    private const int Length = MicCapture.SampleRate * 3;

    /// <summary>Without packets for this long, nothing is playing (Windows sends none then): don't wait for them.</summary>
    private const long Idle = MicCapture.SampleRate / 5;

    private static readonly object SharedGate = new();
    private static PlaybackReference? _shared;
    private static int _users;

    private readonly object _gate = new();
    private readonly float[] _ring = new float[Length];
    private readonly StreamClock _clock = new();
    private long _written = long.MinValue;
    private long _lastPacket = long.MinValue;
    private WasapiRecorder? _recorder;
    private string? _deviceId;
    private BufferedWaveProvider? _buffer;
    private ISampleProvider? _resampled;
    private byte[] _bytes = [];
    private float[] _chunk = new float[4096];
    private Timer? _watch;

    private PlaybackReference() { }

    public static PlaybackReference Acquire()
    {
        lock (SharedGate)
        {
            _shared ??= new PlaybackReference();
            if (_users++ == 0) _shared.Start();
            return _shared;
        }
    }

    public static void Release()
    {
        lock (SharedGate)
        {
            if (_users == 0 || --_users > 0) return;
            _shared?.Stop();
        }
    }

    private void Start()
    {
        Open();
        // The default output may change (another device chosen, headphones plugged in): follow it.
        _watch = new Timer(_ => FollowDefault(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    private void Stop()
    {
        _watch?.Dispose();
        _watch = null;
        Close();
    }

    private void FollowDefault()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (device.ID == _deviceId) return;
        }
        catch
        {
            return; // no output device right now
        }
        lock (SharedGate)
        {
            if (_users == 0) return;
            Close();
            Open();
        }
    }

    private void Open()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var recorder = new WasapiRecorderBuilder().WithDevice(device).WithLoopbackCapture().WithSharedMode().WithEventSync().Build();
            _buffer = new BufferedWaveProvider(recorder.WaveFormat, TimeSpan.FromSeconds(2)) { DiscardOnBufferOverflow = true, ReadFully = false };
            _resampled = AudioFormats.ToVoice(_buffer.ToSampleProvider());
            _clock.Reset();
            recorder.DataAvailable += OnData;
            recorder.StartRecording();
            _recorder = recorder;
            _deviceId = device.ID;
            Log.Info($"Cancelación de eco: escuchando lo que suena en {device.FriendlyName} ({recorder.WaveFormat})");
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudo capturar lo que suena en el PC (sin cancelación de eco): " + ex.Message);
            _recorder = null;
            _deviceId = null;
        }
    }

    private void Close()
    {
        if (_recorder is not { } recorder) return;
        _recorder = null;
        recorder.DataAvailable -= OnData;
        try { recorder.StopRecording(); } catch { /* already stopped */ }
        recorder.Dispose();
        lock (_gate)
        {
            _written = long.MinValue;
            _lastPacket = long.MinValue;
        }
    }

    private void OnData(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpc)
    {
        if (data.IsEmpty || _buffer is null || _resampled is null) return;
        var now = AudioClock.Now;
        if (_bytes.Length < data.Length) _bytes = new byte[data.Length];
        if ((flags & AudioClientBufferFlags.Silent) != 0) Array.Clear(_bytes, 0, data.Length);
        else data.CopyTo(_bytes);
        _buffer.AddSamples(_bytes, 0, data.Length);
        var count = AudioFormats.ReadAll(_resampled, ref _chunk);
        if (count == 0) return;
        // A packet without a valid stamp: as if its last sample were being played now.
        var stamped = qpc > 0 && (flags & AudioClientBufferFlags.TimestampError) == 0;
        var tick = stamped ? AudioClock.FromQpc(qpc) : now - count;
        var start = _clock.Place(tick, count, (flags & AudioClientBufferFlags.DataDiscontinuity) != 0);
        lock (_gate)
        {
            if (_written == long.MinValue || start - _written >= Length) Array.Clear(_ring);
            else for (var t = _written; t < start; t++) _ring[Index(t)] = 0; // a gap: nothing was playing
            for (var i = 0; i < count; i++) _ring[Index(start + i)] = _chunk[i];
            _written = Math.Max(_written == long.MinValue ? start + count : _written, start + count);
            _lastPacket = now;
        }
    }

    private static int Index(long tick) => (int)(((tick % Length) + Length) % Length);

    public bool Read(long tick, Span<float> destination)
    {
        lock (_gate)
        {
            var written = _written;
            for (var i = 0; i < destination.Length; i++)
            {
                var t = tick + i;
                destination[i] = written != long.MinValue && t < written && t >= written - Length ? _ring[Index(t)] : 0;
            }
            // While packets keep coming, what is still to arrive will; when they stopped, nothing is playing.
            var playing = _lastPacket != long.MinValue && AudioClock.Now - _lastPacket < Idle;
            return !playing || tick + destination.Length <= written;
        }
    }
}

/// <summary>A recorded reference for the tests (<c>KDESKTOP_VOICE_FAKE_RENDER</c>, <c>--aec-test</c>): tick 0 = its first sample.</summary>
internal sealed class FileReference(float[] audio) : IEchoReference
{
    public bool Read(long tick, Span<float> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            var t = tick + i;
            destination[i] = t >= 0 && t < audio.Length ? audio[t] : 0;
        }
        return true;
    }
}

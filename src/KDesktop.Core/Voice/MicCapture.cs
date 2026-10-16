using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KDesktop.Core.Voice;

/// <summary>
/// Records a microphone (shared mode, whatever format the device uses) and hands out mono 16 kHz float samples,
/// the format of every voice model. Only runs between <see cref="Start"/> and <see cref="Stop"/>: nothing is recorded
/// while voice is idle. With <see cref="EchoCancel"/>, what the PC is playing is taken out of it (see
/// <see cref="EchoCanceller"/>).
/// </summary>
public sealed class MicCapture : IDisposable
{
    public const int SampleRate = 16000;

    private readonly object _gate = new();
    private readonly List<float> _output = new(SampleRate / 10);
    private readonly StreamClock _clock = new();
    private WasapiRecorder? _capture;
    private BufferedWaveProvider? _buffer;
    private ISampleProvider? _resampled;
    private EchoStage? _echo;
    private byte[] _bytes = [];
    private float[] _chunk = new float[4096];

    /// <summary>New 16 kHz mono samples (on the capture thread).</summary>
    public event Action<float[]>? Samples;

    /// <summary>Peak level of the last block, 0–1 (for the meter).</summary>
    public float Level { get; private set; }

    public bool IsRunning => _capture is not null;

    /// <summary>Take what the PC plays out of the microphone (from the next <see cref="Start"/>).</summary>
    public bool EchoCancel { get; set; }

    /// <summary>The echo canceller is working in this recording.</summary>
    public bool EchoCancelling => _echo is not null;

    /// <summary>Starts recording <paramref name="deviceId"/> (null: the Windows default microphone).</summary>
    public void Start(string? deviceId)
    {
        lock (_gate)
        {
            if (_capture is not null) return;
            using var enumerator = new MMDeviceEnumerator();
            var device = FindDevice(enumerator, deviceId) ?? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            var capture = new WasapiRecorderBuilder().WithDevice(device).WithSharedMode().WithEventSync().Build();
            _buffer = new BufferedWaveProvider(capture.WaveFormat, TimeSpan.FromSeconds(2)) { DiscardOnBufferOverflow = true, ReadFully = false };
            _resampled = AudioFormats.ToVoice(_buffer.ToSampleProvider());
            _clock.Reset();
            if (EchoCancel && EchoCanceller.TryCreate() is { } canceller) _echo = new EchoStage(canceller, PlaybackReference.Acquire());
            capture.DataAvailable += OnData;
            capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null) Log.Warn("El micrófono dejó de grabar: " + e.Exception.Message);
            };
            try
            {
                capture.StartRecording();
            }
            catch
            {
                capture.Dispose();
                DropEcho();
                throw;
            }
            _capture = capture;
            Log.Info($"Micrófono: {device.FriendlyName} ({capture.WaveFormat}){(_echo is not null ? " · con cancelación de eco" : "")}");
        }
    }

    public void Stop()
    {
        WasapiRecorder capture;
        lock (_gate)
        {
            if (_capture is null) return;
            capture = _capture;
            _capture = null;
            capture.DataAvailable -= OnData;
            _buffer = null;
            _resampled = null;
            DropEcho();
            Level = 0;
        }
        // Outside the lock: stopping waits for the capture thread, which may be waiting for the lock in OnData.
        try { capture.StopRecording(); } catch { /* already stopped */ }
        capture.Dispose();
    }

    private void DropEcho()
    {
        if (_echo is null) return;
        _echo.Dispose();
        _echo = null;
        PlaybackReference.Release();
    }

    private void OnData(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpc)
    {
        if (data.IsEmpty) return;
        float[] output;
        lock (_gate)
        {
            if (_buffer is null || _resampled is null) return;
            if (_bytes.Length < data.Length) _bytes = new byte[data.Length];
            if ((flags & AudioClientBufferFlags.Silent) != 0) Array.Clear(_bytes, 0, data.Length);
            else data.CopyTo(_bytes);
            _buffer.AddSamples(_bytes, 0, data.Length);
            var total = AudioFormats.ReadAll(_resampled, ref _chunk);
            if (total == 0) return;
            if (_echo is null) output = _chunk[..total];
            else
            {
                // On the clock shared with the speakers' loopback, so each frame meets what was playing then.
                var now = AudioClock.Now;
                var stamped = qpc > 0 && (flags & AudioClientBufferFlags.TimestampError) == 0;
                var tick = _clock.Place(stamped ? AudioClock.FromQpc(qpc) : now - total, total,
                    (flags & AudioClientBufferFlags.DataDiscontinuity) != 0);
                _output.Clear();
                _echo.Process(_chunk.AsSpan(0, total), tick, now, _output);
                if (_output.Count == 0) return;
                output = _output.ToArray();
            }
        }
        var peak = 0f;
        foreach (var s in output) peak = Math.Max(peak, Math.Abs(s));
        Level = Math.Min(1, peak);
        Samples?.Invoke(output);
    }

    /// <summary>The capture devices that are on, for the options: (id, name).</summary>
    public static IReadOnlyList<(string Id, string Name)> Devices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).Select(d => (d.ID, d.FriendlyName)).ToList();
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudieron listar los micrófonos: " + ex.Message);
            return [];
        }
    }

    private static MMDevice? FindDevice(MMDeviceEnumerator enumerator, string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        try
        {
            var device = enumerator.GetDevice(id);
            return device.State == DeviceState.Active ? device : null;
        }
        catch
        {
            return null; // unplugged: the default one is used
        }
    }

    public void Dispose() => Stop();
}

/// <summary>Conversions of captured audio to the voice format (mono 16 kHz float).</summary>
internal static class AudioFormats
{
    /// <summary><paramref name="source"/> mixed down to one channel and resampled to <see cref="MicCapture.SampleRate"/>.</summary>
    public static ISampleProvider ToVoice(ISampleProvider source)
    {
        if (source.WaveFormat.Channels > 1) source = new MonoMix(source);
        return source.WaveFormat.SampleRate == MicCapture.SampleRate ? source : new WdlResamplingSampleProvider(source, MicCapture.SampleRate);
    }

    /// <summary>Reads everything <paramref name="source"/> has ready into <paramref name="chunk"/> (grown as needed).</summary>
    public static int ReadAll(ISampleProvider source, ref float[] chunk)
    {
        var total = 0;
        while (true)
        {
            if (total == chunk.Length) Array.Resize(ref chunk, chunk.Length * 2);
            var read = source.Read(chunk.AsSpan(total));
            if (read <= 0) return total;
            total += read;
        }
    }

    /// <summary>Averages every channel into one.</summary>
    private sealed class MonoMix(ISampleProvider source) : ISampleProvider
    {
        private readonly int _channels = source.WaveFormat.Channels;
        private float[] _buffer = [];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(Span<float> buffer)
        {
            var needed = buffer.Length * _channels;
            if (_buffer.Length < needed) _buffer = new float[needed];
            var read = source.Read(_buffer.AsSpan(0, needed));
            var frames = read / _channels;
            for (var f = 0; f < frames; f++)
            {
                var sum = 0f;
                for (var c = 0; c < _channels; c++) sum += _buffer[f * _channels + c];
                buffer[f] = sum / _channels;
            }
            return frames;
        }
    }
}

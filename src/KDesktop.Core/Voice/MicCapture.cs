using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KDesktop.Core.Voice;

/// <summary>
/// Records a microphone (shared mode, whatever format the device uses) and hands out mono 16 kHz float samples,
/// the format of every voice model. Only runs between <see cref="Start"/> and <see cref="Stop"/>: nothing is recorded
/// while voice is idle.
/// </summary>
public sealed class MicCapture : IDisposable
{
    public const int SampleRate = 16000;

    private readonly object _gate = new();
    private WasapiCapture? _capture;
    private BufferedWaveProvider? _buffer;
    private ISampleProvider? _resampled;
    private float[] _chunk = new float[4096];

    /// <summary>New 16 kHz mono samples (on the capture thread).</summary>
    public event Action<float[]>? Samples;

    /// <summary>Peak level of the last block, 0–1 (for the meter).</summary>
    public float Level { get; private set; }

    public bool IsRunning => _capture is not null;

    /// <summary>Starts recording <paramref name="deviceId"/> (null: the Windows default microphone).</summary>
    public void Start(string? deviceId)
    {
        lock (_gate)
        {
            if (_capture is not null) return;
            using var enumerator = new MMDeviceEnumerator();
            var device = FindDevice(enumerator, deviceId) ?? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            var capture = new WasapiCapture(device, useEventSync: true, audioBufferMillisecondsLength: 50);
            _buffer = new BufferedWaveProvider(capture.WaveFormat, TimeSpan.FromSeconds(2)) { DiscardOnBufferOverflow = true, ReadFully = false };
            ISampleProvider samples = _buffer.ToSampleProvider();
            if (samples.WaveFormat.Channels > 1) samples = new MonoMix(samples);
            _resampled = samples.WaveFormat.SampleRate == SampleRate ? samples : new WdlResamplingSampleProvider(samples, SampleRate);
            capture.DataAvailable += OnData;
            capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null) Log.Warn("El micrófono dejó de grabar: " + e.Exception.Message);
            };
            capture.StartRecording();
            _capture = capture;
            Log.Info($"Micrófono: {device.FriendlyName} ({capture.WaveFormat})");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_capture is not { } capture) return;
            _capture = null;
            capture.DataAvailable -= OnData;
            try { capture.StopRecording(); } catch { /* already stopped */ }
            capture.Dispose();
            _buffer = null;
            _resampled = null;
            Level = 0;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        float[] output;
        lock (_gate)
        {
            if (_buffer is null || _resampled is null) return;
            _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            var total = 0;
            while (true)
            {
                if (total == _chunk.Length) Array.Resize(ref _chunk, _chunk.Length * 2);
                var read = _resampled.Read(_chunk.AsSpan(total));
                if (read <= 0) break;
                total += read;
            }
            if (total == 0) return;
            output = _chunk[..total];
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

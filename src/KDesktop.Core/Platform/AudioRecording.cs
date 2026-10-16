using System.Diagnostics;
using KDesktop.Core.Model;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KDesktop.Core.Platform;

/// <summary>
/// Listens to what the PC plays (loopback) and/or the microphone and keeps the last seconds on a common time line
/// (48 kHz stereo), so both can be mixed and read back by time. Silence is silence: the loopback sends nothing while
/// nothing plays, and those gaps read as zeros.
/// </summary>
public sealed class AudioTap : IDisposable
{
    public const int Rate = 48000;
    public const int Channels = 2;

    private sealed class Source
    {
        public required WasapiRecorder Recorder { get; init; }
        public required BufferedWaveProvider Buffer { get; init; }
        public required ISampleProvider Converted { get; init; }
        public required float[] Ring { get; init; }
        public byte[] Bytes = [];
        public float[] Chunk = new float[8192];
        public long Next = long.MinValue;   // frame where the next packet goes when it comes on time
        public long Written = long.MinValue; // frames before this are on the ring (if not older than it)
    }

    private readonly Lock _lock = new();
    private readonly List<Source> _sources = [];
    private readonly int _frames;

    /// <param name="seconds">How much it keeps.</param>
    public AudioTap(AudioSource source, int seconds)
    {
        _frames = Math.Max(2, seconds) * Rate;
        using var enumerator = new MMDeviceEnumerator();
        if (source is AudioSource.Output or AudioSource.Both)
            Add(enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia), loopback: true);
        if (source is AudioSource.Microphone or AudioSource.Both)
            Add(enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console), loopback: false);
    }

    /// <summary>The current frame on the time line.</summary>
    public static long Now => (long)(Stopwatch.GetTimestamp() * ((double)Rate / Stopwatch.Frequency));

    private void Add(MMDevice device, bool loopback)
    {
        var builder = new WasapiRecorderBuilder().WithDevice(device).WithSharedMode().WithEventSync();
        if (loopback) builder = builder.WithLoopbackCapture();
        var recorder = builder.Build();
        var buffer = new BufferedWaveProvider(recorder.WaveFormat, TimeSpan.FromSeconds(2)) { DiscardOnBufferOverflow = true, ReadFully = false };
        ISampleProvider converted = buffer.ToSampleProvider();
        converted = converted.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(converted),
            2 => converted,
            _ => new FirstTwoChannels(converted),
        };
        if (converted.WaveFormat.SampleRate != Rate) converted = new WdlResamplingSampleProvider(converted, Rate);
        var source = new Source { Recorder = recorder, Buffer = buffer, Converted = converted, Ring = new float[_frames * Channels] };
        recorder.DataAvailable += (data, flags, _, _) => OnData(source, data, flags);
        recorder.StartRecording();
        _sources.Add(source);
        Log.Info($"Grabación: {(loopback ? "lo que suena en" : "micrófono")} {device.FriendlyName} ({recorder.WaveFormat})");
        device.Dispose();
    }

    private void OnData(Source s, ReadOnlySpan<byte> data, AudioClientBufferFlags flags)
    {
        if (data.IsEmpty) return;
        lock (_lock)
        {
            if (s.Bytes.Length < data.Length) s.Bytes = new byte[data.Length];
            if ((flags & AudioClientBufferFlags.Silent) != 0) Array.Clear(s.Bytes, 0, data.Length);
            else data.CopyTo(s.Bytes);
            s.Buffer.AddSamples(s.Bytes, 0, data.Length);
            var total = 0;
            while (true)
            {
                if (total == s.Chunk.Length) Array.Resize(ref s.Chunk, s.Chunk.Length * 2);
                var read = s.Converted.Read(s.Chunk.AsSpan(total));
                if (read <= 0) break;
                total += read;
            }
            var frames = total / Channels;
            if (frames == 0) return;
            // Packets that follow on keep their place; after a gap (silence) or drift, it starts over from the clock.
            var start = Now - frames;
            if (s.Next != long.MinValue && Math.Abs(start - s.Next) < Rate / 10) start = s.Next;
            if (s.Written != long.MinValue && start > s.Written)
                for (var f = s.Written; f < Math.Min(start, s.Written + _frames); f++) Set(s, f, 0, 0);
            for (var i = 0; i < frames; i++) Set(s, start + i, s.Chunk[i * 2], s.Chunk[i * 2 + 1]);
            s.Next = start + frames;
            s.Written = s.Written == long.MinValue ? s.Next : Math.Max(s.Written, s.Next);
        }
    }

    private void Set(Source s, long frame, float left, float right)
    {
        var i = (int)(((frame % _frames) + _frames) % _frames) * Channels;
        s.Ring[i] = left;
        s.Ring[i + 1] = right;
    }

    /// <summary>The frames [<paramref name="from"/>, <paramref name="to"/>) mixed, interleaved stereo; what isn't kept is silence.</summary>
    public float[] Read(long from, long to)
    {
        var count = (int)Math.Max(0, to - from);
        var output = new float[count * Channels];
        lock (_lock)
        {
            foreach (var s in _sources)
            {
                if (s.Written == long.MinValue) continue;
                for (var f = 0; f < count; f++)
                {
                    var frame = from + f;
                    if (frame >= s.Written || frame < s.Written - _frames) continue;
                    var i = (int)(((frame % _frames) + _frames) % _frames) * Channels;
                    output[f * 2] += s.Ring[i];
                    output[f * 2 + 1] += s.Ring[i + 1];
                }
            }
        }
        for (var i = 0; i < output.Length; i++) output[i] = Math.Clamp(output[i], -1f, 1f);
        return output;
    }

    public void Dispose()
    {
        foreach (var s in _sources)
        {
            try { s.Recorder.StopRecording(); } catch { /* already stopped */ }
            s.Recorder.Dispose();
        }
        _sources.Clear();
    }

    /// <summary>Surround sound: just the front left and right.</summary>
    private sealed class FirstTwoChannels(ISampleProvider source) : ISampleProvider
    {
        private readonly int _channels = source.WaveFormat.Channels;
        private float[] _buffer = [];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);

        public int Read(Span<float> buffer)
        {
            var frames = buffer.Length / 2;
            if (_buffer.Length < frames * _channels) _buffer = new float[frames * _channels];
            var read = source.Read(_buffer.AsSpan(0, frames * _channels)) / _channels;
            for (var f = 0; f < read; f++)
            {
                buffer[f * 2] = _buffer[f * _channels];
                buffer[f * 2 + 1] = _buffer[f * _channels + 1];
            }
            return read * 2;
        }
    }
}

/// <summary>A .wav writer for the time line's audio (16-bit, 48 kHz, stereo).</summary>
public static class Wav
{
    public static WaveFileWriter Create(string path) => new(path, new WaveFormat(AudioTap.Rate, 16, AudioTap.Channels));

    public static void Write(WaveFileWriter writer, float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var v = (short)Math.Round(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            bytes[i * 2] = (byte)v;
            bytes[i * 2 + 1] = (byte)(v >> 8);
        }
        writer.Write(bytes, 0, bytes.Length);
    }
}

/// <summary>
/// «Grabar audio»: one recording at a time (to a .wav that grows as it records), and the replay — the last seconds
/// kept all the time while it is on (Settings › Capturas), saved on demand.
/// </summary>
public sealed class AudioRecording : IDisposable
{
    /// <summary>What the time line is read behind «now»: the last packets may still be on their way.</summary>
    private const int Latency = AudioTap.Rate / 4;

    private readonly Lock _lock = new();
    private AudioTap? _recordTap;
    private WaveFileWriter? _writer;
    private Timer? _writeTimer;
    private long _position;
    private DateTime _started;

    private AudioTap? _replay;
    private int _replaySeconds;
    private AudioSource _replaySource;

    /// <summary>A recording started or stopped (the tray shows it).</summary>
    public event Action? Changed;

    public bool IsRecording => _writer is not null;

    /// <summary>The file being recorded, while <see cref="IsRecording"/>.</summary>
    public string? CurrentFile { get; private set; }

    public bool ReplayOn => _replay is not null;

    public void Start(AudioSource source, string path)
    {
        lock (_lock)
        {
            if (_writer is not null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tap = new AudioTap(source, 10);
            _writer = Wav.Create(path);
            _recordTap = tap;
            _position = AudioTap.Now;
            _started = DateTime.Now;
            CurrentFile = path;
            _writeTimer = new Timer(_ => Flush(final: false), null, 250, 250);
        }
        Log.Info($"Grabación de audio: empieza ({source}) → {path}");
        Changed?.Invoke();
    }

    /// <summary>Stops the recording; returns its file and length (null when nothing was recording).</summary>
    public (string Path, TimeSpan Length)? Stop()
    {
        string? file;
        TimeSpan length;
        lock (_lock)
        {
            if (_writer is null) return null;
            _writeTimer?.Dispose();
            _writeTimer = null;
            Flush(final: true);
            _writer.Dispose();
            _writer = null;
            _recordTap?.Dispose();
            _recordTap = null;
            file = CurrentFile;
            CurrentFile = null;
            length = DateTime.Now - _started;
        }
        Log.Info($"Grabación de audio: termina ({length:mm\\:ss}) → {file}");
        Changed?.Invoke();
        return (file!, length);
    }

    private void Flush(bool final)
    {
        lock (_lock)
        {
            if (_writer is null || _recordTap is null) return;
            var to = AudioTap.Now - (final ? AudioTap.Rate / 20 : Latency);
            if (to <= _position) return;
            Wav.Write(_writer, _recordTap.Read(_position, to));
            _position = to;
            if (!final) _writer.Flush();
        }
    }

    /// <summary>Turns the replay on or off, or starts it over with another length or source.</summary>
    public void ConfigureReplay(bool on, int seconds, AudioSource source)
    {
        seconds = Math.Clamp(seconds, 5, 600);
        lock (_lock)
        {
            if (on && _replay is not null && seconds == _replaySeconds && source == _replaySource) return;
            _replay?.Dispose();
            _replay = null;
            if (!on) return;
            try
            {
                _replay = new AudioTap(source, seconds + 2);
                _replaySeconds = seconds;
                _replaySource = source;
                Log.Info($"Repetición de audio: guardando siempre los últimos {seconds} s");
            }
            catch (Exception ex)
            {
                Log.Warn("No se pudo empezar la repetición de audio: " + ex.Message);
            }
        }
    }

    /// <summary>Writes the last seconds of the replay to <paramref name="path"/>; false when the replay is off.</summary>
    public bool SaveReplay(string path)
    {
        float[] audio;
        lock (_lock)
        {
            if (_replay is null) return false;
            var to = AudioTap.Now - Latency;
            audio = _replay.Read(to - (long)_replaySeconds * AudioTap.Rate, to);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = Wav.Create(path);
        Wav.Write(writer, audio);
        Log.Info($"Repetición de audio guardada: {path}");
        return true;
    }

    public void Dispose()
    {
        Stop();
        lock (_lock)
        {
            _replay?.Dispose();
            _replay = null;
        }
    }
}

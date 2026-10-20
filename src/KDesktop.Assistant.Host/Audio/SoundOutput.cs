using System.Runtime.InteropServices;
using KDesktop.Core;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.Audio;

/// <summary>
/// Plays the <see cref="Mixer"/> on the default device (waveOut, 16-bit stereo). It only exists while something
/// sounds: the first sound opens the device on a thread of its own, and after a while of silence the device closes
/// and the thread ends, so an assistant at rest keeps no audio stream open (nothing in the volume mixer, no wake-ups).
/// </summary>
internal sealed unsafe class SoundOutput : IDisposable
{
    /// <summary>Frames per buffer (about 21 ms); with four queued, under 90 ms from a Play to the speakers.</summary>
    private const int BufferFrames = 1024;
    private const int BufferCount = 4;

    /// <summary>Silence after which the device closes.</summary>
    private static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(15);

    /// <summary>After a device that would not open, how long before trying again.</summary>
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly Mixer _mixer;
    private readonly Lock _gate = new();
    private readonly AutoResetEvent _signal = new(false);
    private Thread? _thread;
    private volatile bool _disposed;
    /// <summary>Before this moment (TickCount64) a device that would not open is not tried again.</summary>
    private long _retryAt;

    public SoundOutput(Mixer mixer) => _mixer = mixer;

    /// <summary>Something was played or a loop went up: make sure it is heard (any thread).</summary>
    public void Wake()
    {
        if (_disposed) return;
        lock (_gate)
        {
            if (_thread is null)
            {
                if (Environment.TickCount64 < _retryAt) return;
                _thread = new Thread(Run) { IsBackground = true, Name = "K-Desktop assistant sound", Priority = ThreadPriority.AboveNormal };
                _thread.Start();
                return;
            }
        }
        _signal.Set();
    }

    private void Run()
    {
        var format = new WAVEFORMATEX
        {
            wFormatTag = WAVE_FORMAT_PCM,
            nChannels = 2,
            nSamplesPerSec = Mixer.Rate,
            wBitsPerSample = 16,
            nBlockAlign = 4,
            nAvgBytesPerSec = Mixer.Rate * 4,
        };
        var result = waveOutOpen(out var device, WAVE_MAPPER, &format, _signal.SafeWaitHandle.DangerousGetHandle(), 0, CALLBACK_EVENT);
        if (result != 0)
        {
            Log.Warn($"Asistente: no se ha podido abrir la salida de sonido (waveOutOpen {result})");
            lock (_gate)
            {
                _retryAt = Environment.TickCount64 + (long)RetryAfter.TotalMilliseconds;
                _thread = null;
            }
            _mixer.Silence();
            return;
        }

        var memory = (short*)NativeMemory.AllocZeroed((nuint)(BufferFrames * 2 * BufferCount * sizeof(short)));
        var headers = (WAVEHDR*)NativeMemory.AllocZeroed((nuint)(BufferCount * sizeof(WAVEHDR)));
        var mix = new float[BufferFrames * 2];
        try
        {
            for (var i = 0; i < BufferCount; i++)
            {
                headers[i].lpData = (byte*)(memory + i * BufferFrames * 2);
                headers[i].dwBufferLength = BufferFrames * 2 * sizeof(short);
                _ = waveOutPrepareHeader(device, &headers[i], (uint)sizeof(WAVEHDR));
                headers[i].dwFlags |= WHDR_DONE;
            }
            Log.Info("Asistente: salida de sonido abierta");
            Pump(device, headers, mix);
        }
        finally
        {
            _ = waveOutReset(device);
            for (var i = 0; i < BufferCount; i++) _ = waveOutUnprepareHeader(device, &headers[i], (uint)sizeof(WAVEHDR));
            _ = waveOutClose(device);
            NativeMemory.Free(headers);
            NativeMemory.Free(memory);
            Log.Info("Asistente: salida de sonido cerrada");
        }
    }

    /// <summary>Keeps the buffers full while there is sound; returns after <see cref="IdleClose"/> of silence.</summary>
    private void Pump(nint device, WAVEHDR* headers, float[] mix)
    {
        var quietSince = Environment.TickCount64;
        while (!_disposed)
        {
            var queued = 0;
            for (var i = 0; i < BufferCount && !_disposed; i++)
            {
                var header = &headers[i];
                if ((header->dwFlags & WHDR_DONE) == 0)
                {
                    queued++;
                    continue;
                }
                if (!_mixer.HasSound) continue;
                _mixer.Mix(mix);
                var output = (short*)header->lpData;
                for (var s = 0; s < mix.Length; s++) output[s] = (short)(SoftClip(mix[s]) * short.MaxValue);
                header->dwFlags &= ~WHDR_DONE;
                _ = waveOutWrite(device, header, (uint)sizeof(WAVEHDR));
                queued++;
            }

            if (queued > 0 || _mixer.HasSound) quietSince = Environment.TickCount64;
            else if (Environment.TickCount64 - quietSince > IdleClose.TotalMilliseconds)
            {
                lock (_gate)
                {
                    // A sound that came in meanwhile keeps it open.
                    if (!_mixer.HasSound)
                    {
                        _thread = null;
                        return;
                    }
                }
            }
            // A finished buffer or a new sound sets the event; at rest, only to count the silence.
            _signal.WaitOne(queued > 0 ? 50 : 1000);
        }
    }

    /// <summary>Transparent up to 0.6; above it bends smoothly towards 1 instead of clipping.</summary>
    internal static float SoftClip(float x)
    {
        const float knee = 0.6f;
        var a = MathF.Abs(x);
        if (a <= knee) return x;
        var over = (a - knee) / (1 - knee);
        return MathF.Sign(x) * (knee + (1 - knee) * over / (1 + over));
    }

    public void Dispose()
    {
        _disposed = true;
        _signal.Set();
        Thread? thread;
        lock (_gate) thread = _thread;
        thread?.Join(1000);
        _signal.Dispose();
    }
}

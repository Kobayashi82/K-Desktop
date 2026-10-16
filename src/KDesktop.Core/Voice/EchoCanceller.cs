using System.Runtime.InteropServices;

namespace KDesktop.Core.Voice;

/// <summary>
/// Echo canceller for mono 16 kHz audio in 10 ms frames: given what the PC was playing, it takes it out of what the
/// microphone heard, so music or a film don't drown the wake word and the orders. Speex's adaptive filter (MDF), on its
/// own: it only subtracts its estimate of the echo, it never turns the voice down, which is what speech recognition
/// needs (the cancellers made for calls also suppress what is left, and with it the first syllables, i.e. the wake
/// word). With nothing playing, the voice passes through unchanged.
/// </summary>
public sealed class EchoCanceller : IDisposable
{
    /// <summary>Samples per frame: 10 ms at 16 kHz.</summary>
    public const int Frame = MicCapture.SampleRate / 100;

    /// <summary>The echo it can model: 256 ms after the reference (the room's reverberation, the delay of the devices).</summary>
    private const int FilterLength = 4096;

    private const string Library = "speexdsp";
    private const int SetSamplingRate = 24;

    private static bool? _available;
    private readonly short[] _capture = new short[Frame];
    private readonly short[] _render = new short[Frame];
    private readonly short[] _output = new short[Frame];
    private IntPtr _state;

    private EchoCanceller(IntPtr state) => _state = state;

    /// <summary>A new canceller, or null when the native library can't be used (it is then logged once).</summary>
    public static EchoCanceller? TryCreate()
    {
        if (_available == false) return null;
        try
        {
            var state = speex_echo_state_init(Frame, FilterLength);
            if (state == IntPtr.Zero) throw new InvalidOperationException("speex_echo_state_init");
            var rate = MicCapture.SampleRate;
            speex_echo_ctl(state, SetSamplingRate, ref rate);
            _available = true;
            return new EchoCanceller(state);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or InvalidOperationException)
        {
            if (_available is null) Log.Warn("Sin cancelación de eco (speexdsp): " + ex.Message);
            _available = false;
            return null;
        }
    }

    /// <summary>
    /// Cleans one frame of the microphone (<paramref name="capture"/>, −1…1, in place) given what was playing then
    /// (<paramref name="render"/>, a little ahead of it: the echo comes after).
    /// </summary>
    public void Cancel(Span<float> capture, ReadOnlySpan<float> render)
    {
        if (capture.Length != Frame || render.Length != Frame) throw new ArgumentException("10 ms");
        for (var i = 0; i < Frame; i++)
        {
            _capture[i] = ToShort(capture[i]);
            _render[i] = ToShort(render[i]);
        }
        speex_echo_cancellation(_state, _capture, _render, _output);
        for (var i = 0; i < Frame; i++) capture[i] = _output[i] / 32768f;
    }

    /// <summary>Forgets what it learnt about the echo (the reference moved: it is learnt again in a few seconds).</summary>
    public void Reset() => speex_echo_state_reset(_state);

    private static short ToShort(float sample) => (short)Math.Clamp(MathF.Round(sample * 32767f), short.MinValue, short.MaxValue);

    public void Dispose()
    {
        if (_state == IntPtr.Zero) return;
        speex_echo_state_destroy(_state);
        _state = IntPtr.Zero;
    }

    // SpeexDSP's echo canceller (speexdsp.dll, from the SpeexDSPSharp.Natives package).
    [DllImport(Library)] private static extern IntPtr speex_echo_state_init(int frameSize, int filterLength);
    [DllImport(Library)] private static extern int speex_echo_ctl(IntPtr state, int request, ref int value);
    [DllImport(Library)] private static extern void speex_echo_cancellation(IntPtr state, short[] capture, short[] render, short[] output);
    [DllImport(Library)] private static extern void speex_echo_state_destroy(IntPtr state);
    [DllImport(Library)] private static extern void speex_echo_state_reset(IntPtr state);
}

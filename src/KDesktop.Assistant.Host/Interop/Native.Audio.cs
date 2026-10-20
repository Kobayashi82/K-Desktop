using System.Runtime.InteropServices;

namespace KDesktop.Assistant.Host.Interop;

/// <summary>The sound output (waveOut: the default device, mixed by Windows like any application).</summary>
internal static unsafe partial class Native
{
    public const uint WAVE_MAPPER = 0xFFFFFFFF;
    public const ushort WAVE_FORMAT_PCM = 1;
    public const uint CALLBACK_EVENT = 0x00050000;
    public const uint WHDR_DONE = 0x00000001;

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    public struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WAVEHDR
    {
        public byte* lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public nint dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public nint lpNext;
        public nint reserved;
    }

    [LibraryImport("winmm.dll")]
    public static partial uint waveOutOpen(out nint device, uint deviceId, WAVEFORMATEX* format, nint callback, nint instance, uint flags);

    [LibraryImport("winmm.dll")]
    public static partial uint waveOutPrepareHeader(nint device, WAVEHDR* header, uint size);

    [LibraryImport("winmm.dll")]
    public static partial uint waveOutUnprepareHeader(nint device, WAVEHDR* header, uint size);

    [LibraryImport("winmm.dll")]
    public static partial uint waveOutWrite(nint device, WAVEHDR* header, uint size);

    [LibraryImport("winmm.dll")]
    public static partial uint waveOutReset(nint device);

    [LibraryImport("winmm.dll")]
    public static partial uint waveOutClose(nint device);
}

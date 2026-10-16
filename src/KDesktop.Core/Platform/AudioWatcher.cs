using System.Runtime.InteropServices;

namespace KDesktop.Core.Platform;

/// <summary>
/// Tells when playback devices come and go or the default one changes (Windows' endpoint notifications). Windows calls
/// in on its own threads and must not be called back from there, so <see cref="Changed"/> comes a moment later, once,
/// from a timer: look at <see cref="AudioDevices.Playback"/> then.
/// </summary>
public sealed class AudioWatcher : IDisposable
{
    private const int RenderFlow = 0, MultimediaRole = 1;

    private readonly IMMDeviceEnumerator _enumerator;
    private readonly Client _client;
    private readonly Timer _settle;
    private volatile bool _disposed;

    public event Action? Changed;

    public AudioWatcher()
    {
        _settle = new Timer(_ =>
        {
            if (_disposed) return;
            try { Changed?.Invoke(); }
            catch (Exception ex) { Log.Error("Error en un cambio de dispositivos de audio", ex); }
        }, null, Timeout.Infinite, Timeout.Infinite);
        _client = new Client(this);
        // Created and registered on a pool (MTA) thread, not on the UI one.
        _enumerator = Task.Run(() =>
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            Marshal.ThrowExceptionForHR(enumerator.RegisterEndpointNotificationCallback(_client));
            return enumerator;
        }).GetAwaiter().GetResult();
    }

    private void Poke()
    {
        if (!_disposed) _settle.Change(TimeSpan.FromMilliseconds(700), Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _enumerator.UnregisterEndpointNotificationCallback(_client); } catch { /* ignore */ }
        try { Marshal.ReleaseComObject(_enumerator); } catch { /* ignore */ }
        _settle.Dispose();
    }

    /// <summary>Public only because COM can only call public interfaces of a managed object.</summary>
    public sealed class Client(AudioWatcher owner) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, int newState) => owner.Poke();
        public void OnDeviceAdded(string deviceId) => owner.Poke();
        public void OnDeviceRemoved(string deviceId) => owner.Poke();

        public void OnDefaultDeviceChanged(int flow, int role, string? defaultDeviceId)
        {
            if (flow == RenderFlow && role == MultimediaRole) owner.Poke();
        }

        // Names, icons, formats… change all the time and say nothing about connecting.
        public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
    }

    // ── interop ──────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
    }

    [ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
        void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);
        void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        void OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);
        void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IntPtr endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom;
}

using System.Globalization;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace KDesktop.Core.Platform;

public enum AudioFormFactor { Speakers, Headphones, Headset, Monitor, Digital, Other }

/// <summary>A playback device as Windows lists it.</summary>
public sealed record AudioDeviceInfo(string Id, string Name, string Adapter, bool IsActive, bool IsDefault, bool IsDefaultCommunications, AudioFormFactor FormFactor);

/// <summary>Lists playback devices and changes the default one (the same thing the Windows sound settings do).</summary>
public static class AudioDevices
{
    /// <summary>Every playback device that is enabled (connected or not). Disabled/removed devices are left out.</summary>
    public static IReadOnlyList<AudioDeviceInfo> Playback()
    {
        var result = new List<AudioDeviceInfo>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultId = DefaultId(enumerator, Role.Multimedia);
            var commsId = DefaultId(enumerator, Role.Communications);
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active | DeviceState.Unplugged))
            {
                using (device)
                {
                    try
                    {
                        result.Add(new AudioDeviceInfo(
                            device.ID,
                            Safe(() => device.FriendlyName) ?? device.ID,
                            Safe(() => device.DeviceFriendlyName) ?? string.Empty,
                            device.State == DeviceState.Active,
                            device.ID == defaultId,
                            device.ID == commsId,
                            FormFactorOf(device)));
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("No se pudo leer un dispositivo de audio: " + ex.Message);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron enumerar los dispositivos de audio", ex);
        }
        return result.OrderByDescending(d => d.IsActive).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static string? DefaultPlaybackId()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return DefaultId(enumerator, Role.Multimedia);
        }
        catch { return null; }
    }

    /// <summary>Makes <paramref name="id"/> the default playback device (and the communications one if asked).</summary>
    public static bool SetDefault(string id, bool communications)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            try
            {
                var hr = policy.SetDefaultEndpoint(id, ERole.Console);
                if (hr == 0) hr = policy.SetDefaultEndpoint(id, ERole.Multimedia);
                if (hr == 0 && communications) hr = policy.SetDefaultEndpoint(id, ERole.Communications);
                if (hr != 0) Log.Warn($"SetDefaultEndpoint devolvió 0x{hr:X8}");
                return hr == 0;
            }
            finally
            {
                Marshal.ReleaseComObject(policy);
            }
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo cambiar el dispositivo de audio", ex);
            return false;
        }
    }

    /// <summary>
    /// Switches to the device after the current default one in <paramref name="rotation"/> (an empty list means every
    /// connected device). Devices that are not connected are skipped. Returns the device now in use, or null.
    /// </summary>
    public static AudioDeviceInfo? Next(IReadOnlyList<string> rotation, bool communications)
    {
        var all = Playback();
        var candidates = rotation.Count == 0
            ? all.Where(d => d.IsActive).ToList()
            : rotation.Select(id => all.FirstOrDefault(d => d.Id == id)).OfType<AudioDeviceInfo>().Where(d => d.IsActive).ToList();
        if (candidates.Count == 0) return null;

        var current = candidates.FindIndex(d => d.IsDefault);
        var next = candidates[(current + 1) % candidates.Count];
        if (next.IsDefault && (!communications || next.IsDefaultCommunications)) return next;
        return SetDefault(next.Id, communications) ? next : null;
    }

    private static string? DefaultId(MMDeviceEnumerator enumerator, Role role)
    {
        if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, role)) return null;
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
        return device.ID;
    }

    private static AudioFormFactor FormFactorOf(MMDevice device)
    {
        try
        {
            var props = device.Properties;
            if (!props.Contains(PropertyKeys.PKEY_AudioEndpoint_FormFactor)) return AudioFormFactor.Other;
            return Convert.ToInt32(props[PropertyKeys.PKEY_AudioEndpoint_FormFactor].Value, CultureInfo.InvariantCulture) switch
            {
                1 => AudioFormFactor.Speakers,
                3 => AudioFormFactor.Headphones,
                5 => AudioFormFactor.Headset,
                8 => AudioFormFactor.Digital,  // SPDIF
                9 => AudioFormFactor.Monitor,  // DigitalAudioDisplayDevice (HDMI / DisplayPort)
                _ => AudioFormFactor.Other,
            };
        }
        catch { return AudioFormFactor.Other; }
    }

    private static string? Safe(Func<string> get)
    {
        try { return get(); } catch { return null; }
    }

    // ── IPolicyConfig (undocumented, used by the Windows sound settings since Vista) ──

    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class PolicyConfigClient;

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int ResetDeviceFormat(IntPtr a);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility(IntPtr a, int b);
    }
}

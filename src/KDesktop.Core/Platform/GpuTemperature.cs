using System.Runtime.InteropServices;

namespace KDesktop.Core.Platform;

/// <summary>
/// The temperature of the graphics card, read often (the panels): NVIDIA's library (nvml.dll, with the driver) is opened
/// once and kept open until <see cref="Dispose"/>. Without an NVIDIA card it gives null. One instance per reader.
/// </summary>
public sealed class GpuTemperature : IDisposable
{
    private bool _open, _failed;
    private IntPtr[] _devices = [];

    /// <summary>Degrees of the hottest card; null when it can't be known.</summary>
    public int? Read()
    {
        if (!Open()) return null;
        int? hottest = null;
        foreach (var device in _devices)
            if (nvmlDeviceGetTemperature(device, 0, out var degrees) == 0) hottest = Math.Max(hottest ?? 0, (int)degrees);
        return hottest;
    }

    private bool Open()
    {
        if (_open) return true;
        if (_failed) return false;
        try
        {
            if (nvmlInit_v2() != 0 || nvmlDeviceGetCount_v2(out var count) != 0)
            {
                _failed = true;
                return false;
            }
            var devices = new List<IntPtr>();
            for (uint i = 0; i < count; i++)
                if (nvmlDeviceGetHandleByIndex_v2(i, out var device) == 0) devices.Add(device);
            _devices = [.. devices];
            _open = true;
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _failed = true;
            return false;
        }
    }

    public void Dispose()
    {
        if (!_open) return;
        _open = false;
        _devices = [];
        nvmlShutdown();
    }

    [DllImport("nvml.dll")] private static extern int nvmlInit_v2();
    [DllImport("nvml.dll")] private static extern int nvmlShutdown();
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetCount_v2(out uint count);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint degrees);
}

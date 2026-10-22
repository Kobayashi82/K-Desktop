using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using KDesktop.Core.Model;
using Microsoft.Win32;

namespace KDesktop.Core.Platform;

/// <summary>A graphics card: name, use of its 3D engine, video memory and temperature (what can be read of it).</summary>
public sealed record GpuStatus(string Name, int? Load, long? MemoryUsed, long? MemoryTotal, int? Temperature);

/// <summary>A fixed disk: letter, label, free and total bytes.</summary>
public sealed record DiskStatus(string Letter, string Label, long Free, long Total);

/// <summary>The battery (null when the PC has none).</summary>
/// <param name="Minutes">Time left on battery as Windows estimates it; null while plugged in or unknown.</param>
public sealed record BatteryStatus(int Percent, bool PluggedIn, bool Charging, int? Minutes);

/// <summary>The network: connected networks, local addresses and the traffic during the measuring.</summary>
public sealed record NetworkStatus(IReadOnlyList<string> Networks, IReadOnlyList<string> Addresses, long DownPerSecond, long UpPerSecond);

/// <summary>How the PC is doing (stage 8, <c>estado_pc</c>): what Task Manager shows, read in one go.</summary>
public sealed record PcStatus(
    string? CpuName, int Cores, int Cpu, long MemoryUsed, long MemoryTotal, IReadOnlyList<GpuStatus> Gpus,
    IReadOnlyList<DiskStatus> Disks, BatteryStatus? Battery, NetworkStatus Network, TimeSpan UpTime,
    IReadOnlyList<ProgramUsage> Programs)
{
    /// <summary>How long the processor, the graphics card, the network and the programs are measured.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(600);

    /// <summary>Reads everything: the changing values (processor, programs, network, 3D) over <paramref name="window"/>.</summary>
    public static async Task<PcStatus> MeasureAsync(TimeSpan? window = null, CancellationToken cancellationToken = default)
    {
        using var load = new SystemLoad();
        var before = ProcessUsage.Read();
        var bytesBefore = NetworkBytes();
        var cpuStart = GetSystemTimes(out var idle0, out var kernel0, out var user0);
        load.Read(LoadResource.Gpu); // the first GPU reading only starts the counters (it waits 250 ms)
        await Task.Delay(window ?? Window, cancellationToken).ConfigureAwait(false);
        var after = ProcessUsage.Read();
        var bytesAfter = NetworkBytes();
        var cpu = cpuStart && GetSystemTimes(out var idle1, out var kernel1, out var user1)
            ? Percent(idle1 - idle0, kernel1 - kernel0 + user1 - user0) : load.Read(LoadResource.Cpu);
        var gpuLoad = load.Read(LoadResource.Gpu);
        var seconds = Math.Max(0.1, (window ?? Window).TotalSeconds);

        var memory = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref memory);
        var gpus = ReadGpus(gpuLoad);
        return new PcStatus(
            ReadCpuName(), Environment.ProcessorCount, cpu,
            (long)(memory.ullTotalPhys - memory.ullAvailPhys), (long)memory.ullTotalPhys,
            gpus, ReadDisks(), ReadBattery(),
            new NetworkStatus(NetworkInfo.ConnectedNetworks(), NetworkInfo.LocalIPv4(),
                (long)((bytesAfter.Down - bytesBefore.Down) / seconds), (long)((bytesAfter.Up - bytesBefore.Up) / seconds)),
            TimeSpan.FromMilliseconds(Environment.TickCount64),
            before is not null && after is not null ? ProcessUsage.Between(before, after) : []);

        static int Percent(long idle, long total) => total <= 0 ? 0 : (int)Math.Round(Math.Clamp((total - idle) * 100.0 / total, 0, 100));
    }

    /// <summary>The processor's name as Windows shows it («AMD Ryzen 7 5800X3D 8-Core Processor»).</summary>
    public static string? ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>Fixed disks that are ready, by letter.</summary>
    public static List<DiskStatus> ReadDisks()
    {
        var disks = new List<DiskStatus>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                disks.Add(new DiskStatus(drive.Name.TrimEnd('\\'), drive.VolumeLabel, drive.AvailableFreeSpace, drive.TotalSize));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a drive that went away */ }
        }
        return disks;
    }

    /// <summary>The battery, or null without one.</summary>
    public static BatteryStatus? ReadBattery()
    {
        if (!GetSystemPowerStatus(out var status)) return null;
        // 128 = no system battery; 255 = unknown status.
        if ((status.BatteryFlag & 128) != 0 || status.BatteryFlag == 255 || status.BatteryLifePercent > 100) return null;
        var plugged = status.ACLineStatus == 1;
        return new BatteryStatus(status.BatteryLifePercent, plugged, (status.BatteryFlag & 8) != 0,
            !plugged && status.BatteryLifeTime > 0 ? status.BatteryLifeTime / 60 : null);
    }

    /// <summary>The graphics cards: NVIDIA ones through NVML (with memory and temperature), the rest from the registry.</summary>
    public static List<GpuStatus> ReadGpus(int? load = null)
    {
        var gpus = Nvml.Read();
        if (gpus.Count > 0)
        {
            // NVML gives its own use; the counter of the busiest card only fills in a missing one.
            return gpus.Select((g, i) => g.Load is null && i == 0 ? g with { Load = load } : g).ToList();
        }
        return RegistryGpus().Select((g, i) => i == 0 ? g with { Load = load } : g).ToList();
    }

    private static List<GpuStatus> RegistryGpus()
    {
        var result = new List<GpuStatus>();
        try
        {
            using var display = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (display is null) return result;
            foreach (var name in display.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsAsciiDigit)))
            {
                using var key = display.OpenSubKey(name);
                if (key?.GetValue("DriverDesc") is not string description || description.Contains("Basic", StringComparison.OrdinalIgnoreCase)) continue;
                long? memory = key.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long l when l > 0 => l,
                    byte[] { Length: 8 } b => BitConverter.ToInt64(b),
                    _ => null,
                };
                if (result.All(g => g.Name != description)) result.Add(new GpuStatus(description, null, null, memory, null));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { /* nothing to tell */ }
        return result;
    }

    /// <summary>Bytes received and sent by the adapters that are up (no loopback), since they started.</summary>
    private static (long Down, long Up) NetworkBytes()
    {
        long down = 0, up = 0;
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var statistics = adapter.GetIPStatistics();
                down += statistics.BytesReceived;
                up += statistics.BytesSent;
            }
        }
        catch (NetworkInformationException) { /* no figures */ }
        return (down, up);
    }

    /// <summary>NVIDIA's management library (nvml.dll comes with the driver): what the graphics card is doing.</summary>
    private static class Nvml
    {
        private static int _missing; // 1: there is no nvml.dll, do not try again

        public static List<GpuStatus> Read()
        {
            var result = new List<GpuStatus>();
            if (Volatile.Read(ref _missing) == 1) return result;
            try
            {
                if (nvmlInit_v2() != 0) return result;
                try
                {
                    if (nvmlDeviceGetCount_v2(out var count) != 0) return result;
                    for (uint i = 0; i < count; i++)
                    {
                        if (nvmlDeviceGetHandleByIndex_v2(i, out var device) != 0) continue;
                        var buffer = new byte[96];
                        var name = nvmlDeviceGetName(device, buffer, (uint)buffer.Length) == 0
                            ? System.Text.Encoding.UTF8.GetString(buffer, 0, Math.Max(0, Array.IndexOf(buffer, (byte)0))) : "NVIDIA";
                        int? load = nvmlDeviceGetUtilizationRates(device, out var rates) == 0 ? (int)rates.gpu : null;
                        int? temperature = nvmlDeviceGetTemperature(device, 0, out var degrees) == 0 ? (int)degrees : null;
                        var hasMemory = nvmlDeviceGetMemoryInfo(device, out var memory) == 0;
                        result.Add(new GpuStatus(name, load, hasMemory ? (long)memory.used : null, hasMemory ? (long)memory.total : null, temperature));
                    }
                }
                finally { nvmlShutdown(); }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                Volatile.Write(ref _missing, 1);
            }
            return result;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlUtilization
        {
            public uint gpu, memory;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NvmlMemory
        {
            public ulong total, free, used;
        }

        [DllImport("nvml.dll")] private static extern int nvmlInit_v2();
        [DllImport("nvml.dll")] private static extern int nvmlShutdown();
        [DllImport("nvml.dll")] private static extern int nvmlDeviceGetCount_v2(out uint count);
        [DllImport("nvml.dll")] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
        [DllImport("nvml.dll")] private static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);
        [DllImport("nvml.dll")] private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint degrees);
        [DllImport("nvml.dll")] private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization rates);
        [DllImport("nvml.dll")] private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}

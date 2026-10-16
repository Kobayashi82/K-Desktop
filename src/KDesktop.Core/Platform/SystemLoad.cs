using System.Runtime.InteropServices;
using KDesktop.Core.Model;

namespace KDesktop.Core.Platform;

/// <summary>
/// How busy the PC is, as Task Manager shows it: processor and memory in use, and the graphics card's 3D engine
/// (games, renders, AI). Each reading compares with the previous one, so it measures the time in between; one
/// instance per watcher.
/// </summary>
public sealed class SystemLoad : IDisposable
{
    private long _idle, _kernel, _user;
    private IntPtr _gpuQuery, _gpuCounter;
    private bool _gpuFailed;

    /// <summary>% of the resource in use since the previous reading (the first reading of the CPU/GPU waits a moment).</summary>
    public int Read(LoadResource resource) => resource switch
    {
        LoadResource.Cpu => Cpu(),
        LoadResource.Gpu => Gpu(),
        _ => Memory(),
    };

    /// <summary>A one-off reading (the «Si…» condition): measures over <paramref name="milliseconds"/>.</summary>
    public static int Sample(LoadResource resource, int milliseconds = 500)
    {
        using var load = new SystemLoad();
        if (resource == LoadResource.Memory) return load.Read(resource);
        load.Read(resource);
        Thread.Sleep(milliseconds);
        return load.Read(resource);
    }

    private int Cpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        long di = idle - _idle, dk = kernel - _kernel, du = user - _user;
        var first = _kernel == 0;
        (_idle, _kernel, _user) = (idle, kernel, user);
        if (first)
        {
            Thread.Sleep(250);
            return Cpu();
        }
        var total = dk + du; // kernel time includes the idle time
        return total <= 0 ? 0 : (int)Math.Round(Math.Clamp((total - di) * 100.0 / total, 0, 100));
    }

    private static int Memory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? (int)status.dwMemoryLoad : 0;
    }

    /// <summary>
    /// The busiest graphics card's 3D engine: the sum of what every process uses of it (Task Manager's «GPU» column),
    /// from the «GPU Engine» performance counters.
    /// </summary>
    private int Gpu()
    {
        if (_gpuFailed) return 0;
        var first = _gpuQuery == IntPtr.Zero;
        if (first)
        {
            if (PdhOpenQuery(null, IntPtr.Zero, out _gpuQuery) != 0 ||
                PdhAddEnglishCounter(_gpuQuery, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _gpuCounter) != 0)
            {
                _gpuFailed = true;
                Log.Warn("No se puede medir el uso de la tarjeta gráfica en este PC");
                return 0;
            }
            PdhCollectQueryData(_gpuQuery);
            Thread.Sleep(250);
        }
        if (PdhCollectQueryData(_gpuQuery) != 0) return 0;

        uint size = 0, count = 0;
        var result = PdhGetFormattedCounterArray(_gpuCounter, PDH_FMT_DOUBLE, ref size, ref count, IntPtr.Zero);
        if (result != PDH_MORE_DATA || size == 0) return 0;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(_gpuCounter, PDH_FMT_DOUBLE, ref size, ref count, buffer) != 0) return 0;
            // Instances: «pid_1234_luid_0x…_0x…_phys_0_eng_0_engtype_3D»; added per card (luid) and engine.
            var perEngine = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM_DOUBLE>();
            for (var i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM_DOUBLE>(buffer + i * itemSize);
                if (item.FmtValue.CStatus != 0) continue;
                var name = Marshal.PtrToStringUni(item.szName) ?? string.Empty;
                var luid = name.IndexOf("_luid_", StringComparison.OrdinalIgnoreCase);
                var key = luid >= 0 ? name[luid..] : name;
                perEngine[key] = perEngine.GetValueOrDefault(key) + item.FmtValue.doubleValue;
            }
            return perEngine.Count == 0 ? 0 : (int)Math.Round(Math.Clamp(perEngine.Values.Max(), 0, 100));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose()
    {
        if (_gpuQuery != IntPtr.Zero) PdhCloseQuery(_gpuQuery);
        _gpuQuery = IntPtr.Zero;
    }

    // ── P/Invoke ─────────────────────────────────────────────────────────

    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_MORE_DATA = 0x800007D2;

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
    private struct PDH_FMT_COUNTERVALUE_DOUBLE
    {
        public uint CStatus;
        public double doubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM_DOUBLE
    {
        public IntPtr szName;
        public PDH_FMT_COUNTERVALUE_DOUBLE FmtValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}

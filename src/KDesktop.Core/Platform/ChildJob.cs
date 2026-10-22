using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KDesktop.Core.Platform;

/// <summary>
/// A job object that kills the processes in it when the assistant process ends, however it ends (closed, crashed,
/// killed): a llama-server must never stay behind holding the graphics card, nor an MCP server.
/// </summary>
public static partial class ChildJob
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint KillOnJobClose = 0x2000;

    private static readonly Lazy<IntPtr> Job = new(Create);

    /// <summary>Puts the process in the job (if it cannot, it is logged: it is still stopped normally).</summary>
    /// <param name="what">What it is, for the log.</param>
    public static void Add(Process process, string what)
    {
        var job = Job.Value;
        if (job == IntPtr.Zero || !AssignProcessToJobObject(job, process.Handle))
            Log.Warn($"Asistente: {what} fuera del job ({Marshal.GetLastPInvokeError()}): si el proceso del asistente se cae, puede quedarse abierto");
    }

    private static IntPtr Create()
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;
        var info = new ExtendedLimitInformation { BasicLimitInformation = new BasicLimitInformation { LimitFlags = KillOnJobClose } };
        var size = Marshal.SizeOf<ExtendedLimitInformation>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)size)) return IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        // Never closed: the handle closes when the process ends, and that kills what is in the job.
        return job;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateJobObjectW(IntPtr attributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(IntPtr job, IntPtr process);
}

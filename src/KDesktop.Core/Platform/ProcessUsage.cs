using System.Runtime.InteropServices;
using KDesktop.Core.Input;

namespace KDesktop.Core.Platform;

/// <summary>One program in <see cref="ProcessUsage"/>: its processes added together, like Task Manager groups them.</summary>
/// <param name="Name">Normalised process name («chrome»).</param>
/// <param name="Pids">Its processes.</param>
/// <param name="Cpu">% of the whole processor (all cores = 100) between the two readings.</param>
/// <param name="Memory">Private working set in bytes (Task Manager's «Memory» column).</param>
public sealed record ProgramUsage(string Name, IReadOnlyList<uint> Pids, double Cpu, long Memory);

/// <summary>
/// What every running process uses of the processor and the memory, from one <c>NtQuerySystemInformation</c> call: it
/// sees all processes, also elevated and protected ones, without opening them (about a millisecond).
/// </summary>
public static class ProcessUsage
{
    /// <summary>One reading: per process, its name, its processor time and its private memory.</summary>
    public sealed class Reading
    {
        internal Reading(long at, Dictionary<uint, (string Name, long Time, long Memory)> processes)
        {
            At = at;
            Processes = processes;
        }

        internal long At { get; }

        internal Dictionary<uint, (string Name, long Time, long Memory)> Processes { get; }
    }

    /// <summary>The processes now (null: Windows did not answer).</summary>
    public static Reading? Read()
    {
        var size = 512 * 1024;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                var status = NtQuerySystemInformation(SystemProcessInformation, buffer, size, out var needed);
                if (status == STATUS_INFO_LENGTH_MISMATCH)
                {
                    size = Math.Max(size * 2, needed + 64 * 1024);
                    continue;
                }
                if (status != 0) return null;
                return new Reading(Environment.TickCount64 * TimeSpan.TicksPerMillisecond, Parse(buffer));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        return null;
    }

    // Offsets in SYSTEM_PROCESS_INFORMATION on x64 (the module is x64 only).
    private const int NextEntryOffset = 0, WorkingSetPrivateSize = 8, UserTime = 40, KernelTime = 48, ImageNameLength = 56, ImageNameBuffer = 64, UniqueProcessId = 80;

    private static Dictionary<uint, (string Name, long Time, long Memory)> Parse(IntPtr buffer)
    {
        var result = new Dictionary<uint, (string, long, long)>();
        var entry = buffer;
        while (true)
        {
            var pid = (uint)Marshal.ReadIntPtr(entry, UniqueProcessId);
            var length = (ushort)Marshal.ReadInt16(entry, ImageNameLength);
            var namePtr = Marshal.ReadIntPtr(entry, ImageNameBuffer);
            if (pid != 0 && length > 0 && namePtr != IntPtr.Zero)
            {
                var name = ProcessNames.Normalize(Marshal.PtrToStringUni(namePtr, length / 2));
                var time = Marshal.ReadInt64(entry, UserTime) + Marshal.ReadInt64(entry, KernelTime);
                result[pid] = (name, time, Marshal.ReadInt64(entry, WorkingSetPrivateSize));
            }
            var next = Marshal.ReadInt32(entry, NextEntryOffset);
            if (next == 0) break;
            entry += next;
        }
        return result;
    }

    /// <summary>
    /// Each program between two readings (the later one's processes; the processor only counts processes in both), grouped
    /// by name. The «System» process, interrupts and the like are left out.
    /// </summary>
    public static List<ProgramUsage> Between(Reading before, Reading after)
    {
        var elapsed = (after.At - before.At) * (double)Environment.ProcessorCount;
        var groups = new Dictionary<string, (List<uint> Pids, long Time, long Memory)>(StringComparer.Ordinal);
        foreach (var (pid, now) in after.Processes)
        {
            if (now.Name.Length == 0 || Hidden.Contains(now.Name)) continue;
            var time = before.Processes.TryGetValue(pid, out var then) && then.Name == now.Name ? Math.Max(0, now.Time - then.Time) : 0;
            if (!groups.TryGetValue(now.Name, out var group)) group = ([], 0, 0);
            group.Pids.Add(pid);
            groups[now.Name] = (group.Pids, group.Time + time, group.Memory + now.Memory);
        }
        return groups.Select(g => new ProgramUsage(g.Key, g.Value.Pids,
            elapsed > 0 ? Math.Clamp(g.Value.Time * 100.0 / elapsed, 0, 100) : 0, g.Value.Memory)).ToList();
    }

    /// <summary>Not programs the user would name: the kernel and its pieces.</summary>
    private static readonly HashSet<string> Hidden = new(StringComparer.Ordinal) { "system", "registry", "secure system", "memory compression", "interrupts" };

    private const int SystemProcessInformation = 5;
    private const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int needed);
}

using System.Diagnostics;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Tests;

/// <summary>Stage 8 (<c>estado_pc</c>): what Task Manager shows, read for real on this PC.</summary>
public class PcStatusTests
{
    [Fact]
    public async Task ThePcIsMeasured()
    {
        var status = await PcStatus.MeasureAsync(TimeSpan.FromMilliseconds(300));
        Assert.InRange(status.Cpu, 0, 100);
        Assert.True(status.MemoryTotal > 1L << 30);
        Assert.InRange(status.MemoryUsed, 1, status.MemoryTotal);
        Assert.Equal(Environment.ProcessorCount, status.Cores);
        Assert.Contains(status.Disks, d => d.Letter.Equals(Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        Assert.All(status.Disks, d => Assert.InRange(d.Free, 0, d.Total));
        Assert.True(status.UpTime > TimeSpan.Zero);
        // Every program, with this one among them, and no process counted twice.
        var own = Environment.ProcessId;
        var mine = Assert.Single(status.Programs, p => p.Pids.Contains((uint)own));
        Assert.True(mine.Memory > 0);
        Assert.Equal(status.Programs.Sum(p => p.Pids.Count), status.Programs.SelectMany(p => p.Pids).Distinct().Count());
        Assert.DoesNotContain(status.Programs, p => p.Name == "system");
        Assert.True(status.Programs.Sum(p => p.Cpu) <= 100.5);
    }

    [Fact]
    public void AProgramThatWorksIsSeenWorking()
    {
        var before = ProcessUsage.Read()!;
        var end = Environment.TickCount64 + 400;
        var spin = 0L;
        while (Environment.TickCount64 < end) spin++;
        var after = ProcessUsage.Read()!;
        var own = (uint)Environment.ProcessId;
        var mine = Assert.Single(ProcessUsage.Between(before, after), p => p.Pids.Contains(own));
        // One thread busy the whole time: about one core's share.
        Assert.True(mine.Cpu >= 100.0 / Environment.ProcessorCount * 0.5, $"{mine.Cpu:0.0}% ({spin})");
    }
}

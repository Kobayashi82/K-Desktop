using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 8: <c>pc_status</c> (figures for the model) and <c>end_process</c> (through K-Desktop).</summary>
public class PcToolsTests
{
    private static readonly PcStatus Sample = new(
        "AMD Ryzen 7 5800X3D 8-Core Processor", 16, 23, 14L << 30, 32L << 30,
        [new GpuStatus("NVIDIA GeForce RTX 4070 SUPER", 97, 9L << 30, 12L << 30, 64)],
        [new DiskStatus("C:", "", 120L << 30, 931L << 30), new DiskStatus("D:", "Datos", 2L << 40, 4L << 40)],
        null,
        new NetworkStatus(["Casa"], ["192.168.1.10"], 3L << 20, 40L << 10),
        TimeSpan.FromHours(50),
        [new ProgramUsage("fallguys_client", [1], 41.5, 3L << 30), new ProgramUsage("chrome", [2, 3], 4, 2L << 30), new ProgramUsage("idle", [4], 0.1, 1L << 20)]);

    [Fact]
    public void TheOverviewHasABitOfEverything()
    {
        var text = PcStatusTool.Describe(Sample, "overview");
        Assert.Contains("Processor: 23% in use (AMD Ryzen 7 5800X3D 8-Core Processor, 16 threads)", text, StringComparison.Ordinal);
        Assert.Contains("Memory (RAM): 14 GB of 32 GB in use (44%), 18 GB free.", text, StringComparison.Ordinal);
        Assert.Contains("NVIDIA GeForce RTX 4070 SUPER: 97% in use, video memory 9 GB of 12 GB in use, 64 °C.", text, StringComparison.Ordinal);
        Assert.Contains("C: 120 GB free of 931 GB; D: (Datos) 2 TB free of 4 TB", text, StringComparison.Ordinal);
        Assert.Contains("connected to \"Casa\"; now 3 MB/s down and 40 KB/s up; local IP 192.168.1.10.", text, StringComparison.Ordinal);
        Assert.Contains("Turned on for 2 days 2 h.", text, StringComparison.Ordinal);
        Assert.Contains("fallguys_client 41.5%", text, StringComparison.Ordinal);
        // No battery: not mentioned unless asked; a program at 0.1% is not «using the processor».
        Assert.DoesNotContain("Battery", text, StringComparison.Ordinal);
        Assert.DoesNotContain("idle 0.1%", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OnePartAlone()
    {
        Assert.Equal("Disks: C: 120 GB free of 931 GB; D: (Datos) 2 TB free of 4 TB.", PcStatusTool.Describe(Sample, "disks"));
        Assert.Equal("Battery: this PC has none (it is plugged in).", PcStatusTool.Describe(Sample, "battery"));
        var battery = Sample with { Battery = new BatteryStatus(57, false, false, 135) };
        Assert.Equal("Battery: 57%, on battery, about 2 h 15 min left.", PcStatusTool.Describe(battery, "battery"));
        Assert.StartsWith("Graphics card", PcStatusTool.Describe(Sample, "gpu"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhatItIsAskedIsWhatItLooksAt()
    {
        var tool = new PcStatusTool(_ => Task.FromResult(Sample));
        var result = await tool.RunAsync(ToolArguments.Parse("""{"about":"RAM"}"""), new ToolContext("lander"), default);
        Assert.StartsWith("Memory (RAM)", result.Text, StringComparison.Ordinal);
        Assert.Contains("Using the most memory", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Disks", result.Text, StringComparison.Ordinal);
        Assert.True(tool.ReadsOnly);
        // Nothing asked: the overview.
        Assert.Contains("Disks", (await tool.RunAsync(ToolArguments.Parse("{}"), new ToolContext(null), default)).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ok", false, "He terminado Fall Guys.")]
    [InlineData("declined", true, null)]
    public async Task EndingGoesThroughKDesktop(string status, bool failed, string? reply)
    {
        var bridge = new EndBridge(new EndMessage("r", status, [], "Fall Guys"));
        var result = await new EndProcessTool(bridge).RunAsync(ToolArguments.Parse("""{"app":"fall guys"}"""), new ToolContext("lander"), default);
        Assert.Equal("kill:fall guys:lander", bridge.Asked);
        Assert.Equal(failed, result.Failed);
        if (reply is not null) Assert.Equal(reply, result.Reply);
    }

    [Fact]
    public async Task WindowsItselfIsNotEnded()
    {
        var bridge = new EndBridge(new EndMessage("r", "failed", [], "explorer", "notAllowed"));
        var result = await new EndProcessTool(bridge).RunAsync(ToolArguments.Parse("""{"app":"explorer"}"""), new ToolContext(null), default);
        Assert.True(result.Failed);
        Assert.Contains("must not be ended", result.Text, StringComparison.Ordinal);
    }
}

/// <summary>K-Desktop for the tools that go through it: every act ends as given (none: no answer); nothing else.</summary>
internal sealed class EndBridge(EndMessage? end = null) : ICommandBridge
{
    public string? Asked { get; private set; }

    public Task<EndMessage?> ActAsync(string op, string app, int? value, string? assistant, CancellationToken cancellationToken)
    {
        Asked = $"{op}:{app}:{assistant}";
        return Task.FromResult<EndMessage?>(end);
    }

    public Task<MatchedMessage?> MatchAsync(string text, CancellationToken cancellationToken) => Task.FromResult<MatchedMessage?>(null);

    public Task<EndMessage?> RunAsync(string command, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

    public Task<EndMessage?> MediaAsync(string order, int? value, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

    public Task<DescribedMessage?> DescribeAsync(string command, CancellationToken cancellationToken) => Task.FromResult<DescribedMessage?>(null);

    public Task<EndMessage?> RunEventAsync(string name, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);
}

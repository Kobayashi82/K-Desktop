using System.Diagnostics;
using System.Reflection;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 1.3: the assistant process comes back when it dies, stays off when stopped, and a faulty assistant is unloaded.</summary>
public class SupervisorTests
{
    private static string Exe { get; } = Path.GetFullPath(typeof(SupervisorTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static WelcomeMessage Welcome() => new("es", "dark", false);

    private static string UniquePipe() => "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];

    private static Process? StartPlain(string exe, string args) =>
        Process.Start(new ProcessStartInfo(exe, args + " --allow-elevated") { UseShellExecute = false });

    private static async Task Until(Func<bool> condition, int seconds = 25)
    {
        var limit = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < limit, "No ha pasado a tiempo");
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task KillingItBringsItBackAndStoppingLeavesItOff()
    {
        await using var supervisor = new AssistantSupervisor(Welcome, StartPlain, UniquePipe(), executable: Exe);
        supervisor.Start();
        await Until(() => supervisor.IsRunning);
        var first = supervisor.ProcessId;
        Assert.NotEqual(0, first);

        Process.GetProcessById(first).Kill();
        await Until(() => supervisor.IsRunning && supervisor.ProcessId != first && supervisor.ProcessId != 0);
        var second = supervisor.ProcessId;

        await supervisor.StopAsync();
        Assert.Equal(AssistantProcessState.Off, supervisor.State);
        Assert.Equal(0, supervisor.ProcessId);
        await Task.Delay(1500);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(second)); // closed, and nobody started another
    }

    [Fact]
    public async Task ThreeFallsUnloadTheNewestAssistant()
    {
        // A process that dies at once, as if the assistant it loads made it crash.
        static Process? Crashing(string exe, string args) =>
            Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 3") { UseShellExecute = false, CreateNoWindow = true });

        await using var supervisor = new AssistantSupervisor(Welcome, Crashing, UniquePipe(), executable: Exe);
        // Two on the desktop: the one put there last is the one blamed; the other stays.
        supervisor.MarkLoaded("dot");
        supervisor.MarkLoaded("blob");
        var failed = new TaskCompletionSource<string>();
        supervisor.AssistantFailed += id => failed.TrySetResult(id);
        supervisor.Start();

        Assert.Equal("blob", await failed.Task.WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.Equal(["dot"], supervisor.LoadedAssistants);
    }

    [Fact]
    public async Task ItSendsWhatKDesktopSays()
    {
        await using var supervisor = new AssistantSupervisor(Welcome, StartPlain, UniquePipe(), executable: Exe);
        supervisor.Start();
        await Until(() => supervisor.IsRunning);
        Assert.True(supervisor.Send(new PauseMessage(true)));
        await supervisor.StopAsync();
        Assert.False(supervisor.Send(new PauseMessage(false)));
    }
}

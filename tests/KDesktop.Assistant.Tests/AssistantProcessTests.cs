using System.Diagnostics;
using System.Reflection;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// The real KDesktop.exe in assistant mode, as a process of its own, against a server in this test: they greet each
/// other, and a «bye» closes it. Its data folder is a temporary one (nothing of the user's is read or touched).
/// </summary>
public class AssistantProcessTests
{
    private static string Exe { get; } = Path.GetFullPath(typeof(AssistantProcessTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    [Fact]
    public async Task TheProcessGreetsAndLeavesOnBye()
    {
        Assert.True(File.Exists(Exe), "Falta " + Exe);
        var data = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "assistant-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(data);
        var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];

        Process? process = null;
        await using var server = new AssistantPipeServer(name, new ClientPolicy(Exe, pid => pid == process?.Id), () => new WelcomeMessage("en", "dark", false));
        var hello = new TaskCompletionSource<HelloMessage>();
        var status = new TaskCompletionSource<StatusMessage>();
        server.Connected += h => hello.TrySetResult(h);
        server.Received += m => { if (m is StatusMessage s) status.TrySetResult(s); };
        server.Start();

        process = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--pipe", name, "--data", data, "--allow-elevated"]) { UseShellExecute = false })!;
        try
        {
            var greeting = await hello.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(process.Id, greeting.Pid);
            Assert.Equal(AssistantPipe.Protocol, greeting.Protocol);
            Assert.Equal("idle", (await status.Task.WaitAsync(TimeSpan.FromSeconds(10))).State);

            Assert.True(server.Send(new ByeMessage("exit")));
            using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(exited.Token);
            Assert.Equal(0, process.ExitCode);

            var log = await File.ReadAllTextAsync(Path.Combine(data, "Logs", "assistant.log"));
            Assert.Contains("conectado con K-Desktop (idioma en, tema dark)", log);
            Assert.Contains("bye: exit", log);
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            process.Dispose();
        }
    }

    [Fact]
    public async Task WithoutKDesktopItGivesUp()
    {
        var data = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "assistant-" + Guid.NewGuid().ToString("N")[..8]);
        using var process = Process.Start(new ProcessStartInfo(Exe,
            ["--assistant", "--pipe", "KDesktop.Assistant.Test.nobody", "--data", data, "--allow-elevated"]) { UseShellExecute = false })!;
        try
        {
            using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(exited.Token);
            Assert.Contains("no ha abierto la tubería a tiempo", await File.ReadAllTextAsync(Path.Combine(data, "Logs", "assistant.log")));
        }
        finally
        {
            if (!process.HasExited) process.Kill();
        }
    }
}

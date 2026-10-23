using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 13.1 with the real KDesktop.exe in assistant mode: Pip (an assistant without code) reloaded over itself leaves
/// as reloading, saves where it stood and comes back right there. Pip shows above the taskbar for a few seconds.
/// </summary>
[Collection(OnTheDesktop.Name)]
public class EntranceTests
{
    private static string Exe { get; } = Path.GetFullPath(typeof(EntranceTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static string PipFolder => Path.GetFullPath(Path.Combine(Contracts.DocsDir, "..", "..", "samples", "CharacterAssistant", "Package"));

    [Fact]
    public async Task AReloadedAssistantComesBackWhereItWas()
    {
        var data = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "entrance-" + Guid.NewGuid().ToString("N")[..8]);
        var pipData = Path.Combine(data, "Assistants", "Data", "pip");
        var spot = Path.Combine(pipData, "spot.json");
        var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];

        Process? process = null;
        var loaded = new ConcurrentQueue<LoadedMessage>();
        await using var server = new AssistantPipeServer(name, new ClientPolicy(Exe, pid => pid == process?.Id), () => new WelcomeMessage("es", "dark", false));
        var hello = new TaskCompletionSource();
        server.Connected += _ => hello.TrySetResult();
        server.Received += m =>
        {
            if (m is LoadedMessage l) loaded.Enqueue(l);
        };
        server.Start();

        process = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--pipe", name, "--data", data, "--allow-elevated"]) { UseShellExecute = false })!;
        try
        {
            await hello.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(server.Send(Load(pipData, LoadMessage.Startup)));
            await Until(() => !loaded.IsEmpty);
            Assert.False(File.Exists(spot));

            // Reloaded over itself: the old one saves where it stood, the new one comes back there…
            Assert.True(server.Send(Load(pipData, LoadMessage.Reloaded)));
            await Until(() => loaded.Count >= 2);
            Assert.True(File.Exists(spot));
            var first = JsonSerializer.Deserialize<double[]>(File.ReadAllText(spot));
            // …so reloaded again at once it saves the same spot (Pip waits 3 s at least before wandering).
            Assert.True(server.Send(Load(pipData, LoadMessage.Reloaded)));
            await Until(() => loaded.Count >= 3);
            Assert.All(loaded, l => Assert.True(l.Ok, l.Error));
            Assert.Equal(first, JsonSerializer.Deserialize<double[]>(File.ReadAllText(spot)));

            Assert.True(server.Send(new ByeMessage("exit")));
            using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(exited.Token);
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            process.Dispose();
        }

        var log = await File.ReadAllTextAsync(Path.Combine(data, "Logs", "assistant.log"));
        Assert.Contains("«pip» cargado (1 en el escritorio, Startup)", log);
        Assert.Contains("«pip» descargado (Reloading)", log);
        Assert.Contains("«pip» cargado (1 en el escritorio, Reloaded)", log);
        Assert.Contains("«pip» descargado (Shutdown)", log);
    }

    private static LoadMessage Load(string pipData, string entrance) => new("pip", "1.0.0", PipFolder, pipData, entrance);

    private static async Task Until(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < limit, "No ha pasado a tiempo");
            await Task.Delay(100);
        }
    }
}

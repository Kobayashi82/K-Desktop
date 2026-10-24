using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 1.11 with the real KDesktop.exe in assistant mode: Dot and a second assistant (a copy of Dot with another id)
/// on the desktop at once, each with its own panel; one leaves and comes back while the other stays, the other is
/// reloaded, and closing makes both leave, each time with its reason (stage 13.1). Two dots show somewhere above the
/// taskbar for a few seconds; the data folder is a temporary one.
/// </summary>
public class SeveralAssistantsTests
{
    private static string Exe { get; } = Path.GetFullPath(typeof(SeveralAssistantsTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static string DotPackage =>
        DevLoader.FindPackage(Path.Combine(Contracts.DocsDir, "..", "..", "tests", "DotAssistant"))
        ?? throw new InvalidOperationException("Compila tests/DotAssistant primero");

    [Fact]
    public async Task TwoAssistantsLiveSideBySide()
    {
        var root = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "several-" + Guid.NewGuid().ToString("N")[..8]);
        var data = Path.Combine(root, "data");
        var dot = Copy(DotPackage, Path.Combine(root, "dot"));
        var blob = Copy(DotPackage, Path.Combine(root, "blob"), manifest =>
        {
            manifest["id"] = "blob";
            manifest["name"] = "Blob";
        });
        var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];

        Process? process = null;
        var loaded = new ConcurrentQueue<LoadedMessage>();
        var panels = new ConcurrentDictionary<string, bool>();
        await using var server = new AssistantPipeServer(name, new ClientPolicy(Exe, pid => pid == process?.Id), () => new WelcomeMessage("es", "dark", false));
        var hello = new TaskCompletionSource();
        server.Connected += _ => hello.TrySetResult();
        server.Received += m =>
        {
            if (m is LoadedMessage l) loaded.Enqueue(l);
            if (m is PanelMessage { Assistant: { } whose }) panels[whose] = true;
        };
        server.Start();

        process = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--pipe", name, "--data", data, "--allow-elevated"]) { UseShellExecute = false })!;
        try
        {
            await hello.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(server.Send(Load("dot", dot, data, LoadMessage.Startup)));
            Assert.True(server.Send(Load("blob", blob, data, LoadMessage.Startup)));
            await Until(() => loaded.Count >= 2);
            Assert.All(loaded, l => Assert.True(l.Ok, l.Error));
            Assert.Equal(["blob", "dot"], loaded.Select(l => l.Assistant).Order());
            // Each one publishes its own panel, with its id.
            await Until(() => panels.ContainsKey("dot") && panels.ContainsKey("blob"));

            // Their settings and extras reach only the one they are for.
            Assert.True(server.Send(new ConfigMessage("assistantSettings", new JsonObject { ["size"] = 40, ["color"] = "orange", ["wander"] = true }, "blob")));
            Assert.True(server.Send(new ExtraMessage("blob", "dot.jump")));

            // Dot is dismissed and summoned back; Blob stays all the time.
            Assert.True(server.Send(new UnloadMessage("dot", UnloadMessage.Dismissed)));
            Assert.True(server.Send(Load("dot", dot, data, LoadMessage.Summoned)));
            await Until(() => loaded.Count >= 3);
            Assert.True(loaded.Last().Ok);

            // Blob again over itself (a new version, another language): the old one leaves reloading, the new one is reloaded.
            Assert.True(server.Send(Load("blob", blob, data, LoadMessage.Reloaded)));
            await Until(() => loaded.Count >= 4);
            Assert.True(loaded.Last().Ok);

            Assert.True(server.Send(new ByeMessage("exit")));
            using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(exited.Token);
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            process.Dispose();
        }

        var log = await File.ReadAllTextAsync(Path.Combine(data, "Logs", "assistant.log"));
        Assert.Contains("«blob» cargado (2 en el escritorio, Startup)", log);
        Assert.Contains("«dot» descargado (Dismissed)", log);
        // Dot came back next to Blob, which never left (but to reload) until the end.
        Assert.True(log.IndexOf("«dot» cargado (2 en el escritorio, Summoned)", StringComparison.Ordinal) > log.IndexOf("«dot» descargado (Dismissed)", StringComparison.Ordinal));
        Assert.DoesNotContain("«blob» descargado (Dismissed)", log);
        Assert.True(log.IndexOf("«blob» cargado (2 en el escritorio, Reloaded)", StringComparison.Ordinal) > log.IndexOf("«blob» descargado (Reloading)", StringComparison.Ordinal));
        Assert.Contains("«blob» descargado (Shutdown)", log);
        Assert.Contains("«dot» descargado (Shutdown)", log);
        Assert.DoesNotContain("ha fallado", log);
    }

    private static LoadMessage Load(string id, string folder, string data, string entrance) =>
        new(id, "1.1.0", folder, Path.Combine(data, "Assistants", "Data", id), entrance);

    /// <summary>The words of the pipe and the SDK's reasons (stage 13.1).</summary>
    [Fact]
    public void ReasonsComeAndGoAsTheSdkSays()
    {
        Assert.Equal(Sdk.EntranceKind.Startup, Host.AssistantSession.EntranceOf(LoadMessage.Startup));
        Assert.Equal(Sdk.EntranceKind.Summoned, Host.AssistantSession.EntranceOf(LoadMessage.Summoned));
        Assert.Equal(Sdk.EntranceKind.Reloaded, Host.AssistantSession.EntranceOf(LoadMessage.Reloaded));
        Assert.Equal(Sdk.ExitKind.Dismissed, Host.AssistantSession.ExitOf(UnloadMessage.Dismissed));
        Assert.Equal(Sdk.ExitKind.Uninstalling, Host.AssistantSession.ExitOf(UnloadMessage.Uninstalling));
        Assert.Equal(Sdk.ExitKind.Reloading, Host.AssistantSession.ExitOf(UnloadMessage.Reloading));
        // K-Desktop closing or the module off: «bye»; a new process at once (language, theme, «Restart»): as reloading.
        Assert.Equal(Sdk.ExitKind.Shutdown, Host.AssistantSession.ExitOnClose("bye: exit"));
        Assert.Equal(Sdk.ExitKind.Shutdown, Host.AssistantSession.ExitOnClose("bye: disable"));
        Assert.Equal(Sdk.ExitKind.Shutdown, Host.AssistantSession.ExitOnClose("sin K-Desktop"));
        Assert.Equal(Sdk.ExitKind.Reloading, Host.AssistantSession.ExitOnClose("bye: " + ByeMessage.Restart));
    }

    /// <summary>A copy of a package folder, with its manifest changed if asked.</summary>
    private static string Copy(string from, string to, Action<JsonObject>? change = null)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        if (change is not null)
        {
            var path = Path.Combine(to, "assistant.json");
            var manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            change(manifest);
            File.WriteAllText(path, manifest.ToJsonString());
        }
        return to;
    }

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

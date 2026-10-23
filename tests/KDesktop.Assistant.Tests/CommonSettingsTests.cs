using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using KDesktop.Assistant.Host.Input;
using KDesktop.Assistant.Sdk;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 13.13: the settings every assistant has the same way (size, volume, liveliness, dragging).</summary>
[Collection(OnTheDesktop.Name)]
public class CommonSettingsTests
{
    private static string Exe { get; } = Path.GetFullPath(typeof(CommonSettingsTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static string PipFolder => Path.GetFullPath(Path.Combine(Contracts.DocsDir, "..", "..", "samples", "CharacterAssistant", "Package"));

    private static PointerEvent Drag(PointerEventKind kind, string area = "body") => new(kind, area, new Point(10, 10));

    [Fact]
    public void ADragOfItsBodyIsKeptWholeWhileItCannotBePickedUp()
    {
        var gate = new DragGate();
        // Not allowed: the whole drag of its body is kept; a click still arrives.
        Assert.True(gate.LetsThrough(Drag(PointerEventKind.Down), dragging: false));
        Assert.False(gate.LetsThrough(Drag(PointerEventKind.DragStart), dragging: false));
        Assert.False(gate.LetsThrough(Drag(PointerEventKind.DragMove), dragging: false));
        // Allowed halfway: that drag stays kept until it ends (never an end without a start).
        Assert.False(gate.LetsThrough(Drag(PointerEventKind.DragMove), dragging: true));
        Assert.False(gate.LetsThrough(Drag(PointerEventKind.DragEnd), dragging: true));
        Assert.True(gate.LetsThrough(Drag(PointerEventKind.Up), dragging: false));
        Assert.True(gate.LetsThrough(Drag(PointerEventKind.Click), dragging: false));

        // A toy (another hit area) can be dragged anyway.
        Assert.True(gate.LetsThrough(Drag(PointerEventKind.DragStart, "ball"), dragging: false));
        Assert.True(gate.LetsThrough(Drag(PointerEventKind.DragEnd, "ball"), dragging: false));

        // Allowed when it starts: it goes through whole, even if switched off halfway.
        Assert.True(gate.LetsThrough(Drag(PointerEventKind.DragStart), dragging: true));
        Assert.True(gate.LetsThrough(Drag(PointerEventKind.DragMove), dragging: false));
        Assert.True(gate.LetsThrough(Drag(PointerEventKind.DragEnd), dragging: false));
    }

    /// <summary>With the real KDesktop.exe: the common settings reach Pip on the desktop (and those it was loaded with).</summary>
    [Fact]
    public async Task TheyReachTheAssistantOnTheDesktop()
    {
        var data = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "common-" + Guid.NewGuid().ToString("N")[..8]);
        var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];

        Process? process = null;
        var loaded = new ConcurrentQueue<LoadedMessage>();
        // Pip comes already smaller; the setting of an assistant that is not there is kept for when it comes.
        var welcome = new JsonObject
        {
            ["common"] = new JsonObject
            {
                ["pip"] = new JsonObject { ["size"] = 0.75 },
                ["lander"] = new JsonObject { ["volume"] = 0.2 },
            },
        };
        await using var server = new AssistantPipeServer(name, new ClientPolicy(Exe, pid => pid == process?.Id),
            () => new WelcomeMessage("es", "dark", false, welcome));
        var hello = new TaskCompletionSource();
        server.Connected += _ => hello.TrySetResult();
        server.Received += m =>
        {
            if (m is LoadedMessage l) loaded.Enqueue(l);
        };
        server.Start();

        process = Process.Start(new ProcessStartInfo(Exe, ["--assistant", "--pipe", name, "--data", data, "--allow-elevated"]) { UseShellExecute = false })!;
        var log = Path.Combine(data, "Logs", "assistant.log");
        try
        {
            await hello.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(server.Send(new LoadMessage("pip", "1.1.0", PipFolder, Path.Combine(data, "Assistants", "Data", "pip"), LoadMessage.Startup)));
            await Until(() => !loaded.IsEmpty);
            Assert.True(loaded.Single().Ok, loaded.Single().Error);

            Assert.True(server.Send(new ConfigMessage("common", new JsonObject
            {
                ["pip"] = new JsonObject { ["size"] = 1.5, ["liveliness"] = 0, ["dragging"] = false },
            })));
            await Until(() => File.Exists(log) && Read(log).Contains("ajustes comunes de «pip»", StringComparison.Ordinal));

            Assert.True(server.Send(new ByeMessage("exit")));
            using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(exited.Token);
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            process.Dispose();
        }

        Assert.Contains("ajustes comunes de «pip»: tamaño 150 %, volumen 100 %, movido 0 %, sin cogerlo", Read(log));
        Assert.DoesNotContain("ha fallado", Read(log));
    }

    private static string Read(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return new StreamReader(stream).ReadToEnd();
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

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Sdk;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 13.14: room for everybody (<see cref="IWorld.Others"/>): no assistant stays on top of another.</summary>
[Collection(OnTheDesktop.Name)]
public class RoomTests
{
    private static string Exe { get; } = Path.GetFullPath(typeof(RoomTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static string TicFolder => TestCharacter.Folder;

    [Fact]
    public void APlaceIsTakenOnlyByOneThatStaysThere()
    {
        var world = new FakeWorld([new OtherAssistant("lander", new PixelRect(100, 100, 160, 160), Moving: false), new OtherAssistant("kobii", new PixelRect(400, 100, 460, 160), Moving: true)]);
        Assert.Equal("lander", world.TakenBy(new PixelRect(150, 150, 200, 200))?.Id);
        Assert.Null(world.TakenBy(new PixelRect(160, 100, 220, 160)));
        // Passing by does not take the place, unless asked.
        Assert.Null(world.TakenBy(new PixelRect(420, 120, 440, 140)));
        Assert.Equal("kobii", world.TakenBy(new PixelRect(420, 120, 440, 140), moving: true)?.Id);
    }

    [Fact]
    public void EachOneSeesTheOthersButNotItself()
    {
        using var world = new Host.Runtime.HostWorld(() => 1);
        var lander = new Host.Runtime.WorldView(world, "lander");
        Assert.Empty(lander.Others);
        world.Publish([new OtherAssistant("lander", new PixelRect(0, 0, 50, 50), false), new OtherAssistant("tic", new PixelRect(60, 0, 110, 50), true)]);
        Assert.Equal("tic", Assert.Single(lander.Others).Id);
        Assert.Same(lander.Others, lander.Others);
        // Nothing changed: the same list stays.
        var before = world.Others;
        world.Publish([new OtherAssistant("lander", new PixelRect(0, 0, 50, 50), false), new OtherAssistant("tic", new PixelRect(60, 0, 110, 50), true)]);
        Assert.Same(before, world.Others);
    }

    /// <summary>
    /// With the real KDesktop.exe: two Tics (Tic and a copy with another id) come back to the same spot; the one that comes
    /// second sees the first in <see cref="IWorld.Others"/> and walks aside. They show above the taskbar for a few seconds.
    /// </summary>
    [Fact]
    public async Task TheOneThatComesSecondStepsAside()
    {
        var data = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "room-" + Guid.NewGuid().ToString("N")[..8]);
        var tic2 = Path.Combine(data, "tic2");
        foreach (var file in Directory.EnumerateFiles(TicFolder, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(tic2, Path.GetRelativePath(TicFolder, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        var manifestFile = Path.Combine(tic2, "assistant.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestFile))!.AsObject();
        manifest["id"] = "tic2";
        File.WriteAllText(manifestFile, manifest.ToJsonString());
        var ticData = Path.Combine(data, "Assistants", "Data", "tic");
        var tic2Data = Path.Combine(data, "Assistants", "Data", "tic2");
        var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];

        Process? process = null;
        var loaded = new ConcurrentQueue<LoadedMessage>();
        // Neither strolls about on its own: only stepping aside moves them.
        var welcome = new JsonObject
        {
            ["common"] = new JsonObject
            {
                ["tic"] = new JsonObject { ["liveliness"] = 0 },
                ["tic2"] = new JsonObject { ["liveliness"] = 0 },
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
            Assert.True(server.Send(new LoadMessage("tic", "1.1.0", TicFolder, ticData, LoadMessage.Startup)));
            await Until(() => !loaded.IsEmpty);
            // Reloaded: it saves where it stands (and comes back there).
            Assert.True(server.Send(new LoadMessage("tic", "1.1.0", TicFolder, ticData, LoadMessage.Reloaded)));
            await Until(() => loaded.Count >= 2);
            var spot = JsonSerializer.Deserialize<double[]>(File.ReadAllText(Path.Combine(ticData, "spot.json")))!;

            // The copy comes back to that very spot, once Tic has been still there for a while.
            await Task.Delay(1000);
            Directory.CreateDirectory(tic2Data);
            File.WriteAllText(Path.Combine(tic2Data, "spot.json"), JsonSerializer.Serialize(spot));
            Assert.True(server.Send(new LoadMessage("tic2", "1.1.0", tic2, tic2Data, LoadMessage.Reloaded)));
            await Until(() => loaded.Count >= 3);
            Assert.All(loaded, l => Assert.True(l.Ok, l.Error));
            await Until(() => File.Exists(log) && Read(log).Contains("se aparta", StringComparison.Ordinal));

            // Where it ends up: away from Tic by about a body (Tic is 80 DIP high and as wide).
            await Task.Delay(2500);
            Assert.True(server.Send(new LoadMessage("tic2", "1.1.0", tic2, tic2Data, LoadMessage.Reloaded)));
            await Until(() => loaded.Count >= 4);
            var moved = JsonSerializer.Deserialize<double[]>(File.ReadAllText(Path.Combine(tic2Data, "spot.json")))!;
            Assert.Equal(spot[1], moved[1], 1);
            Assert.True(Math.Abs(moved[0] - spot[0]) > 40, $"{spot[0]} → {moved[0]}");

            Assert.True(server.Send(new ByeMessage("exit")));
            using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(exited.Token);
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            process.Dispose();
        }

        Assert.DoesNotContain("ha fallado", Read(log));
        // Tic was there first: it never had to move.
        Assert.DoesNotContain("[tic] se aparta", Read(log));
        Assert.Contains("[tic2] se aparta", Read(log));
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

    private sealed class FakeWorld(IReadOnlyList<OtherAssistant> others) : IWorld
    {
        public WorldSnapshot Current => WorldSnapshot.Empty;

        public SystemState System { get; } = new();

        public IReadOnlyList<OtherAssistant> Others => others;

        public MusicBeat Music => default;

        public event Action<WorldSnapshot>? Changed { add { } remove { } }

        public void WatchClosely(double seconds) { }

        public void ListenToMusic(bool listen) { }
    }
}

using KDesktop.Core.Engine;
using KDesktop.Core.Engine.Events;
using KDesktop.Core.Model;
using KDesktop.Core.Storage;

namespace KDesktop.Core.Tests;

public sealed class FileEventTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "files-" + Guid.NewGuid().ToString("N")[..8]);

    public FileEventTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task SettlerWaitsUntilTheFileStopsGrowing()
    {
        var path = Path.Combine(_dir, "growing.bin");
        var writing = Task.Run(async () =>
        {
            await using var f = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            for (var i = 0; i < 6; i++)
            {
                await f.WriteAsync(new byte[1000]);
                await f.FlushAsync();
                await Task.Delay(200);
            }
        });
        await Task.Delay(50);
        Assert.True(await FileSettler.WaitAsync(path, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(10), CancellationToken.None));
        Assert.True(writing.IsCompleted);
        Assert.Equal(6000, new FileInfo(path).Length);
    }

    [Fact]
    public async Task SettlerGivesUpOnAMissingFile()
    {
        Assert.False(await FileSettler.WaitAsync(Path.Combine(_dir, "nope.txt"), TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2), CancellationToken.None));
    }

    [Fact]
    public async Task NewMatchingFileRunsTheCommandsOnce()
    {
        var store = new ConfigStore();
        var command = new Command { Name = "file" };
        command.Actions.Add(new WaitAction { Seconds = 0 });
        store.Config.Commands.Add(command);
        var rule = new EventRule { Name = "png", Trigger = new FileTrigger { Folder = _dir, Filter = "*.png" } };
        rule.Commands.Add(command.Id);
        store.Config.Events.Add(rule);
        var engine = new CommandEngine(store, new FakeUi());
        var runs = 0;
        engine.Executed += (_, s) => { if (s == TriggerSource.Event) Interlocked.Increment(ref runs); };
        using var service = new EventService(engine, new FakeUi(), () => store.Config, new EventState(Path.Combine(_dir, "state.json")));
        service.Start(afterUpdate: false);

        await File.WriteAllTextAsync(Path.Combine(_dir, "ignored.txt"), "x");
        await File.WriteAllTextAsync(Path.Combine(_dir, "image.png"), "png");
        for (var i = 0; i < 40 && runs == 0; i++) await Task.Delay(100);
        await Task.Delay(1500);
        Assert.Equal(1, runs);
        Assert.NotNull(rule.LastFired);
    }

    [Fact]
    public async Task UdpCallFiresTheEvent()
    {
        var store = new ConfigStore();
        var command = new Command { Name = "door" };
        command.Actions.Add(new WaitAction { Seconds = 0 });
        store.Config.Commands.Add(command);
        var rule = new EventRule { Name = "Puerta", Trigger = new RemoteCallTrigger() };
        rule.Commands.Add(command.Id);
        store.Config.Events.Add(rule);
        var engine = new CommandEngine(store, new FakeUi());
        var runs = 0;
        engine.Executed += (_, s) => { if (s == TriggerSource.Event) Interlocked.Increment(ref runs); };
        using var service = new EventService(engine, new FakeUi(), () => store.Config, new EventState(Path.Combine(_dir, "state.json")));
        service.Start(afterUpdate: false);
        var udp = new KDesktop.Core.Remote.UdpServer(engine, new FakeUi());

        Assert.Contains("(done)", udp.Handle("evento puerta"));
        Assert.Contains("Ningún", udp.Handle("evento ventana"));
        for (var i = 0; i < 30 && runs == 0; i++) await Task.Delay(100);
        Assert.Equal(1, runs);
    }
}

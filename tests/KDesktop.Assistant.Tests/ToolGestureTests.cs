using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Assistant.Sdk;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 13.5: what the AI's tools do is seen too. A tool asks its character for a gesture (a beacon by the clock, waiting
/// for the window of what it opens, the tray…) and goes on without waiting; the host shows it with the assistant's gestures.
/// </summary>
[Collection(OnTheDesktop.Name)]
public sealed class ToolGestureTests : IDisposable
{
    private static string Exe { get; } = Path.GetFullPath(typeof(ToolGestureTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "KDesktopExe").Value!);

    private static string DotPackage =>
        DevLoader.FindPackage(Path.Combine(Contracts.Root, "tests", "DotAssistant"))
        ?? throw new InvalidOperationException("Compila tests/DotAssistant primero");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-tests", "toolgestures-" + Guid.NewGuid().ToString("N")[..8]);

    public ToolGestureTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }

    private static JsonElement Args(string json) => ToolArguments.Parse(json);

    /// <summary>A context that keeps what is shown, and when (how many things it had opened by then).</summary>
    private static ToolContext Watching(List<ToolGesture> shown) => new("lander", "x") { Show = shown.Add };

    [Fact]
    public async Task TheRunnerLetsAToolAskForAGestureAndSaysForWhom()
    {
        var registry = new ToolRegistry();
        registry.Add(new Waving());
        var runner = new ToolRunner(registry, () => new HashSet<string>(), (_, _) => Task.FromResult("yes"), _ => { }, Path.Combine(_folder, "undo.json"));
        var shown = new ConcurrentQueue<ToolShown>();
        runner.Showing += shown.Enqueue;

        var outcome = await runner.RunAsync("waving", Args("{}"), new ToolContext("tic"), "saluda", "ai", default);
        Assert.Equal("shown", outcome.Result!.Text);
        Assert.Equal(new ToolShown("tic", "waving", ToolGesture.GoTo(ToolSpot.Tray)), Assert.Single(shown));

        // Whoever listens cannot hold the tool up nor break it.
        runner.Showing += _ => throw new InvalidOperationException("roto");
        Assert.Equal("shown", (await runner.RunAsync("waving", Args("{}"), new ToolContext("tic"), "saluda", "ai", default)).Result!.Text);
    }

    [Fact]
    public async Task ATimerLeavesABeaconByTheClockWithTheTimeItRings()
    {
        using var timers = new TimerStore(Path.Combine(_folder, "timers.json"));
        var shown = new List<ToolGesture>();
        var set = await new SetTimerTool(timers).RunAsync(Args("""{"minutes":10}"""), Watching(shown), default);
        Assert.False(set.Failed);
        var mark = Assert.Single(shown);
        Assert.Equal((GestureKind.Mark, ToolSpot.Clock), (mark.Kind, mark.Where));
        Assert.Equal(timers.Pending.Single().Due.ToLocalTime().ToString("t", KDesktop.Core.Localization.Loc.Instance.Culture), mark.Text);

        shown.Clear();
        await new CancelTimerTool(timers).RunAsync(Args("{}"), Watching(shown), default);
        Assert.Equal(ToolGesture.PointAt(ToolSpot.Clock), Assert.Single(shown));

        // Nothing to remove: nothing to show.
        shown.Clear();
        await new CancelTimerTool(timers).RunAsync(Args("{}"), Watching(shown), default);
        Assert.Empty(shown);
    }

    [Fact]
    public async Task WhatOpensIsWaitedForAndAskedBeforeOpening()
    {
        var shown = new List<ToolGesture>();
        var before = -1;
        var web = new OpenWebTool(_ => { before = shown.Count; return true; });
        await web.RunAsync(Args("""{"url":"github.com"}"""), Watching(shown), default);
        Assert.Equal(ToolGesture.WaitForWindow(), Assert.Single(shown));
        Assert.Equal(1, before);

        // A made-up address is not opened: nothing to wait for.
        shown.Clear();
        await web.RunAsync(Args("""{"url":"jokes.one"}"""), new ToolContext("lander", "x", Sources: "ábreme una de chistes") { Show = shown.Add }, default);
        Assert.Empty(shown);

        var search = new SearchWebTool(_ => true);
        await search.RunAsync(Args("""{"query":"lentejas"}"""), Watching(shown), default);
        Assert.Equal(ToolGesture.WaitForWindow(), Assert.Single(shown));

        shown.Clear();
        var app = new OpenAppTool(() => Task.FromResult<IReadOnlyList<InstalledApp>>([new InstalledApp("Calculadora", "calc")]), _ => { before = shown.Count; return true; });
        await app.RunAsync(Args("""{"name":"calculadora"}"""), Watching(shown), default);
        Assert.Equal((ToolGesture.WaitForWindow(), 1), (Assert.Single(shown), before));

        // One that is not installed: nothing.
        shown.Clear();
        await app.RunAsync(Args("""{"name":"photoshop"}"""), Watching(shown), default);
        Assert.Empty(shown);
    }

    [Fact]
    public async Task TheMusicIsInTheTray()
    {
        var playing = new MediaNow("Spotify", "Spotify.exe", "Song", "Band", null, MediaState.Playing, null, null, true);
        var shown = new List<ToolGesture>();
        await new WhatsPlayingTool(_ => Task.FromResult<IReadOnlyList<MediaNow>>([playing])).RunAsync(Args("{}"), Watching(shown), default);
        Assert.Equal(ToolGesture.GoTo(ToolSpot.Tray), Assert.Single(shown));

        shown.Clear();
        await new WhatsPlayingTool(_ => Task.FromResult<IReadOnlyList<MediaNow>>([])).RunAsync(Args("{}"), Watching(shown), default);
        Assert.Empty(shown);

        await new ControlMediaTool((_, _, _) => Task.FromResult<IReadOnlyList<MediaNow>>([playing])).RunAsync(Args("""{"action":"pause"}"""), Watching(shown), default);
        Assert.Equal(ToolGesture.GoTo(ToolSpot.Tray), Assert.Single(shown));
    }

    [Fact]
    public async Task DotShowsTheTimerItSetWithoutTheOrderWaiting()
    {
        var data = Path.Combine(_folder, "data");
        var name = "KDesktop.Assistant.Test." + Guid.NewGuid().ToString("N")[..12];
        var config = new JsonObject { ["brain"] = new JsonObject { ["model"] = "none" } };
        Process? process = null;
        var said = new TaskCompletionSource();
        var loaded = new TaskCompletionSource();
        await using var server = new AssistantPipeServer(name, new ClientPolicy(Exe, pid => pid == process?.Id), () => new WelcomeMessage("es", "dark", false, config));
        var hello = new TaskCompletionSource();
        server.Connected += _ => hello.TrySetResult();
        server.Received += m =>
        {
            if (m is LoadedMessage) loaded.TrySetResult();
            if (m is ChatLineMessage { Role: "assistant", Final: true }) said.TrySetResult();
        };
        server.Start();

        var start = new ProcessStartInfo(Exe, ["--assistant", "--pipe", name, "--data", data, "--allow-elevated"]) { UseShellExecute = false };
        // Dot pretends to know every gesture, and each one takes it a moment.
        start.Environment["DOT_GESTURES"] = "commit";
        process = Process.Start(start)!;
        var log = Path.Combine(data, "Logs", "assistant.log");
        try
        {
            await hello.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(server.Send(new LoadMessage("dot", "1.2.0", DotPackage, Path.Combine(data, "Assistants", "Data", "dot"), LoadMessage.Startup)));
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));

            Assert.True(server.Send(new HeardMessage("pon un temporizador de 10 minutos", "panel", "dot")));
            await said.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var limit = DateTime.UtcNow.AddSeconds(10);
            while (!Read(log).Contains("«dot» enseña Mark por set_timer", StringComparison.Ordinal))
            {
                Assert.True(DateTime.UtcNow < limit, "Dot no ha enseñado el temporizador");
                await Task.Delay(100);
            }

            Assert.True(server.Send(new ByeMessage("exit")));
            using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(exited.Token);
        }
        finally
        {
            if (!process.HasExited) process.Kill();
            process.Dispose();
        }
    }

    private static string Read(string file)
    {
        if (!File.Exists(file)) return string.Empty;
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>A tool that only asks for a gesture.</summary>
    private sealed class Waving : ITool
    {
        public string Name => "waving";
        public string Description => "Waves.";
        public string ParametersSchemaJson => """{"type":"object","properties":{}}""";
        public IReadOnlyList<string> Examples => [];
        public ToolRisk Risk => ToolRisk.Safe;
        public string Intent(JsonElement arguments) => "saludar";

        public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
        {
            context.Show?.Invoke(ToolGesture.GoTo(ToolSpot.Tray));
            return Task.FromResult<ToolResult>("shown");
        }
    }
}

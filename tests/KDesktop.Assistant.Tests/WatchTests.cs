using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 8 (<c>avisame_cuando</c>): what is waited for, resolved when it is set, and told once when it happens.</summary>
public sealed class WatchTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-watch-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Probe _probe = new();
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 22, 18, 0, 0, TimeSpan.FromHours(2)));
    private readonly List<string> _told = [];
    private WatchStore _store;

    public WatchTests()
    {
        _store = Store();
    }

    private WatchStore Store()
    {
        var store = new WatchStore(Path.Combine(_folder, "watches.json"), _probe, _time);
        store.Fired += (item, detail) => _told.Add(WatchStore.Happened(item, detail));
        return store;
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private Task<ToolResult> NotifyAsync(string json) =>
        new NotifyWhenTool(_store).RunAsync(ToolArguments.Parse(json), new ToolContext("lander"), default);

    private static readonly OpenWindow Blender = new(10, 1, "blender", "Blender", "Blender");
    private static readonly OpenWindow YouTube = new(11, 2, "chrome", "YouTube - Google Chrome", "Google Chrome");
    private static readonly OpenWindow Mail = new(12, 2, "chrome", "Correo - Google Chrome", "Google Chrome");

    [Fact]
    public async Task AProgramClosingIsToldOnce()
    {
        _probe.Running.Add("blender");
        _probe.Open.Add(Blender);
        var set = await NotifyAsync("""{"when":"closes","what":"Blender"}""");
        Assert.False(set.Failed);
        Assert.Equal("Vale, te aviso cuando se cierre Blender.", set.Reply);
        Assert.Equal("blender", Assert.Single(_store.Pending).Process);
        _store.Check();
        Assert.Empty(_told);
        _probe.Running.Remove("blender");
        _store.Check();
        _store.Check();
        Assert.Equal(["Blender se ha cerrado."], _told);
        Assert.Empty(_store.Pending);
    }

    [Fact]
    public async Task AWindowOfAProgramIsThatWindowOnly()
    {
        _probe.Running.Add("chrome");
        _probe.Open.AddRange([YouTube, Mail]);
        await NotifyAsync("""{"when":"closes","what":"YouTube"}""");
        Assert.Equal(YouTube.Hwnd, (IntPtr)Assert.Single(_store.Pending).Window!.Value);
        _probe.Open.Remove(YouTube);
        _store.Check();
        Assert.Single(_told);
    }

    [Fact]
    public async Task WhatIsNotOpenIsNotWaitedFor()
    {
        var set = await NotifyAsync("""{"when":"closes","what":"Photoshop"}""");
        Assert.True(set.Failed);
        Assert.Equal("Photoshop no está abierto.", set.Reply);
        Assert.Empty(_store.Pending);
    }

    [Fact]
    public async Task OnlyANewWindowAppears()
    {
        var teams = new OpenWindow(20, 5, "ms-teams", "Chat | Microsoft Teams", "Microsoft Teams");
        _probe.Open.Add(teams);
        var set = await NotifyAsync("""{"when":"appears","what":"Teams"}""");
        Assert.Contains("already open", set.Text, StringComparison.Ordinal);
        _store.Check();
        Assert.Empty(_told);
        _probe.Open.Add(new OpenWindow(21, 5, "ms-teams", "Llamada | Microsoft Teams", "Microsoft Teams"));
        _store.Check();
        Assert.StartsWith("Ya ha aparecido ", Assert.Single(_told), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheProcessorBelowForAWhile()
    {
        _probe.Cpu = 85;
        var set = await NotifyAsync("""{"when":"cpu_below","percent":30}""");
        Assert.Equal("Vale, te aviso cuando el procesador baje del 30 %.", set.Reply);
        _probe.Cpu = 12;
        _store.Check();
        _time.Advance(TimeSpan.FromSeconds(10));
        // A moment up again starts the count over.
        _probe.Cpu = 60;
        _store.Check();
        _probe.Cpu = 12;
        _time.Advance(TimeSpan.FromSeconds(2));
        _store.Check();
        _time.Advance(TimeSpan.FromSeconds(14));
        _store.Check();
        Assert.Empty(_told);
        _time.Advance(TimeSpan.FromSeconds(2));
        _store.Check();
        Assert.Equal(["El procesador ya ha bajado: va al 12 %."], _told);
    }

    [Fact]
    public async Task AlreadyBelowIsSaidAndNotWatched()
    {
        _probe.Gpu = 5;
        var set = await NotifyAsync("""{"when":"gpu_below"}""");
        Assert.Equal("La gráfica ya está al 5 %.", set.Reply);
        Assert.Empty(_store.Pending);
    }

    [Fact]
    public async Task ABrowserDownloadEndsWhenItsPartialFileGoes()
    {
        _probe.PartialFiles.Add(@"D:\Downloads\juego.zip.crdownload");
        var set = await NotifyAsync("""{"when":"download"}""");
        Assert.Equal("Vale, te aviso cuando termine la descarga.", set.Reply);
        Assert.False(Assert.Single(_store.Pending).Network);
        _store.Check();
        Assert.Empty(_told);
        _probe.PartialFiles.Clear();
        _probe.NewestFile = "juego.zip";
        _store.Check();
        Assert.Equal(["Ha terminado la descarga: juego.zip."], _told);
    }

    [Fact]
    public async Task ADownloadWithoutFilesEndsWhenTheNetworkRests()
    {
        // Steam: nothing in Downloads, but the network busy.
        _probe.ReceivedBytes.Enqueue(0);
        _probe.ReceivedBytes.Enqueue(2_000_000);
        await NotifyAsync("""{"when":"download"}""");
        Assert.True(Assert.Single(_store.Pending).Network);
        _probe.ReceivedBytes.Enqueue(3_000_000);
        _store.Check();
        _time.Advance(TimeSpan.FromSeconds(2));
        _probe.ReceivedBytes.Enqueue(3_010_000);
        _store.Check();
        _time.Advance(TimeSpan.FromSeconds(17));
        _probe.ReceivedBytes.Enqueue(3_020_000);
        _store.Check();
        Assert.Empty(_told);
        _time.Advance(TimeSpan.FromSeconds(2));
        _probe.ReceivedBytes.Enqueue(3_030_000);
        _store.Check();
        Assert.Equal(["La descarga ha terminado."], _told);
    }

    [Fact]
    public async Task NoDownloadIsNoWatch()
    {
        _probe.ReceivedBytes.Enqueue(0);
        _probe.ReceivedBytes.Enqueue(1_000);
        var set = await NotifyAsync("""{"when":"download"}""");
        Assert.True(set.Failed);
        Assert.Equal("No veo ninguna descarga en marcha.", set.Reply);
    }

    [Fact]
    public async Task AFolderThatStopsChanging()
    {
        _probe.FolderStamp = "1";
        Directory.CreateDirectory(_folder);
        await NotifyAsync($$"""{"when":"folder_quiet","what":{{System.Text.Json.JsonSerializer.Serialize(_folder)}}}""");
        _store.Check();
        _time.Advance(TimeSpan.FromSeconds(20));
        _probe.FolderStamp = "2";
        _store.Check();
        _time.Advance(TimeSpan.FromSeconds(29));
        _store.Check();
        Assert.Empty(_told);
        _time.Advance(TimeSpan.FromSeconds(2));
        _store.Check();
        Assert.Equal([Path.GetFileName(_folder) + " ya no cambia."], _told);
    }

    [Fact]
    public async Task TheyOutliveARestartButNotADay()
    {
        _probe.Running.Add("blender");
        await NotifyAsync("""{"when":"closes","what":"Blender"}""");
        _store.Dispose();
        _store = Store();
        Assert.Equal("blender", Assert.Single(_store.Pending).Process);
        _time.Advance(WatchStore.MaxAge + TimeSpan.FromMinutes(1));
        _probe.Running.Clear();
        _store.Check();
        Assert.Empty(_told);
        Assert.Empty(_store.Pending);
    }

    [Fact]
    public async Task StoppingOneAndUndoingIt()
    {
        _probe.Running.UnionWith(["blender", "obs64"]);
        await NotifyAsync("""{"when":"closes","what":"Blender"}""");
        await NotifyAsync("""{"when":"closes","what":"OBS"}""");
        var stop = new StopWatchingTool(_store);
        var stopped = await stop.RunAsync(ToolArguments.Parse("""{"what":"blender"}"""), new ToolContext("lander"), default);
        Assert.Equal("Vale, ya no te aviso cuando se cierre Blender.", stopped.Reply);
        Assert.Equal("obs64", Assert.Single(_store.Pending).Process);
        Assert.True(await stop.UndoAsync(stopped.Undo!, default));
        Assert.Equal(2, _store.Pending.Count);
        var none = await stop.RunAsync(ToolArguments.Parse("""{"what":"descarga"}"""), new ToolContext("lander"), default);
        Assert.True(none.Failed);
        Assert.Contains("cuando se cierre Blender", none.Text, StringComparison.Ordinal);
    }

    private sealed class Probe : IWatchProbe
    {
        public HashSet<string> Running { get; } = new(StringComparer.Ordinal);
        public List<OpenWindow> Open { get; } = [];
        public int Cpu { get; set; }
        public int Gpu { get; set; }
        public Queue<long> ReceivedBytes { get; } = new();
        public List<string> PartialFiles { get; } = [];
        public string? FolderStamp { get; set; }
        public string? NewestFile { get; set; }

        public IReadOnlySet<string> Processes() => Running;

        public string? FindProcess(string said) => Running.FirstOrDefault(r => said.Contains(r[..3], StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<OpenWindow> Windows() => [.. Open];

        public bool WindowExists(long hwnd) => Open.Any(w => (long)w.Hwnd == hwnd);

        public int Load(LoadResource resource) => resource == LoadResource.Gpu ? Gpu : Cpu;

        private long _last;

        public long Received() => ReceivedBytes.TryDequeue(out var next) ? _last = next : _last;

        public IReadOnlyList<string> Partials(string folder) => [.. PartialFiles];

        public string? Stamp(string folder) => FolderStamp;

        public string? Newest(string folder, DateTimeOffset after) => NewestFile;
    }

    /// <summary>A clock the test moves by hand; its timers never fire (the test looks with <see cref="WatchStore.Check"/>).</summary>
    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private readonly TimeZoneInfo _zone = TimeZoneInfo.CreateCustomTimeZone("test", start.Offset, "test", "test");
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => _zone;

        public void Advance(TimeSpan by) => _now += by;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Still();

        private sealed class Still : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() { }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}

using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 3.3: permissions, activity and undo, and the built-in tools.</summary>
public sealed class ToolsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-tools-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly ManualTime _time = new(new DateTimeOffset(2026, 10, 21, 17, 30, 0, TimeSpan.FromHours(2)));
    private readonly List<LogMessage> _log = [];
    private readonly List<AskMessage> _asked = [];
    private readonly HashSet<string> _trusted = new(StringComparer.Ordinal);
    private string _answer = "yes";

    private (ToolRunner Runner, BuiltinTools Tools) Make()
    {
        var builtin = new BuiltinTools(_folder, _time);
        var registry = new ToolRegistry();
        builtin.AddTo(registry);
        registry.Add(new RiskyTool(ToolRisk.AskAlways));
        registry.Add(new RiskyTool(ToolRisk.AskOnce, "risky_once"));
        var runner = new ToolRunner(registry, () => _trusted, (ask, _) =>
        {
            lock (_asked) _asked.Add(ask);
            return Task.FromResult(_answer);
        }, m => { lock (_log) _log.Add(m); }, Path.Combine(_folder, "undo.json"));
        return (runner, builtin);
    }

    private static JsonElement Args(string json) => ToolArguments.Parse(json);

    private static readonly ToolContext Lander = new("lander");

    [Fact]
    public async Task ATimerCanBeSetRingsAndIsUndone()
    {
        var (runner, tools) = Make();
        using var _ = tools;
        var outcome = await runner.RunAsync("set_timer", Args("""{"minutes":10,"reason":"sacar la pizza"}"""), Lander, "avísame en 10 minutos", "rule", default);
        Assert.True(outcome.Ran);
        Assert.False(outcome.Result!.Failed);
        var timer = Assert.Single(tools.Timers.Pending);
        Assert.Equal(_time.GetLocalNow().AddMinutes(10), timer.Due);
        Assert.Equal("lander", timer.Assistant);
        var entry = Assert.Single(_log).Entry;
        Assert.True(entry.Ok);
        Assert.Equal("rule", entry.Route);
        Assert.NotNull(entry.Undo);
        Assert.Empty(_asked);

        // Undo takes it away, and says so in the activity.
        Assert.True(await runner.UndoAsync(entry.Undo!, default));
        Assert.Empty(tools.Timers.Pending);
        Assert.Equal(entry.Undo, _log[^1].Entry.Undid);
        Assert.False(await runner.UndoAsync(entry.Undo!, default));

        // Another one rings when its time comes.
        await runner.RunAsync("set_timer", Args("""{"time":"17:45"}"""), Lander, "", "ai", default);
        TimerItem? rang = null;
        tools.Timers.Rang += t => rang = t;
        _time.Advance(TimeSpan.FromMinutes(16));
        tools.Timers.Start();
        Assert.NotNull(rang);
        Assert.Empty(tools.Timers.Pending);
    }

    [Fact]
    public async Task ACancelledTimerComesBackWithUndo()
    {
        var (runner, tools) = Make();
        using var _ = tools;
        await runner.RunAsync("set_timer", Args("""{"minutes":30,"reason":"llamar a mamá"}"""), Lander, "", "rule", default);
        await runner.RunAsync("set_timer", Args("""{"minutes":5,"reason":"té"}"""), Lander, "", "rule", default);
        var cancel = await runner.RunAsync("cancel_timer", Args("""{"reason":"mamá"}"""), Lander, "quita lo de mamá", "rule", default);
        Assert.Equal("té", Assert.Single(tools.Timers.Pending).Text);
        Assert.True(await runner.UndoAsync(cancel.Entry.Undo!, default));
        Assert.Equal(2, tools.Timers.Pending.Count);
    }

    [Fact]
    public async Task TimersAndUndosSurviveARestart()
    {
        var (runner, tools) = Make();
        var outcome = await runner.RunAsync("set_timer", Args("""{"minutes":20}"""), Lander, "", "rule", default);
        tools.Dispose();

        var (again, restarted) = Make();
        using var _ = restarted;
        Assert.Single(restarted.Timers.Pending);
        Assert.True(await again.UndoAsync(outcome.Entry.Undo!, default));
        Assert.Empty(restarted.Timers.Pending);
    }

    [Fact]
    public async Task NotesAreTakenFoundAndUndone()
    {
        var (runner, tools) = Make();
        using var _ = tools;
        var saved = await runner.RunAsync("save_note", Args("""{"note":"Comprar pilas AA para el mando"}"""), new ToolContext(null), "apunta…", "rule", default);
        await runner.RunAsync("save_note", Args("""{"note":"Llamar al fontanero el jueves"}"""), Lander, "", "rule", default);
        var again = await runner.RunAsync("save_note", Args("""{"note":"comprar pilas aa para el mando"}"""), Lander, "", "rule", default);
        Assert.Null(again.Entry.Undo);
        Assert.Equal(2, tools.Notes.All.Count);

        var found = await runner.RunAsync("find_notes", Args("""{"search":"pilas"}"""), Lander, "", "ai", default);
        Assert.Contains("pilas", found.Result!.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fontanero", found.Result.Text, StringComparison.Ordinal);

        Assert.True(await runner.UndoAsync(saved.Entry.Undo!, default));
        Assert.Equal("Llamar al fontanero el jueves", Assert.Single(tools.Notes.All).Text);
    }

    /// <summary>🔴 asks every time and «no» or silence do nothing; 🟡 does not ask once it is trusted.</summary>
    [Fact]
    public async Task RiskyToolsAskFirst()
    {
        var (runner, tools) = Make();
        using var _ = tools;
        _answer = "no";
        var denied = await runner.RunAsync("risky", Args("{}"), Lander, "haz algo arriesgado", "ai", default);
        Assert.False(denied.Ran);
        Assert.False(denied.Entry.Ok);
        Assert.Equal("risky", Assert.Single(_asked).Tool);
        Assert.False(_asked[0].CanTrust);

        _answer = "timeout";
        Assert.False((await runner.RunAsync("risky", Args("{}"), Lander, "", "ai", default)).Ran);

        _answer = "yes";
        Assert.True((await runner.RunAsync("risky", Args("{}"), Lander, "", "ai", default)).Ran);
        Assert.Equal(3, _asked.Count);

        Assert.True((await runner.RunAsync("risky_once", Args("{}"), Lander, "", "ai", default)).Ran);
        Assert.True(_asked[^1].CanTrust);
        _trusted.Add("risky_once");
        _asked.Clear();
        Assert.True((await runner.RunAsync("risky_once", Args("{}"), Lander, "", "ai", default)).Ran);
        Assert.Empty(_asked);

        // Safe tools never ask.
        await runner.RunAsync("calculate", Args("""{"expression":"240*18/100"}"""), Lander, "", "ai", default);
        Assert.Empty(_asked);
    }

    [Fact]
    public async Task AnUnknownToolIsLoggedAsFailed()
    {
        var (runner, tools) = Make();
        using var _ = tools;
        var outcome = await runner.RunAsync("does_not_exist", Args("{}"), Lander, "x", "ai", default);
        Assert.False(outcome.Ran);
        Assert.False(Assert.Single(_log).Entry.Ok);
    }

    [Theory]
    [InlineData("240*18/100", 43.2)]
    [InlineData("2^10", 1024)]
    [InlineData("-(3+4)*2", -14)]
    [InlineData("18%240", 43.2)]
    [InlineData("18% * 240", 43.2)]
    [InlineData("240 * 18 %", 43.2)]
    [InlineData("50%", 0.5)]
    [InlineData("1,5 × 4", 6)]
    public void ArithmeticWorks(string expression, double expected)
    {
        Assert.True(Arithmetic.TryEvaluate(expression, out var value, out _));
        Assert.Equal(expected, value, 6);
    }

    [Theory]
    [InlineData("1/0")]
    [InlineData("2+")]
    [InlineData("rm -rf")]
    public void ArithmeticRefusesWhatIsNot(string expression) => Assert.False(Arithmetic.TryEvaluate(expression, out _, out _));

    [Theory]
    [InlineData("17:30", 17, 30)]
    [InlineData("17.30", 17, 30)]
    [InlineData("17h", 17, 0)]
    [InlineData("5:30 pm", 17, 30)]
    [InlineData("5 am", 5, 0)]
    public void TimesAreRead(string text, int hour, int minute)
    {
        Assert.True(TimerStore.TryParseTime(text, out var time));
        Assert.Equal(new TimeOnly(hour, minute), time);
    }

    [Fact]
    public async Task TimersInThePastOrTooFarAreRefused()
    {
        var (runner, tools) = Make();
        using var _ = tools;
        Assert.True((await runner.RunAsync("set_timer", Args("""{"minutes":0}"""), Lander, "", "ai", default)).Result!.Failed);
        Assert.True((await runner.RunAsync("set_timer", Args("""{"minutes":20000}"""), Lander, "", "ai", default)).Result!.Failed);
        Assert.True((await runner.RunAsync("set_timer", Args("{}"), Lander, "", "ai", default)).Result!.Failed);
        Assert.Empty(tools.Timers.Pending);
    }

    [Theory]
    [InlineData("github.com", "https://github.com/")]
    [InlineData("http://192.168.1.150:8080", "http://192.168.1.150:8080/")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("file:///C:/Windows", null)]
    [InlineData("hola mundo", null)]
    public void OnlyWebAddressesOpen(string text, string? expected) => Assert.Equal(expected, Browser.Address(text)?.AbsoluteUri);

    [Fact]
    public void AppsAreFoundLikeTheLauncherDoes()
    {
        IReadOnlyList<InstalledApp> apps = [new("Calculadora", "calc"), new("Spotify", "spotify"), new("Visual Studio Code", "code"), new("Steam", "steam")];
        Assert.Equal("calc", OpenAppTool.Find(apps, "calculadora")!.Id);
        Assert.Equal("code", OpenAppTool.Find(apps, "vs code")?.Id ?? OpenAppTool.Find(apps, "visual studio")!.Id);
        Assert.Equal("spotify", OpenAppTool.Find(apps, "spoti")!.Id);
        Assert.Null(OpenAppTool.Find(apps, "photoshop"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private sealed class RiskyTool(ToolRisk risk, string name = "risky") : ITool
    {
        public string Name => name;
        public string Description => "Something risky.";
        public string ParametersSchemaJson => """{"type":"object","properties":{}}""";
        public IReadOnlyList<string> Examples => [];
        public ToolRisk Risk => risk;
        public string Intent(JsonElement arguments) => "hacer algo arriesgado";
        public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken) => Task.FromResult<ToolResult>("done");
    }

    /// <summary>A clock the test moves by hand (timers are checked when the store ticks).</summary>
    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private readonly TimeZoneInfo _zone = TimeZoneInfo.CreateCustomTimeZone("test", start.Offset, "test", "test");
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();

        public override TimeZoneInfo LocalTimeZone => _zone;

        public void Advance(TimeSpan by) => _now += by;
    }
}

using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Core.Assistant;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// «Usar una herramienta» (G1): a tool run from one of the user's commands counts as allowed, except a script not approved
/// yet and what the user set to «Preguntar siempre»; run by itself (an event), what would ask is not done. And the texts of
/// the arguments become typed by the tool's schema.
/// </summary>
public sealed class ToolActionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-toolaction-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<string> _asked = [];
    private readonly List<string> _ran = [];

    public ToolActionTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    /// <summary>A tool that would change something (here it only says so).</summary>
    private sealed class Acting(string name, ToolRisk risk, List<string> ran, string? fingerprint = null) : ITool
    {
        public string Name => name;
        public string Description => "Does something.";
        public string ParametersSchemaJson => """{"type":"object","properties":{}}""";
        public IReadOnlyList<string> Examples => [];
        public ToolRisk Risk => risk;
        public string? Fingerprint => fingerprint;
        public string Intent(JsonElement arguments) => name;

        public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
        {
            lock (ran) ran.Add(name);
            return Task.FromResult<ToolResult>("Done.");
        }
    }

    private ToolRunner Make(string answer = "yes")
    {
        var registry = new ToolRegistry();
        registry.Add(new Acting("send_message", ToolRisk.AskOnce, _ran));
        registry.Add(new Acting("end_process", ToolRisk.AskAlways, _ran));
        registry.Add(new Acting("chosen_always", ToolRisk.AskOnce, _ran));
        registry.Add(new Acting("my_script", ToolRisk.AskOnce, _ran, fingerprint: "abc123"));
        registry.SetPreferences(new Dictionary<string, ToolPreference> { ["chosen_always"] = new(Risk: "askAlways") });
        // The user's policy asks the first time: a command still does not.
        return new ToolRunner(registry, () => new HashSet<string>(), (ask, _) =>
        {
            lock (_asked) _asked.Add(ask.Tool);
            return Task.FromResult(answer);
        }, _ => { }, Path.Combine(_folder, "undo.json"), () => AskPolicy.First, Path.Combine(_folder, "approved.json"));
    }

    private static Task<ToolOutcome> Run(ToolRunner runner, string tool, bool unattended) =>
        runner.RunAsync(tool, JsonDocument.Parse("{}").RootElement, new ToolContext(null), "(comando «Prueba»)",
            unattended ? ToolRunner.UnattendedActionRoute : ToolRunner.ActionRoute, default);

    [Theory]
    [InlineData("send_message", false)]
    [InlineData("end_process", false)] // always asks by itself, but the command is the user's
    [InlineData("chosen_always", true)] // the user set it to always ask
    [InlineData("my_script", true)] // a script not approved yet
    public async Task FromACommandOnlyTheAgreedOnesAsk(string tool, bool asks)
    {
        var outcome = await Run(Make(), tool, unattended: false);
        Assert.Equal("Done.", outcome.Result!.Text);
        Assert.Equal(asks, _asked.Contains(tool));
        Assert.Equal(ToolRunner.ActionRoute, outcome.Entry.Route);
    }

    [Theory]
    [InlineData("send_message", true)]
    [InlineData("end_process", true)]
    [InlineData("chosen_always", false)]
    [InlineData("my_script", false)]
    public async Task FromAnEventNobodyIsAskedAndWhatWouldAskIsNotDone(string tool, bool runs)
    {
        var outcome = await Run(Make(), tool, unattended: true);
        Assert.Empty(_asked);
        Assert.Equal(runs, outcome.Ran);
        Assert.Equal(runs, _ran.Contains(tool));
        Assert.Equal(ToolRunner.ActionRoute, outcome.Entry.Route);
        if (!runs) Assert.False(outcome.Entry.Ok);
    }

    [Fact]
    public async Task AScriptApprovedForGoodNoLongerAsks()
    {
        var runner = Make("always");
        await Run(runner, "my_script", unattended: false);
        _asked.Clear();
        var again = await Run(runner, "my_script", unattended: true);
        Assert.True(again.Ran);
        Assert.Empty(_asked);
    }

    [Fact]
    public async Task SayingNoDoesNotRunIt()
    {
        var outcome = await Run(Make("no"), "chosen_always", unattended: false);
        Assert.False(outcome.Ran);
        Assert.DoesNotContain("chosen_always", _ran);
    }

    [Fact]
    public void TextsBecomeTypedByTheSchema()
    {
        const string schema = """
            {"type":"object","properties":{
              "chat":{"type":"string"},"count":{"type":"integer"},"ratio":{"type":["number","null"]},
              "silent":{"type":"boolean"},"tags":{"type":"array"},"bad":{"type":"integer"}}}
            """;
        var written = JsonDocument.Parse("""{"chat":"12","count":"5","ratio":"0,5","silent":"sí","tags":"[\"a\",\"b\"]","bad":"cinco","other":"7"}""").RootElement;
        var typed = ToolArguments.Typed(written, schema);
        Assert.Equal(JsonValueKind.String, typed.GetProperty("chat").ValueKind);
        Assert.Equal(5, typed.GetProperty("count").GetInt64());
        Assert.Equal(0.5, typed.GetProperty("ratio").GetDouble());
        Assert.True(typed.GetProperty("silent").GetBoolean());
        Assert.Equal(2, typed.GetProperty("tags").GetArrayLength());
        Assert.Equal("cinco", typed.GetProperty("bad").GetString()); // left as it was: the tool says what is wrong
        Assert.Equal("7", typed.GetProperty("other").GetString()); // not in the schema
    }
}

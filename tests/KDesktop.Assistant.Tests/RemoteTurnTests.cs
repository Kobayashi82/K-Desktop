using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Telegram, phase 4.2–4.3: an order from another channel only uses what that person may (the rest is not run), whatever is
/// not safe asks the owner when it is someone else's, text of third parties makes whatever acts ask, the questions carry
/// the turn's key and wait long enough to answer by Telegram, and the activity says who asked for it.
/// </summary>
public sealed class RemoteTurnTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-remote-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<AskMessage> _asked = [];
    private readonly List<ActivityEntry> _activity = [];

    public RemoteTurnTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private sealed class Fake(string name, ToolRisk risk, bool readsOnly = false) : ITool
    {
        public string Name => name;
        public string Description => "Does something.";
        public string ParametersSchemaJson => """{"type":"object","properties":{}}""";
        public IReadOnlyList<string> Examples => [];
        public ToolRisk Risk => risk;
        public bool ReadsOnly => readsOnly;
        public string Intent(JsonElement arguments) => name;
        public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken) => Task.FromResult<ToolResult>("Done.");
    }

    private ToolRunner Make(string policy = AskPolicy.Never)
    {
        var registry = new ToolRegistry();
        registry.Add(new Fake("read_it", ToolRisk.Safe, readsOnly: true));
        registry.Add(new Fake("safe_act", ToolRisk.Safe));
        registry.Add(new Fake("change_it", ToolRisk.AskOnce));
        return new ToolRunner(registry, () => new HashSet<string> { "change_it" }, (ask, _) =>
        {
            lock (_asked) _asked.Add(ask);
            return Task.FromResult("yes");
        }, log => { lock (_activity) _activity.Add(log.Entry); }, Path.Combine(_folder, "undo.json"), () => policy);
    }

    private static readonly JsonElement None = JsonDocument.Parse("{}").RootElement;

    private static HeardOrigin Origin(bool owner = false, string scope = HeardOrigin.ScopeAll, IReadOnlyList<string>? tools = null, bool untrusted = false) =>
        new("k1", HeardOrigin.Telegram, "Ana", owner, scope, tools, null, untrusted, owner ? null : "telegram:2", owner ? "por Telegram" : "pedido por Ana vía Telegram", 345);

    private static async Task<ToolOutcome> Run(ToolRunner runner, string tool, HeardOrigin? origin)
    {
        using var _ = origin is null ? null : TurnOrigin.Enter(origin);
        return await runner.RunAsync(tool, None, new ToolContext("lander", "x"), "x", "ai", default);
    }

    [Theory]
    [InlineData(HeardOrigin.ScopeNone, "read_it", false)]
    [InlineData(HeardOrigin.ScopeRead, "read_it", true)]
    [InlineData(HeardOrigin.ScopeRead, "safe_act", false)]
    [InlineData(HeardOrigin.ScopeChosen, "change_it", true)]
    [InlineData(HeardOrigin.ScopeChosen, "safe_act", false)]
    [InlineData(HeardOrigin.ScopeAll, "safe_act", true)]
    public async Task OnlyWhatThatPersonMayUseRuns(string scope, string tool, bool runs)
    {
        var outcome = await Run(Make(), tool, Origin(scope: scope, tools: ["change_it"]));
        Assert.Equal(runs, outcome.Ran);
        if (!runs) Assert.False(outcome.Entry.Ok);
    }

    [Fact]
    public async Task WhatTheyGaveMayBeLookedAtEvenOnlyTalking()
    {
        // Phase 5.3: the photo someone who may only talk sent is read (vision); nothing else of the PC is.
        var registry = new ToolRegistry();
        registry.Add(new Fake(ToolRunner.ReadGivenTool, ToolRisk.Safe, readsOnly: true));
        var runner = new ToolRunner(registry, () => new HashSet<string>(), (_, _) => Task.FromResult("yes"), _ => { }, Path.Combine(_folder, "undo.json"), () => AskPolicy.Never);
        var photo = Path.Combine(_folder, "foto.jpg");
        async Task<bool> Reads(string? path, IReadOnlyList<string> given)
        {
            using var _ = TurnOrigin.Enter(Origin(scope: HeardOrigin.ScopeNone));
            var arguments = JsonDocument.Parse(path is null ? "{}" : JsonSerializer.Serialize(new { path })).RootElement;
            return (await runner.RunAsync(ToolRunner.ReadGivenTool, arguments, new ToolContext("lander", "x") { Files = given }, "x", "ai", default)).Ran;
        }
        Assert.True(await Reads(photo, [photo]));
        Assert.True(await Reads(null, [photo]));
        Assert.True(await Reads("foto.jpg", [photo]));
        Assert.False(await Reads(@"C:\Users\secreto.txt", [photo]));
        Assert.False(await Reads(photo, []));
    }

    [Fact]
    public async Task SomeoneElsesOrderAsksTheOwnerForWhatIsNotSafe()
    {
        // The user never asks and trusts change_it: for them nothing asks; for Ana (even «Como tú») it does, with the turn's key.
        var runner = Make();
        Assert.True((await Run(runner, "change_it", null)).Ran);
        Assert.True((await Run(runner, "change_it", Origin(owner: true))).Ran);
        Assert.Empty(_asked);
        Assert.True((await Run(runner, "change_it", Origin())).Ran);
        var ask = Assert.Single(_asked);
        Assert.Equal("k1", ask.Origin);
        Assert.Equal(345, ask.Seconds);
        Assert.False(ask.CanTrust);
        // What is safe or only reads does not ask.
        Assert.True((await Run(runner, "safe_act", Origin())).Ran);
        Assert.True((await Run(runner, "read_it", Origin())).Ran);
        Assert.Single(_asked);
    }

    [Fact]
    public async Task TextOfThirdPartiesMakesWhatActsAsk()
    {
        // The owner's turn with a forwarded message: what acts asks (even safe, even with «no preguntar»); reading does not.
        var runner = Make();
        Assert.True((await Run(runner, "safe_act", Origin(owner: true, untrusted: true))).Ran);
        Assert.True((await Run(runner, "read_it", Origin(owner: true, untrusted: true))).Ran);
        var ask = Assert.Single(_asked);
        Assert.Equal("safe_act", ask.Tool);
        Assert.Contains("otras personas", ask.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheActivitySaysWhoAskedForIt()
    {
        var runner = Make();
        await Run(runner, "read_it", Origin());
        await Run(runner, "read_it", null);
        Assert.Equal("pedido por Ana vía Telegram", _activity[0].By);
        Assert.Null(_activity[1].By);
    }

    [Fact]
    public void NothingIsApprovedForLaterFromTextOfThirdParties()
    {
        var context = new ToolContext("lander", "x");
        Assert.True(ScheduleTool.MayApprove(context));
        using (TurnOrigin.Enter(Origin(owner: true))) Assert.True(ScheduleTool.MayApprove(context));
        using (TurnOrigin.Enter(Origin(owner: true, untrusted: true))) Assert.False(ScheduleTool.MayApprove(context));
        using (TurnOrigin.Enter(Origin())) Assert.False(ScheduleTool.MayApprove(context));
        context.Trust.MarkOutside();
        Assert.False(ScheduleTool.MayApprove(context));
    }

    [Fact]
    public void TheTelegramToolsAskByWhoTheyWriteTo()
    {
        var send = new TelegramSendTool(new NoBridge());
        Assert.Equal(ToolRisk.AskOnce, send.RiskFor(JsonDocument.Parse("""{"text":"hola"}""").RootElement));
        Assert.Equal(ToolRisk.AskOnce, send.RiskFor(JsonDocument.Parse("""{"to":"me","text":"hola"}""").RootElement));
        Assert.Equal(ToolRisk.AskAlways, send.RiskFor(JsonDocument.Parse("""{"to":"Ana","text":"hola"}""").RootElement));
        Assert.True(new TelegramReadTool(new NoBridge()).ReadsOnly);
        Assert.False(((ITool)new TelegramSendFileTool(new NoBridge())).ReadsOnly);
    }

    [Fact]
    public async Task WithoutKDesktopNothingIsSent()
    {
        var outcome = await new TelegramSendTool(new NoBridge()).RunAsync(JsonDocument.Parse("""{"text":"hola"}""").RootElement, new ToolContext(null), default);
        Assert.True(outcome.Failed);
    }

    private sealed class NoBridge : ICommandBridge
    {
        public Task<MatchedMessage?> MatchAsync(string text, CancellationToken cancellationToken) => Task.FromResult<MatchedMessage?>(null);
        public Task<EndMessage?> RunAsync(string command, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);
        public Task<EndMessage?> MediaAsync(string order, int? value, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);
        public Task<DescribedMessage?> DescribeAsync(string command, CancellationToken cancellationToken) => Task.FromResult<DescribedMessage?>(null);
        public Task<EndMessage?> RunEventAsync(string name, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);
    }
}

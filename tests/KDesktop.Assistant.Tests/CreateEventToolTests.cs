using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>B8 (5.0.351): <c>create_event</c> sends the event to a K-Desktop of the test, saved at once, and «deshaz» removes it.</summary>
public sealed class CreateEventToolTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-event-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly BuiltinTools _builtin;
    private readonly ToolRegistry _registry = new();

    public CreateEventToolTests()
    {
        _builtin = new BuiltinTools(_folder);
        _builtin.AddTo(_registry);
    }

    public void Dispose()
    {
        _builtin.Dispose();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private sealed class Desk : ICommandBridge
    {
        public List<MakeEventMessage> Asked { get; } = [];

        public MadeEventMessage? Answer { get; set; } = new("x", true, "ev-1", "Al abrir steam: Modo juego", "Al abrir steam", ["Modo juego"], ["steam"]);

        public Task<MadeEventMessage?> MakeEventAsync(MakeEventMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request);
            return Task.FromResult(request.Op == "remove" ? new MadeEventMessage(request.Id, true, request.Remove) : Answer);
        }

        public Task<MatchedMessage?> MatchAsync(string text, CancellationToken cancellationToken) => Task.FromResult<MatchedMessage?>(null);

        public Task<EndMessage?> RunAsync(string command, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<EndMessage?> MediaAsync(string order, int? value, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<DescribedMessage?> DescribeAsync(string command, CancellationToken cancellationToken) => Task.FromResult<DescribedMessage?>(null);

        public Task<EndMessage?> RunEventAsync(string name, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);
    }

    private static readonly string[] Steam = ["Steam"];
    private static readonly string[] GameMode = ["Modo juego"];
    private static readonly string[] Teleport = ["Teletransporte"];
    private static readonly string[] Look = ["look_at_screen", "teleport"];

    [Fact]
    public async Task ItIsMadeAndSavedAndUndone()
    {
        var desk = new Desk();
        var tool = new CreateEventTool(desk, _registry);
        var arguments = Args(new { when = "program_opens", programs = Steam, commands = GameMode, only_from = "18:00", once = false });
        var result = await tool.RunAsync(arguments, new ToolContext("kobii"), default);
        Assert.True(result.Acted);
        Assert.False(result.Failed);
        var spec = desk.Asked[0].Event!;
        Assert.Equal(("add", "kobii"), (desk.Asked[0].Op, desk.Asked[0].Assistant));
        Assert.Equal(("program_opens", "18:00", false), (spec.When, spec.OnlyFrom, spec.Once));
        Assert.Equal(Steam, spec.Programs);
        Assert.Equal(GameMode, spec.Commands);
        Assert.Contains("Made and saved the event", result.Text, StringComparison.Ordinal);
        Assert.Contains("steam", result.Text, StringComparison.Ordinal);
        Assert.Contains("Al abrir steam", result.Reply, StringComparison.Ordinal);

        Assert.True(await tool.UndoAsync(result.Undo!, default));
        Assert.Equal(("remove", "ev-1"), (desk.Asked[1].Op, desk.Asked[1].Remove));
    }

    [Fact]
    public async Task TheFolderIsAFullPathAndOnlyRealToolsAreApproved()
    {
        var desk = new Desk();
        var tool = new CreateEventTool(desk, _registry);
        await tool.RunAsync(Args(new { when = "file", folder = "downloads", filter = "*.pdf", order = "avísame", tools = Look }), new ToolContext("kobii"), default);
        var spec = desk.Asked[0].Event!;
        Assert.True(Path.IsPathFullyQualified(spec.Folder!));
        Assert.True(Directory.Exists(spec.Folder));
        Assert.Equal(["look_at_screen"], spec.Tools);

        var missing = await tool.RunAsync(Args(new { when = "file", folder = @"Z:\no\existe", order = "x" }), new ToolContext(null), default);
        Assert.True(missing.Failed);
        Assert.Single(desk.Asked);
    }

    [Fact]
    public void ItAsksLikeWhatItWillDo()
    {
        var tool = new CreateEventTool(new Desk(), _registry);
        Assert.Equal(ToolRisk.Safe, tool.RiskFor(Args(new { when = "battery", order = "avísame" })));
        Assert.Equal(ToolRisk.AskOnce, tool.RiskFor(Args(new { when = "lock", commands = GameMode })));
        Assert.False(((ITool)tool).ReadsOnly);
        Assert.Contains("→", tool.Intent(Args(new { when = "program_opens", programs = Steam, commands = GameMode })), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhatKDesktopRefusesIsSaid()
    {
        var desk = new Desk { Answer = new MadeEventMessage("x", false, Reason: "nothing", Unknown: ["Teletransporte"]) };
        var tool = new CreateEventTool(desk, _registry);
        var refused = await tool.RunAsync(Args(new { when = "lock", commands = Teleport }), new ToolContext(null), default);
        Assert.True(refused.Failed);
        Assert.Contains("Teletransporte", refused.Text, StringComparison.Ordinal);
        Assert.Null(refused.Undo);

        Assert.True((await tool.RunAsync(Args(new { when = "mañana" }), new ToolContext(null), default)).Failed);
        desk.Answer = null;
        Assert.True((await tool.RunAsync(Args(new { when = "lock", order = "x" }), new ToolContext(null), default)).Failed);
    }
}

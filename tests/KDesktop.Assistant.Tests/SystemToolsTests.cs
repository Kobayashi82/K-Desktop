using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Tests;

/// <summary>B9 (5.0.352): the tools for K-Desktop's own actions send the right <c>system</c> request (to a K-Desktop of the test).</summary>
public class SystemToolsTests
{
    private static readonly string[] SafeHere = ["set_volume", "power_pc", "display", "lock_pc"];

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private sealed class Desk : ICommandBridge
    {
        public List<string> Asked { get; } = [];

        public EndMessage? End { get; set; } = new("r", "ok", [], "Volumen: 30 %");

        public Task<EndMessage?> SystemAsync(string group, string op, int? value, string text, string? assistant, CancellationToken cancellationToken)
        {
            Asked.Add($"{group}|{op}|{value}|{text}");
            return Task.FromResult(End);
        }

        public Task<MatchedMessage?> MatchAsync(string text, CancellationToken cancellationToken) => Task.FromResult<MatchedMessage?>(null);

        public Task<EndMessage?> RunAsync(string command, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<EndMessage?> MediaAsync(string order, int? value, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<DescribedMessage?> DescribeAsync(string command, CancellationToken cancellationToken) => Task.FromResult<DescribedMessage?>(null);

        public Task<EndMessage?> RunEventAsync(string name, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);
    }

    public static TheoryData<string, object, string> Requests() => new()
    {
        { "set_volume", new { action = "set", value = 30 }, "volume|set|30|" },
        { "set_volume", new { value = 30 }, "volume|set|30|" },
        { "set_volume", new { action = "mute", device = "microphone" }, "volume|mute||microphone" },
        { "audio_device", new { device = "cascos" }, "audio|output||cascos" },
        { "audio_device", new { kind = "input", device = "webcam" }, "audio|input||webcam" },
        { "display", new { action = "darker" }, "display|darker||" },
        { "display", new { projection = "clone" }, "display|projection||clone" },
        { "windows_theme", new { mode = "dark" }, "theme|dark||" },
        { "toggle_radio", new { radio = "Bluetooth", action = "off" }, "radio|off||bluetooth" },
        { "lock_pc", new { }, "power|lock||" },
        { "power_pc", new { action = "restart", seconds = 60 }, "power|restart|60|" },
        { "take_screenshot", new { area = "window" }, "screenshot|take||window" },
        { "take_screenshot", new { }, "screenshot|take||screen" },
        { "power_plan", new { plan = "alto rendimiento" }, "powerPlan|set||alto rendimiento" },
        { "power_plan", new { }, "powerPlan|next||" },
        { "keep_awake", new { action = "on", minutes = 120 }, "awake|on|120|" },
        { "keep_awake", new { action = "off" }, "awake|off||" },
    };

    private static ITool Tool(string name, Desk desk)
    {
        var registry = new ToolRegistry();
        CommandTools.AddTo(registry, desk);
        return registry.All.Single(t => t.Name == name);
    }

    [Theory]
    [MemberData(nameof(Requests))]
    public async Task EachToolAsksForItsAction(string tool, object arguments, string expected)
    {
        var desk = new Desk();
        var result = await Tool(tool, desk).RunAsync(Args(arguments), new ToolContext("lander"), default);
        Assert.Equal(expected, Assert.Single(desk.Asked));
        Assert.True(result.Acted);
        // Every request is one K-Desktop knows.
        var parts = expected.Split('|');
        Assert.Contains(parts[1], AssistantSystem.Ops[parts[0]]);
    }

    [Fact]
    public async Task WhatMakesNoSenseIsNotSent()
    {
        var desk = new Desk();
        Assert.True((await Tool("set_volume", desk).RunAsync(Args(new { action = "set" }), new ToolContext(null), default)).Failed);
        Assert.True((await Tool("power_pc", desk).RunAsync(Args(new { action = "explode" }), new ToolContext(null), default)).Failed);
        Assert.True((await Tool("display", desk).RunAsync(Args(new { action = "projection" }), new ToolContext(null), default)).Failed);
        Assert.Empty(desk.Asked);
    }

    [Fact]
    public async Task KDesktopAsksAndSaysWhy()
    {
        var desk = new Desk { End = new EndMessage("r", "declined", [], "Apagar") };
        var declined = await Tool("power_pc", desk).RunAsync(Args(new { action = "shutdown" }), new ToolContext(null), default);
        Assert.True(declined.Failed);
        Assert.Contains("said no", declined.Text, StringComparison.Ordinal);
        desk.End = new EndMessage("r", "failed", [], "turbo", "noPlan");
        var noPlan = await Tool("power_plan", desk).RunAsync(Args(new { plan = "turbo" }), new ToolContext(null), default);
        Assert.Contains("no power plan called \"turbo\"", noPlan.Text, StringComparison.Ordinal);
        // The tools never ask themselves: K-Desktop does, by the action's risk.
        Assert.All(SafeHere, name => Assert.Equal(ToolRisk.Safe, Tool(name, desk).Risk));
    }

    [Fact]
    public void TheSchemasAreJson()
    {
        var desk = new Desk();
        foreach (var name in new[] { "set_volume", "audio_device", "display", "windows_theme", "toggle_radio", "lock_pc", "power_pc", "take_screenshot", "power_plan", "keep_awake" })
        {
            using var schema = JsonDocument.Parse(Tool(name, desk).ParametersSchemaJson);
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
            Assert.NotEmpty(Tool(name, desk).Examples);
        }
    }
}

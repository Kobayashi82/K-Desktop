using System.Text.Json;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 10.2: <c>whats_playing</c> and <c>control_media</c>, with made-up sessions (the real media controls are only
/// read: a test never pauses what the user is playing).
/// </summary>
public class MediaToolsTests
{
    private static readonly MediaNow Spotify = new("Spotify", "Spotify.exe", "Bohemian Rhapsody", "Queen", "A Night at the Opera",
        MediaState.Paused, TimeSpan.FromSeconds(83), TimeSpan.FromSeconds(354), Current: false);

    private static readonly MediaNow Chrome = new("Chrome", "Chrome", "Cómo hacer pan", "Cocina fácil", null,
        MediaState.Playing, TimeSpan.FromMinutes(64), TimeSpan.FromMinutes(118), Current: true);

    private static WhatsPlayingTool Playing(params MediaNow[] now) => new(_ => Task.FromResult<IReadOnlyList<MediaNow>>(now));

    [Fact]
    public async Task WhatPlaysIsSaidAndDescribed()
    {
        var result = await Playing(Chrome, Spotify).RunAsync(ToolArguments.Parse("{}"), new ToolContext("lander"), default);
        Assert.Equal("Suena «Cómo hacer pan», de Cocina fácil, en Chrome.", result.Reply);
        Assert.Contains("Chrome: playing «Cómo hacer pan» by Cocina fácil, 1:04:00 of 1:58:00 (the media keys act on it).", result.Text, StringComparison.Ordinal);
        Assert.Contains("Spotify: paused «Bohemian Rhapsody» by Queen (album A Night at the Opera), 1:23 of 5:54.", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void PausedNothingAndUntitled()
    {
        Assert.Equal("En Spotify tienes en pausa «Bohemian Rhapsody», de Queen.", WhatsPlayingTool.Say([Spotify]));
        Assert.Equal("No está sonando nada.", WhatsPlayingTool.Say([]));
        Assert.Equal("Suena algo en Chrome, pero no dice qué.", WhatsPlayingTool.Say([Chrome with { Title = null }]));
        Assert.Equal("Suena «Cómo hacer pan» en Chrome.", WhatsPlayingTool.Say([Chrome with { Artist = null }]));
        Assert.StartsWith("Nothing is playing", WhatsPlayingTool.Describe([]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ControlSaysWhatItDidOrThatItCouldNot()
    {
        (MediaOp Op, string? App)? asked = null;
        var tool = new ControlMediaTool((op, app, _) =>
        {
            asked = (op, app);
            return Task.FromResult<IReadOnlyList<MediaNow>>(app == "aimp" ? [] : [Spotify]);
        });

        var paused = await tool.RunAsync(ToolArguments.Parse("""{"action":"pause","app":"spotify"}"""), new ToolContext("lander"), default);
        Assert.Equal((MediaOp.Pause, "spotify"), asked);
        Assert.Equal("En pausa: Spotify.", paused.Reply);
        Assert.True(paused.Acted);

        await tool.RunAsync(ToolArguments.Parse("""{"action":"skip"}"""), new ToolContext("lander"), default);
        Assert.Equal((MediaOp.Next, (string?)null), asked);

        var missing = await tool.RunAsync(ToolArguments.Parse("""{"action":"play","app":"aimp"}"""), new ToolContext("lander"), default);
        Assert.True(missing.Failed);
        Assert.Equal("No veo que aimp esté reproduciendo nada.", missing.Reply);
    }

    [Fact]
    public void TheSchemaIsValidJson()
    {
        using var schema = JsonDocument.Parse(new ControlMediaTool().ParametersSchemaJson);
        Assert.Equal(6, schema.RootElement.GetProperty("properties").GetProperty("action").GetProperty("enum").GetArrayLength());
        using var none = JsonDocument.Parse(new WhatsPlayingTool().ParametersSchemaJson);
    }

    [Theory]
    [InlineData("Lander, ¿qué suena?")]
    [InlineData("¿qué canción es esta?")]
    [InlineData("cómo se llama esta canción")]
    [InlineData("what's playing")]
    [InlineData("wie heißt dieses Lied")]
    public void WhatsPlayingIsAskedWithoutTheAi(string said) => Assert.Equal(QuickRuleKind.WhatsPlaying, QuickRules.Match(said, ["Lander"])?.Kind);

    [Fact]
    public async Task TheRealMediaControlsCanBeRead()
    {
        // Only reading (whatever the user has playing now); never controlling.
        var now = await MediaSessions.ListAsync();
        Assert.All(now, m => Assert.False(string.IsNullOrEmpty(m.App)));
    }
}

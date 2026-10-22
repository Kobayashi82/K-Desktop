using KDesktop.Assistant.Host.Runtime;
using KDesktop.Assistant.Host.World;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Stage 12: a game on the whole screen frees the graphics card; a film, the brain answering, or a model that is not
/// loaded do not.
/// </summary>
public sealed class GamesTests
{
    private sealed class Pc
    {
        public bool Enabled = true, Loaded = true, Answering, Fullscreen = true, Exclusive;
        public int? Load = 85;
        public DateTimeOffset LastActive = DateTimeOffset.UtcNow.AddMinutes(-5);
        public int Freed;

        public ModuleGames Games() => new(new GameProbe(() => Enabled, () => Loaded, () => Answering, () => LastActive,
            () => (Fullscreen, Exclusive), () => Load, _ =>
            {
                Freed++;
                return Task.CompletedTask;
            }), Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public async Task AGameThatKeepsTheCardBusyFreesItAfterAWhile()
    {
        var pc = new Pc();
        using var games = pc.Games();
        Assert.False(await games.CheckAsync());
        Assert.False(await games.CheckAsync());
        Assert.True(await games.CheckAsync());
        Assert.Equal(1, pc.Freed);
    }

    [Fact]
    public async Task AnExclusiveGameFreesItAtOnce()
    {
        var pc = new Pc { Exclusive = true, Load = null };
        using var games = pc.Games();
        Assert.True(await games.CheckAsync());
    }

    [Theory]
    [InlineData("film")]
    [InlineData("windowed")]
    [InlineData("answering")]
    [InlineData("justAnswered")]
    [InlineData("notLoaded")]
    [InlineData("off")]
    [InlineData("noReading")]
    public async Task OtherwiseItStays(string what)
    {
        var pc = new Pc();
        switch (what)
        {
            case "film": pc.Load = 12; break;
            case "windowed": pc.Fullscreen = false; break;
            case "answering": pc.Answering = true; break;
            case "justAnswered": pc.LastActive = DateTimeOffset.UtcNow.AddSeconds(-20); break;
            case "notLoaded": pc.Loaded = false; break;
            case "off": pc.Enabled = false; break;
            case "noReading": pc.Load = null; break;
        }
        using var games = pc.Games();
        for (var i = 0; i < 5; i++) Assert.False(await games.CheckAsync());
        Assert.Equal(0, pc.Freed);
    }

    [Fact]
    public async Task ABreakInTheGameStartsCountingAgain()
    {
        var pc = new Pc();
        using var games = pc.Games();
        await games.CheckAsync();
        await games.CheckAsync();
        pc.Load = 20;
        await games.CheckAsync();
        pc.Load = 90;
        Assert.False(await games.CheckAsync());
        Assert.False(await games.CheckAsync());
        Assert.True(await games.CheckAsync());
    }

    [Fact]
    public async Task TheScreenCanBeReadFromAnyThread() => _ = await Task.Run(SystemStatus.Now);
}

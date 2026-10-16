using KDesktop.Core.Engine.Events;

namespace KDesktop.Core.Tests;

public class ProcessTrackerTests
{
    private static HashSet<string> Running(params string[] names) => new(names);

    [Fact]
    public void OpensAndClosesWithTheLastInstance()
    {
        var t = new ProcessTracker();
        t.SetGroups(new Dictionary<string, string[]> { ["games"] = ["steam", "epicgameslauncher"] }, Running("explorer"));
        Assert.False(t.IsOpen("games"));

        Assert.Equal([("games", true)], t.Update(Running("explorer", "steam")));
        Assert.Empty(t.Update(Running("explorer", "steam", "epicgameslauncher"))); // already open
        Assert.Empty(t.Update(Running("explorer", "epicgameslauncher")));          // one is still open
        Assert.Equal([("games", false)], t.Update(Running("explorer")));            // the last one closed
    }

    [Fact]
    public void AlreadyOpenIsNotOpening()
    {
        var t = new ProcessTracker();
        t.SetGroups(new Dictionary<string, string[]> { ["s"] = ["steam"] }, Running("steam"));
        Assert.True(t.IsOpen("s"));
        Assert.Empty(t.Update(Running("steam")));
        Assert.Equal([("s", false)], t.Update(Running()));
    }

    [Fact]
    public void GroupsThatStayKeepTheirState()
    {
        var t = new ProcessTracker();
        t.SetGroups(new Dictionary<string, string[]> { ["s"] = ["steam"] }, Running());
        t.Update(Running("steam"));
        // Another event added: «s» stays open, the new one starts from what is running
        t.SetGroups(new Dictionary<string, string[]> { ["s"] = ["steam"], ["o"] = ["obs64"] }, Running("steam", "obs64"));
        Assert.True(t.IsOpen("s"));
        Assert.True(t.IsOpen("o"));
        Assert.Equal(new[] { "obs64", "steam" }, t.Names.Order());
    }

    [Fact]
    public void ChangedGroupStartsOver()
    {
        var t = new ProcessTracker();
        t.SetGroups(new Dictionary<string, string[]> { ["s"] = ["steam"] }, Running());
        t.Update(Running("steam"));
        t.SetGroups(new Dictionary<string, string[]> { ["s"] = ["gog"] }, Running("steam"));
        Assert.False(t.IsOpen("s"));
        Assert.Equal([("s", true)], t.Update(Running("gog")));
    }
}

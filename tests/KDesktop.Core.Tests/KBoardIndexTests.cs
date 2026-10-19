using KDesktop.Core.Input;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

public class KBoardIndexTests
{
    private static KBoardProfile Profile(string name, string to, params string[] processes)
    {
        var p = new KBoardProfile { Name = name };
        foreach (var proc in processes) p.Processes.Add(proc);
        p.Remaps.Add(new KeyRemap { From = "F1", To = to });
        return p;
    }

    [Fact]
    public void ProfileOfTheProgramWinsOverTheGeneralOneListedFirst()
    {
        var index = new KBoardIndex([Profile("general", "F2"), Profile("game", "F3", "game.exe")], [], []);
        Assert.Equal("game", index.Resolve(Modifiers.None, "F1", () => "game")?.Profile.Name);
        Assert.Equal("general", index.Resolve(Modifiers.None, "F1", () => "notepad")?.Profile.Name);
    }

    [Fact]
    public void GeneralProfileRespectsDisabledInWithoutAlwaysRun()
    {
        var profile = Profile("general", "F2");
        profile.IgnoreDisabledApps = false;
        var index = new KBoardIndex([profile], [], ["game"]);
        Assert.Null(index.Resolve(Modifiers.None, "F1", () => "game"));
    }

    [Fact]
    public void GeneralProfileWithAlwaysRunWorksInDisabledApps()
    {
        var index = new KBoardIndex([Profile("general", "F2")], [], ["game"]);
        Assert.Equal("general", index.Resolve(Modifiers.None, "F1", () => "game")?.Profile.Name);
    }
}

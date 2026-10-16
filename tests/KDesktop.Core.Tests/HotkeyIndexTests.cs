using KDesktop.Core.Input;
using KDesktop.Core.Model;

namespace KDesktop.Core.Tests;

public class HotkeyIndexTests
{
    private static Command Cmd(string name, string hotkey, params string[] processes)
    {
        var c = new Command { Name = name };
        c.Hotkeys.Add(hotkey);
        foreach (var p in processes) c.Processes.Add(p);
        return c;
    }

    private static string[] Run(HotkeyIndex index, string process) =>
        index.Resolve(Hotkey.Normalize("Mouse 5"), process, out _).Select(c => c.Name).Order().ToArray();

    [Fact]
    public void SpecificWinsInItsProcess()
    {
        var index = new HotkeyIndex([Cmd("general", "Mouse 5"), Cmd("photoshop", "Mouse 5", "Photoshop.exe")], new AppSettings());
        Assert.Equal(["photoshop"], Run(index, "photoshop"));
        Assert.Equal(["general"], Run(index, "notepad"));
    }

    [Fact]
    public void TwoGeneralBothRun()
    {
        var index = new HotkeyIndex([Cmd("a", "Mouse 5"), Cmd("b", "Mouse 5")], new AppSettings());
        Assert.Equal(["a", "b"], Run(index, "notepad"));
    }

    [Fact]
    public void TwoSpecificOfTheSameProcessBothRun()
    {
        var index = new HotkeyIndex([Cmd("g", "Mouse 5"), Cmd("a", "Mouse 5", "photoshop"), Cmd("b", "Mouse 5", "photoshop", "gimp")], new AppSettings());
        Assert.Equal(["a", "b"], Run(index, "photoshop"));
        Assert.Equal(["b"], Run(index, "gimp"));
        Assert.Equal(["g"], Run(index, "word"));
    }

    [Fact]
    public void BlockComesFromTheCommandsThatRun()
    {
        var general = Cmd("general", "Mouse 5");
        var specific = Cmd("photoshop", "Mouse 5", "photoshop");
        specific.BlockKey = false;
        var index = new HotkeyIndex([general, specific], new AppSettings());
        index.Resolve(Hotkey.Normalize("Mouse 5"), "photoshop", out var block);
        Assert.False(block);
        index.Resolve(Hotkey.Normalize("Mouse 5"), "notepad", out block);
        Assert.True(block);
    }

    [Fact]
    public void SpecificSkippedByDisabledInDoesNotHideGeneral()
    {
        var general = Cmd("general", "Mouse 5");
        general.IgnoreDisabledApps = true;
        var settings = new AppSettings();
        settings.DisabledIn.Add("photoshop");
        var index = new HotkeyIndex([general, Cmd("photoshop", "Mouse 5", "photoshop")], settings);
        Assert.Equal(["general"], Run(index, "photoshop"));
    }
}

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

    [Fact]
    public void VoiceAndLauncherRespectDisabledInUnlessAskedNot()
    {
        var settings = new AppSettings { VoiceHotkey = "Scroll", LauncherHotkey = "ALT + Space" };
        settings.DisabledIn.Add("game");
        var voice = Hotkey.Normalize("Scroll");
        var launcher = Hotkey.Normalize("ALT + Space");

        var index = new HotkeyIndex([], settings);
        Assert.True(index.IsVoiceHotkey(voice, () => "notepad"));
        Assert.False(index.IsVoiceHotkey(voice, () => "game"));
        Assert.True(index.IsLauncherHotkey(launcher, () => "notepad"));
        Assert.False(index.IsLauncherHotkey(launcher, () => "game"));

        settings.VoiceIgnoreDisabledApps = settings.LauncherIgnoreDisabledApps = true;
        index = new HotkeyIndex([], settings);
        Assert.True(index.IsVoiceHotkey(voice, () => "game"));
        Assert.True(index.IsLauncherHotkey(launcher, () => "game"));
        Assert.False(new HotkeyIndex([], settings, paused: true).IsLauncherHotkey(launcher, () => "notepad"));

        Assert.True(index.VoiceBlock && index.LauncherBlock); // swallowed unless asked
        settings.LauncherBlockKey = false;
        Assert.False(new HotkeyIndex([], settings).LauncherBlock);
    }

    [Fact]
    public void EachAssistantHasItsKeyToTalkWhileItIsOnTheDesktop()
    {
        var settings = new AppSettings { VoiceHotkey = "Scroll", AssistantTalkHotkeys = new() { ["lander"] = "Pause", ["dot"] = "F8" } };
        settings.AssistantsOnDesktop.Add("lander");
        var talk = Hotkey.Normalize("Pause");
        Assert.False(new HotkeyIndex([], settings).IsTalkKey(talk, () => "notepad", out _));

        settings.AssistantEnabled = true;
        var index = new HotkeyIndex([], settings);
        Assert.True(index.IsTalkKey(talk, () => "notepad", out var who));
        Assert.Equal("lander", who);
        // K-Desktop's voice key: its commands.
        Assert.True(index.IsTalkKey(Hotkey.Normalize("Scroll"), () => "notepad", out who));
        Assert.Null(who);
        // Dot is hidden: its key is nobody's now.
        Assert.False(index.IsTalkKey(Hotkey.Normalize("F8"), () => "notepad", out _));
        Assert.False(index.IsTalkKey(Hotkey.Normalize("F9"), () => "notepad", out _));
        Assert.False(new HotkeyIndex([], settings, paused: true).IsTalkKey(talk, () => "notepad", out _));
        Assert.Contains("Pause", settings.OwnHotkeys);
    }
}

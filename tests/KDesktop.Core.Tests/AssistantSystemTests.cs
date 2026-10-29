using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Tests;

/// <summary>B9 (5.0.352): K-Desktop's own actions for the assistants' tools, one action of the catalogue each, with its risk.</summary>
public class AssistantSystemTests
{
    private static readonly AudioDeviceInfo[] Speakers =
    [
        new("{a}", "Altavoces", "Realtek High Definition Audio", true, true, true, AudioFormFactor.Speakers),
        new("{b}", "Auriculares", "Arctis 7 Game", true, false, false, AudioFormFactor.Headphones),
        new("{c}", "LG TV", "NVIDIA High Definition Audio", false, false, false, AudioFormFactor.Speakers),
    ];

    private static readonly PowerPlan[] Plans =
    [
        new(Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e"), "Equilibrado", true),
        new(Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), "Alto rendimiento", false),
    ];

    private static AssistantSystem.Plan Build(string group, string op, int? value = null, string text = "") =>
        AssistantSystem.Build(new SystemMessage("lander", "r-1", group, op, value, text), () => Speakers, () => [], () => Plans);

    private static T Action<T>(AssistantSystem.Plan plan) where T : CommandAction => Assert.IsType<T>(Assert.Single(plan.Command!.Actions));

    [Fact]
    public void Volume()
    {
        var set = Action<VolumeAction>(Build("volume", "set", 30));
        Assert.Equal((VolumeTarget.System, VolumeMode.Set, 30), (set.Target, set.Mode, set.Value));
        var up = Action<VolumeAction>(Build("volume", "up"));
        Assert.Equal((VolumeMode.Up, AssistantSystem.Step), (up.Mode, up.Value));
        Assert.Equal((VolumeTarget.Microphone, VolumeMode.Mute), (Action<VolumeAction>(Build("volume", "mute", text: "microphone")).Target, Action<VolumeAction>(Build("volume", "mute")).Mode));
    }

    [Fact]
    public void AudioDevicesByTheirName()
    {
        var headphones = Action<AudioOutputAction>(Build("audio", "output", text: "auriculares"));
        Assert.Equal("{b}", Assert.Single(headphones.Devices).Id);
        // By the adapter too; one that is not connected is not chosen.
        Assert.Equal("{b}", Assert.Single(Action<AudioOutputAction>(Build("audio", "output", text: "arctis")).Devices).Id);
        Assert.Equal("noDevice", Build("audio", "output", text: "lg tv").Reason);
        // Nothing said: the next one.
        Assert.Empty(Action<AudioOutputAction>(Build("audio", "output")).Devices);
        Assert.IsType<AudioInputAction>(Assert.Single(Build("audio", "input").Command!.Actions));
    }

    [Fact]
    public void ScreenThemeRadioAndMore()
    {
        var darker = Action<DisplayAction>(Build("display", "darker", 20));
        Assert.Equal((DisplayOperation.Brightness, LevelChange.Down, 20), (darker.Operation, darker.BrightnessMode, darker.Brightness));
        var clone = Action<DisplayAction>(Build("display", "projection", text: "clone"));
        Assert.Equal((DisplayOperation.Projection, ProjectionMode.Clone), (clone.Operation, clone.Projection));
        Assert.Equal(ThemeChoice.Dark, Action<WindowsThemeAction>(Build("theme", "dark")).Mode);
        var bluetooth = Action<RadioAction>(Build("radio", "off", text: "bluetooth"));
        Assert.Equal((RadioDevice.Bluetooth, SwitchMode.Off), (bluetooth.Radio, bluetooth.Mode));
        Assert.Equal(CaptureArea.ActiveWindow, Action<ScreenshotAction>(Build("screenshot", "take", text: "window")).Area);
        var awake = Action<KeepAwakeAction>(Build("awake", "on", 120));
        Assert.Equal((SwitchMode.On, AwakeFor.Minutes, 120), (awake.Mode, awake.For, awake.Minutes));
        Assert.Equal(AwakeFor.UntilOff, Action<KeepAwakeAction>(Build("awake", "on")).For);
    }

    [Fact]
    public void PowerPlansByTheirName()
    {
        var high = Action<PowerPlanAction>(Build("powerPlan", "set", text: "alto rendimiento"));
        Assert.Equal(Plans[1].Id.ToString(), high.PlanId);
        Assert.Equal(PowerPlanMode.Cycle, Action<PowerPlanAction>(Build("powerPlan", "next")).Mode);
        Assert.Equal("noPlan", Build("powerPlan", "set", text: "turbo").Reason);
    }

    [Fact]
    public void PowerAndItsRisk()
    {
        var locking = Action<PowerAction>(Build("power", "lock"));
        Assert.Equal((PowerMode.Lock, 0), (locking.Mode, locking.DelaySeconds));
        // Locking loses nothing: never asked. Shutting down always is (by the user's «ask before»), with a countdown.
        Assert.Equal(ToolRisk.Safe, ActionRisk.Of(locking));
        var shutdown = Action<PowerAction>(Build("power", "shutdown"));
        Assert.Equal((PowerMode.Shutdown, AssistantSystem.PowerDelay), (shutdown.Mode, shutdown.DelaySeconds));
        Assert.Equal(ToolRisk.AskAlways, ActionRisk.Of(shutdown));
        Assert.Equal(PowerMode.LogOff, Action<PowerAction>(Build("power", "sign_out")).Mode);
        Assert.Equal(ToolRisk.AskOnce, ActionRisk.Of(Action<DisplayAction>(Build("display", "off"))));
    }

    [Fact]
    public void OnlyWhatEachGroupDoes()
    {
        Assert.Equal("op", Build("volume", "explode").Reason);
        Assert.Equal("op", Build("script", "run").Reason);
        Assert.Equal("op", Build("power", "set").Reason);
        // The command is named by what it does, for the history and the question.
        var plan = Build("theme", "light");
        Assert.Equal(plan.Name, plan.Command!.Name);
    }
}

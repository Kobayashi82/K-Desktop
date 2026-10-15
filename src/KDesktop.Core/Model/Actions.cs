using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Model;

public enum ActionKind
{
    RunApp, OpenFolder, OpenUrl, AppControl, RunScript,
    SendInput, Volume, PlaySound,
    TopMost, HiddenFiles, ToggleDisabledIn, ShowMessage, Power,
    OpenQuickMenu, CommandControl, Wait,
}

public enum WindowMode { Normal, Minimized, Maximized, Hidden }
public enum ProcessPriorityLevel { Idle, BelowNormal, Normal, AboveNormal, High, RealTime }
public enum AppControlMode { Activate, Close, WaitFor }
public enum WaitCondition { Activated, Opened, Closed }
public enum SendInputMode { Keys, Text, MouseMoveTo, MouseMoveBy }
public enum VolumeMode { Set, Up, Down, ToggleMute }
public enum PowerMode { Shutdown, Restart, LogOff, Lock, Sleep, Hibernate }
public enum CommandControlMode { Run, Enable, Disable, Toggle }
public enum ScriptShell { Cmd, PowerShell }

/// <summary>Base class of every action a command can perform.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RunAppAction), "runApp")]
[JsonDerivedType(typeof(OpenFolderAction), "openFolder")]
[JsonDerivedType(typeof(OpenUrlAction), "openUrl")]
[JsonDerivedType(typeof(AppControlAction), "appControl")]
[JsonDerivedType(typeof(RunScriptAction), "runScript")]
[JsonDerivedType(typeof(SendInputAction), "sendInput")]
[JsonDerivedType(typeof(VolumeAction), "volume")]
[JsonDerivedType(typeof(PlaySoundAction), "playSound")]
[JsonDerivedType(typeof(TopMostAction), "topMost")]
[JsonDerivedType(typeof(HiddenFilesAction), "hiddenFiles")]
[JsonDerivedType(typeof(ToggleDisabledInAction), "toggleDisabledIn")]
[JsonDerivedType(typeof(ShowMessageAction), "showMessage")]
[JsonDerivedType(typeof(PowerAction), "power")]
[JsonDerivedType(typeof(OpenQuickMenuAction), "quickMenu")]
[JsonDerivedType(typeof(CommandControlAction), "commandControl")]
[JsonDerivedType(typeof(WaitAction), "wait")]
public abstract partial class CommandAction : ObservableObject
{
    [ObservableProperty] private bool _enabled = true;

    [JsonIgnore] public abstract ActionKind Kind { get; }

    [JsonIgnore] public ActionInfo Info => ActionCatalog.Get(Kind);

    /// <summary>One-line, human readable description of what the action does (localised).</summary>
    [JsonIgnore] public abstract string Summary { get; }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not nameof(Summary)) base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
    }

    /// <summary>Re-evaluates <see cref="Summary"/> (after a language change).</summary>
    public void RefreshSummary() => base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));

    public CommandAction Clone() => Json.Clone(this);

    protected static string Or(string? value, string fallbackKey) => string.IsNullOrWhiteSpace(value) ? Loc.T(fallbackKey) : value;
}

// ── Applications ──────────────────────────────────────────────────────────

public partial class RunAppAction : CommandAction
{
    [ObservableProperty] private string _path = string.Empty;
    [ObservableProperty] private string _arguments = string.Empty;
    [ObservableProperty] private string _workingDirectory = string.Empty;
    [ObservableProperty] private WindowMode _windowMode = WindowMode.Normal;
    [ObservableProperty] private ProcessPriorityLevel _priority = ProcessPriorityLevel.Normal;
    [ObservableProperty] private bool _topMost;
    [ObservableProperty] private bool _runAsAdmin;

    public override ActionKind Kind => ActionKind.RunApp;
    public override string Summary =>
        (RunAsAdmin ? "🛡 " : "") + Or(System.IO.Path.GetFileName(Path.TrimEnd('\\')), "summary.noPath") +
        (string.IsNullOrWhiteSpace(Arguments) ? "" : "  " + Arguments);
}

public partial class OpenFolderAction : CommandAction
{
    /// <summary>Key of a <see cref="SpecialFolders"/> entry; when null <see cref="Path"/> is used.</summary>
    [ObservableProperty] private string? _special;
    [ObservableProperty] private string _path = string.Empty;

    public override ActionKind Kind => ActionKind.OpenFolder;
    public override string Summary => Special is not null ? SpecialFolders.DisplayName(Special) : Or(Path, "summary.noFolder");
}

public partial class OpenUrlAction : CommandAction
{
    [ObservableProperty] private string _url = string.Empty;

    /// <summary>Browser id (see <see cref="Browsers"/>); empty = default browser.</summary>
    [ObservableProperty] private string _browser = string.Empty;
    [ObservableProperty] private bool _newWindow;
    [ObservableProperty] private bool _incognito;

    public override ActionKind Kind => ActionKind.OpenUrl;
    public override string Summary => Or(Url, "summary.noUrl") +
        (Browsers.Get(Browser) is { } b ? $"  ·  {b.Name}" : "") +
        (Incognito ? "  ·  " + Loc.T("summary.incognito") : "");
}

public partial class AppControlAction : CommandAction
{
    [ObservableProperty] private AppControlMode _mode = AppControlMode.Activate;
    [ObservableProperty] private string _processName = string.Empty;

    /// <summary>Close: kill instead of asking politely. Activate: wait until it is in the foreground.</summary>
    [ObservableProperty] private bool _force;
    [ObservableProperty] private WaitCondition _waitFor = WaitCondition.Opened;

    /// <summary>Maximum time to wait, in seconds (0 = forever).</summary>
    [ObservableProperty] private int _timeoutSeconds;

    public override ActionKind Kind => ActionKind.AppControl;
    public override string Summary
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(ProcessName) ? "?" : ProcessName;
            return Mode switch
            {
                AppControlMode.Activate => Loc.T("summary.activate", name),
                AppControlMode.Close => Loc.T(Force ? "summary.kill" : "summary.close", name),
                _ => Loc.T($"summary.waitFor.{WaitFor}", name),
            };
        }
    }
}

public partial class RunScriptAction : CommandAction
{
    [ObservableProperty] private ScriptShell _shell = ScriptShell.Cmd;
    [ObservableProperty] private string _script = string.Empty;
    [ObservableProperty] private WindowMode _windowMode = WindowMode.Hidden;
    [ObservableProperty] private bool _runAsAdmin;
    [ObservableProperty] private bool _waitForExit;

    public override ActionKind Kind => ActionKind.RunScript;
    public override string Summary
    {
        get
        {
            var first = Script.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            return $"{(Shell == ScriptShell.Cmd ? "CMD" : "PowerShell")}: {Or(first, "summary.empty")}";
        }
    }
}

// ── Input and sound ──────────────────────────────────────────────────────

public partial class SendInputAction : CommandAction
{
    [ObservableProperty] private SendInputMode _mode = SendInputMode.Keys;

    /// <summary>Keys: "CTRL + C". Text: literal text. Mouse: "x,y".</summary>
    [ObservableProperty] private string _value = string.Empty;

    public override ActionKind Kind => ActionKind.SendInput;
    public override string Summary => Loc.T($"summary.send.{Mode}", string.IsNullOrEmpty(Value) ? "?" : Value);
}

public partial class VolumeAction : CommandAction
{
    [ObservableProperty] private VolumeMode _mode = VolumeMode.Set;

    /// <summary>Set: target level (0-100). Up/Down: step in % (0 = use the media keys).</summary>
    [ObservableProperty] private int _value = 50;
    [ObservableProperty] private bool _showOsd = true;

    public override ActionKind Kind => ActionKind.Volume;
    public override string Summary => Mode switch
    {
        VolumeMode.Set => Loc.T("summary.volume.set", Value),
        VolumeMode.Up => Value > 0 ? Loc.T("summary.volume.upBy", Value) : Loc.T("summary.volume.up"),
        VolumeMode.Down => Value > 0 ? Loc.T("summary.volume.downBy", Value) : Loc.T("summary.volume.down"),
        _ => Loc.T("summary.volume.mute"),
    };
}

public partial class PlaySoundAction : CommandAction
{
    [ObservableProperty] private string _sound = string.Empty;
    [ObservableProperty] private bool _wait;

    public override ActionKind Kind => ActionKind.PlaySound;
    public override string Summary => Or(System.IO.Path.GetFileNameWithoutExtension(Sound), "summary.noSound") +
        (Wait ? "  ·  " + Loc.T("summary.wait") : "");
}

// ── Windows and system ───────────────────────────────────────────────────

public partial class TopMostAction : CommandAction
{
    [ObservableProperty] private bool _showOsd = true;
    public override ActionKind Kind => ActionKind.TopMost;
    public override string Summary => Loc.T("action.TopMost.desc");
}

public partial class HiddenFilesAction : CommandAction
{
    [ObservableProperty] private bool _showOsd = true;
    public override ActionKind Kind => ActionKind.HiddenFiles;
    public override string Summary => Loc.T("action.HiddenFiles.desc");
}

public partial class ToggleDisabledInAction : CommandAction
{
    [ObservableProperty] private bool _showOsd = true;
    public override ActionKind Kind => ActionKind.ToggleDisabledIn;
    public override string Summary => Loc.T("action.ToggleDisabledIn.desc");
}

public partial class ShowMessageAction : CommandAction
{
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private double _seconds = 3;
    [ObservableProperty] private bool _countdown;
    [ObservableProperty] private bool _wait;

    public override ActionKind Kind => ActionKind.ShowMessage;
    public override string Summary => $"\"{(string.IsNullOrWhiteSpace(Text) ? "…" : Text)}\"  ·  {Seconds:0.#} s";
}

public partial class PowerAction : CommandAction
{
    [ObservableProperty] private PowerMode _mode = PowerMode.Shutdown;
    [ObservableProperty] private int _delaySeconds = 10;
    [ObservableProperty] private bool _showOsd = true;

    public override ActionKind Kind => ActionKind.Power;
    public override string Summary => PowerNames.Of(Mode) + (DelaySeconds > 0 ? "  ·  " + Loc.T("summary.powerDelay", DelaySeconds) : "");
}

// ── K-Desktop ─────────────────────────────────────────────────────────────

public partial class OpenQuickMenuAction : CommandAction
{
    [ObservableProperty] private string _menuId = string.Empty;

    /// <summary>Display-only cache of the menu name (resolved by the UI).</summary>
    [ObservableProperty]
    [property: JsonIgnore] private string? _menuName;

    public override ActionKind Kind => ActionKind.OpenQuickMenu;
    public override string Summary => Loc.T("summary.openMenu", string.IsNullOrWhiteSpace(MenuName) ? MenuId : MenuName);
}

public partial class CommandControlAction : CommandAction
{
    [ObservableProperty] private CommandControlMode _mode = CommandControlMode.Run;
    [ObservableProperty] private string _commandId = string.Empty;

    [ObservableProperty]
    [property: JsonIgnore] private string? _commandName;

    public override ActionKind Kind => ActionKind.CommandControl;
    public override string Summary => Loc.T($"summary.command.{Mode}", string.IsNullOrWhiteSpace(CommandName) ? CommandId : CommandName);
}

public partial class WaitAction : CommandAction
{
    [ObservableProperty] private double _seconds = 1;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _countdown;

    public override ActionKind Kind => ActionKind.Wait;
    public override string Summary => Loc.T("summary.waitSeconds", Seconds) + (string.IsNullOrWhiteSpace(Message) ? "" : $"  ·  \"{Message}\"");
}

public static class PowerNames
{
    public static string Of(PowerMode mode) => Loc.T($"power.{mode}");
}

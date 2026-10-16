using System.Collections.ObjectModel;
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
    PlayMacro, AudioOutput, KBoard, Launcher, HttpRequest,
}

public enum WindowMode { Normal, Minimized, Maximized, Hidden }
public enum ProcessPriorityLevel { Idle, BelowNormal, Normal, AboveNormal, High, RealTime }
public enum AppControlMode { Activate, Close, WaitFor }
public enum WaitCondition { Activated, Opened, Closed }
public enum SendInputMode { Keys, Text, MouseMoveTo, MouseMoveBy }
public enum VolumeMode { Set, Up, Down, ToggleMute }
public enum PowerMode { Shutdown, Restart, LogOff, SwitchUser, Lock, Sleep, Hibernate }
public enum CommandControlMode { Run, Enable, Disable, Toggle }

/// <summary>What «Comandos» acts on: a command or an event.</summary>
public enum CommandControlTarget { Command, Event }
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
[JsonDerivedType(typeof(PlayMacroAction), "playMacro")]
[JsonDerivedType(typeof(AudioOutputAction), "audioOutput")]
[JsonDerivedType(typeof(KBoardAction), "kboard")]
[JsonDerivedType(typeof(LauncherAction), "launcher")]
[JsonDerivedType(typeof(HttpRequestAction), "httpRequest")]
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

    /// <summary>A display-only property (not a change to save).</summary>
    public virtual bool IsTransient(string? property) => property is nameof(Summary);

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

/// <summary>A playback device chosen in "Audio output" (the name is kept to show it while it is disconnected).</summary>
public sealed class AudioDeviceRef
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public partial class AudioOutputAction : CommandAction
{
    /// <summary>Devices to rotate through, in order. Empty = every connected device.</summary>
    public ObservableCollection<AudioDeviceRef> Devices
    {
        get;
        set
        {
            if (field is not null) field.CollectionChanged -= OnDevicesChanged;
            field = value ?? [];
            field.CollectionChanged += OnDevicesChanged;
        }
    } = [];

    /// <summary>Also make it the default communications device (calls, Teams, Discord…).</summary>
    [ObservableProperty] private bool _communications = true;
    [ObservableProperty] private bool _showOsd = true;

    /// <summary>Sound played on the new device right after switching (empty = none).</summary>
    [ObservableProperty] private string? _sound;

    public AudioOutputAction() => Devices.CollectionChanged += OnDevicesChanged;

    private void OnDevicesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(Devices));

    public override ActionKind Kind => ActionKind.AudioOutput;
    public override string Summary => Devices.Count switch
    {
        0 => Loc.T("summary.audio.all"),
        1 => Loc.T("summary.audio.one", Devices[0].Name),
        _ => string.Join("  →  ", Devices.Select(d => d.Name)),
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

/// <summary>
/// Only read from configurations of older versions: it is now a hotkey of its own (Settings › Desactivar en), and
/// <see cref="AppConfig.Upgrade"/> turns it into that hotkey.
/// </summary>
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

/// <summary>Opens the launcher: a search box over the commands and, optionally, the installed programs and the web.</summary>
public partial class LauncherAction : CommandAction
{
    [ObservableProperty] private bool _includeApps = true;
    [ObservableProperty] private bool _webSearch = true;

    public override ActionKind Kind => ActionKind.Launcher;
    public override string Summary => Loc.T(IncludeApps ? "summary.launcherApps" : "summary.launcher");
}

public partial class CommandControlAction : CommandAction
{
    [ObservableProperty] private CommandControlMode _mode = CommandControlMode.Run;
    [ObservableProperty] private CommandControlTarget _target = CommandControlTarget.Command;
    [ObservableProperty] private string _commandId = string.Empty;

    /// <summary>The event, when <see cref="Target"/> is <see cref="CommandControlTarget.Event"/>.</summary>
    [ObservableProperty] private string _eventId = string.Empty;

    [ObservableProperty]
    [property: JsonIgnore] private string? _commandName;

    [ObservableProperty]
    [property: JsonIgnore] private string? _eventName;

    public override ActionKind Kind => ActionKind.CommandControl;
    public override string Summary => Target == CommandControlTarget.Event
        ? Loc.T($"summary.event.{Mode}", string.IsNullOrWhiteSpace(EventName) ? EventId : EventName)
        : Loc.T($"summary.command.{Mode}", string.IsNullOrWhiteSpace(CommandName) ? CommandId : CommandName);
}

public partial class WaitAction : CommandAction
{
    [ObservableProperty] private double _seconds = 1;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _countdown;

    public override ActionKind Kind => ActionKind.Wait;
    public override string Summary => Loc.T("summary.waitSeconds", Seconds) + (string.IsNullOrWhiteSpace(Message) ? "" : $"  ·  \"{Message}\"");
}

public partial class PlayMacroAction : CommandAction
{
    [ObservableProperty] private string _macroId = string.Empty;

    /// <summary>How many times the macro is played.</summary>
    [ObservableProperty] private int _repeat = 1;

    /// <summary>Repeats until SHIFT + Escape is pressed (ignores <see cref="Repeat"/>).</summary>
    [ObservableProperty] private bool _untilStopped;

    /// <summary>Playback speed (1 = as recorded, 2 = twice as fast…).</summary>
    [ObservableProperty] private double _speed = 1;

    /// <summary>Pause between repetitions, in milliseconds.</summary>
    [ObservableProperty] private int _repeatDelay;

    [ObservableProperty] private bool _showOsd = true;

    [ObservableProperty]
    [property: JsonIgnore] private string? _macroName;

    public override ActionKind Kind => ActionKind.PlayMacro;
    public override string Summary =>
        Loc.T("summary.playMacro", string.IsNullOrWhiteSpace(MacroName) ? "?" : MacroName) +
        (UntilStopped ? "  ·  ∞" : Repeat > 1 ? $"  ·  ×{Repeat}" : "") +
        (Math.Abs(Speed - 1) > 0.001 ? $"  ·  {Speed:0.##}x" : "");
}

public static class PowerNames
{
    public static string Of(PowerMode mode) => Loc.T($"power.{mode}");
}

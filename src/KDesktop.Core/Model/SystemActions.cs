using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Model;

// ── Ventana ───────────────────────────────────────────────────────────────

/// <summary>Which window «Ventana» acts on: the one in front, or the main window of a program.</summary>
public enum WindowTarget { Active, Process }

/// <summary><see cref="Close"/>: that window only, as its close button (the program may stay open with others).</summary>
public enum WindowOperation { Maximize, Minimize, Restore, ToggleMaximize, Snap, Center, MoveToMonitor, Opacity, Close }

/// <summary>Where «Colocar» puts the window, in its monitor's work area.</summary>
public enum WindowSnap
{
    LeftHalf, RightHalf, TopHalf, BottomHalf,
    TopLeft, TopRight, BottomLeft, BottomRight,
    LeftThird, CenterThird, RightThird, LeftTwoThirds, RightTwoThirds,
    Custom,
}

/// <summary>«Mover a otro monitor»: the next one, the previous one, or one by its number.</summary>
public enum MonitorChoice { Next, Previous, Number }

/// <summary>Maximises, minimises, places, centres, moves to another monitor or makes transparent a window.</summary>
public partial class WindowAction : CommandAction
{
    [ObservableProperty] private WindowTarget _target = WindowTarget.Active;
    [ObservableProperty] private string _processName = string.Empty;
    [ObservableProperty] private WindowOperation _operation = WindowOperation.Snap;
    [ObservableProperty] private WindowSnap _snap = WindowSnap.LeftHalf;

    /// <summary>«Personalizada»: position and size in % of the monitor's work area.</summary>
    [ObservableProperty] private int _x;
    [ObservableProperty] private int _y;
    [ObservableProperty] private int _width = 50;
    [ObservableProperty] private int _height = 100;

    [ObservableProperty] private MonitorChoice _monitor = MonitorChoice.Next;

    /// <summary>Monitor number (1, 2…) for <see cref="MonitorChoice.Number"/>.</summary>
    [ObservableProperty] private int _monitorNumber = 1;

    /// <summary>«Transparencia»: opacity in % (running it again on the same window makes it opaque).</summary>
    [ObservableProperty] private int _opacity = 80;

    [ObservableProperty] private bool _showOsd;

    /// <summary>
    /// The very window to act on, when it was chosen for the occasion (an assistant's «act»: «cierra YouTube» is that
    /// window of Chrome, not another one). Not saved: a window does not outlive the command.
    /// </summary>
    [JsonIgnore] public IntPtr Handle { get; set; }

    /// <summary>
    /// For «Colocar», the exact place (visible bounds), chosen for the occasion (an assistant's <c>arrange_windows</c>: all
    /// on the same monitor, in columns or a grid). Not saved, like <see cref="Handle"/>.
    /// </summary>
    [JsonIgnore] public Platform.ScreenRect? Place { get; set; }

    /// <summary>The window it acts on now: <see cref="Handle"/> while it exists, else as <see cref="Target"/> says.</summary>
    public IntPtr FindWindow(IntPtr foreground) =>
        Handle != IntPtr.Zero && Native.Win32.IsWindow(Handle) ? Handle : Platform.WindowLayout.Find(Target, ProcessName, foreground);

    public override ActionKind Kind => ActionKind.Window;

    public override string Summary
    {
        get
        {
            var what = Operation switch
            {
                WindowOperation.Snap when Snap == WindowSnap.Custom => Loc.T("summary.window.custom", X, Y, Width, Height),
                WindowOperation.Snap => Loc.T($"enum.windowSnap.{Snap}"),
                WindowOperation.MoveToMonitor => Monitor == MonitorChoice.Number
                    ? Loc.T("summary.window.monitorNumber", MonitorNumber)
                    : Loc.T($"enum.monitorChoice.{Monitor}"),
                WindowOperation.Opacity => Loc.T("summary.window.opacity", Opacity),
                _ => Loc.T($"enum.windowOp.{Operation}"),
            };
            var target = Target == WindowTarget.Active || string.IsNullOrWhiteSpace(ProcessName) ? Loc.T("summary.window.active") : ProcessName.Trim();
            return $"{target}: {what}";
        }
    }
}

// ── Portapapeles ──────────────────────────────────────────────────────────

/// <summary>What «Portapapeles» does. <see cref="ShowHistory"/> (5.0.363) opens the clipboard history over the window in front.</summary>
public enum ClipboardMode { SetText, PlainText, Upper, Lower, Capitalize, Trim, CleanUrl, Clear, ShowHistory }

/// <summary>Puts a text in the clipboard, or changes the one it has (plain, upper case, clean links…), and pastes it.</summary>
public partial class ClipboardAction : CommandAction
{
    [ObservableProperty] private ClipboardMode _mode = ClipboardMode.PlainText;

    /// <summary>«Copiar un texto»: the text (with {fecha}, {hora}, {portapapeles}).</summary>
    [ObservableProperty] private string _text = string.Empty;

    /// <summary>Copy what is selected first (Ctrl+C), so the change applies to the selection.</summary>
    [ObservableProperty] private bool _copyFirst;

    /// <summary>Paste the result afterwards (Ctrl+V).</summary>
    [ObservableProperty] private bool _paste = true;

    /// <summary>The text it leaves on the clipboard.</summary>
    public override bool GivesResult => Mode is not (ClipboardMode.Clear or ClipboardMode.ShowHistory);

    partial void OnModeChanged(ClipboardMode value) => OnPropertyChanged(nameof(GivesResult));

    public override ActionKind Kind => ActionKind.Clipboard;

    public override string Summary
    {
        get
        {
            var what = Mode == ClipboardMode.SetText
                ? Loc.T("summary.clipboard.set", string.IsNullOrEmpty(Text) ? "…" : Text.ReplaceLineEndings(" "))
                : Loc.T($"enum.clipboard.{Mode}");
            if (Mode == ClipboardMode.ShowHistory) return what;
            if (CopyFirst && Mode != ClipboardMode.SetText && Mode != ClipboardMode.Clear) what = Loc.T("summary.clipboard.selection") + " · " + what;
            if (Paste && Mode != ClipboardMode.Clear) what += "  ·  " + Loc.T("summary.clipboard.paste");
            return what;
        }
    }
}

// ── Despertar equipo ──────────────────────────────────────────────────────

/// <summary>Wake-on-LAN: sends the «magic packet» that turns on a PC or a server by its MAC address.</summary>
public partial class WakeOnLanAction : CommandAction
{
    /// <summary>«AA:BB:CC:DD:EE:FF», with «:», «-» or nothing between.</summary>
    [ObservableProperty] private string _mac = string.Empty;

    /// <summary>The machine's IP or name: to find its MAC, and to wait until it answers.</summary>
    [ObservableProperty] private string _host = string.Empty;

    /// <summary>Where the packet goes: empty = broadcast on every network; or an address (another network, a router).</summary>
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private int _port = 9;

    /// <summary>Wait until <see cref="Host"/> answers to a ping before the next action (0 = don't wait).</summary>
    [ObservableProperty] private int _waitSeconds;
    [ObservableProperty] private bool _showOsd = true;

    public override ActionKind Kind => ActionKind.WakeOnLan;

    public override string Summary
    {
        get
        {
            var who = !string.IsNullOrWhiteSpace(Host) ? Host.Trim() : string.IsNullOrWhiteSpace(Mac) ? "?" : Mac.Trim();
            return Loc.T("summary.wol", who) + (WaitSeconds > 0 && !string.IsNullOrWhiteSpace(Host) ? "  ·  " + Loc.T("summary.wol.wait", WaitSeconds) : "");
        }
    }
}

// ── Pantalla ──────────────────────────────────────────────────────────────

public enum DisplayOperation { MonitorOff, Projection, Brightness, Mode }

/// <summary>What «Proyectar» does (Win + P).</summary>
public enum ProjectionMode { Internal, Clone, Extend, External }

public enum LevelChange { Set, Up, Down }

/// <summary>Turns the screens off, changes the projection, the brightness, or the resolution and refresh rate of a monitor.</summary>
public partial class DisplayAction : CommandAction
{
    [ObservableProperty] private DisplayOperation _operation = DisplayOperation.MonitorOff;
    [ObservableProperty] private ProjectionMode _projection = ProjectionMode.Extend;
    [ObservableProperty] private LevelChange _brightnessMode = LevelChange.Set;

    /// <summary>Brightness: the level (Set) or the step (Up/Down), in %.</summary>
    [ObservableProperty] private int _brightness = 50;

    /// <summary>«Resolución»: the monitor (GDI name, «\\.\DISPLAY1») and the mode; 0 = keep that part.</summary>
    [ObservableProperty] private string _monitor = string.Empty;
    [ObservableProperty] private int _width;
    [ObservableProperty] private int _height;
    [ObservableProperty] private int _frequency;

    /// <summary>Display cache of the monitor's name (the editor fills it).</summary>
    [ObservableProperty]
    [property: JsonIgnore] private string? _monitorName;

    [ObservableProperty] private bool _showOsd = true;

    /// <summary>«Apagar pantallas»: only these monitors (GDI names), covered in black; empty = all of them, by Windows.</summary>
    public ObservableCollection<string> OffMonitors
    {
        get;
        set
        {
            if (field is not null) field.CollectionChanged -= OnOffMonitorsChanged;
            field = value ?? [];
            field.CollectionChanged += OnOffMonitorsChanged;
        }
    } = [];

    public DisplayAction() => OffMonitors.CollectionChanged += OnOffMonitorsChanged;

    private void OnOffMonitorsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(OffMonitors));

    public override ActionKind Kind => ActionKind.Display;

    /// <summary>The mode as «2560x1440@144» (the editor's list of modes); empty = none chosen.</summary>
    [JsonIgnore]
    public string ModeKey
    {
        get => Width > 0 || Frequency > 0 ? KeyOf(Width, Height, Frequency) : string.Empty;
        set
        {
            var parts = (value ?? string.Empty).Split(['x', '@']);
            if (parts.Length != 3 || !int.TryParse(parts[0], out var w) || !int.TryParse(parts[1], out var h) || !int.TryParse(parts[2], out var f)) return;
            if (w == Width && h == Height && f == Frequency) return;
            Width = w;
            Height = h;
            Frequency = f;
            OnPropertyChanged();
        }
    }

    public static string KeyOf(int width, int height, int frequency) => string.Create(CultureInfo.InvariantCulture, $"{width}x{height}@{frequency}");

    public override bool IsTransient(string? property) => property is nameof(ModeKey) or nameof(MonitorName) || base.IsTransient(property);

    public override string Summary => Operation switch
    {
        DisplayOperation.Projection => Loc.T($"enum.projection.{Projection}"),
        DisplayOperation.Brightness => BrightnessMode switch
        {
            LevelChange.Set => Loc.T("summary.brightness.set", Brightness),
            LevelChange.Up => Loc.T("summary.brightness.up", Brightness),
            _ => Loc.T("summary.brightness.down", Brightness),
        },
        DisplayOperation.Mode => MonitorLabel + ": " + DescribeMode(Width, Height, Frequency),
        _ => OffMonitors.Count == 0 ? Loc.T("enum.displayOp.MonitorOff")
            : Loc.T("summary.monitorsOff", string.Join(", ", OffMonitors.Select(m => new string(m.Where(char.IsDigit).ToArray())))),
    };

    /// <summary>The monitor's own name, or «Monitor 2» from its GDI name while it isn't known.</summary>
    private string MonitorLabel =>
        !string.IsNullOrWhiteSpace(MonitorName) ? MonitorName
        : new string(Monitor.Where(char.IsDigit).ToArray()) is { Length: > 0 } number ? Loc.T("summary.monitor", number) : "?";

    public static string DescribeMode(int width, int height, int frequency)
    {
        var parts = new List<string>(2);
        if (width > 0 && height > 0) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{width}×{height}"));
        if (frequency > 0) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{frequency} Hz"));
        return parts.Count == 0 ? "?" : string.Join(" · ", parts);
    }
}

// ── Plan de energía ───────────────────────────────────────────────────────

public enum PowerPlanMode { Set, Cycle }

/// <summary>Switches the Windows power plan (Alto rendimiento, Equilibrado…), or goes to the next one.</summary>
public partial class PowerPlanAction : CommandAction
{
    [ObservableProperty] private PowerPlanMode _mode = PowerPlanMode.Set;

    /// <summary>The plan's GUID.</summary>
    [ObservableProperty] private string _planId = string.Empty;

    /// <summary>Its name when it was chosen (shown if the plan no longer exists).</summary>
    [ObservableProperty] private string _planName = string.Empty;

    [ObservableProperty] private bool _showOsd = true;

    public override ActionKind Kind => ActionKind.PowerPlan;

    public override string Summary => Mode == PowerPlanMode.Cycle
        ? Loc.T("enum.powerPlan.Cycle")
        : Or(PlanName, "summary.noPlan");
}

// ── Tema de Windows ───────────────────────────────────────────────────────

public enum ThemeChoice { Toggle, Light, Dark }

/// <summary>Light or dark mode of Windows: of the apps, of the taskbar and Start, or both.</summary>
public partial class WindowsThemeAction : CommandAction
{
    [ObservableProperty] private ThemeChoice _mode = ThemeChoice.Toggle;
    [ObservableProperty] private bool _apps = true;
    [ObservableProperty] private bool _system = true;
    [ObservableProperty] private bool _showOsd = true;

    public override ActionKind Kind => ActionKind.WindowsTheme;
    public override string Summary => Loc.T($"enum.themeChoice.{Mode}") +
        (Apps && System ? "" : Apps ? "  ·  " + Loc.T("editor.themeApps") : System ? "  ·  " + Loc.T("editor.themeSystem") : "  ·  ?");
}

// ── Wi-Fi y Bluetooth ─────────────────────────────────────────────────────

public enum RadioDevice { WiFi, Bluetooth }

public enum SwitchMode { Toggle, On, Off }

/// <summary>Turns the Wi-Fi or the Bluetooth on or off (the switches of Windows' quick settings).</summary>
public partial class RadioAction : CommandAction
{
    [ObservableProperty] private RadioDevice _radio = RadioDevice.WiFi;
    [ObservableProperty] private SwitchMode _mode = SwitchMode.Toggle;
    [ObservableProperty] private bool _showOsd = true;

    public override ActionKind Kind => ActionKind.Radio;
    public override string Summary => Loc.T($"summary.radio.{Mode}", Loc.T($"enum.radio.{Radio}"));
}

// ── Si… ───────────────────────────────────────────────────────────────────

public enum ConditionKind
{
    ProcessRunning, Foreground, Schedule, Device, AudioOutput, AudioPlaying, Network, PowerSource,
    HostReachable, FileExists, Clipboard, Load, Variable, BatteryLevel, Screens, Daylight,
}

/// <summary>What «Si…» does when the condition is not met: stop, skip the next actions, or run another command instead (5.0.339).</summary>
public enum ConditionElse { Stop, Skip, RunCommand }

/// <summary>«Un texto o variable» (5.0.339): how {resultado} or any text with variables is compared.</summary>
public enum VariableTest { NotEmpty, Contains, Equals, Greater, Less }

/// <summary>What «Uso del equipo» measures.</summary>
public enum LoadResource { Cpu, Gpu, Memory, Vram }

/// <summary>
/// «Si…»: checks something (a program is open, the time, a device, the network…) and, when it is not so, stops the
/// command or skips the next actions. «No» turns it around.
/// </summary>
public partial class ConditionAction : CommandAction
{
    [ObservableProperty] private ConditionKind _condition = ConditionKind.ProcessRunning;

    /// <summary>«No»: the opposite (not open, not connected…).</summary>
    [ObservableProperty] private bool _negate;

    /// <summary>Program, device, network, host, path or text, depending on <see cref="Condition"/> (empty = any).</summary>
    [ObservableProperty] private string _text = string.Empty;

    /// <summary>«Horario»: «HH:mm» (empty = no limit) and days.</summary>
    [ObservableProperty] private string _timeFrom = string.Empty;
    [ObservableProperty] private string _timeTo = string.Empty;
    [ObservableProperty] private WeekDays _days;

    [ObservableProperty] private PowerSource _powerSource = PowerSource.Mains;

    /// <summary>«Uso del equipo»: what, and above which % (with «No», below).</summary>
    [ObservableProperty] private LoadResource _resource = LoadResource.Cpu;
    [ObservableProperty] private int _percent = 50;

    /// <summary>«Un texto o variable»: what is looked at (a text with variables, usually {resultado}) and how.</summary>
    [ObservableProperty] private string _value = string.Empty;
    [ObservableProperty] private VariableTest _test = VariableTest.NotEmpty;

    [ObservableProperty] private ConditionElse _else = ConditionElse.Stop;

    /// <summary>The command run instead when it is not met (with <see cref="ConditionElse.RunCommand"/>).</summary>
    [ObservableProperty] private string _elseCommandId = string.Empty;

    /// <summary>Display-only: the name of <see cref="ElseCommandId"/> (null: deleted or none).</summary>
    [ObservableProperty]
    [property: JsonIgnore] private string? _elseCommandName;

    partial void OnConditionChanged(ConditionKind value)
    {
        // A new comparison starts with what the previous action gave.
        if (value == ConditionKind.Variable && Value.Trim().Length == 0) Value = Loc.Instance.Language == "es" ? "{resultado}" : "{result}";
        // The number each one starts with (Percent is shared): 20 % of battery, two screens.
        if (value == ConditionKind.BatteryLevel && Percent == 50) Percent = 20;
        if (value == ConditionKind.Screens && Percent == 50) Percent = 2;
    }

    /// <summary>How many of the following actions are skipped when it is not met (with <see cref="ConditionElse.Skip"/>).</summary>
    [ObservableProperty] private int _skipCount = 1;

    public override ActionKind Kind => ActionKind.Condition;

    public override bool IsTransient(string? property) => property is nameof(ElseCommandName) or nameof(Description) || base.IsTransient(property);

    /// <summary><see cref="Describe"/> to bind to (the checks of an event's «Solo si»).</summary>
    [JsonIgnore] public string Description => Describe();

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(Description) or nameof(Summary))) base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Description)));
    }

    /// <summary>The following actions this one governs (skipped when it is not met), for the indentation of the list.</summary>
    [JsonIgnore] public int Governs => Enabled && Else == ConditionElse.Skip ? Math.Max(1, SkipCount) : 0;

    public override string Summary
    {
        get
        {
            var skip = Math.Max(1, SkipCount);
            var otherwise = Else switch
            {
                ConditionElse.Stop => Loc.T("summary.if.stop"),
                ConditionElse.RunCommand => Loc.T("summary.if.run", Shown(ElseCommandId, ElseCommandName, "summary.commandGone")),
                _ => skip == 1 ? Loc.T("summary.if.skip1") : Loc.T("summary.if.skip", skip),
            };
            return $"{Describe()}  ·  {otherwise}";
        }
    }

    /// <summary>«Si chrome está abierto», «Si no hay red»… (localised, with «No» applied).</summary>
    public string Describe()
    {
        var text = Text.Trim();
        var not = Negate ? ".not" : string.Empty;
        switch (Condition)
        {
            case ConditionKind.Schedule:
                var from = EventTime.Parse(TimeFrom)?.ToString("H:mm", CultureInfo.InvariantCulture);
                var to = EventTime.Parse(TimeTo)?.ToString("H:mm", CultureInfo.InvariantCulture);
                var range = from is null && to is null ? string.Empty : Loc.T("if.Schedule.range", from ?? "0:00", to ?? "24:00");
                var when = string.Join(" · ", new[] { range, Days.Describe() }.Where(s => s.Length > 0));
                return Loc.T("if.Schedule" + not, when.Length == 0 ? Loc.T("if.Schedule.always") : when);
            case ConditionKind.PowerSource:
                return Loc.T($"if.PowerSource.{PowerSource}{not}");
            case ConditionKind.Load:
                return Loc.T($"if.Load.{Resource}{not}", Percent);
            case ConditionKind.BatteryLevel or ConditionKind.Screens:
                return Loc.T($"if.{Condition}{not}", Percent);
            case ConditionKind.Daylight:
                return Loc.T("if.Daylight" + not);
            case ConditionKind.Variable:
                var value = Value.Trim().Length == 0 ? "?" : Value.Trim();
                return Test == VariableTest.NotEmpty ? Loc.T("if.Variable.NotEmpty" + not, value) : Loc.T($"if.Variable.{Test}{not}", value, text);
            case ConditionKind.Network or ConditionKind.Clipboard or ConditionKind.AudioPlaying when text.Length == 0:
                return Loc.T($"if.{Condition}.any{not}");
            default:
                return Loc.T($"if.{Condition}{not}", text.Length == 0 ? "?" : text);
        }
    }
}

// ── Captura de pantalla ───────────────────────────────────────────────────

/// <summary>What «Captura de pantalla» takes.</summary>
public enum CaptureArea { Region, ActiveWindow, Monitor, AllMonitors }

/// <summary>The file of a screenshot (Settings › Capturas): PNG and WebP keep every pixel; JPG is smaller, with a quality.</summary>
public enum ImageFileFormat { Png, Jpg, WebP }

/// <summary>Takes a picture of the screen: a region chosen with the mouse, the window in front, a monitor or all of them.</summary>
public partial class ScreenshotAction : CommandAction
{
    [ObservableProperty] private CaptureArea _area = CaptureArea.Region;
    [ObservableProperty] private bool _copy = true;
    [ObservableProperty] private bool _save = true;

    /// <summary>Where it is saved; empty = Settings › Capturas (Pictures\K-Desktop by default).</summary>
    [ObservableProperty] private string _folder = string.Empty;

    /// <summary>Open the picture afterwards (to draw on it, crop it…).</summary>
    [ObservableProperty] private bool _open;
    [ObservableProperty] private bool _showOsd = true;

    /// <summary>The file it saved.</summary>
    public override bool GivesResult => Save;

    partial void OnSaveChanged(bool value) => OnPropertyChanged(nameof(GivesResult));

    public override ActionKind Kind => ActionKind.Screenshot;
    public override string Summary
    {
        get
        {
            var parts = new List<string> { Loc.T($"enum.captureArea.{Area}") };
            if (Copy) parts.Add(Loc.T("summary.capture.copy"));
            if (Save) parts.Add(Loc.T("summary.capture.save"));
            if (Open) parts.Add(Loc.T("summary.capture.open"));
            return string.Join("  ·  ", parts);
        }
    }
}

// ── Copiar texto de la pantalla ──────────────────────────────────────────

/// <summary>
/// «Copiar texto de la pantalla» (stage 10.1): the text of a region chosen with the mouse, the active window or a
/// monitor, read with the OCR of Windows (no AI) and copied, like PowerToys' Text Extractor.
/// </summary>
public partial class ScreenTextAction : CommandAction
{
    [ObservableProperty] private CaptureArea _area = CaptureArea.Region;

    /// <summary>The language of the text («es-ES»); empty = the one of K-Desktop if its OCR is installed, else Windows'.</summary>
    [ObservableProperty] private string _language = string.Empty;

    /// <summary>All the lines joined in one (to paste in a search box, a cell…).</summary>
    [ObservableProperty] private bool _singleLine;
    [ObservableProperty] private bool _showOsd = true;

    /// <summary>The text read.</summary>
    public override bool GivesResult => true;

    public override ActionKind Kind => ActionKind.ScreenText;
    public override string Summary
    {
        get
        {
            var parts = new List<string> { Loc.T($"enum.captureArea.{Area}") };
            if (Language.Length > 0) parts.Add(Language);
            if (SingleLine) parts.Add(Loc.T("summary.screenText.singleLine"));
            return string.Join("  ·  ", parts);
        }
    }
}

// ── Grabar audio ──────────────────────────────────────────────────────────

/// <summary>What «Grabar audio» records: what the PC plays, the microphone, or both mixed.</summary>
public enum AudioSource { Output, Microphone, Both }

/// <summary>The file of a recording (Settings › Grabación de audio): WAV as it is, or compressed to MP3 or Opus when it ends.</summary>
public enum AudioFileFormat { Wav, Mp3, Opus }

/// <summary>Start/stop a recording, or save the last seconds kept by the replay (Settings › Grabación).</summary>
public enum RecordMode { Toggle, Start, Stop, SaveReplay }

public partial class AudioRecordAction : CommandAction
{
    [ObservableProperty] private RecordMode _mode = RecordMode.Toggle;
    [ObservableProperty] private AudioSource _source = AudioSource.Output;

    /// <summary>Where it is saved; empty = Settings › Grabación (Music\K-Desktop by default).</summary>
    [ObservableProperty] private string _folder = string.Empty;
    [ObservableProperty] private bool _showOsd = true;

    public override ActionKind Kind => ActionKind.AudioRecord;
    public override string Summary => Mode == RecordMode.SaveReplay
        ? Loc.T("enum.recordMode.SaveReplay")
        : $"{Loc.T($"enum.recordMode.{Mode}")}  ·  {Loc.T($"enum.audioSource.{Source}")}";
}

// ── Dictar ────────────────────────────────────────────────────────────────

/// <summary>
/// «Dictar» (stage 10.5): what is said is written where the cursor is, a piece at every pause, until a few seconds of
/// silence, Escape, «deja de dictar» or the same action again (Toggle). Without the AI: Parakeet only.
/// </summary>
public partial class DictateAction : CommandAction
{
    [ObservableProperty] private SwitchMode _mode = SwitchMode.Toggle;

    public override ActionKind Kind => ActionKind.Dictate;
    public override string Summary => Loc.T($"enum.dictateMode.{Mode}");
}

// ── Multimedia ────────────────────────────────────────────────────────────

/// <summary>What «Multimedia» does in the player.</summary>
public enum MediaControl { Toggle, Play, Pause, Next, Previous, Stop }

/// <summary>
/// Plays, pauses, skips… with Windows' media controls (5.0.342): in the program that is playing, or in one chosen
/// («Spotify»). Players that don't tell Windows what they play get the media keys.
/// </summary>
public partial class MediaAction : CommandAction
{
    [ObservableProperty] private MediaControl _control = MediaControl.Toggle;

    /// <summary>The program, as Windows' media controls name it («Spotify», «Firefox»); empty = the one playing.</summary>
    [ObservableProperty] private string _app = string.Empty;

    [ObservableProperty] private bool _showOsd = true;

    public override ActionKind Kind => ActionKind.Media;
    public override string Summary => Loc.T($"enum.mediaControl.{Control}") +
        (string.IsNullOrWhiteSpace(App) ? "" : "  ·  " + App.Trim());
}

// ── Mantener despierto ────────────────────────────────────────────────────

/// <summary>How long «Mantener despierto» lasts.</summary>
public enum AwakeFor { Minutes, WhileApp, UntilOff }

/// <summary>The PC does not go to sleep (nor, if asked, the screen off) for a while, while a program is open, or until turned off.</summary>
public partial class KeepAwakeAction : CommandAction
{
    [ObservableProperty] private SwitchMode _mode = SwitchMode.Toggle;
    [ObservableProperty] private AwakeFor _for = AwakeFor.Minutes;
    [ObservableProperty] private int _minutes = 60;
    [ObservableProperty] private string _processName = string.Empty;

    /// <summary>The screen stays on too.</summary>
    [ObservableProperty] private bool _screen = true;

    [ObservableProperty] private bool _showOsd = true;

    public override ActionKind Kind => ActionKind.KeepAwake;

    public override string Summary
    {
        get
        {
            if (Mode == SwitchMode.Off) return Loc.T("summary.awake.off");
            var how = For switch
            {
                AwakeFor.Minutes => Loc.T("summary.awake.minutes", Minutes),
                AwakeFor.WhileApp => Loc.T("summary.awake.whileApp", string.IsNullOrWhiteSpace(ProcessName) ? "?" : ProcessName.Trim()),
                _ => Loc.T("summary.awake.untilOff"),
            };
            return (Mode == SwitchMode.Toggle ? Loc.T("summary.awake.toggle") : Loc.T("summary.awake.on")) + "  ·  " + how +
                (Screen ? "  ·  " + Loc.T("summary.awake.screen") : "");
        }
    }
}

// ── Lupa ──────────────────────────────────────────────────────────────────

/// <summary>«Lupa»: a lens around the mouse (rectangular or round), or the whole monitor it is on enlarged.</summary>
public enum ZoomMode { Lens, FullScreen, Circle }

/// <summary>How big the lens is.</summary>
public enum ZoomLensSize { Small, Medium, Large }

/// <summary>The lupa's colours: as they are, inverted, or with more contrast (to read better).</summary>
public enum ZoomColors { Normal, Inverted, HighContrast }

/// <summary>Zooms the screen around the mouse (it follows it), or takes the zoom away.</summary>
public partial class MagnifierAction : CommandAction
{
    [ObservableProperty] private SwitchMode _mode = SwitchMode.Toggle;

    /// <summary>How much (2 = twice as big); 0 = the level of Settings.</summary>
    [ObservableProperty] private double _zoom;

    public override ActionKind Kind => ActionKind.Magnifier;
    public override string Summary => Loc.T($"summary.zoom.{Mode}") + (Zoom > 1 ? string.Create(CultureInfo.CurrentCulture, $"  ·  ×{Zoom:0.#}") : "");
}

// ── Enviar al móvil ───────────────────────────────────────────────────────

/// <summary>
/// «Enviar al móvil» (B11): a text, a link and/or a small file to the paired phones and browsers (all, or the one named),
/// kept until each has it (<see cref="Remote.Web.PhoneInbox"/>). With variables.
/// </summary>
public partial class SendToPhoneAction : CommandAction
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private string _file = string.Empty;

    /// <summary>Part of the device's name; empty: all of them.</summary>
    [ObservableProperty] private string _device = string.Empty;

    [ObservableProperty] private bool _showOsd = true;

    public override ActionKind Kind => ActionKind.SendToPhone;

    public override string Summary
    {
        get
        {
            var what = Title.Trim().Length > 0 ? Title.Trim() : Text.Trim().Length > 0 ? Text.Trim() : Url.Trim().Length > 0 ? Url.Trim()
                : File.Trim().Length > 0 ? System.IO.Path.GetFileName(File.Trim()) : "…";
            return $"\"{(what.Length > 40 ? what[..40] + "…" : what)}\"  ·  " + (Device.Trim().Length > 0 ? Device.Trim() : Loc.T("summary.phone.all"));
        }
    }
}

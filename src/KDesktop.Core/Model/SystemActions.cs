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

public enum ClipboardMode { SetText, PlainText, Upper, Lower, Capitalize, Trim, CleanUrl, Clear }

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

    public override ActionKind Kind => ActionKind.Clipboard;

    public override string Summary
    {
        get
        {
            var what = Mode == ClipboardMode.SetText
                ? Loc.T("summary.clipboard.set", string.IsNullOrEmpty(Text) ? "…" : Text.ReplaceLineEndings(" "))
                : Loc.T($"enum.clipboard.{Mode}");
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
        _ => Loc.T("enum.displayOp.MonitorOff"),
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
    HostReachable, FileExists, Clipboard, Load,
}

/// <summary>What «Si…» does when the condition is not met.</summary>
public enum ConditionElse { Stop, Skip }

/// <summary>What «Uso del equipo» measures.</summary>
public enum LoadResource { Cpu, Gpu, Memory }

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

    [ObservableProperty] private ConditionElse _else = ConditionElse.Stop;

    /// <summary>How many of the following actions are skipped when it is not met (with <see cref="ConditionElse.Skip"/>).</summary>
    [ObservableProperty] private int _skipCount = 1;

    public override ActionKind Kind => ActionKind.Condition;

    /// <summary>The following actions this one governs (skipped when it is not met), for the indentation of the list.</summary>
    [JsonIgnore] public int Governs => Enabled && Else == ConditionElse.Skip ? Math.Max(1, SkipCount) : 0;

    public override string Summary
    {
        get
        {
            var skip = Math.Max(1, SkipCount);
            var otherwise = Else == ConditionElse.Stop ? Loc.T("summary.if.stop") : skip == 1 ? Loc.T("summary.if.skip1") : Loc.T("summary.if.skip", skip);
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

// ── Grabar audio ──────────────────────────────────────────────────────────

/// <summary>What «Grabar audio» records: what the PC plays, the microphone, or both mixed.</summary>
public enum AudioSource { Output, Microphone, Both }

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

// ── Lupa ──────────────────────────────────────────────────────────────────

/// <summary>«Lupa»: a lens around the mouse, or the whole main monitor enlarged.</summary>
public enum ZoomMode { Lens, FullScreen }

/// <summary>How big the lens is.</summary>
public enum ZoomLensSize { Small, Medium, Large }

/// <summary>Zooms the screen around the mouse (it follows it), or takes the zoom away.</summary>
public partial class MagnifierAction : CommandAction
{
    [ObservableProperty] private SwitchMode _mode = SwitchMode.Toggle;

    /// <summary>How much (2 = twice as big); 0 = the level of Settings.</summary>
    [ObservableProperty] private double _zoom;

    public override ActionKind Kind => ActionKind.Magnifier;
    public override string Summary => Loc.T($"summary.zoom.{Mode}") + (Zoom > 1 ? string.Create(CultureInfo.CurrentCulture, $"  ·  ×{Zoom:0.#}") : "");
}

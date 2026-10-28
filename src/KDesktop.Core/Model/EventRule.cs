using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Model;

/// <summary>
/// An automation: «when X happens, run these commands». The commands are referenced by id, so they still work from
/// their hotkeys, the voice or the web, and one command can be used by several events.
/// </summary>
public partial class EventRule : ObservableObject
{
    [ObservableProperty] private string _id = Ids.New();
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _icon;
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string? _notes;

    /// <summary>«No repetir antes de»: seconds that must pass before it fires again (0 = always).</summary>
    [ObservableProperty] private int _cooldownSeconds;

    /// <summary>«Solo una vez»: it is deleted once it has run (after «Al terminar», if it has those commands).</summary>
    [ObservableProperty] private bool _once;

    partial void OnOnceChanged(bool value) => base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));

    /// <summary>The assistant that programmed it (the tool <c>schedule</c>); null: the user made it.</summary>
    [ObservableProperty] private string? _createdBy;

    /// <summary>«Pedir a un asistente»: an order given to an assistant after the commands; null = none.</summary>
    public EventAsk? Ask
    {
        get;
        set
        {
            if (field is not null) field.PropertyChanged -= OnPartChanged;
            field = value;
            if (field is not null) field.PropertyChanged += OnPartChanged;
            OnPropertyChanged();
        }
    }

    public EventTrigger Trigger
    {
        get;
        set
        {
            if (field is not null) field.PropertyChanged -= OnPartChanged;
            field = value ?? new AppStartTrigger();
            field.PropertyChanged += OnPartChanged;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(HasEnd));
        }
    } = null!;

    /// <summary>Command ids, run in this order when it fires.</summary>
    public ObservableCollection<string> Commands { get; set => Rewire(ref field, value); } = [];

    /// <summary>«Al terminar»: command ids run when the trigger ends (only triggers with an end).</summary>
    public ObservableCollection<string> EndCommands { get; set => Rewire(ref field, value); } = [];

    /// <summary>Commands of «Ejecutar» / «Al terminar» left in the list but unticked: skipped when it fires.</summary>
    public ObservableCollection<string> DisabledCommands { get; set => Rewire(ref field, value); } = [];
    public ObservableCollection<string> DisabledEndCommands { get; set => Rewire(ref field, value); } = [];

    /// <summary>«Solo si»: when it may fire.</summary>
    public EventConditions Conditions
    {
        get;
        set
        {
            if (field is not null) field.PropertyChanged -= OnPartChanged;
            field = value ?? new EventConditions();
            field.PropertyChanged += OnPartChanged;
            OnPropertyChanged();
        }
    } = null!;

    public EventRule()
    {
        Trigger = new AppStartTrigger();
        Conditions = new EventConditions();
        Commands.CollectionChanged += OnCollectionChanged;
        EndCommands.CollectionChanged += OnCollectionChanged;
        DisabledCommands.CollectionChanged += OnCollectionChanged;
        DisabledEndCommands.CollectionChanged += OnCollectionChanged;
    }

    /// <summary>Raised whenever the event or anything inside it changes (autosave, detector rebuild).</summary>
    public event EventHandler? Modified;

    [JsonIgnore] public string Summary => Once ? Loc.T("event.summary.once", Trigger.Summary) : Trigger.Summary;

    [JsonIgnore] public bool HasEnd => Trigger.HasEnd;

    /// <summary>Last time it fired (shown in the list; kept in events-state.json, not in config.json).</summary>
    [ObservableProperty]
    [property: JsonIgnore] private DateTime? _lastFired;

    /// <summary>The events page: refreshes the trigger summary (after a language change).</summary>
    public void RefreshTexts()
    {
        Trigger.RefreshSummary();
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(LastFired) or nameof(Summary) or nameof(HasEnd)) return;
        Modified?.Invoke(this, EventArgs.Empty);
    }

    private void OnPartChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EventTrigger.Summary))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
            return;
        }
        if (sender is EventTrigger) base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(HasEnd)));
        Modified?.Invoke(this, EventArgs.Empty);
    }

    private void Rewire(ref ObservableCollection<string> field, ObservableCollection<string> value)
    {
        if (field is not null) field.CollectionChanged -= OnCollectionChanged;
        field = value ?? [];
        field.CollectionChanged += OnCollectionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Modified?.Invoke(this, EventArgs.Empty);

    /// <summary>Deep copy with a new id.</summary>
    public EventRule Duplicate()
    {
        var copy = Json.Clone(this);
        copy.Id = Ids.New();
        return copy;
    }

    public override string ToString() => Name;
}

/// <summary>
/// «Pedir a un asistente» of an event: the order goes to the assistant as if it had been said to it (like «Asistente:
/// preguntar»), after the event's commands.
/// </summary>
public partial class EventAsk : ObservableObject
{
    [ObservableProperty] private string _assistant = global::KDesktop.Core.Assistant.AssistantTargeting.KDesktop;
    [ObservableProperty] private string _text = string.Empty;

    /// <summary>
    /// Programmed by an assistant (the tool <c>schedule</c>): the tools it may use for it without asking, approved when it
    /// was asked for. Null: an order like any other («Asistente: preguntar»), which asks as usual.
    /// </summary>
    public IReadOnlyList<string>? Tools { get; set; }

    /// <summary>With <see cref="Tools"/>: the ids of the user's commands it may run without asking.</summary>
    public IReadOnlyList<string>? Commands { get; set; }

    [JsonIgnore] public bool IsEmpty => Assistant.Length == 0 || Text.Trim().Length == 0;

    /// <summary>Programmed by an assistant, with what was approved then.</summary>
    [JsonIgnore] public bool IsScheduled => Tools is not null || Commands is not null;
}

/// <summary>Days of the week as flags; none = every day.</summary>
[Flags]
public enum WeekDays
{
    None = 0,
    Monday = 1, Tuesday = 2, Wednesday = 4, Thursday = 8, Friday = 16, Saturday = 32, Sunday = 64,
    Workdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekend = Saturday | Sunday,
}

public static class WeekDaysExtensions
{
    public static WeekDays Of(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => WeekDays.Monday,
        DayOfWeek.Tuesday => WeekDays.Tuesday,
        DayOfWeek.Wednesday => WeekDays.Wednesday,
        DayOfWeek.Thursday => WeekDays.Thursday,
        DayOfWeek.Friday => WeekDays.Friday,
        DayOfWeek.Saturday => WeekDays.Saturday,
        _ => WeekDays.Sunday,
    };

    /// <summary>No day chosen means every day.</summary>
    public static bool Includes(this WeekDays days, DayOfWeek day) => days == WeekDays.None || (days & Of(day)) != 0;

    /// <summary>«L M X J V», «laborables», «fin de semana» (empty = every day).</summary>
    public static string Describe(this WeekDays days)
    {
        if (days is WeekDays.None or (WeekDays.Workdays | WeekDays.Weekend)) return string.Empty;
        if (days == WeekDays.Workdays) return Loc.T("event.days.workdays");
        if (days == WeekDays.Weekend) return Loc.T("event.days.weekend");
        return string.Join(" ", Enum.GetValues<DayOfWeek>().Skip(1).Append(DayOfWeek.Sunday)
            .Where(d => (days & Of(d)) != 0).Select(d => Loc.T($"event.day.{d}")));
    }
}

/// <summary>
/// «Solo si» (5.0.340): the same checks as «Si…» (<see cref="ConditionAction"/>, only what it looks at), all of them or
/// any; Engine.Conditions.AllowsAsync says whether an event may fire. Until 5.0.339 it was a time range, days and a
/// program open or not open: those are only read now and <see cref="Migrate"/> turns them into checks.
/// </summary>
public partial class EventConditions : ObservableObject
{
    public EventConditions() => Items = [];

    /// <summary>The checks, in order.</summary>
    public ObservableCollection<ConditionAction> Items
    {
        get;
        set
        {
            if (field is not null)
            {
                field.CollectionChanged -= OnItemsChanged;
                foreach (var item in field) item.PropertyChanged -= OnItemChanged;
            }
            field = value ?? [];
            field.CollectionChanged += OnItemsChanged;
            foreach (var item in field) item.PropertyChanged += OnItemChanged;
            OnPropertyChanged();
        }
    } = null!;

    /// <summary>«Alguna»: one met is enough (otherwise all of them).</summary>
    [ObservableProperty] private bool _any;

    // Before 5.0.340: only read (null once migrated, so they are not written).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string? TimeFrom { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string? TimeTo { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public WeekDays Days { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string? ProcessRunning { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public string? ProcessNotRunning { get; set; }

    /// <summary>Nothing to check (no enabled check).</summary>
    [JsonIgnore] public bool IsEmpty => !Items.Any(i => i.Enabled);

    /// <summary>The old time range, days and programs as checks («Horario», «Un programa está abierto», with «Al revés»). True if there were some.</summary>
    public bool Migrate()
    {
        var changed = false;
        if (!string.IsNullOrWhiteSpace(TimeFrom) || !string.IsNullOrWhiteSpace(TimeTo) || Days != WeekDays.None)
        {
            Items.Add(new ConditionAction { Condition = ConditionKind.Schedule, TimeFrom = TimeFrom?.Trim() ?? string.Empty, TimeTo = TimeTo?.Trim() ?? string.Empty, Days = Days });
            changed = true;
        }
        if (!string.IsNullOrWhiteSpace(ProcessRunning))
        {
            Items.Add(new ConditionAction { Condition = ConditionKind.ProcessRunning, Text = ProcessRunning.Trim() });
            changed = true;
        }
        if (!string.IsNullOrWhiteSpace(ProcessNotRunning))
        {
            Items.Add(new ConditionAction { Condition = ConditionKind.ProcessRunning, Text = ProcessNotRunning.Trim(), Negate = true });
            changed = true;
        }
        TimeFrom = TimeTo = ProcessRunning = ProcessNotRunning = null;
        Days = WeekDays.None;
        return changed;
    }

    /// <summary>From ≤ time &lt; to; when from &gt; to the range crosses midnight (22:00–06:00).</summary>
    public static bool InRange(TimeOnly time, TimeOnly from, TimeOnly to) =>
        from <= to ? time >= from && (time < to || to == TimeOnly.MaxValue) : time >= from || time < to;

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (ConditionAction item in e.OldItems) item.PropertyChanged -= OnItemChanged;
        if (e.NewItems is not null) foreach (ConditionAction item in e.NewItems) item.PropertyChanged += OnItemChanged;
        OnPropertyChanged(nameof(Items));
    }

    // A check changed (not what is only shown): the event is saved.
    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ConditionAction item && !item.IsTransient(e.PropertyName)) OnPropertyChanged(nameof(Items));
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName != nameof(IsEmpty)) base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsEmpty)));
    }
}

public static class EventTime
{
    /// <summary>«9:00», «09:00», «21:30» → time; empty or invalid → null.</summary>
    public static TimeOnly? Parse(string? text) =>
        TimeOnly.TryParseExact(text?.Trim() ?? string.Empty, ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss"], System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var t) ? t : null;

    /// <summary>«2026-10-31» (how it is saved) → date; empty or invalid → null.</summary>
    public static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text?.Trim() ?? string.Empty, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    public static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

public enum EventKind
{
    AppStart, AppExit, Time, Interval, ProcessStart, Foreground, Idle, Session,
    AudioDevice, Device, Network, PowerSource, File,
    RemoteCall, Gadget, Load,
}

/// <summary>«Uso del equipo»: above or below the threshold.</summary>
public enum LoadComparison { Above, Below }

public enum SessionEvent { Lock, Unlock, Suspend, Resume }

/// <summary>Where the PC takes its power from (laptops).</summary>
public enum PowerSource { Battery, Mains }

/// <summary>«Red»: connected to a network, or without any.</summary>
public enum NetworkState { Connected, Offline }

/// <summary>«Dispositivo de audio»: one is connected, or one becomes the default output.</summary>
public enum AudioDeviceEvent { Connected, BecomesDefault }

/// <summary>What makes an event fire.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AppStartTrigger), "appStart")]
[JsonDerivedType(typeof(AppExitTrigger), "appExit")]
[JsonDerivedType(typeof(TimeTrigger), "time")]
[JsonDerivedType(typeof(IntervalTrigger), "interval")]
[JsonDerivedType(typeof(ProcessStartTrigger), "processStart")]
[JsonDerivedType(typeof(ForegroundTrigger), "foreground")]
[JsonDerivedType(typeof(IdleTrigger), "idle")]
[JsonDerivedType(typeof(SessionTrigger), "session")]
[JsonDerivedType(typeof(PowerSourceTrigger), "powerSource")]
[JsonDerivedType(typeof(FileTrigger), "file")]
[JsonDerivedType(typeof(DriveTrigger), "drive")]
[JsonDerivedType(typeof(NetworkTrigger), "network")]
[JsonDerivedType(typeof(AudioDeviceTrigger), "audioDevice")]
[JsonDerivedType(typeof(RemoteCallTrigger), "remoteCall")]
[JsonDerivedType(typeof(GadgetTrigger), "gadget")]
[JsonDerivedType(typeof(LoadTrigger), "load")]
public abstract partial class EventTrigger : ObservableObject
{
    [JsonIgnore] public abstract EventKind Kind { get; }

    /// <summary>One line, localised: «Cada día laborable a las 9:00», «Al abrir steam».</summary>
    [JsonIgnore] public abstract string Summary { get; }

    /// <summary>It has an end («Al terminar»): the program closes, it stops being in front, you come back…</summary>
    [JsonIgnore] public virtual bool HasEnd => false;

    /// <summary>«No repetir antes de» proposed for a new event of this type.</summary>
    [JsonIgnore] public virtual int DefaultCooldown => 0;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not nameof(Summary)) base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));
    }

    public void RefreshSummary() => base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Summary)));

    protected static string Names(IEnumerable<string> processes)
    {
        var list = processes.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        return list.Count == 0 ? "?" : string.Join(", ", list);
    }

    /// <summary>A list of processes that tells its owner when it changes.</summary>
    protected ObservableCollection<string> Watch(ObservableCollection<string>? list, ObservableCollection<string>? previous)
    {
        if (previous is not null) previous.CollectionChanged -= OnListChanged;
        list ??= [];
        list.CollectionChanged += OnListChanged;
        return list;
    }

    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnPropertyChanged("Processes");
}

/// <summary>When K-Desktop starts (what «Ejecutar al iniciar» was): the commands run one after another.</summary>
public sealed class AppStartTrigger : EventTrigger
{
    public override EventKind Kind => EventKind.AppStart;
    public override string Summary => Loc.T("event.summary.appStart");
}

/// <summary>When K-Desktop exits (what «Ejecutar al salir» was): all at once, 5 s at most.</summary>
public sealed class AppExitTrigger : EventTrigger
{
    public override EventKind Kind => EventKind.AppExit;
    public override string Summary => Loc.T("event.summary.appExit");
}

/// <summary>At a time of day, on some days (or on one date).</summary>
public sealed partial class TimeTrigger : EventTrigger
{
    [ObservableProperty] private string _time = "09:00";
    [ObservableProperty] private WeekDays _days;

    /// <summary>«yyyy-MM-dd»: only that day (the days of the week don't count); empty = it repeats.</summary>
    [ObservableProperty] private string _date = string.Empty;

    [JsonIgnore] public DateOnly? OnDate => EventTime.ParseDate(Date);

    [JsonIgnore] public bool HasDate => OnDate is not null;

    /// <summary>The date for the editor's picker.</summary>
    [JsonIgnore]
    public DateTime? DateValue
    {
        get => OnDate?.ToDateTime(TimeOnly.MinValue);
        set => Date = value is { } d ? EventTime.FormatDate(DateOnly.FromDateTime(d)) : string.Empty;
    }

    partial void OnDateChanged(string value)
    {
        OnPropertyChanged(nameof(HasDate));
        OnPropertyChanged(nameof(DateValue));
    }

    /// <summary>If K-Desktop wasn't running (or the PC was asleep) at that time, run it as soon as it can.</summary>
    [ObservableProperty] private bool _runIfMissed;

    public override EventKind Kind => EventKind.Time;

    public override string Summary
    {
        get
        {
            var time = EventTime.Parse(Time)?.ToString("H:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "?";
            if (OnDate is { } date) return Loc.T("event.summary.timeOn", time, date.ToString("d", Loc.Instance.Culture));
            var days = Days.Describe();
            return days.Length == 0 ? Loc.T("event.summary.timeDaily", time) : Loc.T("event.summary.timeDays", time, days);
        }
    }
}

/// <summary>Every N minutes while K-Desktop runs.</summary>
public sealed partial class IntervalTrigger : EventTrigger
{
    [ObservableProperty] private int _minutes = 60;

    public override EventKind Kind => EventKind.Interval;
    public override string Summary => Loc.T("event.summary.interval", Minutes);
}

/// <summary>A program opens; it ends when the last of its windows… processes closes.</summary>
public sealed partial class ProcessStartTrigger : EventTrigger
{
    public ObservableCollection<string> Processes { get; set => field = Watch(value, field); }

    /// <summary>Also when it was already open when K-Desktop started.</summary>
    [ObservableProperty] private bool _includeRunningAtStart;

    public ProcessStartTrigger() => Processes = [];

    public override EventKind Kind => EventKind.ProcessStart;
    public override bool HasEnd => true;
    public override int DefaultCooldown => 2;
    public override string Summary => Loc.T("event.summary.processStart", Names(Processes));
}

/// <summary>A program comes to the front; it ends when it stops being in front.</summary>
public sealed class ForegroundTrigger : EventTrigger
{
    public ObservableCollection<string> Processes { get; set => field = Watch(value, field); }

    public ForegroundTrigger() => Processes = [];

    public override EventKind Kind => EventKind.Foreground;
    public override bool HasEnd => true;
    public override string Summary => Loc.T("event.summary.foreground", Names(Processes));
}

/// <summary>Some minutes without touching the PC; it ends when you come back.</summary>
public sealed partial class IdleTrigger : EventTrigger
{
    [ObservableProperty] private int _minutes = 15;

    public override EventKind Kind => EventKind.Idle;
    public override bool HasEnd => true;
    public override string Summary => Loc.T("event.summary.idle", Minutes);
}

/// <summary>The session is locked or unlocked, the PC goes to sleep or wakes up.</summary>
public sealed partial class SessionTrigger : EventTrigger
{
    [ObservableProperty] private SessionEvent _event = SessionEvent.Lock;

    public override EventKind Kind => EventKind.Session;
    public override int DefaultCooldown => 2;
    public override string Summary => Loc.T($"event.summary.session.{Event}");
}

/// <summary>A laptop goes on battery or back on mains power.</summary>
public sealed partial class PowerSourceTrigger : EventTrigger
{
    [ObservableProperty] private PowerSource _source = PowerSource.Battery;

    public override EventKind Kind => EventKind.PowerSource;
    public override int DefaultCooldown => 2;
    public override string Summary => Loc.T($"event.summary.power.{Source}");
}

/// <summary>A new file appears in a folder (once it has stopped growing).</summary>
public sealed partial class FileTrigger : EventTrigger
{
    [ObservableProperty] private string _folder = string.Empty;

    /// <summary>«*.png», «*.mp4»… (empty or «*» = any file).</summary>
    [ObservableProperty] private string _filter = "*";
    [ObservableProperty] private bool _subfolders;

    public override EventKind Kind => EventKind.File;
    public override string Summary
    {
        get
        {
            var folder = Path.GetFileName(Folder.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var filter = string.IsNullOrWhiteSpace(Filter) || Filter.Trim() == "*" ? string.Empty : " (" + Filter.Trim() + ")";
            return Loc.T("event.summary.file", folder.Length == 0 ? "?" : folder) + filter;
        }
    }
}

/// <summary>A drive is connected (USB stick, disk, memory card…); it ends when it is removed.</summary>
public sealed partial class DriveTrigger : EventTrigger
{
    /// <summary>A letter («E:») or part of the drive's name («Kingston»); empty = any.</summary>
    [ObservableProperty] private string _filter = string.Empty;

    public override EventKind Kind => EventKind.Device;
    public override bool HasEnd => true;
    public override int DefaultCooldown => 2;
    public override string Summary => string.IsNullOrWhiteSpace(Filter) ? Loc.T("event.summary.driveAny") : Loc.T("event.summary.drive", Filter.Trim());

    /// <summary>Whether the drive (its letter and its name, if known) is the one asked for.</summary>
    public static bool Matches(string filter, char letter, string? label)
    {
        filter = filter.Trim();
        if (filter.Length == 0) return true;
        var asLetter = filter.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':');
        if (asLetter.Length == 1 && char.IsLetter(asLetter[0])) return char.ToUpperInvariant(asLetter[0]) == char.ToUpperInvariant(letter);
        if (string.IsNullOrEmpty(label)) return false;
        var tokens = Engine.FuzzyMatch.Tokens(filter);
        var folded = new string(label.Select(Engine.FuzzyMatch.Fold).ToArray());
        return tokens.All(token => folded.Contains(token, StringComparison.Ordinal));
    }
}

/// <summary>Connected to a network (by name, or any) or left without network; it ends when that changes back.</summary>
public sealed partial class NetworkTrigger : EventTrigger
{
    [ObservableProperty] private NetworkState _state = NetworkState.Connected;

    /// <summary>Part of the network's name («MiWifi»); empty = any network.</summary>
    [ObservableProperty] private string _name = string.Empty;

    public override EventKind Kind => EventKind.Network;
    public override bool HasEnd => true;
    public override int DefaultCooldown => 2;

    public override string Summary => State == NetworkState.Offline ? Loc.T("event.summary.offline")
        : string.IsNullOrWhiteSpace(Name) ? Loc.T("event.summary.networkAny") : Loc.T("event.summary.network", Name.Trim());

    /// <summary>Whether it is «on» with these networks connected.</summary>
    public bool IsActive(IReadOnlyCollection<string> networks)
    {
        if (State == NetworkState.Offline) return networks.Count == 0;
        if (string.IsNullOrWhiteSpace(Name)) return networks.Count > 0;
        var tokens = Engine.FuzzyMatch.Tokens(Name);
        return networks.Any(n =>
        {
            var folded = new string(n.Select(Engine.FuzzyMatch.Fold).ToArray());
            return tokens.All(token => folded.Contains(token, StringComparison.Ordinal));
        });
    }
}

/// <summary>
/// A playback device is connected (headphones, a USB or Bluetooth speaker…) or becomes the default output — or, with
/// <see cref="Input"/>, a recording one (a microphone); it ends when it is disconnected or stops being the default.
/// </summary>
public sealed partial class AudioDeviceTrigger : EventTrigger
{
    [ObservableProperty] private AudioDeviceEvent _event = AudioDeviceEvent.Connected;

    /// <summary>Recording devices (microphones) instead of playback ones.</summary>
    [ObservableProperty] private bool _input;

    /// <summary>Part of the device's name («Auriculares», «JBL»); empty = any.</summary>
    [ObservableProperty] private string _name = string.Empty;

    public override EventKind Kind => EventKind.AudioDevice;
    public override bool HasEnd => true;
    public override int DefaultCooldown => 2;

    public override string Summary
    {
        get
        {
            var any = string.IsNullOrWhiteSpace(Name);
            var io = Input ? "Input" : string.Empty;
            return Event == AudioDeviceEvent.Connected
                ? any ? Loc.T("event.summary.audioAny" + io) : Loc.T("event.summary.audio", Name.Trim())
                : any ? Loc.T("event.summary.audioDefaultAny" + io) : Loc.T("event.summary.audioDefault" + io, Name.Trim());
        }
    }

    public bool Matches(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(Name)) return true;
        var folded = new string(deviceName.Select(Engine.FuzzyMatch.Fold).ToArray());
        return Engine.FuzzyMatch.Tokens(Name).All(token => folded.Contains(token, StringComparison.Ordinal));
    }
}

/// <summary>
/// Something outside asks for it by name: a UDP message «evento nombre» (the remote control port) or POST api/event
/// {"name": "nombre"} to the web server. Home Assistant, a script, another PC…
/// </summary>
public sealed partial class RemoteCallTrigger : EventTrigger
{
    /// <summary>The name to call it by; empty = the event's own name.</summary>
    [ObservableProperty] private string _name = string.Empty;

    public override EventKind Kind => EventKind.RemoteCall;
    public override string Summary => string.IsNullOrWhiteSpace(Name) ? Loc.T("event.summary.remoteCallOwn") : Loc.T("event.summary.remoteCall", Name.Trim());

    /// <summary>Whether a call named <paramref name="call"/> is for <paramref name="rule"/> (no case, no accents).</summary>
    public bool Answers(string call, EventRule rule)
    {
        var name = string.IsNullOrWhiteSpace(Name) ? rule.Name : Name;
        static string Fold(string s) => new(s.Trim().Select(Engine.FuzzyMatch.Fold).ToArray());
        return Fold(name).Length > 0 && Fold(name) == Fold(call);
    }
}

/// <summary>
/// A device is connected: a controller (Bluetooth or USB), a keyboard, a headset, a USB gadget…; it ends when it is
/// disconnected. Paired Bluetooth devices count only while they are connected.
/// </summary>
public sealed partial class GadgetTrigger : EventTrigger
{
    /// <summary>Part of the device's name («8Bitdo», «Xbox»); empty = any device.</summary>
    [ObservableProperty] private string _name = string.Empty;

    public override EventKind Kind => EventKind.Gadget;
    public override bool HasEnd => true;
    public override int DefaultCooldown => 2;
    public override string Summary => string.IsNullOrWhiteSpace(Name) ? Loc.T("event.summary.gadgetAny") : Loc.T("event.summary.gadget", Name.Trim());

    public bool Matches(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(Name)) return true;
        var folded = new string(deviceName.Select(Engine.FuzzyMatch.Fold).ToArray());
        return Engine.FuzzyMatch.Tokens(Name).All(token => folded.Contains(token, StringComparison.Ordinal));
    }
}

/// <summary>
/// The processor, the graphics card or the memory stays above (or below) a % for a while: a game or a render starts,
/// a download or a long task has finished… It ends when that stops for the same time.
/// </summary>
public sealed partial class LoadTrigger : EventTrigger
{
    [ObservableProperty] private LoadResource _resource = LoadResource.Cpu;
    [ObservableProperty] private LoadComparison _comparison = LoadComparison.Above;
    [ObservableProperty] private int _percent = 80;

    /// <summary>How long it must hold, in seconds.</summary>
    [ObservableProperty] private int _seconds = 60;

    public override EventKind Kind => EventKind.Load;
    public override bool HasEnd => true;
    public override string Summary => Loc.T($"event.summary.load.{Comparison}", Loc.T($"enum.loadResource.{Resource}"), Percent, Seconds);

    /// <summary>Whether a reading of <paramref name="percent"/> % is on the side it waits for.</summary>
    public bool Holds(int percent) => Comparison == LoadComparison.Above ? percent > Percent : percent < Percent;
}

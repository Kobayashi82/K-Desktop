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
    }

    /// <summary>Raised whenever the event or anything inside it changes (autosave, detector rebuild).</summary>
    public event EventHandler? Modified;

    [JsonIgnore] public string Summary => Trigger.Summary;

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

/// <summary>«Solo si»: a time range, days and a program open or not open.</summary>
public partial class EventConditions : ObservableObject
{
    /// <summary>«HH:mm»; empty = no time range. From 22:00 to 06:00 crosses midnight.</summary>
    [ObservableProperty] private string _timeFrom = string.Empty;
    [ObservableProperty] private string _timeTo = string.Empty;
    [ObservableProperty] private WeekDays _days;
    [ObservableProperty] private string _processRunning = string.Empty;
    [ObservableProperty] private string _processNotRunning = string.Empty;

    [JsonIgnore]
    public bool IsEmpty => Days == WeekDays.None && string.IsNullOrWhiteSpace(TimeFrom) && string.IsNullOrWhiteSpace(TimeTo) &&
                           string.IsNullOrWhiteSpace(ProcessRunning) && string.IsNullOrWhiteSpace(ProcessNotRunning);

    /// <summary>
    /// Whether it may fire at <paramref name="now"/>. <paramref name="isRunning"/> answers for a normalised process
    /// name. <paramref name="reason"/> tells why not (for the log).
    /// </summary>
    public bool Allows(DateTime now, Func<string, bool> isRunning, out string reason)
    {
        reason = string.Empty;
        if (!Days.Includes(now.DayOfWeek))
        {
            reason = "día de la semana";
            return false;
        }
        var from = EventTime.Parse(TimeFrom);
        var to = EventTime.Parse(TimeTo);
        if ((from is not null || to is not null) && !InRange(TimeOnly.FromDateTime(now), from ?? TimeOnly.MinValue, to ?? TimeOnly.MaxValue))
        {
            reason = "franja horaria";
            return false;
        }
        var running = Input.ProcessNames.Normalize(ProcessRunning);
        if (running.Length > 0 && !isRunning(running))
        {
            reason = $"{running} no está abierto";
            return false;
        }
        var notRunning = Input.ProcessNames.Normalize(ProcessNotRunning);
        if (notRunning.Length > 0 && isRunning(notRunning))
        {
            reason = $"{notRunning} está abierto";
            return false;
        }
        return true;
    }

    /// <summary>From ≤ time &lt; to; when from &gt; to the range crosses midnight (22:00–06:00).</summary>
    public static bool InRange(TimeOnly time, TimeOnly from, TimeOnly to) =>
        from <= to ? time >= from && (time < to || to == TimeOnly.MaxValue) : time >= from || time < to;

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
}

public enum EventKind
{
    AppStart, AppExit, Time, Interval, ProcessStart, Foreground, Idle, Session,
    AudioDevice, Device, Network, PowerSource, File,
}

public enum SessionEvent { Lock, Unlock, Suspend, Resume }

/// <summary>Where the PC takes its power from (laptops).</summary>
public enum PowerSource { Battery, Mains }

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

/// <summary>At a time of day, on some days.</summary>
public sealed partial class TimeTrigger : EventTrigger
{
    [ObservableProperty] private string _time = "09:00";
    [ObservableProperty] private WeekDays _days;

    /// <summary>If K-Desktop wasn't running (or the PC was asleep) at that time, run it as soon as it can.</summary>
    [ObservableProperty] private bool _runIfMissed;

    public override EventKind Kind => EventKind.Time;

    public override string Summary
    {
        get
        {
            var time = EventTime.Parse(Time)?.ToString("H:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "?";
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

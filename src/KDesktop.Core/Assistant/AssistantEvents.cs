using System.Globalization;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Input;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Assistant;

/// <summary>
/// The events the assistants make (B8, <see cref="MakeEventMessage"/>): of any kind, saved at once and marked with who made
/// them (<see cref="EventRule.CreatedBy"/>). They show in Eventos like any other, and the user can change or delete them
/// there; the assistant tells what it made and «deshaz» removes it. Changed on the thread that owns the configuration.
/// </summary>
public static class AssistantEvents
{
    /// <summary>Events made by assistants kept at most (with the programmed orders).</summary>
    public const int MaxMade = 100;

    private const int NameLength = 60;

    /// <summary>What can fire an event, as the model names it.</summary>
    public static readonly IReadOnlyList<string> Kinds =
    [
        "start", "exit", "time", "every", "program_opens", "program_in_front", "idle", "lock", "unlock", "sleep", "wake",
        "on_battery", "on_mains", "file", "drive", "network", "offline", "audio_device", "device", "remote_call", "load",
        "battery", "sunrise", "sunset", "screen_connected", "screen_disconnected",
    ];

    /// <param name="processOf">The process name of a program as the user names it (null: <see cref="ProcessOf"/>).</param>
    public static MadeEventMessage Handle(AppConfig config, MakeEventMessage request, DateTime nowLocal, Func<string, string>? processOf = null) => request.Op switch
    {
        "add" when request.Event is { } spec => Add(config, request.Id, request.Assistant ?? AssistantTargeting.KDesktop, spec, nowLocal, processOf ?? ProcessOf),
        "remove" => Remove(config, request),
        _ => new MadeEventMessage(request.Id, false, Reason: "op"),
    };

    private static MadeEventMessage Remove(AppConfig config, MakeEventMessage request)
    {
        // Only one an assistant made: the user's own events are never removed this way.
        var rule = config.Events.FirstOrDefault(e => e.Id == request.Remove && e.CreatedBy is not null);
        if (rule is null) return new MadeEventMessage(request.Id, false, Reason: "notFound");
        config.Events.Remove(rule);
        Log.Info($"Asistente: quitado el evento «{rule.Name}»");
        return new MadeEventMessage(request.Id, true, rule.Id, rule.Name);
    }

    private static MadeEventMessage Add(AppConfig config, string id, string assistant, EventSpec spec, DateTime nowLocal, Func<string, string> processOf)
    {
        MadeEventMessage No(string reason) => new(id, false, Reason: reason);
        if (config.Events.Count(e => e.CreatedBy is not null) >= MaxMade) return No("tooMany");
        if (Trigger(spec, nowLocal, processOf, out var reason) is not { } trigger) return No(reason ?? "when");
        var programs = Programs(spec, processOf);

        var (commands, unknown) = AssistantSchedule.FindCommands(config, spec.Commands ?? []);
        var (endCommands, unknownEnd) = trigger.HasEnd ? AssistantSchedule.FindCommands(config, spec.EndCommands ?? []) : ([], []);
        var order = string.Join(' ', (spec.Order ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (commands.Count == 0 && endCommands.Count == 0 && order.Length == 0)
            return new MadeEventMessage(id, false, Reason: "nothing", Unknown: unknown.Count + unknownEnd.Count > 0 ? [.. unknown, .. unknownEnd] : null);

        var rule = new EventRule
        {
            Trigger = trigger,
            CreatedBy = assistant,
            // A date: only that day. Otherwise as asked.
            Once = spec.Once ?? trigger is TimeTrigger { HasDate: true },
            CooldownSeconds = trigger.DefaultCooldown,
        };
        foreach (var command in commands) rule.Commands.Add(command.Id);
        foreach (var command in endCommands) rule.EndCommands.Add(command.Id);
        if (order.Length > 0)
        {
            rule.Ask = new EventAsk
            {
                Assistant = assistant,
                Text = order,
                Tools = [.. (spec.Tools ?? []).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal)],
                Commands = [],
            };
        }
        AddConditions(rule, spec, processOf);
        var runs = commands.Select(c => c.Name).Concat(order.Length > 0 ? [$"«{order}»"] : []).ToList();
        var name = spec.Name?.Trim() is { Length: > 0 } given ? given : $"{trigger.Summary}: {string.Join(", ", runs)}";
        rule.Name = name.Length <= NameLength ? name : name[..NameLength].TrimEnd() + "…";
        config.Events.Add(rule);
        unknown = [.. unknown, .. unknownEnd];
        Log.Info($"Asistente: «{assistant}» crea el evento «{rule.Name}» ({rule.Summary})" + (unknown.Count > 0 ? $"; no existen: {string.Join(", ", unknown)}" : string.Empty));
        return new MadeEventMessage(id, true, rule.Id, rule.Name, rule.Summary, runs, programs.Count > 0 ? programs : null, Unknown: unknown.Count > 0 ? unknown : null);
    }

    /// <summary>
    /// What fires the event asked for, or null with the <paramref name="reason"/> (<see cref="MadeEventMessage.Reason"/>). Also in the
    /// assistants' process, to tell what it is going to make (with the programs as they were said).
    /// </summary>
    public static EventTrigger? Trigger(EventSpec spec, DateTime nowLocal, Func<string, string> processOf, out string? reason)
    {
        reason = null;
        EventTrigger? trigger;
        var when = (spec.When ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        var programs = Programs(spec, processOf);
        var match = spec.Match?.Trim() ?? string.Empty;
        switch (when)
        {
            case "start": trigger = new AppStartTrigger(); break;
            case "exit": trigger = new AppExitTrigger(); break;
            case "time":
                if (EventTime.Parse(spec.Time) is not { } time) { reason = "time"; return null; }
                var timed = new TimeTrigger { Time = time.ToString("HH:mm", CultureInfo.InvariantCulture), Days = Days(spec.Days) };
                if (EventTime.ParseDate(spec.Date) is { } date)
                {
                    if (date.ToDateTime(time) <= nowLocal) { reason = "past"; return null; }
                    timed.Date = EventTime.FormatDate(date);
                }
                trigger = timed;
                break;
            case "every": trigger = new IntervalTrigger { Minutes = Math.Clamp(spec.Minutes ?? 60, 1, 24 * 60) }; break;
            case "idle": trigger = new IdleTrigger { Minutes = Math.Clamp(spec.Minutes ?? 15, 1, 24 * 60) }; break;
            case "program_opens" or "program_in_front":
                if (programs.Count == 0) { reason = "programs"; return null; }
                trigger = when == "program_opens" ? new ProcessStartTrigger { Processes = [.. programs] } : new ForegroundTrigger { Processes = [.. programs] };
                break;
            case "lock" or "unlock" or "sleep" or "wake":
                trigger = new SessionTrigger { Event = when switch { "lock" => SessionEvent.Lock, "unlock" => SessionEvent.Unlock, "sleep" => SessionEvent.Suspend, _ => SessionEvent.Resume } };
                break;
            case "on_battery" or "on_mains": trigger = new PowerSourceTrigger { Source = when == "on_battery" ? PowerSource.Battery : PowerSource.Mains }; break;
            case "file":
                var folder = Environment.ExpandEnvironmentVariables(spec.Folder?.Trim().Trim('"') ?? string.Empty);
                if (folder.Length == 0 || !Directory.Exists(folder)) { reason = "folder"; return null; }
                trigger = new FileTrigger
                {
                    Folder = folder,
                    Change = Enum.TryParse<FileChange>(spec.What?.Trim(), true, out var change) ? change : FileChange.Created,
                    Filter = string.IsNullOrWhiteSpace(spec.Filter) ? "*" : spec.Filter.Trim(),
                };
                break;
            case "drive": trigger = new DriveTrigger { Filter = match }; break;
            case "network": trigger = new NetworkTrigger { State = NetworkState.Connected, Name = match }; break;
            case "offline": trigger = new NetworkTrigger { State = NetworkState.Offline }; break;
            case "audio_device":
                trigger = new AudioDeviceTrigger { Name = match, Event = spec.What?.Trim().ToLowerInvariant() is "default" ? AudioDeviceEvent.BecomesDefault : AudioDeviceEvent.Connected };
                break;
            case "device": trigger = new GadgetTrigger { Name = match }; break;
            case "remote_call": trigger = new RemoteCallTrigger { Name = match }; break;
            case "load":
                trigger = new LoadTrigger
                {
                    Resource = Enum.TryParse<LoadResource>(spec.What?.Trim(), true, out var resource) ? resource : LoadResource.Cpu,
                    Comparison = spec.Above == false ? LoadComparison.Below : LoadComparison.Above,
                    Percent = Math.Clamp(spec.Percent ?? 80, 1, 99),
                    Seconds = Math.Clamp((spec.Minutes ?? 1) * 60, 10, 3600),
                };
                break;
            case "battery":
                trigger = new BatteryTrigger { Comparison = spec.Above == true ? LoadComparison.Above : LoadComparison.Below, Percent = Math.Clamp(spec.Percent ?? 20, 1, 99) };
                break;
            case "sunrise" or "sunset":
                trigger = new SunTrigger { Event = when == "sunrise" ? SunEvent.Sunrise : SunEvent.Sunset, OffsetMinutes = Math.Clamp(spec.Offset ?? 0, -240, 240) };
                break;
            case "screen_connected" or "screen_disconnected":
                trigger = new DisplayTrigger { Event = when == "screen_connected" ? DisplayEvent.Connected : DisplayEvent.Disconnected, Filter = match };
                break;
            default: { reason = "when"; return null; }
        }
        return trigger;
    }

    /// <summary>The process names of the programs said.</summary>
    public static List<string> Programs(EventSpec spec, Func<string, string> processOf) =>
        [.. (spec.Programs ?? []).Select(p => p.Trim()).Where(p => p.Length > 0).Select(processOf).Where(p => p.Length > 0).Distinct()];

    /// <summary>«Solo si»: a time range and days, and a program open.</summary>
    private static void AddConditions(EventRule rule, EventSpec spec, Func<string, string> processOf)
    {
        var from = EventTime.Parse(spec.OnlyFrom);
        var to = EventTime.Parse(spec.OnlyTo);
        var days = Days(spec.OnlyDays);
        if (from is not null || to is not null || days != WeekDays.None)
        {
            rule.Conditions.Items.Add(new ConditionAction
            {
                Condition = ConditionKind.Schedule,
                TimeFrom = from?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty,
                TimeTo = to?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty,
                Days = days,
            });
        }
        if (spec.OnlyIfRunning?.Trim() is { Length: > 0 } program && processOf(program) is { Length: > 0 } process)
            rule.Conditions.Items.Add(new ConditionAction { Condition = ConditionKind.ProcessRunning, Text = process });
    }

    /// <summary>«daily», «workdays», «weekend», or English days of the week («monday, friday»).</summary>
    public static WeekDays Days(string? text)
    {
        var words = (text ?? string.Empty).ToLowerInvariant().Split([',', ' ', ';', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var days = WeekDays.None;
        foreach (var word in words)
        {
            days |= word switch
            {
                "workdays" or "weekdays" => WeekDays.Workdays,
                "weekend" or "weekends" => WeekDays.Weekend,
                _ when Enum.TryParse<DayOfWeek>(word.TrimEnd('s'), true, out var day) && !int.TryParse(word, out _) => WeekDaysExtensions.Of(day),
                _ => WeekDays.None,
            };
        }
        return days == (WeekDays.Workdays | WeekDays.Weekend) ? WeekDays.None : days;
    }

    /// <summary>
    /// The process name of a program as the user names it: one that is running («steam»), else the exe of the installed
    /// app («FallGuys_client»), else the words themselves («spotify»).
    /// </summary>
    public static string ProcessOf(string said)
    {
        if (AppFinder.FindProcess(said) is { } running) return running;
        if (AppFinder.FindApp(said, InstalledApps.Current) is { } app && app.Id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return ProcessNames.Normalize(Path.GetFileName(app.Id));
        return ProcessNames.Normalize(said.Replace(" ", string.Empty, StringComparison.Ordinal));
    }
}

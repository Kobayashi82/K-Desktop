using KDesktop.Core.Localization;

namespace KDesktop.Core.Model;

/// <summary>Display data of a type of event (texts in the language files: <c>event.kind.{Kind}</c> and <c>.desc</c>).</summary>
/// <param name="Symbol">Fluent symbol of its default icon.</param>
/// <param name="Color">Colour of the default icon (K-Desktop palette).</param>
public sealed record EventKindInfo(EventKind Kind, string Symbol, string Color, Func<EventTrigger> Create)
{
    public string Name => Loc.T($"event.kind.{Kind}");
    public string Description => Loc.T($"event.kind.{Kind}.desc");

    /// <summary>Default icon of the events of this type ("kd:Clock24:blue").</summary>
    public string Icon => $"kd:{Symbol}:{Color}";

    public override string ToString() => Name;
}

public static class EventCatalog
{
    /// <summary>In the order of the «Cuándo» list: starting and exiting K-Desktop first.</summary>
    public static IReadOnlyList<EventKindInfo> All { get; } =
    [
        new(EventKind.AppStart, "Play24", "green", () => new AppStartTrigger()),
        new(EventKind.AppExit, "Power24", "red", () => new AppExitTrigger()),
        new(EventKind.Time, "Clock24", "blue", () => new TimeTrigger()),
        new(EventKind.Interval, "ArrowRepeatAll24", "sky", () => new IntervalTrigger()),
        new(EventKind.ProcessStart, "WindowApps24", "indigo", () => new ProcessStartTrigger()),
        new(EventKind.Foreground, "WindowArrowUp24", "purple", () => new ForegroundTrigger()),
        new(EventKind.Idle, "WeatherMoon24", "dark", () => new IdleTrigger()),
        new(EventKind.Session, "LockClosed24", "orange", () => new SessionTrigger()),
        new(EventKind.PowerSource, "BatteryCharge24", "lime", () => new PowerSourceTrigger()),
        new(EventKind.Gadget, "XboxController24", "green", () => new GadgetTrigger()),
        new(EventKind.AudioDevice, "HeadphonesSoundWave24", "teal", () => new AudioDeviceTrigger()),
        new(EventKind.Device, "UsbStick24", "gray", () => new DriveTrigger()),
        new(EventKind.Network, "Wifi124", "teal", () => new NetworkTrigger()),
        new(EventKind.File, "DocumentAdd24", "yellow", () => new FileTrigger()),
        new(EventKind.RemoteCall, "PlugConnected24", "pink", () => new RemoteCallTrigger()),
    ];

    private static readonly Dictionary<EventKind, EventKindInfo> ByKind = All.ToDictionary(k => k.Kind);

    public static EventKindInfo Get(EventKind kind) => ByKind[kind];

    /// <summary>
    /// A trigger of another type for the same event, keeping what still makes sense (the programs between «al abrir» and
    /// «en primer plano»).
    /// </summary>
    public static EventTrigger Convert(EventTrigger from, EventKind to)
    {
        var trigger = Get(to).Create();
        var processes = from switch
        {
            ProcessStartTrigger p => p.Processes,
            ForegroundTrigger f => f.Processes,
            _ => null,
        };
        if (processes is not null)
        {
            var target = trigger switch
            {
                ProcessStartTrigger p => p.Processes,
                ForegroundTrigger f => f.Processes,
                _ => null,
            };
            if (target is not null) foreach (var name in processes) target.Add(name);
        }
        return trigger;
    }
}

using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Assistant;

/// <summary>
/// K-Desktop's side of a <see cref="SystemMessage"/> (B9): one action of the catalogue for one group of tools (volume,
/// audio devices, screen, theme, radios, power, screenshot, power plan, keep awake), made for the occasion and run like a
/// command (<see cref="AssistantCommands.RunCommandAsync"/>: its risk from <see cref="ActionRisk"/>, the user's «ask
/// before», the history and the assistant's steps). Only these: the assistant's process never sends an action itself.
/// </summary>
public static class AssistantSystem
{
    /// <summary>The groups and what each can do.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Ops = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["volume"] = ["set", "up", "down", "mute", "unmute"],
        ["audio"] = ["output", "input"],
        ["display"] = ["brightness", "brighter", "darker", "projection", "off"],
        ["theme"] = ["light", "dark", "toggle"],
        ["radio"] = ["on", "off", "toggle"],
        ["power"] = ["lock", "sleep", "hibernate", "shutdown", "restart", "sign_out"],
        ["screenshot"] = ["take"],
        ["powerPlan"] = ["set", "next"],
        ["awake"] = ["on", "off"],
    };

    /// <summary>Volume and brightness steps, in %, when no value is said.</summary>
    public const int Step = 10;

    /// <summary>Shutting down, restarting or signing out: seconds of countdown (to cancel it) when none is said.</summary>
    public const int PowerDelay = 10;

    /// <param name="Name">What it does, for the user (the action's summary).</param>
    /// <param name="Reason">Why not: <c>op</c>, <c>noDevice</c>, <c>noPlan</c>.</param>
    public sealed record Plan(Command? Command, string Name, string? Reason);

    /// <param name="playback">The speakers (null: Windows'); <paramref name="recording"/> the microphones; <paramref name="plans"/> the power plans.</param>
    public static Plan Build(SystemMessage request, Func<IReadOnlyList<AudioDeviceInfo>>? playback = null, Func<IReadOnlyList<AudioDeviceInfo>>? recording = null,
        Func<IReadOnlyList<PowerPlan>>? plans = null)
    {
        var op = request.Op.Trim();
        var text = request.Text.Trim();
        if (!Ops.TryGetValue(request.Group, out var ops) || !ops.Contains(op)) return new Plan(null, op, "op");
        CommandAction? action = request.Group switch
        {
            "volume" => new VolumeAction
            {
                Target = text.Equals("microphone", StringComparison.OrdinalIgnoreCase) ? VolumeTarget.Microphone : VolumeTarget.System,
                Mode = op switch { "up" => VolumeMode.Up, "down" => VolumeMode.Down, "mute" => VolumeMode.Mute, "unmute" => VolumeMode.Unmute, _ => VolumeMode.Set },
                Value = Math.Clamp(request.Value ?? (op == "set" ? 50 : Step), 0, 100),
                ShowOsd = true,
            },
            "audio" => Audio(op == "input", text, op == "input" ? recording ?? AudioDevices.Recording : playback ?? AudioDevices.Playback),
            "display" => new DisplayAction
            {
                Operation = op switch { "projection" => DisplayOperation.Projection, "off" => DisplayOperation.MonitorOff, _ => DisplayOperation.Brightness },
                BrightnessMode = op switch { "brighter" => LevelChange.Up, "darker" => LevelChange.Down, _ => LevelChange.Set },
                Brightness = Math.Clamp(request.Value ?? (op == "brightness" ? 50 : Step), 0, 100),
                Projection = Enum.TryParse<ProjectionMode>(text, true, out var projection) ? projection : ProjectionMode.Extend,
                ShowOsd = true,
            },
            "theme" => new WindowsThemeAction { Mode = op switch { "light" => ThemeChoice.Light, "dark" => ThemeChoice.Dark, _ => ThemeChoice.Toggle }, ShowOsd = true },
            "radio" => new RadioAction
            {
                Radio = text.Contains("blue", StringComparison.OrdinalIgnoreCase) ? RadioDevice.Bluetooth : RadioDevice.WiFi,
                Mode = op switch { "on" => SwitchMode.On, "off" => SwitchMode.Off, _ => SwitchMode.Toggle },
                ShowOsd = true,
            },
            "power" => new PowerAction
            {
                Mode = op switch
                {
                    "lock" => PowerMode.Lock,
                    "sleep" => PowerMode.Sleep,
                    "hibernate" => PowerMode.Hibernate,
                    "restart" => PowerMode.Restart,
                    "sign_out" => PowerMode.LogOff,
                    _ => PowerMode.Shutdown,
                },
                // Locking and sleeping at once; the rest with a countdown that can be cancelled.
                DelaySeconds = op is "lock" or "sleep" or "hibernate" ? 0 : Math.Clamp(request.Value ?? PowerDelay, 0, 3600),
                ShowOsd = true,
            },
            "screenshot" => new ScreenshotAction
            {
                Area = text.ToLowerInvariant() switch
                {
                    "window" => CaptureArea.ActiveWindow,
                    "all" => CaptureArea.AllMonitors,
                    "region" => CaptureArea.Region,
                    _ => CaptureArea.Monitor,
                },
                Copy = true,
                Save = true,
                ShowOsd = true,
            },
            "powerPlan" => PowerPlanOf(op, text, plans ?? PowerPlans.All),
            _ => new KeepAwakeAction
            {
                Mode = op == "off" ? SwitchMode.Off : SwitchMode.On,
                For = request.Value is > 0 ? AwakeFor.Minutes : AwakeFor.UntilOff,
                Minutes = Math.Clamp(request.Value ?? 60, 1, 24 * 60),
                ShowOsd = true,
            },
        };
        if (action is null) return new Plan(null, text, request.Group == "powerPlan" ? "noPlan" : "noDevice");
        var command = new Command { Name = action.Summary };
        command.Actions.Add(action);
        return new Plan(command, action.Summary, null);
    }

    /// <summary>The device named (part of its name or adapter); empty: the next one (rotating through all, like the action does).</summary>
    private static AudioOutputAction? Audio(bool input, string name, Func<IReadOnlyList<AudioDeviceInfo>> devices)
    {
        AudioOutputAction action = input ? new AudioInputAction() : new AudioOutputAction();
        action.ShowOsd = true;
        if (name.Length == 0) return action;
        var words = TextSimilarity.Words(name);
        var found = devices().Where(d => d.IsActive)
            .FirstOrDefault(d => words.All(w => TextSimilarity.Words(d.Name + " " + d.Adapter).Any(x => TextSimilarity.WordSimilarity(w, x) >= 0.85)));
        if (found is null) return null;
        action.Devices.Add(new AudioDeviceRef { Id = found.Id, Name = found.Name });
        return action;
    }

    private static PowerPlanAction? PowerPlanOf(string op, string name, Func<IReadOnlyList<PowerPlan>> plans)
    {
        if (op == "next") return new PowerPlanAction { Mode = PowerPlanMode.Cycle, ShowOsd = true };
        var words = TextSimilarity.Words(name);
        if (words.Length == 0) return null;
        var plan = plans().FirstOrDefault(p => words.All(w => TextSimilarity.Words(p.Name).Any(x => TextSimilarity.WordSimilarity(w, x) >= 0.85)));
        return plan is null ? null : new PowerPlanAction { Mode = PowerPlanMode.Set, PlanId = plan.Id.ToString(), PlanName = plan.Name, ShowOsd = true };
    }

    /// <summary>Finds what to do and does it, like <see cref="AssistantActs.ActAsync"/>.</summary>
    public static async Task<EndMessage> RunAsync(Engine.CommandEngine engine, SystemMessage request, string who, Func<Command, ToolRisk, Task<string>> ask,
        AssistantChoreography? choreography = null, Func<Command, bool>? allowed = null)
    {
        if (engine.IsPaused) return new EndMessage(request.Run, "failed", [Localization.Loc.T("pause.refused")]);
        var plan = Build(request);
        if (plan.Command is null) return new EndMessage(request.Run, "failed", [], plan.Name, plan.Reason);
        var end = await AssistantCommands.RunCommandAsync(engine, plan.Command, request.Run, who, ask, choreography, allowed, AssistantCommands.SourceOf(request.Shared)).ConfigureAwait(false);
        return end with { Name = plan.Name };
    }
}

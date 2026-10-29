using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// K-Desktop's own actions for the assistants (B9, 5.0.352), by groups and not as one tool with a huge schema (small models
/// get lost in it, and <c>ToolChooser</c> picks better by meaning): each sends a <c>system</c> request and K-Desktop runs
/// that one action of its catalogue (<see cref="AssistantSystem"/>), asking like its risk in <see cref="ActionRisk"/> and the
/// user's «ask before». So the tools themselves are «safe»: asking here too would ask twice.
/// </summary>
public abstract class SystemTool(ICommandBridge bridge) : ITool
{
    public abstract string Name { get; }

    public abstract string Description { get; }

    public abstract string ParametersSchemaJson { get; }

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public virtual string Intent(JsonElement arguments) => Loc.T($"tool.{Name}.title");

    /// <summary>The group, operation, value and text for <see cref="AssistantSystem"/>; null when the arguments make no sense (with why).</summary>
    protected abstract (string Group, string Op, int? Value, string Text)? Request(JsonElement arguments, out string? problem);

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (Request(arguments, out var problem) is not var (group, op, value, text)) return ToolResult.Fail(problem ?? "Those arguments are not valid.");
        var end = await bridge.SystemAsync(group, op, value, text, context.Assistant, cancellationToken).ConfigureAwait(false);
        if (end is null) return ToolResult.Fail("K-Desktop did not answer.");
        var name = end.Name is { Length: > 0 } found ? found : Loc.T($"tool.{Name}.title");
        return end.Status switch
        {
            "ok" => new ToolResult($"Done: {name}.") { Done = name, Reply = Loc.T("tool.system.reply", name), Acted = true },
            "declined" => new ToolResult("Not done: the user said no to it. Tell them you did not do it.")
            {
                Done = Loc.T("tool.run_command.declined", name),
                Reply = Loc.T("tool.run_command.replyDeclined", name),
                Failed = true,
            },
            _ when end.Reason is { } reason => NotDone(reason, text),
            _ => new ToolResult("Failed. " + string.Join(" ", end.Messages)) { Done = Loc.T("tool.run_command.failed", name), Failed = true },
        };
    }

    /// <summary>Why K-Desktop did not do it, for the model and the user.</summary>
    protected virtual ToolResult NotDone(string reason, string text) => ToolResult.Fail($"Not done ({reason}).");

    /// <summary>A text argument, lowercase, if it is one of <paramref name="allowed"/>.</summary>
    protected static string? OneOf(JsonElement arguments, string name, params string[] allowed) =>
        arguments.GetString(name)?.Trim().ToLowerInvariant() is { } given && allowed.Contains(given) ? given : null;

    protected static int? Whole(JsonElement arguments, string name) => arguments.GetNumber(name) is { } n ? (int)Math.Round(n) : null;

    protected static string Choices(params string[] values) => string.Join(",", values.Select(v => $"\"{v}\""));
}

/// <summary>«sube el volumen», «ponlo al 30», «silencia el micro».</summary>
public sealed class SetVolumeTool(ICommandBridge bridge) : SystemTool(bridge)
{
    public override string Name => "set_volume";

    public override string Description => "Changes the volume of the whole PC (the speakers) or of the microphone: set to a level, up, down, mute or unmute. For one program's volume use control_app.";

    public override string ParametersSchemaJson { get; } =
        $$$"""{"type":"object","properties":{"action":{"type":"string","enum":[{{{Choices("set", "up", "down", "mute", "unmute")}}}]},"value":{"type":"integer","description":"set: the level 0-100; up/down: how much (default 10)"},"device":{"type":"string","enum":[{{{Choices("speakers", "microphone")}}}],"description":"Default: speakers"}},"required":["action"]}""";

    protected override (string, string, int?, string)? Request(JsonElement arguments, out string? problem)
    {
        problem = null;
        var action = OneOf(arguments, "action", "set", "up", "down", "mute", "unmute");
        var value = Whole(arguments, "value");
        if (action is null) action = value is not null ? "set" : null;
        if (action is null)
        {
            problem = "Say the action: set, up, down, mute or unmute.";
            return null;
        }
        if (action == "set" && value is null)
        {
            problem = "Which level (0-100)?";
            return null;
        }
        return ("volume", action, value, OneOf(arguments, "device", "microphone", "mic") is null ? string.Empty : "microphone");
    }
}

/// <summary>«pon los cascos», «cambia a los altavoces», «usa el micro de la webcam».</summary>
public sealed class AudioDeviceTool(ICommandBridge bridge) : SystemTool(bridge)
{
    public override string Name => "audio_device";

    public override string Description => "Switches where the PC's sound comes out (headphones, speakers, the TV…) or which microphone it uses. Give part of the device's name; empty: the next one.";

    public override string ParametersSchemaJson { get; } =
        $$$$"""{"type":"object","properties":{"kind":{"type":"string","enum":[{{{{Choices("output", "input")}}}}],"description":"output: speakers/headphones (default); input: microphone"},"device":{"type":"string","description":"Part of the device's name (empty: the next one)"}}}""";

    protected override (string, string, int?, string)? Request(JsonElement arguments, out string? problem)
    {
        problem = null;
        return ("audio", OneOf(arguments, "kind", "input") ?? "output", null, ToolText.Clean(arguments.GetString("device")));
    }

    protected override ToolResult NotDone(string reason, string text)
    {
        if (reason != "noDevice") return base.NotDone(reason, text);
        var devices = AudioDevices.Playback().Concat(AudioDevices.Recording()).Where(d => d.IsActive).Select(d => $"{d.Name} ({(d.IsInput ? "microphone" : "output")})");
        return new ToolResult($"Not done: there is no device called \"{text}\". The devices are: {string.Join("; ", devices)}.")
        {
            Done = Loc.T("tool.audio_device.noDevice", text),
            Reply = Loc.T("tool.audio_device.noDevice", text),
            Failed = true,
        };
    }
}

/// <summary>«baja el brillo», «duplica la pantalla», «apaga la pantalla».</summary>
public sealed class DisplayTool(ICommandBridge bridge) : SystemTool(bridge)
{
    public override string Name => "display";

    public override string Description => "The screen: brightness (set, brighter, darker), projection like Win+P (internal: only the PC screen, clone, extend, external: only the second screen), or turning the screens off.";

    public override string ParametersSchemaJson { get; } =
        $$$"""{"type":"object","properties":{"action":{"type":"string","enum":[{{{Choices("brightness", "brighter", "darker", "projection", "off")}}}]},"value":{"type":"integer","description":"brightness: the level 0-100; brighter/darker: how much (default 10)"},"projection":{"type":"string","enum":[{{{Choices("internal", "clone", "extend", "external")}}}]}},"required":["action"]}""";

    protected override (string, string, int?, string)? Request(JsonElement arguments, out string? problem)
    {
        problem = null;
        var action = OneOf(arguments, "action", "brightness", "brighter", "darker", "projection", "off");
        var projection = OneOf(arguments, "projection", "internal", "clone", "extend", "external");
        action ??= projection is not null ? "projection" : null;
        if (action is null || action == "projection" && projection is null)
        {
            problem = "Say the action (brightness, brighter, darker, projection with its kind, or off).";
            return null;
        }
        return ("display", action, Whole(arguments, "value"), projection ?? string.Empty);
    }
}

/// <summary>«pon el modo oscuro».</summary>
public sealed class WindowsThemeTool(ICommandBridge bridge) : SystemTool(bridge)
{
    public override string Name => "windows_theme";

    public override string Description => "Puts Windows in light or dark mode (or switches it).";

    public override string ParametersSchemaJson { get; } = $$$"""{"type":"object","properties":{"mode":{"type":"string","enum":[{{{Choices("light", "dark", "toggle")}}}]}},"required":["mode"]}""";

    protected override (string, string, int?, string)? Request(JsonElement arguments, out string? problem)
    {
        problem = null;
        return ("theme", OneOf(arguments, "mode", "light", "dark") ?? "toggle", null, string.Empty);
    }
}

/// <summary>«apaga el bluetooth», «enciende el wifi».</summary>
public sealed class ToggleRadioTool(ICommandBridge bridge) : SystemTool(bridge)
{
    public override string Name => "toggle_radio";

    public override string Description => "Turns the Wi-Fi or the Bluetooth on or off.";

    public override string ParametersSchemaJson { get; } =
        $$$"""{"type":"object","properties":{"radio":{"type":"string","enum":[{{{Choices("wifi", "bluetooth")}}}]},"action":{"type":"string","enum":[{{{Choices("on", "off", "toggle")}}}]}},"required":["radio","action"]}""";

    protected override (string, string, int?, string)? Request(JsonElement arguments, out string? problem)
    {
        problem = null;
        var radio = arguments.GetString("radio")?.Contains("blue", StringComparison.OrdinalIgnoreCase) == true ? "bluetooth" : "wifi";
        return ("radio", OneOf(arguments, "action", "on", "off") ?? "toggle", null, radio);
    }
}

/// <summary>«bloquea el PC».</summary>
public sealed class LockPcTool(ICommandBridge bridge) : SystemTool(bridge)
{
    public override string Name => "lock_pc";

    public override string Description => "Locks the PC (the session stays open, like Win+L).";

    public override string ParametersSchemaJson => ToolText.NoParameters;

    protected override (string, string, int?, string)? Request(JsonElement arguments, out string? problem)
    {
        problem = null;
        return ("power", "lock", null, string.Empty);
    }
}

/// <summary>«apaga el ordenador», «reinicia», «suspende el PC».</summary>
public sealed class PowerPcTool(ICommandBridge bridge) : SystemTool(bridge)
{
    public override string Name => "power_pc";

    public override string Description => "Shuts down, restarts, signs out, sleeps or hibernates the PC (shutdown, restart and sign_out after a short countdown that can be cancelled). Always the last thing of an order.";

    public override string ParametersSchemaJson { get; } =
        $$$"""{"type":"object","properties":{"action":{"type":"string","enum":[{{{Choices("shutdown", "restart", "sign_out", "sleep", "hibernate")}}}]},"seconds":{"type":"integer","description":"Countdown before shutting down, restarting or signing out (default 10)"}},"required":["action"]}""";

    protected override (string, string, int?, string)? Request(JsonElement arguments, out string? problem)
    {
        problem = null;
        if (OneOf(arguments, "action", "shutdown", "restart", "sign_out", "sleep", "hibernate") is not { } action)
        {
            problem = "Say the action: shutdown, restart, sign_out, sleep or hibernate.";
            return null;
        }
        return ("power", action, Whole(arguments, "seconds"), string.Empty);
    }
}

/// <summary>«haz una captura», «captura esta ventana».</summary>
public sealed class TakeScreenshotTool(ICommandBridge bridge) : SystemTool(bridge)
{
    public override string Name => "take_screenshot";

    public override string Description => "Takes a screenshot, copies it and saves it in the screenshots folder: the screen, the window in front, all the screens, or a region the user picks with the mouse. To understand what is on screen use look_at_screen.";

    public override string ParametersSchemaJson { get; } = $$$$"""{"type":"object","properties":{"area":{"type":"string","enum":[{{{{Choices("screen", "window", "all", "region")}}}}],"description":"Default: screen"}}}""";

    protected override (string, string, int?, string)? Request(JsonElement arguments, out string? problem)
    {
        problem = null;
        return ("screenshot", "take", null, OneOf(arguments, "area", "window", "all", "region") ?? "screen");
    }
}

/// <summary>«pon alto rendimiento», «cambia el plan de energía».</summary>
public sealed class PowerPlanTool(ICommandBridge bridge) : SystemTool(bridge)
{
    public override string Name => "power_plan";

    public override string Description => "Switches the Windows power plan (high performance, balanced, power saver… by its name) or to the next one.";

    public override string ParametersSchemaJson => ToolText.Optional(("plan", "string", "Part of the plan's name (empty: the next one)"));

    protected override (string, string, int?, string)? Request(JsonElement arguments, out string? problem)
    {
        problem = null;
        var plan = ToolText.Clean(arguments.GetString("plan"));
        return ("powerPlan", plan.Length == 0 ? "next" : "set", null, plan);
    }

    protected override ToolResult NotDone(string reason, string text) => reason != "noPlan" ? base.NotDone(reason, text)
        : new ToolResult($"Not done: there is no power plan called \"{text}\". The plans are: {string.Join(", ", PowerPlans.All().Select(p => p.Name))}.")
        {
            Done = Loc.T("tool.power_plan.noPlan", text),
            Reply = Loc.T("tool.power_plan.noPlan", text),
            Failed = true,
        };
}

/// <summary>«no dejes que se suspenda en dos horas», «ya puede suspenderse».</summary>
public sealed class KeepAwakeTool(ICommandBridge bridge) : SystemTool(bridge)
{
    public override string Name => "keep_awake";

    public override string Description => "Keeps the PC from going to sleep (for some minutes, or until told to stop), or lets it sleep again.";

    public override string ParametersSchemaJson { get; } =
        $$$"""{"type":"object","properties":{"action":{"type":"string","enum":[{{{Choices("on", "off")}}}]},"minutes":{"type":"integer","description":"on: for how long (empty: until off)"}},"required":["action"]}""";

    protected override (string, string, int?, string)? Request(JsonElement arguments, out string? problem)
    {
        problem = null;
        return ("awake", OneOf(arguments, "action", "off") ?? "on", Whole(arguments, "minutes"), string.Empty);
    }
}

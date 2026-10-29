using KDesktop.Core.Model;

namespace KDesktop.Core.Assistant;

/// <summary>When the assistant must ask before doing something (§6 and §8 of the plan).</summary>
public enum ToolRisk
{
    /// <summary>Without asking.</summary>
    Safe,

    /// <summary>Asks the first time; «yes, and don't ask again» turns it into Safe.</summary>
    AskOnce,

    /// <summary>Asks every time.</summary>
    AskAlways,
}

/// <summary>
/// The user's «Before doing something, ask» (setting <c>assistantAskBefore</c>): how much of <see cref="ToolRisk"/> is
/// actually asked. Asking for every command by voice tired the user out; by default only what can do real harm asks.
/// </summary>
public static class AskPolicy
{
    /// <summary>Never asks, not even to switch the PC off.</summary>
    public const string Never = "never";

    /// <summary>Only what always asks (power, scripts, simulated input, macros, web requests). The default.</summary>
    public const string Risky = "risky";

    /// <summary>Also the first time a command or tool is used, until «yes, and don't ask again».</summary>
    public const string First = "first";

    /// <summary>Whether to ask before something of <paramref name="risk"/>; <paramref name="trusted"/>: the user said not to ask again about it.</summary>
    public static bool Asks(ToolRisk risk, string? policy, bool trusted = false) => policy switch
    {
        Never => false,
        First => risk == ToolRisk.AskAlways || (risk == ToolRisk.AskOnce && !trusted),
        _ => risk == ToolRisk.AskAlways,
    };
}

/// <summary>
/// How risky a K-Desktop action or command is when the assistant runs it (tool ejecutar_comando, §6.1). K-Desktop
/// computes it (it has the commands) and sends it with each candidate of «matched».
/// </summary>
public static class ActionRisk
{
    /// <summary>Risk of one action on its own.</summary>
    public static ToolRisk Of(CommandAction action) => action switch
    {
        // Locking loses nothing (B9): the session is there when you come back.
        PowerAction { Mode: PowerMode.Lock } => ToolRisk.Safe,

        // Power, scripts, simulated input, macros and web requests can do anything: always ask (§6.1).
        PowerAction or RunScriptAction or SendInputAction or PlayMacroAction or HttpRequestAction => ToolRisk.AskAlways,

        // Closing may lose unsaved work; killing even more.
        AppControlAction { Mode: AppControlMode.Close, Force: true } => ToolRisk.AskAlways,
        AppControlAction { Mode: AppControlMode.Close } or WindowAction { Operation: WindowOperation.Close } => ToolRisk.AskOnce,

        // Switching off the screen or a radio (Wi-Fi) can leave the user without the PC or the network.
        DisplayAction { Operation: DisplayOperation.MonitorOff } => ToolRisk.AskOnce,
        RadioAction { Mode: not SwitchMode.On } => ToolRisk.AskOnce,

        // Turning commands and events on and off changes how K-Desktop behaves from then on.
        CommandControlAction { Mode: not CommandControlMode.Run } => ToolRisk.AskOnce,

        // Files: deleting and moving lose things where they were; a backup that cleans its destination too.
        FilesAction { Operation: FileOperation.Delete or FileOperation.Move } => ToolRisk.AskAlways,
        FilesAction { Operation: FileOperation.Backup, BackupMode: BackupMode.Mirror, MirrorDelete: true } => ToolRisk.AskOnce,

        _ => OfKind(action.Kind),
    };

    /// <summary>Risk by kind alone, ignoring the settings (the table of docs/assistant/acciones.md).</summary>
    public static ToolRisk OfKind(ActionKind kind) => kind switch
    {
        ActionKind.Power or ActionKind.RunScript or ActionKind.SendInput or ActionKind.PlayMacro or ActionKind.HttpRequest => ToolRisk.AskAlways,

        ActionKind.RunApp or ActionKind.OpenFolder or ActionKind.OpenUrl or ActionKind.AppControl or ActionKind.Volume or
        ActionKind.PlaySound or ActionKind.TopMost or ActionKind.HiddenFiles or ActionKind.ToggleDisabledIn or
        ActionKind.ShowMessage or ActionKind.ShowNotification or ActionKind.Files or ActionKind.OpenQuickMenu or ActionKind.CommandControl or ActionKind.Wait or
        ActionKind.AudioOutput or ActionKind.AudioInput or ActionKind.KBoard or ActionKind.Launcher or ActionKind.Window or ActionKind.Clipboard or
        ActionKind.WakeOnLan or ActionKind.Display or ActionKind.PowerPlan or ActionKind.WindowsTheme or ActionKind.Radio or ActionKind.Media or ActionKind.KeepAwake or ActionKind.SendToPhone or
        ActionKind.Condition or ActionKind.Screenshot or ActionKind.ScreenText or ActionKind.AudioRecord or ActionKind.Dictate or ActionKind.Magnifier or ActionKind.AssistantExtra or ActionKind.AssistantAsk or ActionKind.AssistantSay or ActionKind.AssistantBrain => ToolRisk.Safe,

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Acción sin riesgo: añádela a ActionRisk."),
    };

    /// <summary>
    /// Risk of running a whole command: the highest of its enabled actions, of the commands it runs (followed, with
    /// loops cut) and of both branches of every «Si…» (all its actions are looked at, whichever way it goes). Never
    /// below <see cref="ToolRisk.AskOnce"/>, so a command is confirmed the first time (§6.1), unless the user marked
    /// it as trusted. Trust never lifts what always asks (power, scripts, simulated input…, also when a called command has
    /// it).
    /// </summary>
    public static ToolRisk OfCommand(Command command, Func<string, Command?> findCommand, bool trusted = false)
    {
        var risk = Walk(command, findCommand, []);
        if (risk == ToolRisk.AskAlways) return risk;
        return trusted ? ToolRisk.Safe : ToolRisk.AskOnce;
    }

    private static ToolRisk Walk(Command command, Func<string, Command?> findCommand, HashSet<string> seen)
    {
        if (!seen.Add(command.Id)) return ToolRisk.Safe;
        var risk = ToolRisk.Safe;
        foreach (var action in command.Actions)
        {
            if (!action.Enabled) continue;
            var own = Of(action);
            if (action is CommandControlAction { Mode: CommandControlMode.Run, Target: CommandControlTarget.Command } run &&
                findCommand(run.CommandId) is { } called)
                own = Max(own, Walk(called, findCommand, seen));
            // «Si… si no, ejecutar otro comando» (5.0.339): that one may run too.
            if (action is ConditionAction { Else: ConditionElse.RunCommand } condition && findCommand(condition.ElseCommandId) is { } instead)
                own = Max(own, Walk(instead, findCommand, seen));
            risk = Max(risk, own);
            if (risk == ToolRisk.AskAlways) break;
        }

        return risk;
    }

    private static ToolRisk Max(ToolRisk a, ToolRisk b) => a > b ? a : b;
}

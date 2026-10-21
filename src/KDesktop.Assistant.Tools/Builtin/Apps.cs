using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// «minimiza Steam», «sal de Chrome», «silencia Spotify» (stage 8.1): something done to a program that is open. K-Desktop
/// finds it by the user's words and does it with the actions of its catalogue (<see cref="AssistantActs"/>), with the
/// assistant's steps and asking first like a command.
/// </summary>
public sealed class ControlAppTool(ICommandBridge bridge) : ITool
{
    public string Name => "control_app";

    public string Description => "Does something to a program or window that is open on the PC: close_window (closes only that window), quit (closes the whole program), minimize, maximize, restore, front (brings it to the front), left or right (half of the screen), center, other_monitor, mute, unmute, volume_up, volume_down, volume (to a level: value 0-100). Give the program as the user names it; leave app empty for the window in front (\"this\", \"the window\"). It does not open programs.";

    public string ParametersSchemaJson { get; } =
        $$$"""{"type":"object","properties":{"action":{"type":"string","enum":[{{{string.Join(",", Actions.Keys.Select(k => $"\"{k}\""))}}}]},"app":{"type":"string","description":"The program, as the user names it (empty: the window in front)"},"value":{"type":"integer","description":"For volume: the level, 0 to 100"}},"required":["action"]}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    // K-Desktop asks the user before closing (it is a command for the occasion): the tool itself does not.
    public ToolRisk Risk => ToolRisk.Safe;

    /// <summary>The model's names for the operations → the protocol's (<see cref="AssistantActs.Ops"/>).</summary>
    public static readonly IReadOnlyDictionary<string, string> Actions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["close_window"] = "closeWindow", ["quit"] = "quit", ["minimize"] = "minimize", ["maximize"] = "maximize",
        ["restore"] = "restore", ["front"] = "front", ["left"] = "left", ["right"] = "right", ["center"] = "center",
        ["other_monitor"] = "otherMonitor", ["mute"] = "mute", ["unmute"] = "unmute", ["volume_up"] = "volumeUp",
        ["volume_down"] = "volumeDown", ["volume"] = "volume",
    };

    public string Intent(JsonElement arguments) =>
        Op(arguments) is { } op ? Loc.T("act." + op, NameOf(arguments.GetString("app"))) : Loc.T("tool.control_app.intent");

    private static string? Op(JsonElement arguments) =>
        arguments.GetString("action") is { } action && Actions.TryGetValue(action.Trim(), out var op) ? op
        : arguments.GetString("action") is { } given && AssistantActs.Ops.Contains(given.Trim()) ? given.Trim() : null;

    private static string NameOf(string? app) => ToolText.Clean(app) is { Length: > 0 } name ? name : Loc.T("act.current");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (Op(arguments) is not { } op) return ToolResult.Fail("Unknown action: use one of " + string.Join(", ", Actions.Keys) + ".");
        var app = arguments.GetString("app")?.Trim() ?? string.Empty;
        var value = arguments.GetNumber("value") is { } v ? (int?)Math.Round(v) : null;
        if (op == "volume" && value is null) return ToolResult.Fail("Which volume level (0-100)?");
        var end = await bridge.ActAsync(op, app, value, context.Assistant, cancellationToken).ConfigureAwait(false);
        if (end is null) return ToolResult.Fail("K-Desktop did not answer.");
        var name = end.Name is { Length: > 0 } found ? found : NameOf(app);
        return end.Status switch
        {
            "ok" => new ToolResult($"Done: {op} {name}.")
            {
                Done = Loc.T("act." + op, name),
                Reply = Loc.T("act." + op + ".reply", name, value ?? 0),
                Acted = true,
            },
            "declined" => new ToolResult($"Not done: the user said no to it. Tell them you did not do it.")
            {
                Done = Loc.T("tool.run_command.declined", Loc.T("act." + op, name)),
                Reply = Loc.T("tool.run_command.replyDeclined", Loc.T("act." + op, name)),
                Failed = true,
            },
            _ when end.Reason is { } reason => new ToolResult($"Not done: {reason switch
            {
                "noWindow" => $"there is no open window of \"{app}\"",
                "noProcess" => $"\"{app}\" is not running",
                _ => "K-Desktop does not do that to itself or the desktop",
            }}. Say so.")
            {
                Done = Loc.T("act.reason." + reason, name),
                Reply = Loc.T("act.reason." + reason, name),
                Failed = true,
            },
            _ => new ToolResult("Failed. " + string.Join(" ", end.Messages)) { Done = Loc.T("tool.run_command.failed", Loc.T("act." + op, name)), Failed = true },
        };
    }
}

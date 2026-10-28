using System.Globalization;
using System.Text;
using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// «¿Qué ventanas tengo abiertas?», «¿dónde está el Word?» (B7, 5.0.350): the open windows read in the assistant process
/// (<see cref="AppFinder.OpenWindows"/>, like <c>notify_when</c>), the one in front first, with their state and place. It
/// is also what the model looks at before <c>arrange_windows</c> or <c>control_app</c> when the user names them vaguely.
/// </summary>
public sealed class ListWindowsTool(Func<(IReadOnlyList<ListedWindow> Windows, int Monitors)>? read = null) : ITool
{
    /// <summary>The most windows told.</summary>
    public const int MaxTold = 25;

    public string Name => "list_windows";

    public string Description => "Lists the windows open on the PC now (the one in front first): program, title, whether minimized or maximized, its monitor and where on it. To answer which windows or programs are open, or to find the one the user means.";

    public string ParametersSchemaJson => ToolText.NoParameters;

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => Loc.T("tool.list_windows.intent");

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var (windows, monitors) = (read ?? WindowList.Read)();
        return Task.FromResult(new ToolResult(Describe(windows, monitors)) { Done = Loc.T("tool.list_windows.done", windows.Count) });
    }

    /// <summary>The list for the model, in English.</summary>
    public static string Describe(IReadOnlyList<ListedWindow> windows, int monitors)
    {
        if (windows.Count == 0) return "There are no windows open (only the desktop).";
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"{windows.Count} windows open{(monitors > 1 ? $" on {monitors} monitors" : string.Empty)}, the one in front first:");
        var i = 0;
        foreach (var w in windows.Take(MaxTold))
        {
            var state = w.Minimized ? "minimized" : w.Maximized ? "maximized" : w.Place == "full" ? "covers the screen" : $"{PlaceWords(w.Place)}, {w.Share}% of the screen";
            var title = w.Title.Length > 0 && w.Title != w.Name ? $" \"{ToolText.Clean(w.Title)}\"" : string.Empty;
            text.AppendLine(CultureInfo.InvariantCulture, $"{++i}. {w.Name}{title} ({w.Process}){(w.InFront ? ", in front" : string.Empty)}: {state}{(monitors > 1 ? $", monitor {w.Monitor}" : string.Empty)}.");
        }
        if (windows.Count > MaxTold) text.AppendLine(CultureInfo.InvariantCulture, $"…and {windows.Count - MaxTold} more.");
        return text.ToString().TrimEnd();
    }

    private static string PlaceWords(string place) => place switch
    {
        "center" => "in the middle",
        "topLeft" => "top left",
        "topRight" => "top right",
        "bottomLeft" => "bottom left",
        "bottomRight" => "bottom right",
        "top" or "bottom" => "at the " + place,
        _ => "on the " + place,
    };
}

/// <summary>
/// «el navegador y VS Code uno al lado del otro», «pon estas tres en cuadrícula en la otra pantalla» (B7, 5.0.350):
/// K-Desktop finds each program and places them all at once on one monitor (<see cref="AssistantActs.BuildArrange"/>);
/// an assistant on the desktop carries them one by one, like when it moves one window.
/// </summary>
public sealed class ArrangeWindowsTool(ICommandBridge bridge) : ITool
{
    public string Name => "arrange_windows";

    public string Description => "Places two or more open windows together on one monitor: columns (side by side), rows (one above the other), grid, or main (the first big on the left, the rest on the right). Give the programs as the user names them, in order from left to right (\"this\" for the window in front). For one window alone use control_app.";

    public string ParametersSchemaJson { get; } =
        $$$"""{"type":"object","properties":{"apps":{"type":"array","items":{"type":"string"},"description":"The programs, as the user names them, left to right"},"layout":{"type":"string","enum":[{{{string.Join(",", AssistantActs.Layouts.Select(l => $"\"{l}\""))}}}],"description":"How to share the screen (empty: columns, or grid from 4 windows)"},"monitor":{"type":"integer","description":"The monitor, 1, 2… (empty: where the first window is)"}},"required":["apps"]}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    // K-Desktop asks if the command it makes should be asked (placing windows is safe).
    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("act.arrange", string.Join(", ", NoteTools.List(arguments, "apps").Select(ToolText.Clean)));

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var apps = NoteTools.List(arguments, "apps");
        if (apps.Count == 1 && apps[0].Contains(" y ", StringComparison.OrdinalIgnoreCase) || apps.Count == 1 && apps[0].Contains(" and ", StringComparison.OrdinalIgnoreCase))
            apps = [.. apps[0].Split([" y ", " and "], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
        if (apps.Count < 2) return ToolResult.Fail("Give at least two programs to place together (for one window alone use control_app left, right, maximize…).");
        if (apps.Count > AssistantActs.MaxArranged) return ToolResult.Fail($"At most {AssistantActs.MaxArranged} windows at once.");
        var layout = arguments.GetString("layout")?.Trim().ToLowerInvariant() switch
        {
            { } given when AssistantActs.Layouts.Contains(given) => given,
            "side_by_side" or "side by side" or "horizontal" => "columns",
            "stacked" or "vertical" => "rows",
            "quarters" or "tiles" => "grid",
            "big" or "focus" => "main",
            _ => null,
        };
        var monitor = arguments.GetNumber("monitor") is { } m && m >= 1 ? (int?)Math.Round(m) : null;
        var end = await bridge.ArrangeAsync(apps, layout, monitor, context.Assistant, cancellationToken).ConfigureAwait(false);
        if (end is null) return ToolResult.Fail("K-Desktop did not answer.");
        var name = end.Name is { Length: > 0 } found ? found : string.Join(", ", apps);
        return end.Status switch
        {
            "ok" => new ToolResult($"Done: {name} placed together.") { Done = Loc.T("act.arrange", name), Reply = Loc.T("act.arrange.reply", name), Acted = true },
            "declined" => new ToolResult("Not done: the user said no to it. Tell them you did not do it.")
            {
                Done = Loc.T("tool.run_command.declined", Loc.T("act.arrange", name)),
                Reply = Loc.T("tool.run_command.replyDeclined", Loc.T("act.arrange", name)),
                Failed = true,
            },
            _ when end.Reason is { } reason => new ToolResult($"Not done: {(reason == "noWindow" ? $"there is no open window of \"{name}\" (list_windows tells which are open)" : "K-Desktop does not do that to itself or the desktop")}. Say so.")
            {
                Done = Loc.T("act.reason." + reason, name),
                Reply = Loc.T("act.reason." + reason, name),
                Failed = true,
            },
            _ => new ToolResult("Failed. " + string.Join(" ", end.Messages)) { Done = Loc.T("tool.run_command.failed", Loc.T("act.arrange", name)), Failed = true },
        };
    }
}

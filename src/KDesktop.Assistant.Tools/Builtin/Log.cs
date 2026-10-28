using System.Globalization;
using System.Text;
using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// «¿Qué falló ayer?», «¿se ejecutó el evento de las ocho?» (B3, 5.0.345): K-Desktop's «Registro», with the times an
/// event did not run and why.
/// </summary>
public sealed class QueryLogTool(ICommandBridge bridge) : ITool
{
    public string Name => "query_log";

    public string Description => "Reads K-Desktop's log of what ran (commands and events, from where, when, how it went and why it failed), and the times an event did not run and why. Use it for «what failed», «did X run», «what ran this morning».";

    public string ParametersSchemaJson =>
        """{"type":"object","properties":{"since":{"type":"string","description":"From: yyyy-MM-dd or yyyy-MM-dd HH:mm (optional)"},"until":{"type":"string","description":"Until: yyyy-MM-dd (the whole day) or yyyy-MM-dd HH:mm (optional)"},"name":{"type":"string","description":"Words of the command or event (optional)"},"source":{"type":"string","enum":["hotkey","quickMenu","launcher","voice","assistant","mcp","remote","web","event","command","manual","commandLine"],"description":"Only what came from there (optional)"},"problems":{"type":"boolean","description":"Only what failed or did not run"}}}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => ToolText.Clean(arguments.GetString("name")) is { Length: > 0 } name
        ? Loc.T("tool.query_log.intentFor", name)
        : Loc.T("tool.query_log.intent");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var request = new LogQueryMessage(string.Empty, arguments.GetString("since"), arguments.GetString("until"), arguments.GetString("name"),
            arguments.GetString("source"), arguments.GetString("problems") is "true" or "True");
        if (await bridge.QueryLogAsync(request, cancellationToken).ConfigureAwait(false) is not { } found) return ToolResult.Fail("K-Desktop did not answer.");
        var done = Loc.T("tool.query_log.done");
        if (found.Runs.Count == 0 && found.Skips.Count == 0)
            return new ToolResult("Nothing in the log fits." + Oldest(found)) { Done = done };

        var text = new StringBuilder();
        if (found.Runs.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"Runs, newest first ({found.Runs.Count} of {found.Total}): ");
            text.Append(string.Join("; ", found.Runs.Select(Line))).Append('.');
        }
        else text.Append("No runs fit.");
        if (found.Skips.Count > 0)
            text.Append(" Times an event did not run: ").Append(string.Join("; ", found.Skips.Select(Line))).Append('.');
        text.Append(Oldest(found));
        return new ToolResult(text.ToString()) { Done = done };
    }

    private static string Oldest(LogFoundMessage found) =>
        found.Oldest is { } oldest ? $" (The log keeps the last 500 runs, from {oldest}.)" : string.Empty;

    public static string Line(LoggedRun run)
    {
        var line = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"{run.Time} «{run.Name}» from {run.Source}"));
        if (run.Detail is { Length: > 0 } detail) line.Append(CultureInfo.InvariantCulture, $" ({detail})");
        if (run.Depth > 0) line.Append(" (called by another command)");
        line.Append(CultureInfo.InvariantCulture, $": {run.Outcome}");
        if (run.Message is { Length: > 0 } message) line.Append(CultureInfo.InvariantCulture, $" — {message}");
        return line.ToString();
    }

    public static string Line(LoggedSkip skip)
    {
        var why = skip.Reason switch
        {
            "paused" => "K-Desktop was paused",
            "condition" => $"its «only if» was not met: {skip.Detail}",
            "cooldown" => $"too soon after the last time (it waits {skip.Detail} s)",
            "suspended" => "held back for a while for firing too often",
            "loop" => "stopped for firing too many times in a minute",
            "missed" => $"its time ({skip.Detail}) went by with the PC asleep or off",
            _ => skip.Reason,
        };
        return $"{skip.Time} «{skip.Event}»: {why}" + (skip.Times > 1 ? $" ({skip.Times} times in a row, the last one then)" : string.Empty);
    }
}

/// <summary>«¿Qué comando uso más?», «¿cuánto uso la voz?» (B3, 5.0.345).</summary>
public sealed class UsageStatsTool(ICommandBridge bridge) : ITool
{
    public string Name => "usage_stats";

    public string Description => "Tells how the user uses K-Desktop: the commands they run the most (ever and lately), from where (hotkeys, voice, menus…) and other counts. For questions about their own use, not about one run (that is query_log).";

    public string ParametersSchemaJson => ToolText.Optional(("days", "integer", "«Lately» = these last days (default 7)"));

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => Loc.T("tool.usage_stats.intent");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var days = (int)Math.Clamp(arguments.GetNumber("days") ?? 7, 1, 365);
        if (await bridge.UsageAsync(days, cancellationToken).ConfigureAwait(false) is not { } usage) return ToolResult.Fail("K-Desktop did not answer.");
        var text = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"Since {usage.Since}: {usage.Runs} commands run."));
        if (usage.Top.Count > 0) text.Append(" Most run ever: ").Append(string.Join(", ", usage.Top.Select(c => $"{c.Name} ({c.Count})"))).Append('.');
        text.Append(CultureInfo.InvariantCulture, $" Last {usage.Days} days: ");
        text.Append(usage.Recent.Count == 0 ? "nothing ran." : string.Join(", ", usage.Recent.Select(c => $"{c.Name} ({c.Count}{(c.Failed > 0 ? $", {c.Failed} failed" : "")})")) + ".");
        if (usage.Sources.Count > 0) text.Append(" Runs by origin: ").Append(string.Join(", ", usage.Sources.Select(c => $"{c.Name} {c.Count}"))).Append('.');
        if (usage.Counters.Count > 0) text.Append(" Other counts: ").Append(string.Join(", ", usage.Counters.Select(p => $"{p.Key} {p.Value}"))).Append('.');
        return new ToolResult(text.ToString()) { Done = Loc.T("tool.usage_stats.done") };
    }
}

/// <summary>«¿Por qué no funciona el comando de la música?», «¿por qué no saltó el evento de las ocho?» (B3, 5.0.345).</summary>
public sealed class DiagnoseCommandTool(ICommandBridge bridge) : ITool
{
    public string Name => "diagnose_command";

    public string Description => "Finds out why one of the user's K-Desktop commands or events does not work or did not run: what it does, whether it is turned off, a program or folder that is missing, its «only if» conditions now, its last runs and errors, and the times it did not run. Nothing runs.";

    public string ParametersSchemaJson =>
        """{"type":"object","properties":{"what":{"type":"string","description":"The command's or event's name, or words for it"},"kind":{"type":"string","enum":["command","event"],"description":"When the user says which it is (optional)"}},"required":["what"]}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => Loc.T("tool.diagnose_command.intent", ToolText.Clean(arguments.GetString("what")));

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var what = arguments.GetString("what")?.Trim() ?? string.Empty;
        if (what.Length == 0) return ToolResult.Fail("Which command or event?");
        var kind = arguments.GetString("kind") is "command" or "event" ? arguments.GetString("kind") : null;
        if (await bridge.DiagnoseAsync(what, kind, cancellationToken).ConfigureAwait(false) is not { } found) return ToolResult.Fail("K-Desktop did not answer.");
        if (!found.Found) return ToolResult.Fail($"There is no command or event like «{what}».") with { Done = Loc.T("tool.diagnose_command.none", ToolText.Clean(what)) };

        var text = new StringBuilder(string.Create(CultureInfo.InvariantCulture, $"{(found.Kind == "event" ? "Event" : "Command")} «{found.Name}». "));
        text.Append(string.Join(" ", found.Facts));
        text.Append(found.Problems.Count == 0 ? " Problems found: none." : " Problems found: " + string.Join(" ", found.Problems.Select(p => p.EndsWith('.') ? p : p + ".")));
        text.Append(found.Runs.Count == 0 ? " It has not run lately (in the log)." : " Last runs: " + string.Join("; ", found.Runs.Take(5).Select(QueryLogTool.Line)) + ".");
        if (found.Skips.Count > 0) text.Append(" Times it did not run: ").Append(string.Join("; ", found.Skips.Take(5).Select(QueryLogTool.Line))).Append('.');
        return new ToolResult(text.ToString()) { Done = Loc.T("tool.diagnose_command.done", ToolText.Clean(found.Name)) };
    }
}

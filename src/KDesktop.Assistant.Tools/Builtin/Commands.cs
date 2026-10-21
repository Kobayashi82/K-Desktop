using System.Globalization;
using System.Text;
using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// The user's K-Desktop commands, events and built-in media orders, as the assistant process reaches them (phase 4):
/// the host asks K-Desktop through the pipe; K-Desktop asks the user before running what is risky. Null answers: K-Desktop
/// did not answer in time.
/// </summary>
public interface ICommandBridge
{
    Task<MatchedMessage?> MatchAsync(string text, CancellationToken cancellationToken);

    /// <param name="assistant">Who asks (null: the module without a body), for K-Desktop's history and its question.</param>
    Task<EndMessage?> RunAsync(string command, string? assistant, CancellationToken cancellationToken);

    Task<EndMessage?> MediaAsync(string order, int? value, CancellationToken cancellationToken);

    Task<DescribedMessage?> DescribeAsync(string command, CancellationToken cancellationToken);

    Task<EndMessage?> RunEventAsync(string name, string? assistant, CancellationToken cancellationToken);

    /// <summary>
    /// «minimiza Steam» (stage 8.1): one of <see cref="AssistantActs.Ops"/> done to a program K-Desktop finds by the
    /// user's words (<paramref name="app"/>; empty: the window in front). Without K-Desktop (a test, the bench): null.
    /// </summary>
    Task<EndMessage?> ActAsync(string op, string app, int? value, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);
}

/// <summary>The command tools (stage 4.4), with the bridge to K-Desktop.</summary>
public static class CommandTools
{
    /// <summary>
    /// A candidate this good, and this far ahead of the next, is clearly the one meant. K-Desktop's voice accepts the
    /// meaning from 0.62 (it has nothing behind it); an assistant has the AI, and is stricter.
    /// </summary>
    public const double Clear = 0.75;

    /// <summary>What K-Desktop says is sure and is this good runs at once, without the AI.</summary>
    public const double Direct = 0.80;

    public const double Ahead = 0.08;

    /// <summary>Below this, no command of the user is anything like the order (and the AI is not offered run_command).</summary>
    public const double Plausible = Clear;

    public static void AddTo(ToolRegistry registry, ICommandBridge bridge)
    {
        registry.Add(new RunCommandTool(bridge));
        registry.Add(new FindCommandTool(bridge));
        registry.Add(new DescribeCommandTool(bridge));
        registry.Add(new RunEventTool(bridge));
        registry.Add(new ControlAppTool(bridge));
    }

    /// <summary>The one candidate an order clearly means (a sure part, its exact name, or one well ahead), or null.</summary>
    public static MatchedCandidate? Pick(MatchedMessage matched, string asked)
    {
        if (matched.Parts.Count != 1) return null;
        var part = matched.Parts[0];
        if (part.Candidates.Count == 0) return null;
        if (part.Sure && part.Candidates[0].Score >= Clear) return part.Candidates[0];
        if (part.Candidates.FirstOrDefault(c => string.Equals(c.Name.Trim(), asked.Trim(), StringComparison.OrdinalIgnoreCase)) is { } exact) return exact;
        var best = part.Candidates[0];
        var second = part.Candidates.Count > 1 ? part.Candidates[1].Score : 0;
        return best.Score >= Clear && best.Score - second >= Ahead ? best : null;
    }

    /// <summary>
    /// Whether <paramref name="asked"/> names the command <paramref name="name"/> (not just means something like it): each
    /// word of the name of three letters or more is in it, as said or nearly («vacía la papelera» names «Vaciar Papelera»).
    /// </summary>
    public static bool Names(string asked, string name)
    {
        var said = TextSimilarity.Words(asked);
        var words = TextSimilarity.Words(name);
        var named = words.Where(w => w.Length >= 3).ToArray();
        if (named.Length == 0) return said.SequenceEqual(words);
        return named.All(w => said.Any(s => TextSimilarity.WordSimilarity(w, s) >= NameWord));
    }

    /// <summary>How alike a word said must be to a word of a command's name (the same stem, a transcription slip).</summary>
    public const double NameWord = 0.75;

    /// <summary>For the log: each part, whether it is sure, and its best candidate.</summary>
    public static string Describe(MatchedMessage matched) =>
        string.Join(" | ", matched.Parts.Select(p => $"«{p.Text}» {(p.Sure ? "seguro" : "dudoso")}: " +
            (p.Candidates.Count > 0 ? $"{p.Candidates[0].Name} {p.Candidates[0].Score.ToString("0.00", CultureInfo.InvariantCulture)}" : "nada")))
        + (matched.Unmatched.Count > 0 ? $" · sin casar: {string.Join(", ", matched.Unmatched)}" : string.Empty);

    /// <summary>The candidates for the model: «Name (score)», best first.</summary>
    public static string List(IEnumerable<MatchedCandidate> candidates) =>
        string.Join("; ", candidates.Select(c => $"{c.Name} ({c.Score.ToString("0.00", CultureInfo.InvariantCulture)})"));

    /// <summary>How a run went, for the model and for the activity.</summary>
    public static ToolResult Outcome(EndMessage? end, string name, string tool)
    {
        if (end is null) return ToolResult.Fail("K-Desktop did not answer.");
        var said = end.Messages.Count > 0 ? " K-Desktop said: " + string.Join(" ", end.Messages) : string.Empty;
        return end.Status switch
        {
            "ok" => new ToolResult($"Done: {name}.{said}")
            {
                Done = Loc.T($"tool.{tool}.done", name),
                Reply = Loc.T($"tool.{tool}.reply", name),
                Acted = true,
            },
            "declined" => new ToolResult($"Not done: the user said no to {name}. Tell them you did not do it.")
            {
                Done = Loc.T("tool.run_command.declined", name),
                Reply = Loc.T("tool.run_command.replyDeclined", name),
                Failed = true,
            },
            _ => new ToolResult($"Failed: {name}.{said}") { Done = Loc.T("tool.run_command.failed", name), Failed = true },
        };
    }
}

/// <summary>«Vacía la papelera», «pon el modo juego»: runs one of the user's commands (or a built-in media order).</summary>
public sealed class RunCommandTool(ICommandBridge bridge) : ITool
{
    public string Name => "run_command";

    public string Description => "Runs one of the user's own K-Desktop commands (shortcuts they made: modes, window layouts, their folders…). Only when what they ask sounds like one of their commands; web pages, programs, notes, timers and sums have their own tools. Give their words or the command's name; if it is not clear which, it lists the candidates and you call it again with the exact name.";

    public string ParametersSchemaJson => ToolText.OneString("command", "The command's name, or the user's words for it");

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    // K-Desktop asks the user before running what is risky (stage 4.3): the tool itself does not.
    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.run_command.intent", ToolText.Clean(arguments.GetString("command")));

    /// <remarks>
    /// The routing, which already knows the one meant (a clear match, the answer to «¿A o B?»), passes its <c>id</c> or
    /// its <c>media</c> order too (not in the schema: the model gives words) and nothing is looked for again.
    /// </remarks>
    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var asked = arguments.GetString("command")?.Trim() ?? string.Empty;
        if (asked.Length == 0) return ToolResult.Fail("Which command?");
        MatchedCandidate? chosen;
        if (arguments.GetString("id") is { Length: > 0 } id) chosen = new MatchedCandidate(id, asked, 1);
        else if (arguments.GetString("media") is { Length: > 0 } order) chosen = new MatchedCandidate(null, asked, 1, order, (int?)arguments.GetNumber("value"));
        else if (await bridge.MatchAsync(asked, cancellationToken).ConfigureAwait(false) is not { } matched) return ToolResult.Fail("K-Desktop did not answer.");
        else if ((chosen = CommandTools.Pick(matched, asked)) is null)
        {
            var candidates = matched.Parts.SelectMany(p => p.Candidates).Where(c => c.Score >= CommandTools.Plausible).Take(4).ToList();
            return candidates.Count == 0
                ? ToolResult.Fail("The user has no command like that. Use another of your tools if one fits, or say you cannot.")
                : ToolResult.Fail($"Not sure which one. Candidates: {CommandTools.List(candidates)}. Ask the user, or call again with the exact name.");
        }
        // The model's words only sound like it («la vuelta» ran «Anterior» three times: the user had taught it that «la
        // vuelta» opens a web): it decides, with the name in front of it.
        else if (!CommandTools.Names(asked, chosen.Name))
            return ToolResult.Fail($"No command is called \"{asked}\"; the closest is \"{chosen.Name}\", which may be something else. If that is what they mean, call again with that exact name; if not, it is not one of their commands: use another tool (what you remember about them may say what they mean).");
        var end = chosen.Media is { } media
            ? await bridge.MediaAsync(media, chosen.Value, cancellationToken).ConfigureAwait(false)
            : await bridge.RunAsync(chosen.Command!, context.Assistant, cancellationToken).ConfigureAwait(false);
        return CommandTools.Outcome(end, chosen.Name, Name);
    }
}

/// <summary>«¿Tengo algún comando para la música?»: the user's commands that fit some words (nothing runs).</summary>
public sealed class FindCommandTool(ICommandBridge bridge) : ITool
{
    public string Name => "find_command";

    public string Description => "Looks for the user's K-Desktop commands that fit some words, without running anything. Use it when they ask which commands they have.";

    public string ParametersSchemaJson => ToolText.OneString("query", "What the command does or is called");

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => Loc.T("tool.find_command.intent", ToolText.Clean(arguments.GetString("query")));

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var query = arguments.GetString("query")?.Trim() ?? string.Empty;
        if (query.Length == 0) return ToolResult.Fail("What should the command do?");
        if (await bridge.MatchAsync(query, cancellationToken).ConfigureAwait(false) is not { } matched) return ToolResult.Fail("K-Desktop did not answer.");
        var found = matched.Parts.SelectMany(p => p.Candidates).Where(c => c.Score >= 0.5).DistinctBy(c => c.Name).Take(6).ToList();
        return found.Count == 0 ? new ToolResult("No command fits.") : new ToolResult("Commands that fit (name and how well): " + CommandTools.List(found));
    }
}

/// <summary>«¿Qué hace el comando modo juego?»: its actions in words, its hotkeys and whether it asks first.</summary>
public sealed class DescribeCommandTool(ICommandBridge bridge) : ITool
{
    public string Name => "describe_command";

    public string Description => "Tells what one of the user's K-Desktop commands does (its actions), its hotkeys and whether it asks before running. Nothing runs.";

    public string ParametersSchemaJson => ToolText.OneString("command", "The command's name, or words for it");

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => Loc.T("tool.describe_command.intent", ToolText.Clean(arguments.GetString("command")));

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var asked = arguments.GetString("command")?.Trim() ?? string.Empty;
        if (asked.Length == 0) return ToolResult.Fail("Which command?");
        if (await bridge.MatchAsync(asked, cancellationToken).ConfigureAwait(false) is not { } matched) return ToolResult.Fail("K-Desktop did not answer.");
        var chosen = CommandTools.Pick(matched, asked) ?? matched.Parts.SelectMany(p => p.Candidates).FirstOrDefault(c => c.Command is not null && c.Score >= CommandTools.Clear);
        if (chosen?.Command is not { } id) return ToolResult.Fail("There is no command like that.");
        if (await bridge.DescribeAsync(id, cancellationToken).ConfigureAwait(false) is not { Found: true } described) return ToolResult.Fail("There is no command like that.");
        var text = new StringBuilder($"Command \"{described.Name}\". It does, in order: {string.Join("; ", described.Actions)}.");
        if (described.Hotkeys.Count > 0) text.Append(CultureInfo.InvariantCulture, $" Hotkeys: {string.Join(", ", described.Hotkeys)}.");
        text.Append(described.Asks switch
        {
            "always" => " It always asks the user before running.",
            "once" => " It asks the user the first time.",
            _ => " It runs without asking.",
        });
        return new ToolResult(text.ToString());
    }
}

/// <summary>«Lanza el evento de juegos»: runs the commands of one of the user's K-Desktop events.</summary>
public sealed class RunEventTool(ICommandBridge bridge) : ITool
{
    public string Name => "run_event";

    public string Description => "Runs the commands of one of the user's K-Desktop events, by the event's name. Only when they name an event.";

    public string ParametersSchemaJson => ToolText.OneString("event", "The event's name");

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.run_event.intent", ToolText.Clean(arguments.GetString("event")));

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var name = arguments.GetString("event")?.Trim() ?? string.Empty;
        if (name.Length == 0) return ToolResult.Fail("Which event?");
        var end = await bridge.RunEventAsync(name, context.Assistant, cancellationToken).ConfigureAwait(false);
        return CommandTools.Outcome(end, name, Name);
    }
}

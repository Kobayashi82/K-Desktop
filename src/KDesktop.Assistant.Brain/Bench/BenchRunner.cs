using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Bench;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Brain.Bench;

/// <summary>
/// Runs the test bench with a model (stage 3.9, plan §5.4): each order goes the way of a real one — the fixed rules
/// first, then the user's K-Desktop commands (<c>match</c>), then the AI with the tools chosen by meaning — but the
/// tools that do something only pretend (a timer, a note, opening a page, running a command: nothing happens), and
/// those that only read run for real (the time, the notes, a sum, finding a command), so the answer can be checked. No
/// history between orders, no memory, nothing in the activity. Gives what each order did, whether it is what was
/// expected, and how long it took.
/// </summary>
/// <param name="commands">The user's commands (null: without them, like in phase 3).</param>
public sealed class BenchRunner(ToolRegistry tools, ToolChooser chooser, TimeProvider? time = null, ICommandBridge? commands = null)
{
    /// <summary>The tool each fixed rule uses.</summary>
    private static readonly Dictionary<QuickRuleKind, string> RuleTools = new()
    {
        [QuickRuleKind.Time] = "get_time",
        [QuickRuleKind.Timer] = "set_timer",
        [QuickRuleKind.Note] = "save_note",
        [QuickRuleKind.CancelTimer] = "cancel_timer",
        [QuickRuleKind.OpenWeb] = "open_web",
        [QuickRuleKind.Remember] = "remember",
        [QuickRuleKind.Forget] = "forget",
        [QuickRuleKind.Act] = "control_app",
        [QuickRuleKind.OpenApp] = "open_app",
        [QuickRuleKind.Weather] = "weather",
        [QuickRuleKind.WeatherTomorrow] = "weather",
        [QuickRuleKind.WeatherWeek] = "weather",
    };

    /// <param name="progress">Each order when it is done (any thread).</param>
    public async Task<IReadOnlyList<BenchCaseResult>> RunAsync(IChatModel model, string modelName, IReadOnlyList<BenchCase> cases,
        Action<BenchCaseResult>? progress, CancellationToken cancellationToken)
    {
        var results = new List<BenchCaseResult>();
        var persona = AssistantPersona.Module();
        foreach (var item in cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await RunOneAsync(model, modelName, persona, item, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            progress?.Invoke(result);
        }
        return results;
    }

    private async Task<BenchCaseResult> RunOneAsync(IChatModel model, string modelName, AssistantPersona persona, BenchCase item, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var used = new List<string>();
        var ran = new List<string>();
        string route, answer;
        var rule = QuickRules.Match(item.Say, [persona.Name]);
        // As in a real order: the rules about programs only count after the user's own commands («pon chrome a la
        // derecha» is their command, not «minimiza…»), and «abre X» only when X is installed (else the AI).
        if (rule is { Kind: QuickRuleKind.Act or QuickRuleKind.OpenApp })
        {
            var program = rule;
            rule = null;
            var matchedFirst = commands is not null && !TalkService.IsQuestion(item.Say) ? await commands.MatchAsync(item.Say, cancellationToken).ConfigureAwait(false) : null;
            var byCommand = matchedFirst is not null && matchedFirst.Parts.Count > 0 && matchedFirst.Parts.All(p => p.Sure && p.Candidates[0].Score >= CommandTools.Direct)
                && matchedFirst.Unmatched.Count == 0;
            if (!byCommand && (program.Kind == QuickRuleKind.Act || await IsInstalledAsync(program.Text).ConfigureAwait(false))) rule = program;
        }
        var matched = rule is null && commands is not null && !TalkService.IsQuestion(item.Say) ? await commands.MatchAsync(item.Say, cancellationToken).ConfigureAwait(false) : null;
        if (matched is not null) Core.Log.Info("Asistente: banco, comandos: " + CommandTools.Describe(matched));
        // As TalkService: a big model gets the long sentences that name a command in passing.
        var capable = TalkService.IsCapable(modelName);
        if (matched is not null && matched.Parts.All(p => p.Sure && p.Candidates[0].Score >= CommandTools.Direct && (!capable || TalkService.IsShort(p.Text)))
            && matched.Unmatched.Count == 0 && matched.Parts.Count > 0)
        {
            // What clearly names commands runs without the AI (here it only pretends).
            route = "command";
            ran.AddRange(matched.Parts.Select(p => p.Candidates[0].Name));
            used.Add("run_command");
            answer = string.Empty;
        }
        else if (rule is not null && RuleTools.TryGetValue(rule.Kind, out var ruleTool))
        {
            route = "rule";
            used.Add(ruleTool);
            answer = string.Empty;
        }
        else if (rule is not null && rule.Kind is not (QuickRuleKind.First or QuickRuleKind.Second))
        {
            // «Cancela», «repite», «deshaz»: no tool and nothing to say worth checking.
            route = "rule";
            answer = string.Empty;
        }
        else
        {
            route = "ai";
            var registry = new ToolRegistry();
            foreach (var tool in tools.All) registry.Add(new BenchTool(tool, used));
            // The command tools on a bridge that finds for real and runs nothing.
            if (commands is not null)
                foreach (var tool in CommandToolsOn(new DryBridge(commands, ran))) registry.Add(new BenchTool(tool, used, real: true));
            var runner = new ToolRunner(registry, () => new HashSet<string>(registry.All.Select(t => t.Name)), (_, _) => Task.FromResult("yes"), _ => { },
                Path.Combine(Path.GetTempPath(), "K-Desktop", "bench-undo.json"));
            var mind = new TalkMind(runner, chooser, time ?? TimeProvider.System);
            var close = matched is null ? null : TalkService.CloseCommands(matched, [], capable);
            IReadOnlyCollection<string>? leaveOut = matched is not null && !capable && close is { Count: 0 } ? ["run_command"] : null;
            // A big model is told which of them the order surely asks for.
            IReadOnlyList<string>? certain = capable && matched is not null
                ? [.. matched.Parts.Where(p => p.Sure && p.Candidates[0].Score >= CommandTools.Direct && TalkService.IsShort(p.Text)).Select(p => p.Candidates[0].Name)]
                : null;
            var said = await mind.AnswerAsync(model, modelName, persona, [], [], item.Say, new ToolContext(null, item.Say), null, cancellationToken, leaveOut, close, capable,
                certain).ConfigureAwait(false);
            answer = said.Failed ? string.Empty : said.Text;
            if (said.Failed) route = "failed";
        }
        var passed = route != "failed" && Check(item.Expect, used, answer, route is "rule" or "command", ran);
        return new BenchCaseResult(item.Say, item.Expect.Describe(), passed, route, [.. used.Distinct()], answer, (int)watch.ElapsedMilliseconds,
            ran.Count > 0 ? [.. ran.Distinct()] : null);
    }

    private async Task<bool> IsInstalledAsync(string? name) =>
        name is { Length: > 0 } && tools.Find("open_app") is OpenAppTool open && await open.FindAsync(name).ConfigureAwait(false) is not null;

    private static IEnumerable<ITool> CommandToolsOn(ICommandBridge bridge)
    {
        var registry = new ToolRegistry();
        CommandTools.AddTo(registry, bridge);
        return registry.All;
    }

    /// <summary>Finds and describes the user's commands for real; running one (or an event, or a media order) only says it would.</summary>
    private sealed class DryBridge(ICommandBridge real, List<string> ran) : ICommandBridge
    {
        /// <summary>The names of the commands found, by id (what ran is said by name).</summary>
        private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

        public async Task<MatchedMessage?> MatchAsync(string text, CancellationToken cancellationToken)
        {
            var matched = await real.MatchAsync(text, cancellationToken).ConfigureAwait(false);
            lock (_names)
                foreach (var candidate in matched?.Parts.SelectMany(p => p.Candidates) ?? [])
                    if (candidate.Command is { } id) _names[id] = candidate.Name;
            return matched;
        }

        public Task<DescribedMessage?> DescribeAsync(string command, CancellationToken cancellationToken) => real.DescribeAsync(command, cancellationToken);

        public Task<EndMessage?> RunAsync(string command, string? assistant, CancellationToken cancellationToken)
        {
            lock (_names) return Pretend(_names.GetValueOrDefault(command, command));
        }

        public Task<EndMessage?> MediaAsync(string order, int? value, CancellationToken cancellationToken) => Pretend(order);

        public Task<EndMessage?> RunEventAsync(string name, string? assistant, CancellationToken cancellationToken) => Pretend(name);

        public Task<EndMessage?> ActAsync(string op, string app, int? value, string? assistant, CancellationToken cancellationToken) => Pretend($"{op} {app}".Trim());

        private Task<EndMessage?> Pretend(string what)
        {
            lock (ran) ran.Add(what);
            return Task.FromResult<EndMessage?>(new EndMessage("bench", "ok", []));
        }
    }

    /// <summary>
    /// Whether what it did is what was expected (a rule or a command has no answer to check: what it did says it all).
    /// A command expected by name may have run by its id (the bridge got the id): both count.
    /// </summary>
    public static bool Check(BenchExpect expect, IReadOnlyList<string> used, string answer, bool byRule, IReadOnlyList<string>? ran = null)
    {
        if (expect.Commands is { Count: > 0 } names && !(ran ?? []).Any(r => names.Any(n => string.Equals(n.Trim(), r.Trim(), StringComparison.OrdinalIgnoreCase))))
            return false;
        if (expect.Tools.Count > 0 && !used.Any(expect.Tools.Contains)) return false;
        if (expect.NoTool && used.Count > 0) return false;
        if (!byRule && expect.AnswerContains.Count > 0 && !expect.AnswerContains.Any(a => answer.Contains(a, StringComparison.OrdinalIgnoreCase) || SaysNumber(answer, a)))
            return false;
        return true;
    }

    /// <summary>«ciento noventa y cinco minutos» says «195»: an answer for a voice may say its numbers in words.</summary>
    private static bool SaysNumber(string answer, string expected)
    {
        if (!int.TryParse(expected, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return false;
        var words = TextSimilarity.Words(answer);
        for (var i = 0; i < words.Length; i++)
            if (SpokenNumbers.TryRead(words, i, out var value, out var end))
            {
                if (value == number) return true;
                i = end - 1;
            }
        return false;
    }

    /// <summary>
    /// A tool for the bench: the same for the model, but it only acts if it just reads (or is <paramref name="real"/>:
    /// already harmless, on the dry bridge); it says it was used.
    /// </summary>
    private sealed class BenchTool(ITool inner, List<string> used, bool real = false) : ITool
    {
        public string Name => inner.Name;

        public string Description => inner.Description;

        public string ParametersSchemaJson => inner.ParametersSchemaJson;

        public IReadOnlyList<string> Examples => inner.Examples;

        public ToolRisk Risk => ToolRisk.Safe;

        public bool ReadsOnly => inner.ReadsOnly;

        public string Intent(JsonElement arguments) => inner.Intent(arguments);

        public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
        {
            lock (used) used.Add(Name);
            return inner.ReadsOnly || real ? inner.RunAsync(arguments, context, cancellationToken) : Task.FromResult(new ToolResult("Done."));
        }
    }
}

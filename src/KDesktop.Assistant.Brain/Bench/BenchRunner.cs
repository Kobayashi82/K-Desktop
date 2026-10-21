using System.Diagnostics;
using System.Text.Json;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Tools;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Bench;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Brain.Bench;

/// <summary>
/// Runs the test bench with a model (stage 3.9, plan §5.4): each order goes the way of a real one — the fixed rules
/// first, then the AI with the tools chosen by meaning — but the tools that do something only pretend (a timer, a
/// note, opening a page: nothing happens), and those that only read run for real (the time, the notes, a sum), so the
/// answer can be checked. No history between orders, no memory, nothing in the activity. Gives what each order did,
/// whether it is what was expected, and how long it took.
/// </summary>
public sealed class BenchRunner(ToolRegistry tools, ToolChooser chooser, TimeProvider? time = null)
{
    /// <summary>The tool each fixed rule uses.</summary>
    private static readonly Dictionary<QuickRuleKind, string> RuleTools = new()
    {
        [QuickRuleKind.Time] = "get_time",
        [QuickRuleKind.Timer] = "set_timer",
        [QuickRuleKind.Note] = "save_note",
        [QuickRuleKind.CancelTimer] = "cancel_timer",
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
        string route, answer;
        var rule = QuickRules.Match(item.Say, [persona.Name]);
        if (rule is not null && RuleTools.TryGetValue(rule.Kind, out var ruleTool))
        {
            route = "rule";
            used.Add(ruleTool);
            answer = string.Empty;
        }
        else if (rule is not null)
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
            var runner = new ToolRunner(registry, () => new HashSet<string>(registry.All.Select(t => t.Name)), (_, _) => Task.FromResult("yes"), _ => { },
                Path.Combine(Path.GetTempPath(), "K-Desktop", "bench-undo.json"));
            var mind = new TalkMind(runner, chooser, time ?? TimeProvider.System);
            var said = await mind.AnswerAsync(model, modelName, persona, [], [], item.Say, new ToolContext(null), null, cancellationToken).ConfigureAwait(false);
            answer = said.Failed ? string.Empty : said.Text;
            if (said.Failed) route = "failed";
        }
        var passed = route != "failed" && Check(item.Expect, used, answer, route == "rule");
        return new BenchCaseResult(item.Say, item.Expect.Describe(), passed, route, [.. used.Distinct()], answer, (int)watch.ElapsedMilliseconds);
    }

    /// <summary>Whether what it did is what was expected (a rule has no answer to check: its tool says it all).</summary>
    public static bool Check(BenchExpect expect, IReadOnlyList<string> used, string answer, bool byRule)
    {
        if (expect.Tools.Count > 0 && !used.Any(expect.Tools.Contains)) return false;
        if (expect.NoTool && used.Count > 0) return false;
        if (!byRule && expect.AnswerContains.Count > 0 && !expect.AnswerContains.Any(a => answer.Contains(a, StringComparison.OrdinalIgnoreCase))) return false;
        return true;
    }

    /// <summary>A tool for the bench: the same for the model, but it only acts if it just reads; it says it was used.</summary>
    private sealed class BenchTool(ITool inner, List<string> used) : ITool
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
            return inner.ReadsOnly ? inner.RunAsync(arguments, context, cancellationToken) : Task.FromResult(new ToolResult("Done."));
        }
    }
}

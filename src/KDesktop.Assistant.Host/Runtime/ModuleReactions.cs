using System.Collections.Concurrent;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Host.Engine;
using KDesktop.Assistant.Sdk;
using KDesktop.Assistant.Tools;
using KDesktop.Core;
using KDesktop.Core.Assistant.Sharing;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The character reacts to how things went (stage 13.3), with the context of <see cref="ReactionContext"/>: once per order,
/// when its answer is ready (what its tools did: a command or an action of K-Desktop is an <see cref="ReactionSource.Order"/>,
/// anything else a <see cref="ReactionSource.Tool"/>; with no tool, thanks or «está mal» are the user's), when it says a
/// notice on its own, and when the module had to do a step it could not show. A reaction is short and nothing waits for it.
/// </summary>
internal sealed class ModuleReactions(EngineLoop engine, AssistantRuntime runtime, ReactionLog log)
{
    /// <summary>The longest a reaction may last.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromSeconds(1.5);

    /// <summary>The tools that carry out the user's K-Desktop commands or K-Desktop's own actions.</summary>
    private static readonly HashSet<string> OrderTools = new(StringComparer.Ordinal) { "run_command", "run_event", "control_app", "end_process" };

    /// <summary>Tools run around an order but not for it: what it learns afterwards, what other programs ask for.</summary>
    private static readonly HashSet<string> NotForTheOrder = new(StringComparer.Ordinal) { "memory", "learned", McpShare.Route };

    private sealed class Turn(string said)
    {
        public string Said { get; } = said;

        public List<ToolRan> Ran { get; } = [];
    }

    private readonly ConcurrentDictionary<string, Turn> _turns = new(StringComparer.Ordinal);

    /// <summary>An order to <paramref name="assistant"/> starts (<c>heard</c>): what its tools do is counted from now.</summary>
    public void Begin(string? assistant, string said)
    {
        if (assistant is not null) _turns[assistant] = new Turn(said);
    }

    /// <summary>A tool ran (<see cref="ToolRunner.Ran"/>).</summary>
    public void Add(ToolRan ran)
    {
        if (ran.Assistant is null || NotForTheOrder.Contains(ran.Route) || !_turns.TryGetValue(ran.Assistant, out var turn)) return;
        lock (turn) turn.Ran.Add(ran);
    }

    /// <summary>Its answer is ready (or the order is over): it reacts to how it went, once.</summary>
    public void End(string? assistant)
    {
        if (assistant is null || !_turns.TryRemove(assistant, out var turn)) return;
        List<ToolRan> ran;
        lock (turn) ran = [.. turn.Ran];
        if (Decide(turn.Said, ran) is { } reaction) React(assistant, reaction.Reaction, reaction.Source, reaction.What);
    }

    /// <summary>
    /// How an order went, from what its tools did: something done and nothing that failed → Success; done, but something
    /// failed → Warning; nothing done and something failed → Failure. A failure is forgiven when the same tool worked later
    /// in the order (the AI tried again). Reading tools only count when they fail. With no tool, what the user said about
    /// the assistant: «está mal» → Failure, thanks or praise → Celebration. Null: nothing to react to.
    /// </summary>
    internal static (Reaction Reaction, ReactionSource Source, string? What)? Decide(string said, IReadOnlyList<ToolRan> ran)
    {
        var kept = ran.Where((r, i) => !r.Failed || !ran.Skip(i + 1).Any(later => later.Tool == r.Tool && !later.Failed)).ToList();
        var done = kept.Where(r => !r.Failed && !r.ReadsOnly).ToList();
        var failed = kept.Where(r => r.Failed).ToList();
        if (done.Count > 0)
        {
            var last = done[^1];
            return (failed.Any(r => !r.ReadsOnly) ? Reaction.Warning : Reaction.Success, SourceOf(last.Tool), last.Tool);
        }
        if (failed.Count > 0) return (Reaction.Failure, SourceOf(failed[^1].Tool), failed[^1].Tool);
        if (UserSignals.IsWrong(said)) return (Reaction.Failure, ReactionSource.User, "wrong");
        if (UserSignals.IsPraise(said)) return (Reaction.Celebration, ReactionSource.User, "praise");
        return null;
    }

    private static ReactionSource SourceOf(string tool) => OrderTools.Contains(tool) ? ReactionSource.Order : ReactionSource.Tool;

    /// <summary>
    /// It reacts now, if it is on the desktop and knows how (every assistant does: React is always assumed), at most
    /// <see cref="Longest"/>; nothing waits for it.
    /// </summary>
    public void React(string assistant, Reaction reaction, ReactionSource source, string? what) =>
        _ = ReactAsync(assistant, reaction, source, what);

    /// <summary>The same, finished when the reaction is (for the choreography, which reacts between two steps).</summary>
    public Task ReactAsync(string assistant, Reaction reaction, ReactionSource source, string? what)
    {
        if (!runtime.HasBody(assistant)) return Task.CompletedTask;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.Post(async () =>
        {
            try
            {
                if (!runtime.HasBody(assistant)) return;
                var context = log.Next(assistant, reaction, source, what);
                Log.Info($"Asistente: «{assistant}» reacciona: {reaction} ({source}{(what is null ? "" : " " + what)}, racha {context.Streak}{(context.FirstToday ? ", primera del día" : "")})");
                using var limit = new CancellationTokenSource(Longest);
                var gesture = new StepGestureContext(engine, engine.Now + Longest.TotalSeconds, new Core.Assistant.Pipe.StepMessage("", 0, 0, 0, "", "", "", (int)Longest.TotalMilliseconds),
                    () => { }, Task.FromResult(true), null);
                if (runtime.TryPerform(assistant, new Gesture.React(reaction) { Context = context }, gesture, limit.Token) is { } running)
                    await running.WaitAsync(Longest + TimeSpan.FromSeconds(0.4)).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                // A reaction that runs late is left there.
            }
            catch (Exception ex)
            {
                Log.Error($"Asistente: «{assistant}» ha fallado al reaccionar", ex);
            }
            finally
            {
                done.TrySetResult();
            }
        });
        return done.Task;
    }
}

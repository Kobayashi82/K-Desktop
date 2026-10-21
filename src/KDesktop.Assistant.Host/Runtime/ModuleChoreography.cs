using System.Collections.Concurrent;
using KDesktop.Assistant.Host.Pipe;
using KDesktop.Core;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The assistant process's side of a choreographed run (stage 6.3): every <c>step</c> of a run it asked for gets its answer
/// (<c>go</c> for now: the gestures come with the general choreographer, stage 6.4), every <c>result</c> is noted, and an
/// order cancelled while its command runs skips all that is left (<c>skip</c> with <c>all</c>: K-Desktop ends it
/// <c>cancelled</c>).
/// </summary>
internal sealed class ModuleChoreography(KDesktopLink link)
{
    /// <summary>The runs with gestures going on: run → whose.</summary>
    private readonly ConcurrentDictionary<string, string> _runs = new(StringComparer.Ordinal);

    /// <summary>A run with gestures starts; <paramref name="cancellationToken"/> is the order's (cancelling it skips the rest).</summary>
    public void Begin(string run, string assistant, CancellationToken cancellationToken)
    {
        _runs[run] = assistant;
        cancellationToken.Register(() =>
        {
            if (!_runs.TryRemove(run, out _)) return;
            Log.Info($"Asistente: «{assistant}» cancela el run {run}: se salta lo que queda");
            link.Send(new SkipMessage(run, 0, All: true));
        });
    }

    /// <summary>The run ended (<c>end</c>).</summary>
    public void End(string run) => _runs.TryRemove(run, out _);

    /// <summary>A step of one of its runs (pipe thread).</summary>
    public void OnStep(StepMessage step)
    {
        if (!_runs.TryGetValue(step.Run, out var assistant))
        {
            // Not one of ours (any more): let it run.
            link.Send(new GoMessage(step.Run, step.Index));
            return;
        }
        Log.Info($"Asistente: «{assistant}» paso {step.Index} ({step.Kind} {step.Op}, prof. {step.Depth}): «{step.Summary}»" +
                 (step.Target is { } t ? $" en «{t.Title}» ({t.State}{(t.Result is { } r ? $" → {r.X},{r.Y} {r.W}×{r.H}" : "")})" : ""));
        link.Send(new GoMessage(step.Run, step.Index));
    }

    public void OnResult(StepResultMessage result)
    {
        if (result.Status == "ok") return;
        var whose = _runs.TryGetValue(result.Run, out var assistant) ? $"«{assistant}» " : "";
        Log.Info($"Asistente: {whose}paso {result.Index} del run {result.Run}: {result.Status}{(result.Message is { } m ? $" ({m})" : "")}");
    }
}

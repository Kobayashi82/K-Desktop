using System.Collections.Concurrent;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;

namespace KDesktop.Core.Assistant;

/// <summary>
/// A choreographed run of an assistant over the pipe (stage 6.3): before each action it sends <c>step</c> (with where it
/// happens, <see cref="StepTargets"/>) and waits for that step's <c>go</c>, <c>done</c> or <c>skip</c>; afterwards it sends
/// <c>result</c>. The engine never waits longer than <see cref="StepTimeout"/>. A <c>skip</c> with <c>all</c> cancels the run
/// (it ends <c>cancelled</c>). Actions that only call other commands send no step of their own: their actions do.
/// </summary>
public sealed class AssistantChoreography : IChoreography, IDisposable
{
    /// <summary>The longest a step waits for its assistant, unless said otherwise.</summary>
    public static readonly TimeSpan DefaultStepTimeout = TimeSpan.FromSeconds(6);

    private readonly Action<PipeMessage> _send;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<StepGo>> _waiting = new();
    private readonly Dictionary<ChoreographyStep, int> _sent = new(ReferenceEqualityComparer.Instance);
    private readonly CancellationTokenSource _cancel = new();
    private int _next;

    /// <param name="run">The run's id, as the assistant gave it.</param>
    /// <param name="send">Sends a message to the assistant's process.</param>
    public AssistantChoreography(string run, Action<PipeMessage> send, TimeSpan? stepTimeout = null)
    {
        Run = run;
        _send = send;
        StepTimeout = stepTimeout ?? DefaultStepTimeout;
    }

    public string Run { get; }

    public TimeSpan StepTimeout { get; }

    /// <summary>How long each manoeuvre may take (<c>showy</c>, <c>normal</c>, <c>fast</c>), sent with every step (stage 6.7).</summary>
    public string? Speed { get; init; }

    /// <summary>
    /// In a command with more actions than this, only those on windows are steps; the rest run at once without asking
    /// (stage 6.7). 0: all of them.
    /// </summary>
    public int AnimateMax { get; init; }

    /// <summary>Cancelled when the assistant skips everything left (<c>skip</c> with <c>all</c>), or with <see cref="Cancel"/>.</summary>
    public CancellationToken Cancelled => _cancel.Token;

    private volatile bool _abandoned;

    public Task<StepGo> BeforeAsync(ChoreographyStep step, CancellationToken cancellationToken)
    {
        // Calling another command or event is only the way to its actions, which come as steps themselves.
        if (_abandoned || step.Action is CommandControlAction { Mode: CommandControlMode.Run }) return Task.FromResult(StepGo.Run);
        // A long command: only what happens to windows is shown.
        if (AnimateMax > 0 && step.Count > AnimateMax && !ActionGestures.IsWindowAction(step.Action)) return Task.FromResult(StepGo.Run);
        var index = Interlocked.Increment(ref _next);
        var answer = new TaskCompletionSource<StepGo>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiting[index] = answer;
        lock (_sent) _sent[step] = index;
        var action = step.Action;
        _send(new StepMessage(Run, index, step.Count, step.Context.Depth, action.Kind.ToString(), ActionGestures.OpOf(action), action.Summary,
            (int)StepTimeout.TotalMilliseconds, StepTargets.Of(action, step.Context.Foreground), Speed));
        // Whatever ends the wait (the engine's time limit, the run cancelled), the step stops waiting here too.
        cancellationToken.Register(() =>
        {
            if (_waiting.TryRemove(index, out var waiting)) waiting.TrySetCanceled(cancellationToken);
        });
        return answer.Task;
    }

    public void After(ChoreographyStep step, StepOutcome outcome, string? message)
    {
        int index;
        lock (_sent)
            if (!_sent.Remove(step, out index)) return;
        _waiting.TryRemove(index, out _);
        _send(new StepResultMessage(Run, index, outcome switch
        {
            StepOutcome.Ok => "ok",
            StepOutcome.Skipped => "skipped",
            _ => "failed",
        }, message));
    }

    /// <summary>The assistant's answer to a step (pipe thread). False when it is not about a step of this run.</summary>
    public bool OnAnswer(PipeMessage message)
    {
        switch (message)
        {
            case GoMessage go when go.Run == Run:
                Answer(go.Index, StepGo.Run);
                return true;
            case DoneMessage done when done.Run == Run:
                Answer(done.Index, StepGo.Done);
                return true;
            case SkipMessage { All: true } all when all.Run == Run:
                Log.Info($"Asistente: el run {Run} se cancela desde el asistente");
                Cancel();
                return true;
            case SkipMessage skip when skip.Run == Run:
                Answer(skip.Index, StepGo.Skip);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The assistant is gone (its process stopped): what is waiting runs now, and the rest of the run without asking.
    /// </summary>
    public void Abandon()
    {
        _abandoned = true;
        foreach (var index in _waiting.Keys) Answer(index, StepGo.Run);
    }

    /// <summary>Stops the run (the user cancelled it in K-Desktop).</summary>
    public void Cancel()
    {
        try { _cancel.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void Answer(int index, StepGo go)
    {
        if (_waiting.TryRemove(index, out var waiting)) waiting.TrySetResult(go);
    }

    public void Dispose() => _cancel.Dispose();
}

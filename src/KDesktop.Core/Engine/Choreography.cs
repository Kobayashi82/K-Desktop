using KDesktop.Core.Model;

namespace KDesktop.Core.Engine;

/// <summary>What the assistant said before a step of a choreographed run.</summary>
public enum StepGo
{
    /// <summary>Run it now (the assistant's <c>go</c>, at the instant of its gesture; or no answer in time).</summary>
    Run,

    /// <summary>The assistant did it itself (<c>done</c>, a window dragged): K-Desktop applies the exact result anyway.</summary>
    Done,

    /// <summary>Not to be run (<c>skip</c>).</summary>
    Skip,
}

/// <summary>How a step of a choreographed run went.</summary>
public enum StepOutcome { Ok, Failed, Skipped }

/// <summary>
/// One step of a choreographed run: an action about to run, its place among the enabled actions of its command
/// (<paramref name="Index"/> of <paramref name="Count"/>, from 1) and its context (with the depth of nested commands).
/// </summary>
public sealed record ChoreographyStep(CommandAction Action, ExecutionContext Context, int Index, int Count);

/// <summary>
/// An assistant's turn before each action of a run that asks for it (phase 6): the engine waits for its go-ahead, with
/// <see cref="StepTimeout"/> at most per step (then the action runs anyway), and tells it how each step went. Only the runs
/// started with <see cref="CommandEngine.RunChoreographedAsync"/> have one (and the commands they call): hotkeys, menus,
/// the web, the voice of «K-Desktop, …» never wait for anything.
/// </summary>
public interface IChoreography
{
    /// <summary>The longest the engine waits for <see cref="BeforeAsync"/> of one step.</summary>
    TimeSpan StepTimeout { get; }

    /// <summary>
    /// Before <paramref name="step"/> runs: completes when the assistant says go, did it, or skip. Cancelled with the run.
    /// The engine stops waiting after <see cref="StepTimeout"/>, and an error counts as «run it».
    /// </summary>
    Task<StepGo> BeforeAsync(ChoreographyStep step, CancellationToken cancellationToken);

    /// <summary>After <paramref name="step"/>: how it went (with what failed, in the user's language).</summary>
    void After(ChoreographyStep step, StepOutcome outcome, string? message);
}

using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Assistant;

/// <summary>
/// K-Desktop's side of the commands for the assistants (phase 4): which commands an order names (<c>match</c>, the same
/// matcher as the voice: words, meaning and the built-in media orders) and running one (<c>run</c>), recorded in the
/// history as coming from the assistant. Its answers are made here, apart from the window, so they can be tested.
/// </summary>
public static class AssistantCommands
{
    /// <summary>Candidates given per part, at most.</summary>
    public const int MaxCandidates = 4;

    /// <summary>The commands an assistant can find and run: the enabled ones, like the voice.</summary>
    public static IReadOnlyCollection<Command> Available(AppConfig config) => [.. VoiceService.VoiceCommands(config)];

    /// <param name="media">The built-in media orders are on (Options → Multimedia).</param>
    /// <param name="semantic">The meaning layer (null: only the words).</param>
    public static MatchedMessage Match(MatchMessage request, IReadOnlyCollection<Command> commands, bool media, SemanticMatcher? semantic)
    {
        var plan = IntentMatcher.Plan(request.Text, commands, media, semantic);
        var parts = plan.Parts.Count > 0
            ? plan.Parts.Select(p => Part(p, sure: true)).ToList()
            : [Part(plan.Whole, sure: false)];
        return new MatchedMessage(request.Id, Math.Round(plan.Score, 3), plan.Unmatched, parts);
    }

    private static MatchedPart Part(IntentResult result, bool sure) =>
        new(result.Text, sure && result.Winner is not null, [.. result.Candidates.Take(MaxCandidates).Select(Candidate)]);

    private static MatchedCandidate Candidate(IntentCandidate candidate) => candidate.Media is { } media
        ? new MatchedCandidate(null, media.Name, Math.Round(candidate.Score, 3), char.ToLowerInvariant(media.Kind.ToString()[0]) + media.Kind.ToString()[1..], media.Value)
        : new MatchedCandidate(candidate.Command!.Id, candidate.Command.Name, Math.Round(candidate.Score, 3));

    /// <summary>
    /// Runs a command for an assistant and says how it went with what the command said. Waits for it to finish (a long
    /// one makes its assistant wait, which it shows its own way). Paused, or not found: <c>failed</c> at once.
    /// </summary>
    /// <param name="who">Who asked, for the history («Lander»; «K-Desktop» without a body).</param>
    public static async Task<EndMessage> RunAsync(CommandEngine engine, RunMessage request, string who)
    {
        if (engine.IsPaused) return new EndMessage(request.Run, "failed", [Loc.T("pause.refused")]);
        if (engine.Config.FindCommand(request.Command) is not { Enabled: true } command)
            return new EndMessage(request.Run, "failed", [Loc.T("assistantCommand.notFound")]);
        var feedback = new ExecutionFeedback();
        Task run;
        using (feedback.Begin())
        using (ExecutionHistory.Describe(who))
            run = engine.RunAsync(command, TriggerSource.Assistant);
        try
        {
            await run.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new EndMessage(request.Run, "cancelled", feedback.Messages);
        }
        return new EndMessage(request.Run, feedback.HasError ? "failed" : "ok", feedback.Messages);
    }

    /// <summary>A built-in media order by its name (<c>volumeDown</c>…), or null when there is no such order.</summary>
    public static MediaOrder? MediaOrderOf(MediaMessage request) =>
        Enum.TryParse<MediaOrderKind>(request.Order, ignoreCase: true, out var kind) && Enum.IsDefined(kind) ? new MediaOrder(kind, request.Value) : null;
}

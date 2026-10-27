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

    /// <summary>
    /// The commands an assistant can find and run: the enabled ones, like the voice, but not those that ask an assistant
    /// something («Asistente: preguntar»): it would end up asking itself, again and again.
    /// </summary>
    public static IReadOnlyCollection<Command> Available(AppConfig config) =>
        [.. VoiceService.VoiceCommands(config).Where(c => !c.Actions.Any(a => a is AssistantAskAction { Enabled: true }))];

    /// <param name="media">The built-in media orders are on (Options → Multimedia).</param>
    /// <param name="semantic">The meaning layer (null: only the words).</param>
    public static MatchedMessage Match(MatchMessage request, IReadOnlyCollection<Command> commands, bool media, SemanticMatcher? semantic)
    {
        var plan = IntentMatcher.Plan(request.Text, commands, media, semantic);
        var parts = plan.Parts.Count > 0
            ? plan.Parts.Select(p => Part(p, sure: true)).ToList()
            : [Part(plan.Whole, sure: false)];
        // Nothing clear: the closest by meaning too, for the assistant's model to choose among (5.0.200).
        var near = plan.Parts.Count == 0 && semantic is not null && SemanticMatcher.Available ? Near(request.Text, commands, media, semantic) : null;
        return new MatchedMessage(request.Id, Math.Round(plan.Score, 3), plan.Unmatched, parts, near);
    }

    /// <summary>
    /// A part with its candidates, without those that do the opposite of what it says, nor those it says no to («no
    /// anterior»; then it is not sure either).
    /// </summary>
    private static MatchedPart Part(IntentResult result, bool sure)
    {
        var kept = result.Candidates.Where(c => (c.Command is null || !Contradicts(result.Text, c.Command)) && !IntentMatcher.Refuses(result.Text, c)).ToList();
        var winnerKept = result.Winner is not null && kept.Count > 0 && ReferenceEquals(kept[0], result.Candidates[0]);
        // The winner first, then the rest best first, each command once (the words and the meaning both list it).
        var ordered = winnerKept ? kept.Take(1).Concat(kept.Skip(1).OrderByDescending(c => c.Score)) : kept.OrderByDescending(c => c.Score);
        var each = ordered.DistinctBy(c => c.Command is { } command ? command.Id : "#" + c.Media!.Kind + c.Media.Value);
        return new(result.Text, sure && winnerKept, [.. each.Take(MaxCandidates).Select(Candidate)]);
    }

    /// <summary>
    /// Whether an order says the opposite of what a command does: «cierra Steam» (or Parakeet's «Sierra Steam») must never
    /// run the command that opens Steam, nor «abre…» one that closes it. With nothing asked first (<see cref="AskPolicy"/>),
    /// this is what stops it. The verbs are <c>assistant.verbs.close</c> and <c>assistant.verbs.open</c>, any language.
    /// </summary>
    public static bool Contradicts(string said, Command command)
    {
        var does = Direction(command);
        if (does == 0) return false;
        var words = " " + string.Join(' ', TextSimilarity.Words(said)) + " ";
        var (close, open) = Verbs.Value;
        var saysClose = close.Any(v => words.Contains(v, StringComparison.Ordinal));
        var saysOpen = open.Any(v => words.Contains(v, StringComparison.Ordinal));
        return does > 0 ? saysClose && !saysOpen : saysOpen && !saysClose;
    }

    /// <summary>1: it opens (programs, webs, folders, a window brought forward) and closes nothing; -1: it closes and opens nothing; 0: neither.</summary>
    private static int Direction(Command command)
    {
        var actions = command.Actions.Where(a => a.Enabled).ToList();
        var opens = actions.Any(a => a is RunAppAction or OpenUrlAction or OpenFolderAction or AppControlAction { Mode: AppControlMode.Activate });
        var closes = actions.Any(a => a is AppControlAction { Mode: AppControlMode.Close } or WindowAction { Operation: WindowOperation.Close });
        return opens == closes ? 0 : opens ? 1 : -1;
    }

    /// <summary>The verbs, folded, with spaces around (whole words; «sal de» is two).</summary>
    private static readonly Lazy<(string[] Close, string[] Open)> Verbs = new(() => (Words("assistant.verbs.close"), Words("assistant.verbs.open")));

    private static string[] Words(string key) =>
    [
        .. Loc.Available()
            .Select(l => Loc.Strings(l.Code, key).GetValueOrDefault(key) ?? string.Empty)
            .SelectMany(v => v.Split('|', StringSplitOptions.RemoveEmptyEntries))
            .Select(v => " " + string.Join(' ', TextSimilarity.Words(v)) + " ")
            .Where(v => v.Trim().Length > 0)
            .Distinct(StringComparer.Ordinal),
    ];

    private static MatchedCandidate Candidate(IntentCandidate candidate) => candidate.Media is { } media
        ? new MatchedCandidate(null, media.Name, Math.Round(candidate.Score, 3), char.ToLowerInvariant(media.Kind.ToString()[0]) + media.Kind.ToString()[1..], media.Value)
        : new MatchedCandidate(candidate.Command!.Id, candidate.Command.Name, Math.Round(candidate.Score, 3), Power: SemanticMatcher.IsPower(candidate.Command));

    /// <summary>Commands by meaning given for the one-token decision (5.0.200), at most.</summary>
    public const int MaxNear = 10;

    /// <summary>
    /// The commands (and built-in media orders) closest in meaning to the whole order, best first, without those it
    /// contradicts or says no to: what an assistant's model chooses among when nothing was clear (<c>near</c>).
    /// </summary>
    private static List<MatchedCandidate>? Near(string text, IReadOnlyCollection<Command> commands, bool media, SemanticMatcher semantic)
    {
        SemanticScores? scores;
        try
        {
            scores = semantic.Score(text, commands, media);
        }
        catch (Exception ex)
        {
            Log.Error("Error en la capa de significado", ex);
            return null;
        }
        if (scores is null) return null;
        return
        [
            .. scores.Ranked
                .Select(h => h.Media is { } kind ? new IntentCandidate(null, h.Score, new MediaOrder(kind), h.Anchor, Semantic: true) : new IntentCandidate(h.Command, h.Score, Semantic: true))
                .Where(c => (c.Command is null || !Contradicts(text, c.Command)) && !IntentMatcher.Refuses(text, c))
                .Take(MaxNear)
                .Select(Candidate),
        ];
    }

    /// <summary>
    /// Runs a command for an assistant and says how it went with what the command said. First, its risk
    /// (<see cref="ActionRisk.OfCommand"/>, with the commands it calls and every action of its «Si…»): when the user's
    /// <see cref="AskPolicy"/> asks for it, <paramref name="ask"/> asks the user, and without a «yes» it does not run
    /// (<c>declined</c>). Waits for it to finish (a long one makes its assistant wait, which it shows its own way).
    /// Paused, or not found: <c>failed</c> at once.
    /// </summary>
    /// <param name="who">Who asked, for the history («Lander»; «K-Desktop» without a body).</param>
    /// <param name="ask">Asks the user for this command and risk: <c>yes</c>, <c>always</c>, <c>no</c> or <c>timeout</c>.</param>
    /// <param name="choreography">With <see cref="RunMessage.Choreography"/>: the assistant's turn before each step (stage
    /// 6.3); cancelling it ends the run <c>cancelled</c>.</param>
    public static async Task<EndMessage> RunAsync(CommandEngine engine, RunMessage request, string who, Func<Command, ToolRisk, Task<string>> ask,
        AssistantChoreography? choreography = null, Func<Command, bool>? allowed = null)
    {
        if (engine.IsPaused) return new EndMessage(request.Run, "failed", [Loc.T("pause.refused")]);
        if (engine.Config.FindCommand(request.Command) is not { Enabled: true } command || !Available(engine.Config).Contains(command))
            return new EndMessage(request.Run, "failed", [Loc.T("assistantCommand.notFound")]);
        return await RunCommandAsync(engine, command, request.Run, who, ask, choreography, allowed).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a command for an assistant (one of the user's, or one made for the occasion: <see cref="AssistantActs"/>):
    /// its risk first, then the run, in the history as coming from <paramref name="who"/>.
    /// </summary>
    /// <param name="allowed">
    /// An order programmed by an assistant (<see cref="ScheduledPermit"/>): nobody is asked; what is not safe runs only if
    /// this says it was approved, whatever the user's policy.
    /// </param>
    public static async Task<EndMessage> RunCommandAsync(CommandEngine engine, Command command, string run, string who, Func<Command, ToolRisk, Task<string>> ask,
        AssistantChoreography? choreography = null, Func<Command, bool>? allowed = null)
    {
        var risk = ActionRisk.OfCommand(command, engine.Config.FindCommand, engine.Config.Settings.AssistantTrustedCommands.Contains(command.Id));
        if (allowed is not null)
        {
            if (risk != ToolRisk.Safe && !allowed(command))
            {
                Log.Info($"Asistente: «{command.Name}» no se aprobó al programar la orden: no se ejecuta");
                return new EndMessage(run, "declined", [Loc.T("assistantCommand.notApproved", command.Name)], Reason: "notApproved");
            }
        }
        else if (AskPolicy.Asks(risk, engine.Config.Settings.AssistantAskBefore) && await ask(command, risk).ConfigureAwait(false) is not ("yes" or "always"))
            return new EndMessage(run, "declined", [Loc.T("assistantCommand.declined", command.Name)]);
        var feedback = new ExecutionFeedback();
        Task running;
        using (feedback.Begin())
        using (ExecutionHistory.Describe(who))
            running = choreography is null
                ? engine.RunAsync(command, TriggerSource.Assistant)
                : engine.RunChoreographedAsync(command, TriggerSource.Assistant, choreography, choreography.Cancelled);
        try
        {
            await running.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new EndMessage(run, "cancelled", feedback.Messages);
        }
        if (choreography?.Cancelled.IsCancellationRequested == true) return new EndMessage(run, "cancelled", feedback.Messages);
        return new EndMessage(run, feedback.HasError ? "failed" : "ok", feedback.Messages);
    }

    /// <summary>What a command does: its enabled actions in words, its hotkeys and whether an assistant must ask first.</summary>
    public static DescribedMessage Describe(AppConfig config, DescribeMessage request)
    {
        if (config.FindCommand(request.Command) is not { } command) return new DescribedMessage(request.Id, false, string.Empty, [], [], "no");
        var risk = ActionRisk.OfCommand(command, config.FindCommand, config.Settings.AssistantTrustedCommands.Contains(command.Id));
        var asks = AskPolicy.Asks(risk, config.Settings.AssistantAskBefore);
        return new DescribedMessage(request.Id, true, command.Name,
            [.. command.Actions.Where(a => a.Enabled).Select(a => $"{ActionCatalog.Get(a.Kind).Name}: {a.Summary}")],
            [.. command.Hotkeys],
            !asks ? "no" : risk == ToolRisk.AskAlways ? "always" : "once");
    }

    /// <summary>
    /// A voice phrase added to a command or taken out of it (<see cref="PhraseMessage"/>), on the thread that owns the
    /// config (its collections are shown). Never one another command already has (it would be called twice).
    /// </summary>
    public static PhraseDoneMessage Phrase(AppConfig config, PhraseMessage request)
    {
        if (config.FindCommand(request.Command) is not { } command) return new PhraseDoneMessage(request.Id, false, string.Empty, "noCommand");
        var phrase = string.Join(' ', request.Phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.', ',', '!', '?', '¡', '¿', '«', '»', '"');
        var folded = string.Join(' ', TextSimilarity.Words(phrase));
        if (folded.Length == 0) return new PhraseDoneMessage(request.Id, false, command.Name, "empty");
        bool Same(string other) => string.Join(' ', TextSimilarity.Words(other)) == folded;
        if (request.Remove)
        {
            var removed = command.VoicePhrases.Where(Same).ToList();
            foreach (var p in removed) command.VoicePhrases.Remove(p);
            return new PhraseDoneMessage(request.Id, removed.Count > 0, command.Name, removed.Count > 0 ? null : "noPhrase");
        }
        if (Same(command.Name) || command.VoicePhrases.Any(Same)) return new PhraseDoneMessage(request.Id, false, command.Name, "exists");
        if (config.Commands.FirstOrDefault(c => c != command && (Same(c.Name) || c.VoicePhrases.Any(Same))) is { } other)
            return new PhraseDoneMessage(request.Id, false, command.Name, "taken", other.Name);
        command.VoicePhrases.Add(phrase);
        return new PhraseDoneMessage(request.Id, true, command.Name);
    }

    /// <summary>
    /// Runs the commands of an event found by its name (the closest, if it is clearly that one), asking first like for a
    /// command when any of them would ask (the riskiest decides). Not found or paused: <c>failed</c> at once.
    /// </summary>
    /// <param name="allowed">An order programmed by an assistant: whether running events was approved (nobody is asked).</param>
    public static async Task<EndMessage> RunEventAsync(CommandEngine engine, RunEventMessage request, string who, Func<EventRule, ToolRisk, Task<string>> ask,
        bool? allowed = null)
    {
        if (engine.IsPaused) return new EndMessage(request.Run, "failed", [Loc.T("pause.refused")]);
        var config = engine.Config;
        var rule = config.Events.Where(e => e.Name.Trim().Length > 0)
            .Select(e => (Rule: e, Score: FuzzyMatch.Score(FuzzyMatch.Tokens(request.Event), new SearchKey(e.Name))))
            .Where(p => p.Score >= MinEventScore)
            .OrderByDescending(p => p.Score)
            .Select(p => p.Rule)
            .FirstOrDefault();
        if (rule is null) return new EndMessage(request.Run, "failed", [Loc.T("assistantCommand.noEvent", request.Event)]);
        var risk = rule.Commands.Select(config.FindCommand).OfType<Command>()
            .Select(c => ActionRisk.OfCommand(c, config.FindCommand, config.Settings.AssistantTrustedCommands.Contains(c.Id)))
            .DefaultIfEmpty(ToolRisk.Safe).Max();
        if (allowed is { } approved)
        {
            if (risk != ToolRisk.Safe && !approved)
                return new EndMessage(request.Run, "declined", [Loc.T("assistantCommand.notApproved", rule.Name)], Reason: "notApproved");
        }
        // An event is never trusted as a whole: it asks at least once a time, unless every command in it is trusted.
        else if (AskPolicy.Asks(risk, config.Settings.AssistantAskBefore) && await ask(rule, risk).ConfigureAwait(false) is not ("yes" or "always"))
            return new EndMessage(request.Run, "declined", [Loc.T("assistantCommand.declined", rule.Name)]);
        var feedback = new ExecutionFeedback();
        Task run;
        using (feedback.Begin())
        using (ExecutionHistory.Describe(who))
            run = engine.RunEventAsync(rule, end: false, TriggerSource.Assistant);
        await run.ConfigureAwait(false);
        return new EndMessage(request.Run, feedback.HasError ? "failed" : "ok", feedback.Messages);
    }

    /// <summary>How close a name must be to an event's (FuzzyMatch) to take it.</summary>
    private const int MinEventScore = 300;

    /// <summary>A built-in media order by its name (<c>volumeDown</c>…), or null when there is no such order.</summary>
    public static MediaOrder? MediaOrderOf(MediaMessage request) =>
        Enum.TryParse<MediaOrderKind>(request.Order, ignoreCase: true, out var kind) && Enum.IsDefined(kind) ? new MediaOrder(kind, request.Value) : null;
}

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

    /// <summary>
    /// «el navegador y VS Code uno al lado del otro» (B7): several programs placed on one monitor (<see cref="AssistantActs.Arrange"/>,
    /// <see cref="AssistantActs.Layouts"/>; <paramref name="monitor"/> 1, 2…, null: the first one's). Without K-Desktop: null.
    /// </summary>
    /// <summary>One of K-Desktop's own actions for a group of tools (B9, <see cref="AssistantSystem.Ops"/>). Without K-Desktop: null.</summary>
    Task<EndMessage?> SystemAsync(string group, string op, int? value, string text, string? assistant, CancellationToken cancellationToken) =>
        Task.FromResult<EndMessage?>(null);

    /// <summary>Something for the paired phones (B11, <see cref="SendPhoneMessage"/>; its id is put by the bridge). Without K-Desktop: null.</summary>
    Task<PhoneSentMessage?> SendToPhoneAsync(SendPhoneMessage request, CancellationToken cancellationToken) => Task.FromResult<PhoneSentMessage?>(null);

    Task<EndMessage?> ArrangeAsync(IReadOnlyList<string> apps, string? layout, int? monitor, string? assistant, CancellationToken cancellationToken) =>
        Task.FromResult<EndMessage?>(null);

    /// <summary>
    /// A text taken from the window in front or put back in it (transform_text): <paramref name="op"/> <c>grab</c> (with
    /// <paramref name="source"/>) or <c>put</c> (<paramref name="text"/>, <paramref name="mode"/>, <paramref name="hwnd"/>);
    /// see <see cref="TextMessage"/>. Without K-Desktop: null.
    /// </summary>
    Task<TextDoneMessage?> TextAsync(string op, string? assistant, string source, string text, string mode, long hwnd, CancellationToken cancellationToken) =>
        Task.FromResult<TextDoneMessage?>(null);

    /// <summary>
    /// A voice phrase added to one of the user's commands (by its id), or taken out with <paramref name="remove"/>; see
    /// <see cref="PhraseMessage"/>. Without K-Desktop: null.
    /// </summary>
    Task<PhraseDoneMessage?> PhraseAsync(string command, string phrase, bool remove, CancellationToken cancellationToken) =>
        Task.FromResult<PhraseDoneMessage?>(null);

    /// <summary>
    /// An order programmed for later, the list of them or one removed (<see cref="ScheduleMessage"/>; its id is put by the
    /// bridge). Without K-Desktop: null.
    /// </summary>
    Task<ScheduleDoneMessage?> ScheduleAsync(ScheduleMessage request, CancellationToken cancellationToken) =>
        Task.FromResult<ScheduleDoneMessage?>(null);

    /// <summary>K-Desktop’s «Registro» (B3, <see cref="LogQueryMessage"/>; its id is put by the bridge). Without K-Desktop: null.</summary>
    Task<LogFoundMessage?> QueryLogAsync(LogQueryMessage request, CancellationToken cancellationToken) => Task.FromResult<LogFoundMessage?>(null);

    /// <summary>How K-Desktop is used (B3). Without K-Desktop: null.</summary>
    Task<UsageDoneMessage?> UsageAsync(int days, CancellationToken cancellationToken) => Task.FromResult<UsageDoneMessage?>(null);

    /// <summary>Why a command or an event does not work (B3). Without K-Desktop: null.</summary>
    Task<DiagnosedMessage?> DiagnoseAsync(string what, string? kind, CancellationToken cancellationToken) => Task.FromResult<DiagnosedMessage?>(null);

    /// <summary>An event made and saved, or removed again (B8, <see cref="MakeEventMessage"/>; its id is put by the bridge). Without K-Desktop: null.</summary>
    Task<MadeEventMessage?> MakeEventAsync(MakeEventMessage request, CancellationToken cancellationToken) => Task.FromResult<MadeEventMessage?>(null);
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

    /// <summary>
    /// The built-in order that stops K-Desktop's countdowns and waits, like Esc (<c>media</c> <c>cancel</c>): answered «ok»
    /// when something stopped, «none» when nothing was going on.
    /// </summary>
    public const string StopOrder = "cancel";

    /// <summary>Below this, no command of the user is anything like the order (and the AI is not offered run_command).</summary>
    public const double Plausible = Clear;

    /// <param name="writer">The model rewriting texts (transform_text); null: the tool says there is none.</param>
    /// <param name="builtin">Its timers and watches, listed with the programmed orders (B2); null: only those.</param>
    /// <param name="vision">The model looking at a copied picture (read_clipboard); <paramref name="reader"/>, Windows’ OCR for its text.</param>
    public static void AddTo(ToolRegistry registry, ICommandBridge bridge, IWriter? writer = null, BuiltinTools? builtin = null, IVision? vision = null,
        IScreenReader? reader = null)
    {
        registry.Add(new RunCommandTool(bridge));
        registry.Add(new FindCommandTool(bridge));
        registry.Add(new DescribeCommandTool(bridge));
        registry.Add(new DiagnoseCommandTool(bridge));
        registry.Add(new QueryLogTool(bridge));
        registry.Add(new UsageStatsTool(bridge));
        registry.Add(new AddVoicePhraseTool(bridge));
        registry.Add(new RunEventTool(bridge));
        registry.Add(new StopCountdownTool(bridge));
        registry.Add(new ControlAppTool(bridge));
        registry.Add(new ArrangeWindowsTool(bridge));
        registry.Add(new SendToPhoneTool(bridge));
        registry.Add(new SetVolumeTool(bridge));
        registry.Add(new AudioDeviceTool(bridge));
        registry.Add(new DisplayTool(bridge));
        registry.Add(new WindowsThemeTool(bridge));
        registry.Add(new ToggleRadioTool(bridge));
        registry.Add(new LockPcTool(bridge));
        registry.Add(new PowerPcTool(bridge));
        registry.Add(new TakeScreenshotTool(bridge));
        registry.Add(new PowerPlanTool(bridge));
        registry.Add(new KeepAwakeTool(bridge));
        registry.Add(new EndProcessTool(bridge));
        registry.Add(new TransformTextTool(bridge, writer));
        registry.Add(new ReadClipboardTool(bridge, vision, reader));
        registry.Add(new SetClipboardTool(bridge));
        registry.Add(new ScheduleTool(bridge, registry));
        registry.Add(new CreateEventTool(bridge, registry));
        registry.Add(new ListScheduledTool(bridge, builtin));
        registry.Add(new CancelScheduledTool(bridge));
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

    /// <summary>
    /// Whether <paramref name="heard"/> says each word of <paramref name="name"/> of three letters or more as it is written,
    /// or one letter off in a word of five or more (what speech recognition gets wrong); stricter than <see cref="Names"/>.
    /// </summary>
    /// <remarks>
    /// A verb of the name may go unsaid when the rest is said («con música» names «Poner música»: Parakeet heard «con» for
    /// «pon»), and a name said as it sounds counts («Kamovis» for «K-Movies»).
    /// </remarks>
    public static bool NamesClosely(string heard, string name)
    {
        var said = TextSimilarity.Words(heard);
        var words = TextSimilarity.Words(name);
        var all = words.Where(w => w.Length >= 3).ToArray();
        var named = all.Where(w => !IsVerb(w)).ToArray();
        if (named.Length == 0) named = all;
        if (named.Length > 0 && named.All(w => said.Any(s => s == w || (w.Length >= 5 && Math.Abs(s.Length - w.Length) <= 1 && TextSimilarity.Levenshtein(s, w) <= 1))))
            return true;
        return IntentMatcher.SoundScore(said, words).Similarity >= SoundsClosely;
    }

    /// <summary>How alike the sound of the name and of the words said must be for <see cref="NamesClosely"/>.</summary>
    public const double SoundsClosely = 0.85;

    /// <summary>A verb of doing (in any language): «poner», «abrir», «ejecutar»… («pon» is «poner»).</summary>
    private static bool IsVerb(string word) =>
        VoiceVocabulary.Current.Actions.Any(a => a == word || a.Length >= 3 && TextSimilarity.WordSimilarity(a, word) >= 0.85);

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
            // «Cancel» with nothing going on: not a failure, K-Desktop's words for it.
            "none" => new ToolResult($"Nothing to do: {name}.{said}") { Reply = end.Messages.Count > 0 ? end.Messages[0] : null },
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
        // The model chose it by name: it must be what the user asked for, not one that sounds like their words («la pestaña
        // de track series» ran «TrackIR 5» and «Anterior»). The routing (id, media), taught phrases and «repite el comando anterior» already know.
        if (arguments.GetString("id") is not { Length: > 0 } && arguments.GetString("media") is not { Length: > 0 }
            && context is { Heard: { Length: > 0 } heard, Decided: false } && !await AskedForAsync(heard, chosen, context.Remembered, cancellationToken).ConfigureAwait(false))
            return ToolResult.Fail($"Not done: what the user said («{heard}») is not about their command \"{chosen.Name}\"; it only sounds alike. Do not run commands that just sound like their words: do what they ask with another tool, or say plainly you could not.");
        var end = chosen.Media is { } media
            ? await bridge.MediaAsync(media, chosen.Value, cancellationToken).ConfigureAwait(false)
            : await bridge.RunAsync(chosen.Command!, context.Assistant, cancellationToken).ConfigureAwait(false);
        return CommandTools.Outcome(end, chosen.Name, Name);
    }

    /// <summary>How sure K-Desktop must be that the order means a command the model chose by name.</summary>
    public const double Asked = 0.70;

    /// <summary>
    /// Whether <paramref name="heard"/> asks for <paramref name="command"/>: it names it closely (each word of three letters
    /// or more, said as written or one letter off in a long word: «trackir» is not «track») or K-Desktop matches the order
    /// with it from <see cref="Asked"/>, or something that came to mind for the order names it («cuando diga «Kamugi», abre
    /// K-Movies» for «abre Kamovis»).
    /// </summary>
    private async Task<bool> AskedForAsync(string heard, MatchedCandidate command, IReadOnlyList<string>? remembered, CancellationToken cancellationToken)
    {
        if (CommandTools.NamesClosely(heard, command.Name)) return true;
        if (remembered?.Any(m => CommandTools.NamesClosely(m, command.Name)) == true) return true;
        var matched = await bridge.MatchAsync(heard, cancellationToken).ConfigureAwait(false);
        return matched is not null && matched.Parts.SelectMany(p => p.Candidates)
            .Any(c => c.Score >= Asked && (c.Command is { } id ? id == command.Command : string.Equals(c.Name, command.Name, StringComparison.OrdinalIgnoreCase)));
    }
}

/// <summary>
/// «No apagues el ordenador»: stops what K-Desktop is counting down or waiting for (shutting down after a countdown, a
/// wait, a message with a countdown), like Esc. The assistant answers as soon as a shutdown's countdown starts (5.0.300),
/// so it can still be stopped by talking.
/// </summary>
public sealed class StopCountdownTool(ICommandBridge bridge) : ITool
{
    public string Name => "stop_countdown";

    public string Description => "Stops what K-Desktop is counting down or waiting for right now: shutting down, restarting or signing out after a countdown, a wait, a message with a countdown. Like pressing Esc. Use it when the user says not to do it after all.";

    public string ParametersSchemaJson => ToolText.NoParameters;

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.stop_countdown.intent");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (await bridge.MediaAsync(CommandTools.StopOrder, null, cancellationToken).ConfigureAwait(false) is not { } end) return ToolResult.Fail("K-Desktop did not answer.");
        if (end.Status != "ok")
            return ToolResult.Fail("Nothing was counting down or waiting.") with { Reply = Loc.T("tool.stop_countdown.none") };
        return new ToolResult("Stopped: nothing that was counting down will happen.")
        {
            Done = Loc.T("tool.stop_countdown.done"),
            Reply = Loc.T("tool.stop_countdown.reply"),
        };
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
        // «Poner música», «Pausar»: one of K-Desktop's own music orders, not a command of the user (find_command lists them
        // too, and then this said «there is no command like that»).
        if (chosen is { Command: null, Media: { } media })
            return new ToolResult($"\"{chosen.Name}\" is one of K-Desktop's built-in music orders ({media}), not a command the user made: it works on whatever is playing, like the media keys. Run it with run_command and that name.");
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

/// <summary>
/// «Cuando diga «Kamugi», abre K-Movies» (5.0.132): another way of calling one of the user's commands, kept in the
/// command's voice phrases (K-Desktop → Commands → its voice phrases), so it runs from then on without the AI, by voice
/// or written, for K-Desktop's voice too. A name in English said the Spanish way comes out differently each time
/// («Kamovis», «Kamugi»): what speech recognition wrote is what is taught. Undoing it takes the phrase out.
/// </summary>
public sealed class AddVoicePhraseTool(ICommandBridge bridge) : ITool
{
    public string Name => "add_voice_phrase";

    public string Description => "Teaches another way the user calls one of their K-Desktop commands (a word as they say it or as it was heard: «kamugi» for K-Movies), so saying it runs that command from now on. Use it, not remember, when they say «when I say X, open/do Y» and Y is one of their commands.";

    public string ParametersSchemaJson =>
        """{"type":"object","properties":{"command":{"type":"string","description":"The exact name of the user's command"},"phrase":{"type":"string","description":"What they will say for it, as they wrote or said it, without the verb (kamugi, la peli)"}},"required":["command","phrase"]}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) =>
        Loc.T("tool.add_voice_phrase.intent", ToolText.Clean(arguments.GetString("phrase")), ToolText.Clean(arguments.GetString("command")));

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var asked = arguments.GetString("command")?.Trim() ?? string.Empty;
        var phrase = arguments.GetString("phrase")?.Trim() ?? string.Empty;
        if (asked.Length == 0 || phrase.Length == 0) return ToolResult.Fail("Say the command and the phrase.");
        MatchedCandidate? chosen;
        if (arguments.GetString("id") is { Length: > 0 } id) chosen = new MatchedCandidate(id, asked, 1);
        else if (await bridge.MatchAsync(asked, cancellationToken).ConfigureAwait(false) is not { } matched) return ToolResult.Fail("K-Desktop did not answer.");
        else if ((chosen = CommandTools.Pick(matched, asked)) is null || chosen.Command is null || !CommandTools.Names(asked, chosen.Name))
        {
            var candidates = matched.Parts.SelectMany(p => p.Candidates).Where(c => c.Command is not null && c.Score >= CommandTools.Plausible).Take(4).ToList();
            return ToolResult.Fail(candidates.Count == 0
                ? $"The user has no command called \"{asked}\" (only their own commands can be taught phrases; for anything else use remember)."
                : $"Which command? Candidates: {CommandTools.List(candidates)}. Call again with the exact name.");
        }
        var done = await bridge.PhraseAsync(chosen.Command!, phrase, remove: false, cancellationToken).ConfigureAwait(false);
        return done switch
        {
            null => ToolResult.Fail("K-Desktop did not answer."),
            { Ok: true } => new ToolResult($"Done: saying \"{phrase}\" now runs \"{done.Name}\".")
            {
                Done = Loc.T("tool.add_voice_phrase.done", phrase, done.Name),
                Reply = Loc.T("tool.add_voice_phrase.reply", phrase, done.Name),
                Undo = new ToolUndo(Name, chosen.Command! + "\n" + phrase),
            },
            { Reason: "exists" } => new ToolResult($"\"{done.Name}\" is already called that way: nothing to add.") { Reply = Loc.T("tool.add_voice_phrase.exists", phrase, done.Name) },
            { Reason: "taken" } => ToolResult.Fail($"Not done: \"{phrase}\" already runs another command, \"{done.Other}\". Tell the user."),
            _ => ToolResult.Fail("Not done: K-Desktop could not add it."),
        };
    }

    public async Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken)
    {
        var parts = undo.Data.Split('\n', 2);
        return parts.Length == 2 && await bridge.PhraseAsync(parts[0], parts[1], remove: true, cancellationToken).ConfigureAwait(false) is { Ok: true };
    }
}

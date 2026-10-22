using System.Text;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Tools;
using KDesktop.Core;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Brain.Talk;

/// <summary>An answer to say: its text, and whether it goes into the history (not «cancelled» or a repetition).</summary>
public sealed record TalkAnswer(string Text, bool Remember = true, bool Failed = false)
{
    /// <summary>The tools it used (offered again with the next order).</summary>
    public IReadOnlyList<string> Tools { get; init; } = [];

    /// <summary>The AI answered it (not a fixed rule): worth learning from.</summary>
    public bool FromAi { get; init; }

    /// <summary>It asks the user something back («¿A o B?»): the answer is listened for (stage 5.3).</summary>
    public bool AsksBack { get; init; }

    /// <summary>The tool calls and their results between the order and this answer, for the history.</summary>
    public IReadOnlyList<ChatMessage> Steps { get; init; } = [];

    /// <summary>The «Recibido.» its text starts with (null: none): after something already done and said, it goes.</summary>
    public string? Ack { get; init; }

    /// <summary>The text without its <see cref="Ack"/>.</summary>
    public string WithoutAck => Ack is { } ack && Text.StartsWith(ack, StringComparison.Ordinal) ? Text[ack.Length..].TrimStart() : Text;
}

/// <summary>
/// Talking with the model (plan §5.2): the instructions with the persona, the short history and the order; the model may
/// call tools (through the <see cref="ToolRunner"/>, which asks permission and writes the activity), at most
/// <see cref="MaxToolRounds"/> rounds and <see cref="MaxToolCalls"/> calls; then it must answer. Only the tools that fit
/// the order are offered (<see cref="ToolChooser"/>). No thinking (5.0.105: thinking first did not make Bonsai better at
/// dates and sums, only slower; <c>date_math</c> did), short answers, cleaned for a bubble and a voice.
/// </summary>
public sealed class TalkMind(ToolRunner tools, ToolChooser chooser, TimeProvider time, TalkMemory? memory = null)
{
    /// <summary>Rounds in which the model may call tools; after them it is asked without tools, to answer.</summary>
    public const int MaxToolRounds = 3;

    /// <summary>Tool calls for one order, in all its rounds.</summary>
    public const int MaxToolCalls = 3;

    /// <summary>A big model gets more: «abre YouTube, abre Steam, vacía la papelera y ábrela» are four.</summary>
    public const int CapableToolRounds = 5;

    public const int CapableToolCalls = 8;

    /// <summary>Longest answer, in tokens (one or two sentences, with room for a tool call).</summary>
    public const int MaxTokens = 220;

    /// <param name="recentTools">The tools used in the last exchange.</param>
    /// <param name="onText">The answer so far, cleaned, while it is written (any thread).</param>
    /// <param name="leaveOut">Tools not to offer for this order (run_command when no command of the user is anything like it).</param>
    /// <param name="commands">The user's commands K-Desktop found close to the order: named in the instructions, and
    /// run_command is offered whatever the order's meaning says (with «abre Steam y Epic Games» it was not, and the model
    /// said it had opened them).</param>
    /// <param name="capable">A big model: more rounds and calls, and the command tools always at hand.</param>
    public async Task<TalkAnswer> AnswerAsync(IChatModel model, string modelName, AssistantPersona persona, IReadOnlyList<ChatMessage> history,
        IReadOnlyCollection<string> recentTools, string said, ToolContext context, Action<string>? onText, CancellationToken cancellationToken,
        IReadOnlyCollection<string>? leaveOut = null, IReadOnlyList<string>? commands = null, bool capable = false, IReadOnlyList<string>? certain = null)
    {
        var maxRounds = capable ? CapableToolRounds : MaxToolRounds;
        var maxCalls = capable ? CapableToolCalls : MaxToolCalls;
        // The order's meaning once, for the tools and for what comes to mind.
        var order = await MeaningOfAsync(said, cancellationToken).ConfigureAwait(false);
        var known = memory is null ? [] : await memory.RecallAsync(said, order, cancellationToken).ConfigureAwait(false);
        // About the memory: its tools, and not the notes (a small model took «añade a la memoria» for a note).
        var aboutMemory = memory is not null && TalkMemory.IsAbout(said);
        var hidden = new HashSet<string>(leaveOut ?? [], StringComparer.Ordinal);
        if (aboutMemory) hidden.UnionWith(NoteTools);
        var available = hidden.Count > 0 ? [.. tools.Registry.All.Where(t => !hidden.Contains(t.Name))] : tools.Registry.All;
        var always = new List<string>(recentTools.Where(t => !hidden.Contains(t)));
        if (commands is { Count: > 0 }) always.Add(RunCommandTool);
        if (capable) always.AddRange(CommandToolNames.Where(t => !hidden.Contains(t) && tools.Registry.Find(t) is not null));
        if (aboutMemory) always.AddRange(MemoryTools);
        // «Cuando diga «la vuelta», abre…» came to mind: what it asks for must be at hand.
        if (known.Any(m => QuickRules.Sites(m.Text).Count > 0)) always.Add(OpenWebTool);
        var offered = await chooser.ChooseAsync(available, said, [.. always.Distinct()], order, cancellationToken).ConfigureAwait(false);
        var definitions = offered.Tools.Select(t => new ToolDefinition(t.Name, t.Description, t.ParametersSchemaJson)).ToList();
        var messages = new List<ChatMessage> { ChatMessage.System(TalkPrompt.Compose(persona, time.GetLocalNow(), known, commands, certain)) };
        messages.AddRange(history);
        // The language it speaks in, next to the order: a small model answers in the language it is spoken to otherwise
        // (Options → Assistant → General → the language they speak in). Only for this order, not kept in the history.
        messages.Add(ChatMessage.User(said + "\n\n" + Loc.T("assistant.prompt.answerIn")));
        var first = messages.Count;

        var calls = 0;
        string? toolReply = null;
        // What the tools that worked say they did (null: one that has no words of its own), and whether one was an action.
        var replies = new List<string?>();
        var acted = false;
        var used = new List<string>();
        var answer = string.Empty;
        var nudged = false;
        // The calls that failed in this order: the same one again is not run (it asked control_app for Steam three times).
        var failed = new Dictionary<string, string>(StringComparer.Ordinal);
        var worked = new HashSet<string>(StringComparer.Ordinal);
        // It asked again for a call it already made: going round in circles («chiste» as a command, an event, a command…
        // eight times), it answers now.
        var stuck = false;
        ChatMessage? answerNow = null;
        try
        {
            for (var round = 0; round <= maxRounds; round++)
            {
                var offer = round < maxRounds && calls < maxCalls && !stuck;
                // Without tools it is told so: otherwise it writes one more call as text (and its answer was «Falló «chiste»»).
                if (!offer && round > 0 && answerNow is null)
                {
                    answerNow = ChatMessage.User(Loc.T("assistant.prompt.answerNow"));
                    messages.Add(answerNow);
                }
                var request = new ChatRequest(modelName, messages, offer ? definitions : []) { Temperature = capable ? 0.3 : 0.5, MaxTokens = MaxTokens };
                // With commands of theirs at hand, nothing is said while it writes: the text may be one to take back. Nor
                // once it acted with tools that have their own words: those replace its text (its «Recibido.» was said
                // ahead, and then «Recibido, Houston. He ejecutado…» again from the start).
                var silent = (commands is { Count: > 0 } && !acted) || (acted && replies.All(r => r is not null));
                var (text, asked) = await RoundAsync(model, request, silent ? null : onText, cancellationToken).ConfigureAwait(false);
                if (asked.Count == 0)
                {
                    // It names one of their commands without having run it («Let me try the "Abrir Papelera" command»):
                    // once, it is told so, and runs it or answers without it. Not when it already called something in this
                    // order: «Telegram no lo ha aceptado» after send_telegram failed is no command to run (it opened Telegram).
                    if (!nudged && calls == 0 && offer && commands is { Count: > 0 } && NamedCommand(text, commands) is { } named)
                    {
                        nudged = true;
                        Log.Info($"Asistente: la IA nombra «{named}» sin ejecutarlo: otra vuelta");
                        messages.Add(ChatMessage.Assistant(text));
                        messages.Add(ChatMessage.User(Loc.T("assistant.prompt.notRun", named)));
                        continue;
                    }
                    answer = text;
                    break;
                }
                // A call where none was offered (written as text): not run; it answers with what it has.
                if (!offer)
                {
                    Log.Warn($"Asistente: la IA pide {string.Join(", ", asked.Select(c => c.Name))} sin herramientas a mano: no se hace");
                    answer = text;
                    break;
                }

                messages.Add(ChatMessage.Assistant(text, asked));
                foreach (var call in asked)
                {
                    string result;
                    var same = call.Name + " " + call.Arguments.GetRawText();
                    if (++calls > maxCalls) result = "Not done: too many tools for one order. Answer now.";
                    else if (failed.TryGetValue(same, out var before))
                    {
                        result = "Not done: you already tried exactly this in this order and it failed (" + before + "). Say plainly what could not be done.";
                        stuck = true;
                    }
                    // Done already: once is what was asked (it ran «Anterior» twice more in one order).
                    else if (worked.Contains(same))
                    {
                        result = "Not done again: you already did exactly this in this order and it worked. Do not repeat it.";
                        stuck = true;
                    }
                    else
                    {
                        var outcome = await tools.RunAsync(call.Name, call.Arguments, context, said, "ai", cancellationToken).ConfigureAwait(false);
                        result = outcome.Result is { } done ? done.Text : "Not done: " + outcome.Entry.Did;
                        Log.Info($"Asistente: la IA llama a {call.Name} {Short(call.Arguments.GetRawText())} → {Short(result)}");
                        if (outcome.Result is not { Failed: false }) failed[same] = outcome.Entry.Did;
                        // A failure's words for the user, if it has them (not the activity's «Falló «chiste»»).
                        toolReply = outcome.Result is { Failed: false } ok ? ok.Reply ?? ok.Done ?? toolReply : outcome.Result?.Reply ?? toolReply;
                        if (outcome.Result is { Failed: false } good)
                        {
                            if (!used.Contains(call.Name)) used.Add(call.Name);
                            replies.Add(good.Reply);
                            acted |= good.Undo is not null || good.Acted;
                            worked.Add(same);
                        }
                    }
                    messages.Add(ChatMessage.ToolResult(call.Name, result, call.Id));
                }
            }
        }
        catch (ChatModelException ex)
        {
            Log.Warn($"Asistente: la IA no ha contestado ({ex.Failure}: {ex.Message})");
            return new TalkAnswer(persona.Phrase("error"), Remember: false, Failed: true);
        }

        var steps = messages.GetRange(first, messages.Count - first).Where(m => !ReferenceEquals(m, answerNow)).ToList();
        // It did something and every tool has its own words for it: those, exact (a small model may well say «in 25
        // minutes» for a timer set for 20). Its own text is for questions and chat.
        if (acted && replies.Count > 0 && replies.All(r => r is not null))
        {
            var ack = persona.Phrase("ok");
            return new TalkAnswer(ack + " " + string.Join(" ", replies)) { Tools = used, Steps = steps, Ack = ack };
        }

        var clean = ReplyText.Clean(answer);
        // It did or looked something up: nothing is missing, so no question at the end («¿Listo para despegar?»).
        if (used.Count > 0) clean = ReplyText.DropQuestions(clean);
        // Silent after doing something: what the tool says it did.
        if (clean.Length == 0) clean = toolReply ?? persona.Phrase("notFound");
        // «He ejecutado «Captura de pantalla».» with nothing run (and no such command): copied from an earlier answer.
        if (!acted && ClaimsToHaveRun(clean))
        {
            Log.Warn($"Asistente: la IA dice que ha ejecutado algo sin hacerlo: «{clean}»");
            clean = persona.Phrase("notFound");
        }
        return new TalkAnswer(clean) { Tools = used, Steps = steps };
    }

    private const string RunCommandTool = "run_command";

    private const string OpenWebTool = "open_web";

    private static readonly string[] MemoryTools = ["remember", "forget"];

    private static readonly string[] CommandToolNames = ["run_command", "find_command"];

    private static string Short(string text) => text.Length > 160 ? text[..160] + "…" : text;

    private static readonly string[] NoteTools = ["save_note", "find_notes"];

    /// <summary>The first of <paramref name="commands"/> the text names, or null.</summary>
    public static string? NamedCommand(string text, IReadOnlyList<string> commands) =>
        commands.FirstOrDefault(c => c.Trim().Length >= 3 && text.Contains(c.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the text says, in the words of run_command, that a command was run.</summary>
    public static bool ClaimsToHaveRun(string text)
    {
        var reply = Loc.T("tool.run_command.reply", "\u0001");
        var start = reply.IndexOf('\u0001', StringComparison.Ordinal);
        var words = (start > 0 ? reply[..start] : reply).TrimEnd('«', '"', '“', '„', ' ');
        return words.Length >= 4 && text.Contains(words, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<MeaningVectors?> MeaningOfAsync(string said, CancellationToken cancellationToken)
    {
        if (chooser.Meaning is not { } meaning) return null;
        try { return await meaning.EmbedAsync([said], keep: false, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Log.Warn($"Asistente: sin significado para la orden: {ex.Message}");
            return null;
        }
    }

    private static async Task<(string Text, List<ToolCall> Calls)> RoundAsync(IChatModel model, ChatRequest request, Action<string>? onText, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var calls = new List<ToolCall>();
        var shown = string.Empty;
        await foreach (var chunk in model.StreamAsync(request, cancellationToken).ConfigureAwait(false))
        {
            if (chunk.ToolCalls is { Count: > 0 } asked) calls.AddRange(asked);
            if (chunk.Content.Length == 0) continue;
            text.Append(chunk.Content);
            // Text next to a tool call is not the answer yet.
            if (onText is null || calls.Count > 0) continue;
            var clean = ReplyText.Clean(text.ToString());
            if (clean != shown)
            {
                shown = clean;
                onText(clean);
            }
        }
        return (text.ToString(), calls);
    }
}

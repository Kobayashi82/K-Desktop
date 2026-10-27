using System.Text;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Tools;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

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

    /// <summary>«Escribe lo que te diga»: once said, K-Desktop starts a dictation (stage 10.5).</summary>
    public bool Dictates { get; init; }

    /// <summary>The tool calls and their results between the order and this answer, for the history.</summary>
    public IReadOnlyList<ChatMessage> Steps { get; init; } = [];

    /// <summary>The «Recibido.» its text starts with (null: none): after something already done and said, it goes.</summary>
    public string? Ack { get; init; }

    /// <summary>
    /// The whole text in Markdown of a long answer (stage 14.2; null: none): <see cref="Text"/> is then only what is said,
    /// its summary, and K-Desktop shows this on a reading sheet.
    /// </summary>
    public string? Written { get; init; }

    /// <summary>The text without its <see cref="Ack"/>.</summary>
    public string WithoutAck => Ack is { } ack && Text.StartsWith(ack, StringComparison.Ordinal) ? Text[ack.Length..].TrimStart() : Text;
}

/// <summary>
/// How it thinks harder (stage 12): the model (the usual one with its reasoning on, or the one of Options → Brain → «Para
/// pensar más») and how many tokens it may think before answering (0: another model that answers without reasoning, 5.0.289).
/// </summary>
public sealed record Thinker(string Model, int Tokens);

/// <summary>
/// Talking with the model (plan §5.2): the instructions with the persona, the short history and the order; the model may
/// call tools (through the <see cref="ToolRunner"/>, which asks permission and writes the activity), at most
/// <see cref="MaxToolRounds"/> rounds and <see cref="MaxToolCalls"/> calls; then it must answer. Only the tools that fit
/// the order are offered (<see cref="ToolChooser"/>). No thinking as a rule (5.0.105: thinking first did not make Bonsai
/// better at dates and sums, only slower; <c>date_math</c> did), short answers, cleaned for a bubble and a voice. It
/// thinks harder (stage 12) when asked to («piénsalo bien»), when the model itself asks with <c>think_harder</c> (a sum of
/// several steps, a plan, a comparison…), and once more when it gets stuck or says nothing.
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

    /// <summary>
    /// Longest round with tools at hand: a call may carry a long text («envíame la lista completa por Telegram»: the 46
    /// logins were cut at 220, the call arrived empty and nothing was sent).
    /// </summary>
    public const int ToolRoundTokens = 2048;

    /// <summary>About <see cref="MaxTokens"/> in letters: where an answer with no call stops in such a round (it did not fit).</summary>
    private const int SpokenChars = 1000;

    /// <summary>Longest long answer (stage 14.2), in tokens: a summary and a whole text of a few pages.</summary>
    public const int LongTokens = 2000;

    /// <param name="recentTools">The tools used in the last exchange.</param>
    /// <param name="onText">The answer so far, cleaned, while it is written (any thread).</param>
    /// <param name="leaveOut">Tools not to offer for this order (run_command when no command of the user is anything like it).</param>
    /// <param name="commands">The user's commands K-Desktop found close to the order: named in the instructions, and
    /// run_command is offered whatever the order's meaning says (with «abre Steam y Epic Games» it was not, and the model
    /// said it had opened them).</param>
    /// <param name="capable">A big model: more rounds and calls, and the command tools always at hand.</param>
    /// <param name="thinker">How it may think harder (null: it never does).</param>
    /// <param name="thinkFirst">It thinks harder from the start («piénsalo bien», or the answer before was wrong).</param>
    /// <param name="onThinkHard">It starts thinking harder (any thread): the assistant says it is thinking it over.</param>
    /// <param name="doubted">The user said the answer before was wrong: it checks it, and keeps it if it was right.</param>
    /// <param name="onLong">The whole text so far of a long answer (stage 14.2), in Markdown, while it is written (any thread);
    /// <paramref name="onText"/> then has only its summary.</param>
    /// <param name="powerLast">The order turns the PC off among other things: told to leave that for the end.</param>
    public async Task<TalkAnswer> AnswerAsync(IChatModel model, string modelName, AssistantPersona persona, IReadOnlyList<ChatMessage> history,
        IReadOnlyCollection<string> recentTools, string said, ToolContext context, Action<string>? onText, CancellationToken cancellationToken,
        IReadOnlyCollection<string>? leaveOut = null, IReadOnlyList<string>? commands = null, bool capable = false, IReadOnlyList<string>? certain = null,
        Thinker? thinker = null, bool thinkFirst = false, Action? onThinkHard = null, bool doubted = false, Action<string>? onLong = null, bool later = false,
        bool powerLast = false)
    {
        // An order programmed earlier, now due (its tools were approved then): done whole, without asking.
        var scheduled = ScheduledOrder.Current is not null;
        var maxRounds = capable ? CapableToolRounds : MaxToolRounds;
        var maxCalls = capable ? CapableToolCalls : MaxToolCalls;
        var thinking = thinkFirst && thinker is not null;
        // A long answer (stage 14.2): asked for, or one that did not fit and is written again in the long form.
        var writing = LongAnswers.Asked(said);
        // What only steers this order (the long form asked again): not for the history.
        var asides = new List<ChatMessage>();
        // Thinking harder again would only spend the same time once more: once per order.
        var retried = false;
        // Its rounds thinking, and since when: the whole budget goes to the first one (with tools it planned and then
        // thought 1024 tokens again in every round, 22 s each: 44 s for one order), and none after ThinkFor.
        var thoughtRounds = 0;
        var thinkingSince = 0L;
        int ThinkBudget()
        {
            // A model that answers without reasoning (5.0.289) thinks harder only by being another one.
            if (!thinking || thinker!.Tokens == 0) return 0;
            if (Environment.TickCount64 - thinkingSince > ThinkFor.TotalMilliseconds) return 0;
            return thoughtRounds++ == 0 ? thinker!.Tokens : Math.Max(BrainConfig.LeastThinkTokens, thinker!.Tokens / 4);
        }
        // The order's meaning once, for the tools and for what comes to mind.
        var order = await MeaningOfAsync(said, cancellationToken).ConfigureAwait(false);
        var known = memory is null ? [] : await memory.RecallAsync(said, order, cancellationToken).ConfigureAwait(false);
        // About the memory: its tools, and not the notes (a small model took «añade a la memoria» for a note).
        var aboutMemory = memory is not null && TalkMemory.IsAbout(said);
        var hidden = new HashSet<string>(leaveOut ?? [], StringComparer.Ordinal);
        if (aboutMemory) hidden.UnionWith(NoteTools);
        var available = hidden.Count > 0 ? [.. tools.Registry.All.Where(t => !hidden.Contains(t.Name))] : tools.Registry.All;
        var always = new List<string>(WithTheirServer(recentTools).Where(t => !hidden.Contains(t)));
        if (commands is { Count: > 0 }) always.Add(RunCommandTool);
        if (capable) always.AddRange(CommandToolNames.Where(t => !hidden.Contains(t) && tools.Registry.Find(t) is not null));
        if (aboutMemory) always.AddRange(MemoryTools);
        // «Esa ventana», «la pestaña», «lo que ves»: what is on the screen must be at hand (with series in the order,
        // Letterboxd's tools won and it never looked at the TrackSeries tab).
        if (AboutTheScreen(said)) always.AddRange(ScreenTools);
        // Files the user gave it (stage 14.1): reading, opening, moving and deleting them at hand.
        if (context.Files.Count > 0) always.AddRange(GivenFileTools.Where(t => !hidden.Contains(t) && tools.Registry.Find(t) is not null));
        // «Cuando diga «la vuelta», abre…» came to mind: what it asks for must be at hand.
        if (known.Any(m => QuickRules.Sites(m.Text).Count > 0)) always.Add(OpenWebTool);
        // «Cuando diga «Kamugi», abre K-Movies»: a phrase for one of their commands, not a memory that only works said exactly.
        if (TeachesPhrase(said) && tools.Registry.Find(VoicePhraseTool) is not null) always.Add(VoicePhraseTool);
        // «A las siete…»: programming it, or a plain reminder, must be at hand (and finding the user's commands to name them).
        if (later) always.AddRange(LaterTools.Where(t => !hidden.Contains(t) && tools.Registry.Find(t) is not null));
        var remembered = known.Select(m => m.Text).ToList();
        var offered = await chooser.ChooseAsync(available, said, [.. always.Distinct()], order, cancellationToken).ConfigureAwait(false);
        var definitions = offered.Tools.Select(t => new ToolDefinition(t.Name, t.Description, t.ParametersSchemaJson)).ToList();
        var atHand = offered.Tools.ToList();
        // Most of its tools are not at hand (230 with sixteen MCP servers): it can look for more (5.0.140; with none of AIMP's
        // for listing playlists it said it had no access to that library, and changed songs instead).
        if (available.Count > atHand.Count) definitions.Add(FindToolsDefinition(available, atHand.Count));
        // It may ask to think harder when the order needs more than a quick answer.
        if (thinker is not null && !thinking) definitions.Add(ThinkHarderDefinition);
        var searches = 0;
        var commandSearches = 0;
        var messages = new List<ChatMessage> { ChatMessage.System(TalkPrompt.Compose(persona, time.GetLocalNow(), known, commands, certain, powerLast)) };
        messages.AddRange(history);
        // The language it speaks in, next to the order: a small model answers in the language it is spoken to otherwise
        // (Options → Assistant → General → the language they speak in). Only for this order, not kept in the history.
        messages.Add(ChatMessage.User(said + GivenFilesLine(context) + "\n\n" + Loc.T("assistant.prompt.answerIn")
            + (writing ? "\n\n" + Loc.T("assistant.prompt.long") : "")
            + (thinking ? "\n\n" + Loc.T(doubted ? "assistant.prompt.checkAgain" : "assistant.prompt.thinkHard") : "")
            + (later ? "\n\n" + Loc.T("assistant.prompt.later") : "")
            + (scheduled ? "\n\n" + Loc.T("assistant.prompt.scheduled") : "")));
        if (writing) Log.Info("Asistente: la orden pide una respuesta larga");
        if (thinking)
        {
            thinkingSince = Environment.TickCount64;
            Log.Info($"Asistente: la IA piensa más desde el principio ({BrainProviders.ModelLabel(thinker!.Model)}, {thinker.Tokens} tokens)");
            onThinkHard?.Invoke();
        }
        var first = messages.Count;

        var calls = 0;
        string? toolReply = null;
        // What the tools that worked say they did (null: one that has no words of its own), and whether one was an action.
        var replies = new List<string?>();
        var acted = false;
        var used = new List<string>();
        var answer = string.Empty;
        var nudged = false;
        var reminded = false;
        // The calls that failed in this order: the same one again is not run (it asked control_app for Steam three times).
        var failed = new Dictionary<string, string>(StringComparer.Ordinal);
        var worked = new HashSet<string>(StringComparer.Ordinal);
        // It asked again for a call it already made: going round in circles («chiste» as a command, an event, a command…
        // eight times), it answers now.
        var stuck = false;
        ChatMessage? answerNow = null;
        // Thinking harder from now on: more rounds and calls if it needs them, and no asking for it again.
        void StartThinking(int round)
        {
            thinking = true;
            thinkingSince = Environment.TickCount64;
            definitions.RemoveAll(d => d.Name == ThinkHarderTool);
            maxRounds = Math.Max(maxRounds, round + ThinkRounds);
            maxCalls = Math.Max(maxCalls, calls + ThinkCalls);
            onThinkHard?.Invoke();
        }

        try
        {
            var round = 0;
            while (true)
            {
                for (; round <= maxRounds; round++)
                {
                    var offer = round < maxRounds && calls < maxCalls && !stuck;
                    // Without tools it is told so: otherwise it writes one more call as text (and its answer was «Falló «chiste»»).
                    if (!offer && round > 0 && answerNow is null)
                    {
                        answerNow = ChatMessage.User(Loc.T("assistant.prompt.answerNow"));
                        messages.Add(answerNow);
                    }
                    var request = new ChatRequest(thinking ? thinker!.Model : modelName, [.. messages], offer ? [.. definitions] : [])
                    {
                        Temperature = thinking ? ThinkTemperature : capable ? 0.3 : 0.5,
                        MaxTokens = offer ? ToolRoundTokens : writing ? LongTokens : thinking ? ThinkAnswerTokens : MaxTokens,
                        ThinkTokens = ThinkBudget(),
                    };
                    // With commands of theirs at hand, nothing is said while it writes: the text may be one to take back. Nor
                    // once it acted with tools that have their own words: those replace its text (its «Recibido.» was said
                    // ahead, and then «Recibido, Houston. He ejecutado…» again from the start).
                    var silent = (commands is { Count: > 0 } && !acted) || (acted && replies.All(r => r is not null));
                    var (text, asked, cut) = await RoundAsync(model, request, silent ? null : onText, writing ? onLong : null, writing, cancellationToken).ConfigureAwait(false);
                    // It did not fit (220 tokens for a bubble, or a round with tools that went on writing): once, it writes it
                    // again in the long form, for the reading sheet (stage 14.2).
                    if (asked.Count == 0 && cut && !writing)
                    {
                        writing = true;
                        Log.Info($"Asistente: la respuesta no cabe ({text.Length} letras): la escribe larga");
                        asides.Add(ChatMessage.Assistant(text));
                        asides.Add(ChatMessage.User(Loc.T("assistant.prompt.longer")));
                        messages.AddRange(asides);
                        round--;
                        continue;
                    }
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
                        // It was asked to send something and answers without having sent it, with a tool for it at hand: once,
                        // it is told so.
                        if (!reminded && offer && ActionClaims.Missing(said, used) is [var missing, ..]
                            && atHand.Any(t => ActionClaims.IsOfKind(t.Name, missing)))
                        {
                            reminded = true;
                            Log.Info($"Asistente: la IA contesta sin hacer lo que le pedían ({missing}): otra vuelta");
                            messages.Add(ChatMessage.Assistant(text));
                            messages.Add(ChatMessage.User(Loc.T("assistant.prompt.missing." + missing)));
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
                        // Looking for more tools is no tool call: it does nothing, and does not use up the order's calls.
                        if (call.Name == FindToolsTool)
                        {
                            result = ++searches > MaxSearches
                                ? "Enough searching: use the tools you have, or say plainly what you cannot do."
                                : await FindToolsAsync(call.Arguments, available, atHand, definitions, cancellationToken).ConfigureAwait(false);
                            messages.Add(ChatMessage.ToolResult(call.Name, result, call.Id));
                            continue;
                        }
                        // Nor is thinking harder: from the next round it thinks before answering (stage 12).
                        if (call.Name == ThinkHarderTool)
                        {
                            if (thinker is null || thinking) result = "You are already thinking it through. Answer now, or call the tools you need.";
                            else
                            {
                                Log.Info($"Asistente: la IA pide pensar más («{Short(call.Arguments.GetString("reason") ?? string.Empty)}») → {BrainProviders.ModelLabel(thinker.Model)}, {thinker.Tokens} tokens");
                                StartThinking(round);
                                result = "Now take your time: work it out step by step, check it, and then answer (calling tools if they are needed).";
                            }
                            messages.Add(ChatMessage.ToolResult(call.Name, result, call.Id));
                            continue;
                        }
                        // Looking for commands over and over with other words («cerebro», «limpiar», «descargar»… four times for
                        // «libera el cerebro»): their commands do not do it.
                        if (call.Name == FindCommandTool && ++commandSearches > MaxCommandSearches)
                        {
                            Log.Info($"Asistente: la IA busca comandos otra vez ({Short(call.Arguments.GetRawText())}): no se hace");
                            messages.Add(ChatMessage.ToolResult(call.Name, "Not done: you already looked for commands " + MaxCommandSearches + " times in this order. "
                                + "None of their commands does this: use another of your tools (find_tools if none at hand fits) or say plainly that it cannot be done.", call.Id));
                            continue;
                        }
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
                            // Where an address or a name may honestly come from: what was said and what the tools said (never the
                            // instructions, which have examples), and what came to mind.
                            var sources = string.Join('\n', messages.Skip(1).Select(m => m.Content).Concat(remembered));
                            var outcome = await tools.RunAsync(call.Name, call.Arguments, context with { Sources = sources, Remembered = remembered }, said, "ai", cancellationToken).ConfigureAwait(false);
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
                // Going round in circles with what failed, or nothing at all to say: once, it thinks it over (stage 12).
                if (!thinking && !retried && thinker is not null && !acted && worked.Count == 0
                    && (stuck || (ReplyText.Clean(answer).Length == 0 && toolReply is null)))
                {
                    retried = true;
                    Log.Info($"Asistente: la IA {(stuck ? "se atasca" : "no dice nada")}: lo piensa mejor ({BrainProviders.ModelLabel(thinker.Model)}, {thinker.Tokens} tokens)");
                    if (answerNow is not null)
                    {
                        messages.Remove(answerNow);
                        answerNow = null;
                    }
                    if (answer.Trim().Length > 0) messages.Add(ChatMessage.Assistant(answer));
                    messages.Add(ChatMessage.User(Loc.T("assistant.prompt.thinkAgain")));
                    stuck = false;
                    answer = string.Empty;
                    StartThinking(++round);
                    continue;
                }
                break;
            }
        }
        catch (ChatModelException ex)
        {
            Log.Warn($"Asistente: la IA no ha contestado ({ex.Failure}: {ex.Message})");
            return new TalkAnswer(persona.Phrase("error"), Remember: false, Failed: true);
        }

        var steps = messages.GetRange(first, messages.Count - first).Where(m => !ReferenceEquals(m, answerNow) && !asides.Contains(m)).ToList();
        // It did something and every tool has its own words for it: those, exact (a small model may well say «in 25
        // minutes» for a timer set for 20). Its own text is for questions and chat.
        if (acted && replies.Count > 0 && replies.All(r => r is not null))
        {
            var ack = persona.Phrase("ok");
            return new TalkAnswer(ack + " " + string.Join(" ", replies)) { Tools = used, Steps = steps, Ack = ack };
        }

        // A long answer (stage 14.2): its summary is what is said, the rest is written down. Not asked for but too long for a
        // bubble: all of it written down, and its first sentences said.
        string? written = null;
        if (writing)
        {
            var (summary, body) = LongAnswers.Split(answer);
            if (ReplyText.Clean(body).Length >= LongAnswers.LeastChars)
            {
                written = body;
                answer = summary;
            }
            // Too little for a sheet: said as it is.
            else answer = (summary + "\n" + body).Trim();
        }
        else if (LongAnswers.TooLong(answer))
        {
            written = LongAnswers.Markdown(answer);
            answer = string.Empty;
        }
        if (written is not null) Log.Info($"Asistente: respuesta larga ({written.Length} letras) para la hoja de lectura");
        var clean = ReplyText.Clean(answer);
        // It did or looked something up: nothing is missing, so no question at the end («¿Listo para despegar?»).
        if (used.Count > 0) clean = ReplyText.DropQuestions(clean);
        // Written down without a summary: its first sentences, and that the rest is written.
        if (clean.Length == 0 && written is not null) clean = LongAnswers.Lead(written, persona.Phrase("longWritten"));
        // Silent after doing something: what the tool says it did.
        if (clean.Length == 0) clean = toolReply ?? persona.Phrase("notFound");
        // «He ejecutado «Captura de pantalla».» with nothing run (and no such command): copied from an earlier answer.
        if (!acted && ClaimsToHaveRun(clean))
        {
            Log.Warn($"Asistente: la IA dice que ha ejecutado algo sin hacerlo: «{clean}»");
            clean = persona.Phrase("notFound");
        }
        // «Te he enviado a Telegram…» having only opened the app: what it claims and did not do goes, the rest stays.
        var (founded, unfounded) = ActionClaims.Check(clean, used);
        if (unfounded.Count > 0)
        {
            Log.Warn($"Asistente: la IA dice que ha hecho algo que no hizo ({string.Join(", ", unfounded)}; usó {(used.Count > 0 ? string.Join(", ", used) : "nada")}): «{clean}»");
            clean = founded;
        }
        return new TalkAnswer(clean) { Tools = used, Steps = steps, Written = written };
    }

    private const string ThinkHarderTool = "think_harder";

    /// <summary>Rounds and calls it gets on top when it starts thinking harder.</summary>
    public const int ThinkRounds = 3;

    public const int ThinkCalls = 4;

    /// <summary>Thinking, its answer may be longer (a plan, a comparison), and it writes as the reasoning models are tuned to.</summary>
    public const int ThinkAnswerTokens = 400;

    /// <summary>Thinking longer than this in one order, it answers with what it has (each round of 1024 tokens is some 20 s with Bonsai).</summary>
    public static readonly TimeSpan ThinkFor = TimeSpan.FromSeconds(45);

    private const double ThinkTemperature = 0.6;

    /// <summary>
    /// The tool to think harder (stage 12): not one of the registry, like <c>find_tools</c>; it costs no call and does
    /// nothing by itself, only makes the next rounds think first.
    /// </summary>
    private static readonly ToolDefinition ThinkHarderDefinition = new(ThinkHarderTool,
        "Think harder before answering: call it when a quick answer is not enough — a sum or count of several steps, a plan, "
        + "comparing options, a long or careful text, code, a riddle — or when you are not sure. Then you get time to work it "
        + "out step by step. Not for simple questions or orders.",
        """{"type":"object","properties":{"reason":{"type":"string","description":"Why it needs more thought, in a few words"}}}""");

    private const string FindToolsTool = "find_tools";

    /// <summary>Searches for tools in one order, at most.</summary>
    public const int MaxSearches = 3;

    /// <summary>How many tools one search brings (a whole server: up to <see cref="ServerShown"/>).</summary>
    public const int Found = 6;

    public const int ServerShown = 30;

    /// <summary>The tool to look for more tools: what it is for, and the servers there are.</summary>
    private ToolDefinition FindToolsDefinition(IReadOnlyCollection<ITool> available, int atHand)
    {
        var servers = available.Select(t => tools.Registry.GroupOf(t.Name)).OfType<string>()
            .Where(g => g.StartsWith("mcp:", StringComparison.Ordinal)).Select(g => g[4..]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var description = $"Only {atHand} of your {available.Count} tools are at hand now. When none of them does what is asked, look here for more "
            + "before saying you cannot: by what the tool must do (query) or by the name of the MCP server it belongs to (server)"
            + (servers.Count > 0 ? $" — servers: {string.Join(", ", servers)}" : string.Empty)
            + ". The tools found can be called right after. Looking does nothing by itself.";
        const string schema = """{"type":"object","properties":{"query":{"type":"string","description":"What the tool must do, in a few words"},"server":{"type":"string","description":"An MCP server's name: all of its tools"}}}""";
        return new ToolDefinition(FindToolsTool, description, schema);
    }

    /// <summary>
    /// <c>find_tools</c>: the tools that fit (by meaning, from those not at hand yet; or a whole server), put at hand for the
    /// next round, and said to the model with what each does.
    /// </summary>
    private async Task<string> FindToolsAsync(System.Text.Json.JsonElement arguments, IReadOnlyCollection<ITool> available, List<ITool> atHand, List<ToolDefinition> definitions, CancellationToken cancellationToken)
    {
        var query = arguments.GetString("query")?.Trim() ?? string.Empty;
        var server = arguments.GetString("server")?.Trim() ?? string.Empty;
        var pool = available.Where(t => !atHand.Contains(t)).ToList();
        List<ITool> found;
        if (server.Length > 0)
        {
            var group = "mcp:" + server;
            var ofServer = available.Where(t => string.Equals(tools.Registry.GroupOf(t.Name), group, StringComparison.OrdinalIgnoreCase)).ToList();
            found = query.Length > 0 && ofServer.Count > ServerShown
                ? [.. await chooser.SearchAsync(ofServer, query, ServerShown, cancellationToken).ConfigureAwait(false)]
                : [.. ofServer.Take(ServerShown)];
        }
        else found = [.. await chooser.SearchAsync(pool, query, Found, cancellationToken).ConfigureAwait(false)];
        foreach (var tool in found.Where(t => !atHand.Contains(t)))
        {
            atHand.Add(tool);
            definitions.Add(new ToolDefinition(tool.Name, tool.Description, tool.ParametersSchemaJson));
        }
        Log.Info($"Asistente: la IA busca herramientas ({(server.Length > 0 ? "servidor " + server + " " : string.Empty)}«{query}») → {(found.Count > 0 ? string.Join(", ", found.Select(t => t.Name)) : "nada")}");
        if (found.Count == 0)
            return server.Length > 0 ? $"There is no MCP server called «{server}». Look by what the tool must do (query)." : "No tool fits that. Say plainly what you cannot do.";
        return "Now at hand, call them directly:\n" + string.Join('\n', found.Select(t => $"- {t.Name}: {OneLine(t.Description, 160)}"));
    }

    private static string OneLine(string text, int most)
    {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length > most ? line[..most] + "…" : line;
    }

    /// <summary>A small MCP server's tools all go together: one used a moment before brings the rest.</summary>
    public const int SmallServer = 8;

    /// <summary>
    /// The tools used a moment before and, for one of a small MCP server, the others of that server, right after it: a
    /// game started with nueva_partida goes on with probar_letra («La.» is nothing like it).
    /// </summary>
    private IEnumerable<string> WithTheirServer(IReadOnlyCollection<string> recent)
    {
        var all = tools.Registry.All;
        foreach (var name in recent)
        {
            yield return name;
            if (tools.Registry.GroupOf(name) is not { } group || !group.StartsWith("mcp:", StringComparison.Ordinal)) continue;
            var siblings = all.Where(t => t.Name != name && tools.Registry.GroupOf(t.Name) == group).Select(t => t.Name).ToList();
            if (siblings.Count < SmallServer) foreach (var sibling in siblings) yield return sibling;
        }
    }

    private const string RunCommandTool = "run_command";

    private const string FindCommandTool = "find_command";

    /// <summary>Searches for the user's commands in one order, at most.</summary>
    public const int MaxCommandSearches = 2;

    private const string OpenWebTool = "open_web";

    private const string VoicePhraseTool = "add_voice_phrase";

    /// <summary>
    /// Whether the order teaches a way of saying something («cuando diga X…», <c>assistant.words.whenISay</c>, this language
    /// and English).
    /// </summary>
    public static bool TeachesPhrase(string said)
    {
        var folded = " " + string.Join(' ', TextSimilarity.Words(said)) + " ";
        return new[] { Loc.T("assistant.words.whenISay"), Loc.Strings(Loc.Fallback, "assistant.words.").GetValueOrDefault("assistant.words.whenISay") }
            .OfType<string>().Where(p => !p.StartsWith("assistant.", StringComparison.Ordinal))
            .SelectMany(p => p.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(p => " " + string.Join(' ', TextSimilarity.Words(p)) + " ")
            .Any(p => p.Trim().Length > 0 && folded.Contains(p, StringComparison.Ordinal));
    }

    private static readonly string[] MemoryTools = ["remember", "forget"];

    private static readonly string[] CommandToolNames = ["run_command", "find_command"];

    /// <summary>For an order for later: programming it, a plain reminder, and finding the user's commands to name them.</summary>
    private static readonly string[] LaterTools = ["schedule", "set_timer", "find_command"];

    private static string Short(string text) => text.Length > 160 ? text[..160] + "…" : text;

    private static readonly string[] NoteTools = ["save_note", "find_notes"];

    private static readonly string[] ScreenTools = ["look_at_screen", "read_web"];

    /// <summary>What it does with the files the user gives it (stage 14.1).</summary>
    private static readonly string[] GivenFileTools = ["read_file", "open_file", "move_file", "delete_file"];

    /// <summary>Next to the order, the files the user gave it, with their full paths for the tools (none: nothing).</summary>
    private static string GivenFilesLine(ToolContext context) => context.Files.Count == 0 ? string.Empty
        : "\n\n" + Loc.T("assistant.prompt.givenFiles", string.Join("; ", context.Files));

    /// <summary>Whether the order points at what is on the screen (<c>assistant.words.onScreen</c>, this language and English).</summary>
    public static bool AboutTheScreen(string said)
    {
        var folded = " " + string.Join(' ', TextSimilarity.Words(said)) + " ";
        return new[] { Loc.T("assistant.words.onScreen"), Loc.Strings(Loc.Fallback, "assistant.words.").GetValueOrDefault("assistant.words.onScreen") }
            .OfType<string>().SelectMany(p => p.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(p => " " + string.Join(' ', TextSimilarity.Words(p)) + " ")
            .Any(p => p.Trim().Length > 0 && folded.Contains(p, StringComparison.Ordinal));
    }

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

    /// <param name="onText">The answer so far, clean (writing long: its summary).</param>
    /// <param name="onLong">Writing long: the whole text so far, in Markdown.</param>
    /// <returns>Its text, its calls, and whether it did not fit: it reached its most tokens, or —not writing long— it went
    /// on past an answer's length in a round with tools.</returns>
    private static async Task<(string Text, List<ToolCall> Calls, bool Cut)> RoundAsync(IChatModel model, ChatRequest request, Action<string>? onText,
        Action<string>? onLong, bool writing, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var calls = new List<ToolCall>();
        var shown = string.Empty;
        var shownLong = string.Empty;
        var cut = false;
        await foreach (var chunk in model.StreamAsync(request, cancellationToken).ConfigureAwait(false))
        {
            if (chunk.ToolCalls is { Count: > 0 } asked) calls.AddRange(asked);
            cut |= chunk.Cut;
            if (chunk.Content.Length == 0) continue;
            text.Append(chunk.Content);
            // An answer to say, not a call (a call may come written as text, «<function=…>»): as long as it always was. Longer,
            // it did not fit (stage 14.2).
            if (!writing && request.MaxTokens > MaxTokens && calls.Count == 0 && text.Length > SpokenChars && !text.ToString().Contains('<', StringComparison.Ordinal))
            {
                cut = true;
                break;
            }
            // Text next to a tool call is not the answer yet.
            if (calls.Count > 0) continue;
            var raw = text.ToString();
            string clean;
            if (writing)
            {
                // The summary while it comes; no separator yet, the first sentences of what there is (it may never come).
                var (summary, body) = LongAnswers.Split(raw);
                clean = summary.Length > 0 ? ReplyText.Clean(summary) : LongAnswers.Spoken(ReplyText.Clean(LongAnswers.WithoutLabel(LongAnswers.Markdown(raw))));
                // The whole text once there is one: after the separator, or clearly without one.
                if (onLong is not null && body.Length > 0 && (raw.Length > LongAnswers.LongChars || LongAnswers.HasSeparator(raw)) && body != shownLong)
                {
                    shownLong = body;
                    onLong(body);
                }
            }
            else clean = ReplyText.Clean(raw);
            if (onText is not null && clean != shown)
            {
                shown = clean;
                onText(clean);
            }
        }
        return (text.ToString(), calls, cut);
    }
}

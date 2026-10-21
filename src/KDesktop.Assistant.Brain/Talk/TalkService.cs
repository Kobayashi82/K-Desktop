using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Brain.Talk;

/// <summary>Where what an assistant says goes (the host: K-Desktop's chat, its state, the character).</summary>
public interface ITalkOutput
{
    /// <summary>A line of the conversation for K-Desktop (<c>chat</c>).</summary>
    void Chat(ChatLineMessage line);

    /// <summary>What it is doing now: <c>thinking</c>, <c>speaking</c> or <c>idle</c> (its <c>status</c>, and the character shows it).</summary>
    void State(string? assistant, string state);

    /// <summary>The character says it (Communicate) for as long as it takes to read; done at once without a body.</summary>
    Task SpeakAsync(string? assistant, string text, CancellationToken cancellationToken);

    /// <summary>It asked something back to an order said aloud: K-Desktop listens a few seconds for the answer (<c>listen</c>).</summary>
    void Listen(string? assistant, int seconds)
    {
    }

    /// <summary>The answer of that turn so far, while it is written (at most 8 times a second): its voice may start (stage 5.4).</summary>
    void SpeakAhead(string? assistant, string turn, string textSoFar)
    {
    }

    /// <summary>Says the final answer of a turn (what was said ahead is not said again); over when its voice is (or its bubble, without one).</summary>
    Task SpeakTurnAsync(string? assistant, string turn, string text, CancellationToken cancellationToken) => SpeakAsync(assistant, text, cancellationToken);
}

/// <summary>
/// The way an order goes (stage 3.4 and 4.4, plan §4): every <c>heard</c> first tries the fixed rules (the time, a timer,
/// a note, «cancela», «repite», «deshaz»), answered at once with the assistant's phrases; then the user's K-Desktop
/// commands (<c>match</c>, like the voice): what clearly names commands runs in the order said, without the AI, and what
/// is left goes to it; two commands nearly as close are asked about («¿A o B?», answered without the AI: «el primero»
/// or the name); anything else goes to the AI, with the persona of the assistant it was said to, its short history and
/// the command tools. One conversation per assistant (and one for the module without a body); a new order replaces the
/// one still going. Thread-safe.
/// </summary>
public sealed class TalkService
{
    /// <summary>«Deshaz» undoes what was done this long ago at most.</summary>
    public static readonly TimeSpan UndoWithin = TimeSpan.FromMinutes(30);

    /// <summary>«¿A o B?» waits this long for its answer.</summary>
    public static readonly TimeSpan ChoiceWithin = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Two candidates are asked about when the best is this close, the second at least <see cref="Plausible"/>, and they
    /// are less than <see cref="Close"/> apart. With a hundred real commands, anything is 0.70–0.75 away from anything
    /// («minimiza la ventana actual» → «¿Bajar el volumen o Ventana de Incognito?»): only a near match is worth asking.
    /// </summary>
    public const double AskFrom = CommandTools.Direct;

    public const double Plausible = CommandTools.Clear;

    public const double Close = 0.08;

    private readonly Func<string?, (IChatModel? Client, string? Model)> _brain;
    private readonly ToolRunner _tools;
    private readonly Func<string?, AssistantPersona> _persona;
    private readonly ITalkOutput _output;
    private readonly TimeProvider _time;
    private readonly Random _random;
    private readonly TalkMind _mind;
    private readonly TalkMemory? _memory;
    private readonly ICommandBridge? _commands;
    private readonly Func<bool> _learn;
    private readonly Func<Task<bool>>? _recheck;
    private readonly Func<Task<bool>>? _freeBrain;
    private readonly Func<Task>? _loadBrain;
    private readonly ConcurrentDictionary<string, Conversation> _conversations = new(StringComparer.Ordinal);

    /// <param name="brain">The model client and model name for an assistant (null: no AI now).</param>
    /// <param name="persona">The persona of an assistant (null: the module without a body), in the user's language.</param>
    /// <param name="chooser">Which tools reach the AI (null: by the words of the order).</param>
    /// <param name="memory">What is remembered about the user (null: nothing).</param>
    /// <param name="learn">Whether it learns from what is talked about (Options → Memory).</param>
    /// <param name="commands">The user's K-Desktop commands (null: none, the AI answers everything).</param>
    /// <param name="recheck">Looks at the brain again when an order needs it and it is not ready; whether it is now.</param>
    /// <param name="freeBrain">«Descarga el cerebro»: the model leaves the graphics card; whether it was in it.</param>
    /// <param name="loadBrain">«Carga el cerebro»: the model loads now.</param>
    public TalkService(Func<string?, (IChatModel? Client, string? Model)> brain, ToolRunner tools, Func<string?, AssistantPersona> persona,
        ITalkOutput output, TimeProvider? time = null, Random? random = null, ToolChooser? chooser = null, TalkMemory? memory = null, Func<bool>? learn = null,
        ICommandBridge? commands = null, Func<Task<bool>>? recheck = null, Func<Task<bool>>? freeBrain = null, Func<Task>? loadBrain = null)
    {
        _commands = commands;
        _recheck = recheck;
        _freeBrain = freeBrain;
        _loadBrain = loadBrain;
        _brain = brain;
        _tools = tools;
        _persona = persona;
        _output = output;
        _time = time ?? TimeProvider.System;
        _random = random ?? Random.Shared;
        _memory = memory;
        _learn = learn ?? (() => true);
        _mind = new TalkMind(tools, chooser ?? new ToolChooser(null), _time, memory);
    }

    /// <summary>An order for an assistant. Completes when it has been answered (and said), or replaced by another.</summary>
    public async Task HandleAsync(HeardMessage heard)
    {
        var said = heard.Text.Trim();
        if (said.Length == 0) return;
        var who = heard.Assistant;
        var conversation = Of(who);
        var (token, wasBusy) = conversation.Begin();
        var turn = Guid.NewGuid().ToString("N")[..12];
        _output.Chat(new ChatLineMessage("user", said, turn, true, who));
        try
        {
            var persona = _persona(who);
            var answer = await AnswerAsync(conversation, persona, who, said, turn, wasBusy, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            _output.Chat(new ChatLineMessage("assistant", answer.Text, turn, true, who));
            if (answer.Remember) conversation.History.Add(said, answer.Text, answer.Tools, answer.Steps);
            Learn(who, said, answer, token);
            if (answer.Text.Length > 0) conversation.LastAnswer = answer.Text;
            _output.State(who, "speaking");
            await _output.SpeakTurnAsync(who, turn, answer.Text, token).ConfigureAwait(false);
            // Said aloud and answered with a question: the answer may come aloud too, without the wake word (stage 5.3).
            if (heard.Source == "voice" && !token.IsCancellationRequested && (answer.AsksBack || EndsAsking(answer.Text)))
                _output.Listen(who, FollowUpSeconds);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Replaced by another order, or cancelled.
        }
        catch (Exception ex)
        {
            Log.Error($"Asistente: no se ha podido atender «{said}»", ex);
        }
        finally
        {
            if (conversation.End(token)) _output.State(who, "idle");
        }
    }

    /// <summary>
    /// After an answer of the AI, the same model reads the exchange and remembers what is worth it, in the background (a
    /// new order cancels it). Not for short orders, questions (the answer may recite what it knows), failures, or when it
    /// already used «remember».
    /// </summary>
    private void Learn(string? who, string said, TalkAnswer answer, CancellationToken token)
    {
        if (_memory is null || !answer.FromAi || answer.Failed || !_learn() || answer.Tools.Contains("remember") || answer.Tools.Contains("forget")
            || said.Contains('?', StringComparison.Ordinal) || TalkMemory.IsAbout(said)) return;
        if (said.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < 3) return;
        var (client, model) = _brain(who);
        if (client is null || model is null) return;
        _ = Task.Run(() => _memory.LearnAsync(client, model, said, answer.Text, new ToolContext(who), token), CancellationToken.None);
    }

    /// <summary>
    /// «Asistente: decir» (stage 4.5): the assistant says this, in its bubble and its chat, without the AI. It replaces
    /// what it was saying; «repite» repeats it.
    /// </summary>
    public async Task SayAsync(string? who, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        var conversation = Of(who);
        var (token, _) = conversation.Begin();
        try
        {
            var turn = Guid.NewGuid().ToString("N")[..12];
            _output.Chat(new ChatLineMessage("assistant", text, turn, true, who));
            conversation.LastAnswer = text;
            _output.State(who, "speaking");
            await _output.SpeakTurnAsync(who, turn, text, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Replaced by another order.
        }
        finally
        {
            if (conversation.End(token)) _output.State(who, "idle");
        }
    }

    /// <summary>«Para», Escape: stops what that assistant is answering (nothing to say).</summary>
    public void Cancel(string? assistant)
    {
        if (_conversations.TryGetValue(assistant ?? string.Empty, out var conversation)) conversation.Cancel();
    }

    /// <summary>Whether that assistant is answering an order now (thinking or saying it).</summary>
    public bool IsAnswering(string? assistant) =>
        _conversations.TryGetValue(assistant ?? string.Empty, out var conversation) && conversation.Busy;

    /// <summary>A model asked to load less than this long ago is not asked again (it stays in memory a few minutes).</summary>
    public static readonly TimeSpan PrepareEvery = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _prepared = new(StringComparer.Ordinal);

    /// <summary>
    /// The user started talking to <paramref name="assistant"/> (stage 5.7): its model loads while the order is said, so
    /// an answer of the AI does not wait for it afterwards. Never an error; at most once every <see cref="PrepareEvery"/>
    /// per model.
    /// </summary>
    public void Prepare(string? assistant)
    {
        var (client, model) = _brain(assistant);
        if (client is null || string.IsNullOrEmpty(model)) return;
        var now = _time.GetUtcNow();
        if (_prepared.TryGetValue(model, out var last) && now - last < PrepareEvery) return;
        _prepared[model] = now;
        _ = Task.Run(async () =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await client.PreloadAsync(model, limit.Token).ConfigureAwait(false);
                Log.Info($"Asistente: modelo «{model}» listo mientras le hablan ({watch.ElapsedMilliseconds} ms)");
            }
            catch (Exception ex)
            {
                _prepared.TryRemove(model, out _);
                Log.Warn($"Asistente: no se ha podido preparar el modelo «{model}»: {ex.Message}");
            }
        });
    }

    /// <summary>Forgets what was talked with an assistant (it left the desktop).</summary>
    public void Forget(string? assistant)
    {
        if (!_conversations.TryRemove(assistant ?? string.Empty, out var conversation)) return;
        conversation.Cancel();
        conversation.Dispose();
    }

    private Conversation Of(string? assistant) => _conversations.GetOrAdd(assistant ?? string.Empty, _ => new Conversation(_time));

    private async Task<TalkAnswer> AnswerAsync(Conversation conversation, AssistantPersona persona, string? who, string said, string turn, bool wasBusy, CancellationToken token)
    {
        var context = new ToolContext(who);
        var rule = QuickRules.Match(said, [persona.Name]);
        // The answer to «¿A o B?»: «el primero», «la otra» or one of the names.
        var pending = conversation.TakeChoice(_time.GetUtcNow());
        if (pending is not null && ChoiceOf(pending, rule, said) is { } chosen)
            return await CommandsAsync(persona, [chosen], context, said, token).ConfigureAwait(false);
        switch (rule)
        {
            case { Kind: QuickRuleKind.Cancel }:
                return new TalkAnswer(Phrase(persona, wasBusy ? "cancelled" : "nothingToCancel"), Remember: false);
            case { Kind: QuickRuleKind.Repeat }:
                return new TalkAnswer(conversation.LastAnswer ?? Phrase(persona, "nothingToRepeat"), Remember: false);
            case { Kind: QuickRuleKind.Undo }:
                var undone = await _tools.UndoLastAsync(who, UndoWithin, token).ConfigureAwait(false);
                return new TalkAnswer(undone switch
                {
                    null => Phrase(persona, "nothingToUndo"),
                    { Ok: true } u => Phrase(persona, "undone", LowerFirst(u.Did)),
                    _ => Phrase(persona, "undoFailed"),
                });
            case { Kind: QuickRuleKind.Time }:
                return await ToolAsync(persona, "get_time", new JsonObject(), context, said, acknowledge: false, token).ConfigureAwait(false);
            case { Kind: QuickRuleKind.Timer, Seconds: { } seconds } timer:
                var arguments = new JsonObject { ["minutes"] = Math.Round(seconds / 60, 2) };
                if (timer.Text is { Length: > 0 } reason) arguments["reason"] = reason;
                return await ToolAsync(persona, "set_timer", arguments, context, said, acknowledge: true, token).ConfigureAwait(false);
            case { Kind: QuickRuleKind.Note, Text: { Length: > 0 } note }:
                return await ToolAsync(persona, "save_note", new JsonObject { ["note"] = note }, context, said, acknowledge: true, token).ConfigureAwait(false);
            case { Kind: QuickRuleKind.OpenWeb, Text: { } addresses }:
                return await SitesAsync(persona, QuickRules.Sites(addresses), context, said, token).ConfigureAwait(false);
            case { Kind: QuickRuleKind.Remember, Text: { Length: > 0 } fact } when _memory is not null:
                return await RememberAsync(persona, who, fact, context, said, token).ConfigureAwait(false);
            case { Kind: QuickRuleKind.Recall } when _memory is not null:
                return Recall(persona);
            case { Kind: QuickRuleKind.Forget, Text: { Length: > 0 } forget } when _memory is not null:
                return await ToolAsync(persona, "forget", new JsonObject { ["fact"] = forget }, context, said, acknowledge: true, token).ConfigureAwait(false);
            case { Kind: QuickRuleKind.FreeBrain } when _freeBrain is not null:
                // Talking to it loaded it again (stage 5.7): the next time it is talked to, it may load again.
                _prepared.Clear();
                return new TalkAnswer(Phrase(persona, await _freeBrain().ConfigureAwait(false) ? "brainFreed" : "brainNotLoaded"), Remember: false);
            case { Kind: QuickRuleKind.LoadBrain } when _loadBrain is not null:
                var (_, loading) = _brain(who);
                if (loading is null) return new TalkAnswer(Phrase(persona, "noBrain"), Remember: false, Failed: true);
                _output.State(who, "thinking");
                await _loadBrain().ConfigureAwait(false);
                return new TalkAnswer(Phrase(persona, "brainLoaded"), Remember: false);
            case { Kind: QuickRuleKind.CancelTimer } cancelTimer:
                var which = new JsonObject();
                if (cancelTimer.Text is { Length: > 0 } words) which["reason"] = words;
                return await ToolAsync(persona, "cancel_timer", which, context, said, acknowledge: true, token).ConfigureAwait(false);
        }

        // The user's commands: what clearly names them runs now, in order; two nearly as close are asked about. Not for a
        // question («¿qué hace el comando modo juego?» names it, and must not run it): the AI, with the command tools.
        TalkAnswer? ran = null;
        var forAi = said;
        IReadOnlyCollection<string>? leaveOut = null;
        IReadOnlyList<string>? close = null;
        // What is left of the order for the rules about programs and the AI: all of it, or what no command took.
        IReadOnlyList<string> leftover = [said];
        // A phrase the user taught it («cuando diga «la vuelta»…»): the AI, with that memory, before any command that sounds
        // like it («la vuelta» ran «Anterior» at 0.87 and the memory never counted).
        var taught = _memory?.Store.Triggered(said) ?? [];
        if (taught.Count > 0) Log.Info($"Asistente: «{said}» dice una frase de la memoria: a la IA ({taught[0].Text})");
        // A big model (Bonsai 27 B) is trusted with what a small one was kept from: it sees more of the user's commands,
        // always has the command tools, and long sentences that only name a command in passing are its to understand.
        var capable = IsCapable(_brain(who).Model);
        if (_commands is not null && taught.Count > 0 && capable && !IsQuestion(said) && await _commands.MatchAsync(said, token).ConfigureAwait(false) is { } hinted)
        {
            // The taught phrase goes to the AI, but it must know «Steam» is one of the user's commands (it opened the web).
            close = CloseCommands(hinted, [], capable);
            Log.Info("Asistente: comandos para la IA: " + string.Join(", ", close));
        }
        if (_commands is not null && taught.Count == 0 && !IsQuestion(said) && await _commands.MatchAsync(said, token).ConfigureAwait(false) is { } matched)
        {
            Log.Info("Asistente: comandos: " + CommandTools.Describe(matched));
            // At once only what is sure and said shortly («vacía la papelera»); with a big model, «repite el comando anterior
            // completo» is not the command «Anterior» (0.80) but a sentence for it.
            bool Direct(MatchedPart part) => part.Sure && part.Candidates[0].Score >= CommandTools.Direct && (!capable || IsShort(part.Text));
            // The same command named twice in one order («la vuelta, es la vuelta») runs once.
            var sure = matched.Parts.Where(Direct).Select(p => p.Candidates[0])
                .DistinctBy(c => (c.Command ?? c.Name, c.Media, c.Value)).ToList();
            // A big model: all of the order at once, or all of it for the AI. Run in pieces, «…la papelera, esa ventana
            // muévela a la derecha y la otra de equipo al otro monitor» opened «Equipo» and left the rest without it.
            if (capable && (matched.Unmatched.Count > 0 || !matched.Parts.All(Direct))) sure = [];
            // Not sure of any part, but the whole order is that near one command and well ahead of the rest («Va ser la
            // papelera», Parakeet's «vacía la papelera», 0.82): it is that one. K-Desktop still asks before what is risky.
            if (sure.Count == 0 && matched.Parts is [{ Sure: false, Candidates: [var only, ..] } whole] && only.Score >= CommandTools.Direct
                && (whole.Candidates.Count < 2 || only.Score - whole.Candidates[1].Score >= CommandTools.Ahead) && (!capable || IsShort(said)))
                return await CommandsAsync(persona, [only], context, said, token).ConfigureAwait(false);
            // A part K-Desktop is not sure of, or not enough for running it at once, is left to the rules and the AI too
            // («abre Steam y minimízalo»: «minimiza steam» is no command).
            var unmatched = matched.Unmatched.Concat(matched.Parts.Where(p => !Direct(p)).Select(p => p.Text)).ToList();
            if (sure.Count > 0)
            {
                ran = await CommandsAsync(persona, sure, context, said, token).ConfigureAwait(false);
                if (unmatched.Count == 0) return ran;
                forAi = string.Join(", ", unmatched);
                leftover = unmatched;
            }
            else if (matched.Parts is [{ Candidates: [var first, var second, ..] }] && first.Score >= AskFrom && second.Score >= Plausible
                     && first.Score - second.Score < Close)
            {
                conversation.AskChoice([first, second], _time.GetUtcNow());
                return new TalkAnswer(Phrase(persona, "which", first.Name, second.Name), Remember: false) { AsksBack = true };
            }
            // The commands that may be it go to the AI by name; none: a small AI is not tempted to look for one (a big one
            // keeps run_command: «ejecuta el comando de abrir Steam» at 0.69 left it unable to).
            close = CloseCommands(matched, sure, capable);
            if (sure.Count == 0 && close.Count == 0 && !capable) leaveOut = ["run_command"];
        }

        // Programs (stage 8.1): «minimiza Steam», «sal de Chrome», «abre Spotify», in what the user's own commands left.
        if (taught.Count == 0 && !IsQuestion(said))
        {
            var acts = new List<(string Piece, QuickRule Rule)>();
            // A big model understands a long sentence better than a rule does.
            foreach (var piece in leftover.Where(p => !capable || IsShort(p)))
                if ((ReferenceEquals(piece, said) ? rule : QuickRules.Match(piece, [persona.Name])) is { Kind: QuickRuleKind.Act or QuickRuleKind.OpenApp } found
                    && (found.Kind == QuickRuleKind.Act || await IsInstalledAsync(found.Text).ConfigureAwait(false)))
                    acts.Add((piece, found));
            var handled = new List<string>();
            foreach (var (piece, found) in acts)
            {
                var done = await ProgramsAsync(persona, [found], context, said, first: ran is null, token).ConfigureAwait(false);
                // Not done («no veo ninguna ventana de…») and a big model at hand: the AI looks at it (the rule may have
                // taken the wrong words for the program).
                if (done.Failed && capable && _brain(who).Client is not null) continue;
                ran = ran is null ? done : Join(ran, done);
                handled.Add(piece);
            }
            if (handled.Count > 0)
            {
                leftover = [.. leftover.Where(p => !handled.Any(h => ReferenceEquals(h, p)))];
                if (leftover.Count == 0) return ran!;
                forAi = string.Join(", ", leftover);
            }
        }

        var (client, model) = _brain(who);
        // Not ready: the server may have opened since it was last looked at (Ollama, after K-Desktop at start-up).
        if ((client is null || model is null) && _recheck is not null && await _recheck().ConfigureAwait(false)) (client, model) = _brain(who);
        if (client is null || model is null) return NotDone(persona, ran, forAi, Phrase(persona, "noBrain"));
        _output.State(who, "thinking");
        var shown = new PartialText(text =>
        {
            var sofar = (ran is null ? "" : ran.Text + " ") + text;
            _output.Chat(new ChatLineMessage("assistant", sofar, turn, false, who));
            _output.SpeakAhead(who, turn, sofar);
        }, _time);
        var answer = await _mind.AnswerAsync(client, model, persona, conversation.History.Messages(), conversation.History.LastTools(), forAi, context, shown.Show, token,
            leaveOut, close, capable).ConfigureAwait(false);
        answer = answer with { Text = answer.Text.Length > 0 ? answer.Text : Phrase(persona, "ok"), FromAi = true };
        // One «Recibido.», at the start («Recibido. He ejecutado «Equipo». Afirmativo. He pasado…» had two).
        return ran is null ? answer : Join(ran, answer with { Text = answer.WithoutAck, Ack = null }) with { FromAi = true, Failed = answer.Failed };
    }

    /// <summary>Two answers of one order, one after the other.</summary>
    private static TalkAnswer Join(TalkAnswer first, TalkAnswer then) => first with
    {
        Text = (first.Text + " " + then.Text).Trim(),
        Failed = first.Failed || then.Failed,
        Tools = [.. first.Tools, .. then.Tools.Where(t => !first.Tools.Contains(t))],
        Steps = [.. first.Steps, .. then.Steps],
    };

    /// <summary>Commands of the user named to the AI for an order, at most (a big model, more and from further).</summary>
    private const int AssistantCommandsShown = 4;

    private const int CapableCommandsShown = 6;

    /// <summary>How near a command must be to be named to a big model (to a small one, <see cref="Plausible"/>).</summary>
    public const double CapableNear = 0.55;

    /// <summary>Models this big (in billions) are trusted with the long sentences and with all the command tools.</summary>
    public const double CapableBillions = 14;

    /// <summary>Whether the model is big enough to be trusted (by its size in the name: «…-27B-…», «qwen3:32b»).</summary>
    public static bool IsCapable(string? model) =>
        model is { Length: > 0 } && ModelPicker.SizeOf(new ModelInfo(Path.GetFileName(model), 0, 0, [])) >= CapableBillions;

    /// <summary>At most this many words, an order (or part of one) is short enough to run a command it names without the AI.</summary>
    public const int ShortOrderWords = 6;

    public static bool IsShort(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= ShortOrderWords;

    /// <summary>The user's commands that may be what was asked, by name, for the AI (not those already run).</summary>
    internal static List<string> CloseCommands(MatchedMessage matched, IReadOnlyCollection<MatchedCandidate> ran, bool capable) =>
        [.. matched.Parts.SelectMany(p => p.Candidates).Where(c => c.Score >= (capable ? CapableNear : CommandTools.Plausible) && !ran.Contains(c))
            .OrderByDescending(c => c.Score).Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Take(capable ? CapableCommandsShown : AssistantCommandsShown)];

    /// <summary>A tool the routing ran itself, as if the model had called it (for the history).</summary>
    private static IEnumerable<ChatMessage> Step(string tool, JsonObject arguments, string result)
    {
        var id = "call_" + Guid.NewGuid().ToString("N")[..8];
        return [ChatMessage.Assistant(string.Empty, [new ToolCall(tool, JsonSerializer.SerializeToElement(arguments), id)]), ChatMessage.ToolResult(tool, result, id)];
    }

    /// <summary>
    /// Runs commands (or built-in media orders) in order, each through the «run_command» tool (so they are in the
    /// activity; K-Desktop asks before what is risky), and says how they went: «Hecho. He ejecutado «X».», what was
    /// declined, or what failed.
    /// </summary>
    private async Task<TalkAnswer> CommandsAsync(AssistantPersona persona, IReadOnlyList<MatchedCandidate> commands, ToolContext context, string said, CancellationToken token)
    {
        var replies = new List<string>();
        var steps = new List<ChatMessage>();
        var failed = false;
        foreach (var command in commands)
        {
            var arguments = new JsonObject { ["command"] = command.Name };
            if (command.Command is { } id) arguments["id"] = id;
            if (command.Media is { } media) arguments["media"] = media;
            if (command.Value is { } value) arguments["value"] = value;
            var outcome = await _tools.RunAsync("run_command", JsonSerializer.SerializeToElement(arguments), context, said, "kdesktop", token).ConfigureAwait(false);
            // The model only gives the name (the id and the media order are the routing's).
            steps.AddRange(Step("run_command", new JsonObject { ["command"] = command.Name }, outcome.Result?.Text ?? "Not done: " + outcome.Entry.Did));
            if (outcome.Result is { Failed: false } done) replies.Add(done.Reply ?? command.Name);
            else
            {
                failed = true;
                replies.Add(outcome.Result?.Reply ?? Phrase(persona, "error"));
            }
        }
        var text = failed ? string.Join(" ", replies) : Phrase(persona, "ok") + " " + string.Join(" ", replies);
        return new TalkAnswer(text, Failed: failed) { Tools = ["run_command"], Steps = steps };
    }

    /// <summary>Whether a program by that name is installed (the rule «abre {app}» is only for programs).</summary>
    private async Task<bool> IsInstalledAsync(string? name) =>
        name is { Length: > 0 } && _tools.Registry.Find("open_app") is OpenAppTool open && await open.FindAsync(name).ConfigureAwait(false) is not null;

    /// <summary>
    /// What the rules about programs understood, in order, each through its tool (in the activity; K-Desktop asks before
    /// closing), with each one's words for it. <paramref name="first"/>: nothing was said before (the «ok» goes first).
    /// </summary>
    private async Task<TalkAnswer> ProgramsAsync(AssistantPersona persona, IReadOnlyList<QuickRule> rules, ToolContext context, string said, bool first, CancellationToken token)
    {
        var replies = new List<string>();
        var steps = new List<ChatMessage>();
        var tools = new List<string>();
        var failed = false;
        foreach (var rule in rules)
        {
            var (tool, arguments) = rule.Kind == QuickRuleKind.OpenApp
                ? ("open_app", new JsonObject { ["name"] = rule.Text })
                : ("control_app", new JsonObject { ["action"] = rule.Op, ["app"] = rule.Text ?? string.Empty });
            var outcome = await _tools.RunAsync(tool, JsonSerializer.SerializeToElement(arguments), context, said, "rule", token).ConfigureAwait(false);
            steps.AddRange(Step(tool, arguments, outcome.Result?.Text ?? "Not done: " + outcome.Entry.Did));
            if (!tools.Contains(tool)) tools.Add(tool);
            if (outcome.Result is { Failed: false } done) replies.Add(done.Reply ?? done.Done ?? outcome.Entry.Did);
            else
            {
                failed = true;
                replies.Add(outcome.Result?.Reply ?? Phrase(persona, "error"));
            }
        }
        var text = string.Join(" ", replies);
        return new TalkAnswer(first && !failed ? Phrase(persona, "ok") + " " + text : text, Failed: failed) { Tools = tools, Steps = steps };
    }

    /// <summary>
    /// «Abre youtube.com y github.com»: each address, in order, without the AI (which may judge the site and refuse it).
    /// </summary>
    private async Task<TalkAnswer> SitesAsync(AssistantPersona persona, IReadOnlyList<string> sites, ToolContext context, string said, CancellationToken token)
    {
        var steps = new List<ChatMessage>();
        var failed = false;
        foreach (var site in sites)
        {
            var arguments = new JsonObject { ["url"] = site };
            var outcome = await _tools.RunAsync("open_web", JsonSerializer.SerializeToElement(arguments), context, said, "rule", token).ConfigureAwait(false);
            steps.AddRange(Step("open_web", arguments, outcome.Result?.Text ?? "Not done: " + outcome.Entry.Did));
            failed |= outcome.Result is not { Failed: false };
        }
        return new TalkAnswer(Phrase(persona, failed ? "error" : "ok"), Failed: failed) { Tools = ["open_web"], Steps = steps };
    }

    /// <summary>
    /// No AI for what is left of an order: what ran is said and what was not done is named, never dropped in silence
    /// («Lo de «cierra el navegador» no lo he hecho.»). Nothing ran: <paramref name="why"/>.
    /// </summary>
    private TalkAnswer NotDone(AssistantPersona persona, TalkAnswer? ran, string left, string why) =>
        ran is null ? new TalkAnswer(why, Remember: false, Failed: true) : ran with { Text = ran.Text + " " + Phrase(persona, "notDone", left) };

    /// <summary>A question, not an order (it may name a command without asking to run it).</summary>
    public static bool IsQuestion(string said) => said.Contains('?', StringComparison.Ordinal);

    /// <summary>Seconds K-Desktop waits for the answer to a question asked back.</summary>
    public const int FollowUpSeconds = 6;

    /// <summary>An answer that ends asking something («¿Quieres que lo apunte?»).</summary>
    public static bool EndsAsking(string text) => text.TrimEnd().EndsWith('?');

    /// <summary>Which of the two asked about the answer names, or null (then it is a new order).</summary>
    private static MatchedCandidate? ChoiceOf(IReadOnlyList<MatchedCandidate> pending, QuickRule? rule, string said)
    {
        if (rule is { Kind: QuickRuleKind.First }) return pending[0];
        if (rule is { Kind: QuickRuleKind.Second } && pending.Count > 1) return pending[1];
        var words = TextSimilarity.Words(said);
        return pending
            .Select(c => (Candidate: c, Shared: TextSimilarity.Words(c.Name).Count(w => w.Length > 2 && words.Any(s => TextSimilarity.WordSimilarity(w, s) >= 0.85))))
            .Where(p => p.Shared > 0)
            .OrderByDescending(p => p.Shared)
            .Select(p => (MatchedCandidate?)p.Candidate)
            .FirstOrDefault();
    }

    /// <summary>Memories told when asked «¿qué sabes de mí?», at most (the rest are in Options → Memory).</summary>
    public const int RecallTold = 5;

    /// <summary>«Recuerda que…»: kept (reworded by the model when there is one), and said back as understood.</summary>
    private async Task<TalkAnswer> RememberAsync(AssistantPersona persona, string? who, string fact, ToolContext context, string said, CancellationToken token)
    {
        var (client, model) = _brain(who);
        if (client is not null && model is not null) _output.State(who, "thinking");
        var (facts, anyNew) = await _memory!.RememberSaidAsync(client, model, fact, context, said, token).ConfigureAwait(false);
        if (facts.Count == 0) return new TalkAnswer(Phrase(persona, "error"), Failed: true);
        var reply = anyNew ? Phrase(persona, "ok") + " " + Loc.T("tool.remember.replyFacts", string.Join(". ", facts))
            : Loc.T("tool.remember.replyKnown");
        return new TalkAnswer(reply) { Tools = ["remember"] };
    }

    /// <summary>«¿Qué sabes de mí?»: the most valuable memories, without the AI (it saw only a few and made the rest up).</summary>
    private TalkAnswer Recall(AssistantPersona persona)
    {
        var store = _memory!.Store;
        var best = store.Best(RecallTold);
        if (best.Count == 0) return new TalkAnswer(Phrase(persona, "memoryEmpty"));
        var listed = string.Join(". ", best.Select(m => m.Text.TrimEnd('.')));
        var more = store.Count - best.Count;
        return new TalkAnswer(more > 0 ? Phrase(persona, "memoryListMore", listed, more) : Phrase(persona, "memoryList", listed));
    }

    /// <summary>A tool for a rule: what it says it did (after «Recibido.» for an action), or why not.</summary>
    private async Task<TalkAnswer> ToolAsync(AssistantPersona persona, string tool, JsonObject arguments, ToolContext context, string said, bool acknowledge, CancellationToken token)
    {
        var outcome = await _tools.RunAsync(tool, JsonSerializer.SerializeToElement(arguments), context, said, "rule", token).ConfigureAwait(false);
        var steps = Step(tool, arguments, outcome.Result?.Text ?? "Not done: " + outcome.Entry.Did).ToList();
        if (outcome.Result is not { } result) return new TalkAnswer(outcome.Entry.Did, Remember: true, Failed: true) { Steps = steps };
        if (result.Failed) return new TalkAnswer(result.Reply ?? Phrase(persona, "error"), Remember: true, Failed: true) { Steps = steps };
        var reply = result.Reply ?? Phrase(persona, "ok");
        return new TalkAnswer(acknowledge && result.Reply is not null ? Phrase(persona, "ok") + " " + reply : reply) { Tools = [tool], Steps = steps };
    }

    private string Phrase(AssistantPersona persona, string key, params object?[] args) => persona.Phrase(key, _random, args);

    private static string LowerFirst(string text) => text.Length > 1 && char.IsUpper(text[0]) && !char.IsUpper(text[1])
        ? char.ToLower(text[0], Loc.Instance.Culture) + text[1..]
        : text;

    /// <summary>One assistant's conversation: its history, its last answer and the order going on now.</summary>
    private sealed class Conversation(TimeProvider time) : IDisposable
    {
        private readonly object _gate = new();
        private CancellationTokenSource? _current;

        public TalkHistory History { get; } = new(time);

        public string? LastAnswer { get; set; }

        /// <summary>An order is going on.</summary>
        public bool Busy
        {
            get
            {
                lock (_gate) return _current is not null;
            }
        }

        private (IReadOnlyList<MatchedCandidate> Candidates, DateTimeOffset At)? _choice;

        /// <summary>«¿A o B?» was asked: the next order may answer it.</summary>
        public void AskChoice(IReadOnlyList<MatchedCandidate> candidates, DateTimeOffset now)
        {
            lock (_gate) _choice = (candidates, now);
        }

        /// <summary>The question waiting for an answer (still fresh), which the next order takes away either way.</summary>
        public IReadOnlyList<MatchedCandidate>? TakeChoice(DateTimeOffset now)
        {
            lock (_gate)
            {
                var choice = _choice;
                _choice = null;
                return choice is { } c && now - c.At <= ChoiceWithin ? c.Candidates : null;
            }
        }

        /// <summary>A new order: the one going on is cancelled. Whether there was one.</summary>
        public (CancellationToken Token, bool WasBusy) Begin()
        {
            lock (_gate)
            {
                var previous = _current;
                _current = new CancellationTokenSource();
                previous?.Cancel();
                return (_current.Token, previous is not null);
            }
        }

        /// <summary>That order is over; true when it was still the current one.</summary>
        public bool End(CancellationToken token)
        {
            lock (_gate)
            {
                if (_current is null || _current.Token != token) return false;
                _current.Dispose();
                _current = null;
                return true;
            }
        }

        public void Cancel()
        {
            lock (_gate) _current?.Cancel();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _current?.Dispose();
                _current = null;
            }
        }
    }

    /// <summary>The answer so far, at most 8 times per second (the last one always comes as the final line).</summary>
    private sealed class PartialText(Action<string> send, TimeProvider time)
    {
        private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(125);
        private long _last = long.MinValue;

        public void Show(string text)
        {
            var now = time.GetTimestamp();
            if (_last != long.MinValue && time.GetElapsedTime(_last, now) < Every) return;
            _last = now;
            send(text);
        }
    }
}

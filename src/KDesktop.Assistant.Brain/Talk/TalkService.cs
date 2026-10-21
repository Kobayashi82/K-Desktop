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

    /// <summary>Says the final answer of a turn (what was said ahead is not said again).</summary>
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

    /// <summary>Two candidates are asked about when both are this close and less than <see cref="Close"/> apart.</summary>
    public const double Plausible = 0.70;

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
    private readonly ConcurrentDictionary<string, Conversation> _conversations = new(StringComparer.Ordinal);

    /// <param name="brain">The model client and model name for an assistant (null: no AI now).</param>
    /// <param name="persona">The persona of an assistant (null: the module without a body), in the user's language.</param>
    /// <param name="chooser">Which tools reach the AI (null: by the words of the order).</param>
    /// <param name="memory">What is remembered about the user (null: nothing).</param>
    /// <param name="learn">Whether it learns from what is talked about (Options → Memory).</param>
    /// <param name="commands">The user's K-Desktop commands (null: none, the AI answers everything).</param>
    public TalkService(Func<string?, (IChatModel? Client, string? Model)> brain, ToolRunner tools, Func<string?, AssistantPersona> persona,
        ITalkOutput output, TimeProvider? time = null, Random? random = null, ToolChooser? chooser = null, TalkMemory? memory = null, Func<bool>? learn = null,
        ICommandBridge? commands = null)
    {
        _commands = commands;
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
            if (answer.Remember) conversation.History.Add(said, answer.Text, answer.Tools);
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
        if (_memory is null || !answer.FromAi || answer.Failed || !_learn() || answer.Tools.Contains("remember") || said.Contains('?', StringComparison.Ordinal)) return;
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
        if (_commands is not null && !IsQuestion(said) && await _commands.MatchAsync(said, token).ConfigureAwait(false) is { } matched)
        {
            Log.Info("Asistente: comandos: " + CommandTools.Describe(matched));
            var sure = matched.Parts.Where(p => p.Sure && p.Candidates[0].Score >= CommandTools.Direct).Select(p => p.Candidates[0]).ToList();
            // A part K-Desktop is sure of but not enough for running it at once is left to the AI too.
            var unmatched = matched.Unmatched.Concat(matched.Parts.Where(p => p.Sure && p.Candidates[0].Score < CommandTools.Direct).Select(p => p.Text)).ToList();
            if (sure.Count > 0)
            {
                ran = await CommandsAsync(persona, sure, context, said, token).ConfigureAwait(false);
                if (unmatched.Count == 0) return ran;
                forAi = string.Join(", ", unmatched);
            }
            else if (matched.Parts is [{ Candidates: [var first, var second, ..] }] && first.Score >= Plausible && second.Score >= Plausible
                     && first.Score - second.Score < Close)
            {
                conversation.AskChoice([first, second], _time.GetUtcNow());
                return new TalkAnswer(Phrase(persona, "which", first.Name, second.Name), Remember: false) { AsksBack = true };
            }
            // No command of the user is anything like it: the AI is not tempted to look for one.
            if (sure.Count == 0 && !matched.Parts.SelectMany(p => p.Candidates).Any(c => c.Score >= CommandTools.Plausible)) leaveOut = ["run_command"];
        }

        var (client, model) = _brain(who);
        if (client is null || model is null) return ran ?? new TalkAnswer(Phrase(persona, "noBrain"), Remember: false, Failed: true);
        _output.State(who, "thinking");
        var shown = new PartialText(text =>
        {
            var sofar = (ran is null ? "" : ran.Text + " ") + text;
            _output.Chat(new ChatLineMessage("assistant", sofar, turn, false, who));
            _output.SpeakAhead(who, turn, sofar);
        }, _time);
        var answer = await _mind.AnswerAsync(client, model, persona, conversation.History.Messages(), conversation.History.LastTools(), forAi, context, shown.Show, token, leaveOut).ConfigureAwait(false);
        answer = answer with { Text = answer.Text.Length > 0 ? answer.Text : Phrase(persona, "ok"), FromAi = true };
        return ran is null ? answer : answer with { Text = ran.Text + " " + answer.Text, Tools = [.. ran.Tools, .. answer.Tools] };
    }

    /// <summary>
    /// Runs commands (or built-in media orders) in order, each through the «run_command» tool (so they are in the
    /// activity; K-Desktop asks before what is risky), and says how they went: «Hecho. He ejecutado «X».», what was
    /// declined, or what failed.
    /// </summary>
    private async Task<TalkAnswer> CommandsAsync(AssistantPersona persona, IReadOnlyList<MatchedCandidate> commands, ToolContext context, string said, CancellationToken token)
    {
        var replies = new List<string>();
        var failed = false;
        foreach (var command in commands)
        {
            var arguments = new JsonObject { ["command"] = command.Name };
            if (command.Command is { } id) arguments["id"] = id;
            if (command.Media is { } media) arguments["media"] = media;
            if (command.Value is { } value) arguments["value"] = value;
            var outcome = await _tools.RunAsync("run_command", JsonSerializer.SerializeToElement(arguments), context, said, "kdesktop", token).ConfigureAwait(false);
            if (outcome.Result is { Failed: false } done) replies.Add(done.Reply ?? command.Name);
            else
            {
                failed = true;
                replies.Add(outcome.Result?.Reply ?? Phrase(persona, "error"));
            }
        }
        var text = failed ? string.Join(" ", replies) : Phrase(persona, "ok") + " " + string.Join(" ", replies);
        return new TalkAnswer(text, Failed: failed) { Tools = ["run_command"] };
    }

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

    /// <summary>A tool for a rule: what it says it did (after «Recibido.» for an action), or why not.</summary>
    private async Task<TalkAnswer> ToolAsync(AssistantPersona persona, string tool, JsonObject arguments, ToolContext context, string said, bool acknowledge, CancellationToken token)
    {
        var outcome = await _tools.RunAsync(tool, JsonSerializer.SerializeToElement(arguments), context, said, "rule", token).ConfigureAwait(false);
        if (outcome.Result is not { } result) return new TalkAnswer(outcome.Entry.Did, Remember: true, Failed: true);
        if (result.Failed) return new TalkAnswer(result.Reply ?? Phrase(persona, "error"), Remember: true, Failed: true);
        var reply = result.Reply ?? Phrase(persona, "ok");
        return new TalkAnswer(acknowledge && result.Reply is not null ? Phrase(persona, "ok") + " " + reply : reply) { Tools = [tool] };
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

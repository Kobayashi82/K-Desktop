using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Assistant.Tools.Declarative;
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

    /// <summary>
    /// What it is doing now: <c>thinking</c>, <c>thinkingHard</c>, <c>noBrain</c> (right before it says it has none),
    /// <c>speaking</c> or <c>idle</c> (its <c>status</c>, and the character shows it).
    /// </summary>
    void State(string? assistant, string state);

    /// <summary>The character says it (Communicate) for as long as it takes to read; done at once without a body.</summary>
    Task SpeakAsync(string? assistant, string text, CancellationToken cancellationToken);

    /// <summary>It asked something back to an order said aloud: K-Desktop listens a few seconds for the answer (<c>listen</c>).</summary>
    void Listen(string? assistant, int seconds)
    {
    }

    /// <summary>«Escribe lo que te diga»: K-Desktop writes what is said next where the cursor is (<c>dictate</c>, stage 10.5).</summary>
    void Dictate(string? assistant)
    {
    }

    /// <summary>The answer of that turn so far, while it is written (at most 8 times a second): its voice may start (stage 5.4).</summary>
    void SpeakAhead(string? assistant, string turn, string textSoFar)
    {
    }

    /// <summary>
    /// A few words said before the answer of that turn, apart from its text («Un momento.»): thinking takes a while and the
    /// user must know it heard. Not part of the answer (nor of the chat).
    /// </summary>
    void Preface(string? assistant, string turn, string phrase)
    {
    }

    /// <summary>Says the final answer of a turn (what was said ahead is not said again); over when its voice is (or its bubble, without one).</summary>
    Task SpeakTurnAsync(string? assistant, string turn, string text, CancellationToken cancellationToken) => SpeakAsync(assistant, text, cancellationToken);

    /// <summary>Reads a long text aloud (stage 14.2, «Léemela»): only the voice, no bubble; over when it has been said.</summary>
    Task ReadAloudAsync(string? assistant, string turn, string text, CancellationToken cancellationToken) => SpeakTurnAsync(assistant, turn, text, cancellationToken);
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
    private readonly Func<string?, Thinker?>? _thinker;
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
    /// <param name="thinker">How an assistant thinks harder (stage 12; null: it never does), on the same client.</param>
    public TalkService(Func<string?, (IChatModel? Client, string? Model)> brain, ToolRunner tools, Func<string?, AssistantPersona> persona,
        ITalkOutput output, TimeProvider? time = null, Random? random = null, ToolChooser? chooser = null, TalkMemory? memory = null, Func<bool>? learn = null,
        ICommandBridge? commands = null, Func<Task<bool>>? recheck = null, Func<Task<bool>>? freeBrain = null, Func<Task>? loadBrain = null,
        Func<string?, Thinker?>? thinker = null)
    {
        _thinker = thinker;
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
        var fresh = heard.Files is { Count: > 0 };
        if (fresh) conversation.Give(heard.Files!, _time.GetUtcNow());
        Active();
        var turn = Guid.NewGuid().ToString("N")[..12];
        // The conversation is kept only in K-Desktop's memory: the log has each line, to look at it afterwards.
        Log.Info($"Asistente: a «{who ?? "K-Desktop"}» por {heard.Source}: «{said}»");
        _output.Chat(new ChatLineMessage("user", said, turn, true, who));
        try
        {
            var persona = _persona(who);
            var answer = await AnswerAsync(conversation, persona, who, said, turn, wasBusy, token, fresh, heard.Act).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (await LearnPhraseAsync(conversation, who, said, answer, token).ConfigureAwait(false) is { } learned)
                answer = answer with { Text = (answer.Text + " " + learned).Trim() };
            conversation.Missed = Acted(answer) ? null : (said, _time.GetUtcNow());
            Log.Info($"Asistente: «{who ?? "K-Desktop"}» contesta: «{answer.Text}»");
            _output.Chat(new ChatLineMessage("assistant", answer.Text, turn, true, who, answer.Written));
            if (answer.Remember) conversation.History.Add(said, LongAnswers.ForHistory(answer.Text, answer.Written), answer.Tools, answer.Steps);
            Learn(who, said, answer, token);
            if (answer.Text.Length > 0) conversation.LastAnswer = answer.Text;
            // Only an answer of the AI may be called wrong and thought over («piénsalo bien» alone still has its order).
            conversation.LastAiAt = answer.FromAi ? _time.GetUtcNow() : null;
            if (LastCommandOf(answer.Steps) is { } command) conversation.LastCommand = command;
            _output.State(who, "speaking");
            await _output.SpeakTurnAsync(who, turn, answer.Text, token).ConfigureAwait(false);
            // Said aloud and answered with a question: the answer may come aloud too, without the wake word (stage 5.3).
            if (heard.Source == "voice" && !token.IsCancellationRequested && (answer.AsksBack || EndsAsking(answer.Text)))
                _output.Listen(who, FollowUpSeconds);
            // Its voice is over: what is said now is not taken for the text.
            if (answer.Dictates && !token.IsCancellationRequested) _output.Dictate(who);
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
            Active();
            if (conversation.End(token)) _output.State(who, "idle");
            FreeBrainIfAsked();
        }
    }

    /// <summary>The brain is freed once nothing is being answered (free_brain asked for it in the middle of an answer).</summary>
    private int _freeAfter;

    /// <summary>
    /// free_brain (or a part of an order): the model leaves the graphics card as soon as no assistant is answering — at once
    /// when none is (the shared tools). False: there is no brain to free.
    /// </summary>
    public bool FreeBrainAfter(string? assistant)
    {
        if (_freeBrain is null) return false;
        Interlocked.Exchange(ref _freeAfter, 1);
        Log.Info($"Asistente: «{assistant ?? "K-Desktop"}» libera el cerebro en cuanto acabe de contestar");
        FreeBrainIfAsked();
        return true;
    }

    private void FreeBrainIfAsked()
    {
        if (IsAnyAnswering || Interlocked.Exchange(ref _freeAfter, 0) == 0) return;
        _ = Task.Run(async () =>
        {
            try { await FreeBrainNowAsync().ConfigureAwait(false); }
            catch (Exception ex) { Log.Warn($"Asistente: no se ha podido liberar el cerebro: {ex.Message}"); }
        });
    }

    /// <summary>The model leaves the graphics card now; whether it was in it.</summary>
    private async Task<bool> FreeBrainNowAsync()
    {
        // Talking to it loaded it again (stage 5.7): the next time it is talked to, it may load again.
        _prepared.Clear();
        return await _freeBrain!().ConfigureAwait(false);
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

    /// <summary>An order that did nothing is taken back by its correction this long after, at most.</summary>
    public static readonly TimeSpan CorrectionWithin = TimeSpan.FromMinutes(3);

    /// <summary>Words of a short order that did nothing which may become a voice phrase, at most.</summary>
    public const int TaughtWords = 3;

    /// <summary>Whether an answer did something (not only looked things up), well.</summary>
    private bool Acted(TalkAnswer answer) =>
        !answer.Failed && answer.Tools.Any(t => _tools.Registry.FindAny(t) is not { ReadsOnly: true });

    /// <summary>
    /// «Abre Kamovis» did nothing, and the next order, a correction («es un programa, se llama K-Movies»,
    /// <c>assistant.words.correction</c>), ran a command it names: what was not understood becomes a voice phrase of that
    /// command (add_voice_phrase, so it is in the activity and «deshaz» takes it out), and it says so. Speech recognition
    /// writes an English name its own way: next time it runs at once. Null: nothing learned.
    /// </summary>
    private async Task<string?> LearnPhraseAsync(Conversation conversation, string? who, string said, TalkAnswer answer, CancellationToken token)
    {
        if (conversation.Missed is not { } missed || _time.GetUtcNow() - missed.At > CorrectionWithin || _commands is null) return null;
        if (!Acted(answer) || LastCommandOf(answer.Steps) is not { } command || !CommandTools.NamesClosely(said, command) || !IsCorrection(said)) return null;
        var phrase = PhraseOf(missed.Said, _persona(who).Name);
        if (phrase is null || CommandTools.NamesClosely(phrase, command)) return null;
        var arguments = new JsonObject { ["command"] = command, ["phrase"] = phrase };
        var outcome = await _tools.RunAsync("add_voice_phrase", JsonSerializer.SerializeToElement(arguments), new ToolContext(who, said), said, "learned", token).ConfigureAwait(false);
        if (outcome.Result is not { Failed: false, Undo: not null } done) return null;
        Log.Info($"Asistente: aprendido «{phrase}» para «{command}» (de «{missed.Said}»)");
        return Loc.T("assistant.phrase.learnedPhrase", phrase, command);
    }

    /// <summary>
    /// What a short order named, without its verb, fillers and the assistant's name («Abre Kamovis.» → «Kamovis»); null
    /// when nothing is left or it is too long to be a name.
    /// </summary>
    internal static string? PhraseOf(string said, string? name)
    {
        var vocabulary = VoiceVocabulary.Current;
        var names = TextSimilarity.Words(name ?? string.Empty).ToHashSet(StringComparer.Ordinal);
        var kept = said.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', ',', '!', '?', '¡', '¿', '«', '»', '"', ':', ';'))
            .Where(w => TextSimilarity.Words(w) is { Length: > 0 } folded
                        && !folded.All(f => vocabulary.Filler.Contains(f) || vocabulary.Actions.Contains(f) || names.Contains(f)))
            .ToList();
        return kept.Count is > 0 and <= TaughtWords ? string.Join(' ', kept) : null;
    }

    private static readonly Lazy<string[]> CorrectionWords = new(() =>
    [
        .. Loc.Available()
            .Select(l => Loc.Strings(l.Code, "assistant.words.correction").GetValueOrDefault("assistant.words.correction") ?? string.Empty)
            .SelectMany(v => v.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(p => " " + string.Join(' ', TextSimilarity.Words(p)) + " ")
            .Where(p => p.Trim().Length > 0)
            .Distinct(StringComparer.Ordinal),
    ]);

    /// <summary>Whether the order corrects the one before («se llama…», «me refiero a…», «I mean…»), in any language.</summary>
    internal static bool IsCorrection(string said)
    {
        var folded = " " + string.Join(' ', TextSimilarity.Words(said)) + " ";
        return CorrectionWords.Value.Any(p => folded.Contains(p, StringComparison.Ordinal));
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
            Log.Info($"Asistente: «{who ?? "K-Desktop"}» dice: «{text}»");
            _output.Chat(new ChatLineMessage("assistant", text, turn, true, who));
            conversation.LastAnswer = text;
            conversation.History.Said(text);
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
            FreeBrainIfAsked();
        }
    }

    /// <summary>
    /// «Léemela» (stage 14.2): a long answer, its Markdown read as sentences, only aloud (no bubble, chat line or history).
    /// Like an order, another one or «para» stops it.
    /// </summary>
    public async Task ReadAloudAsync(string? who, string markdown)
    {
        var text = LongAnswers.Readable(markdown);
        if (text.Length == 0) return;
        var conversation = Of(who);
        var (token, _) = conversation.Begin();
        try
        {
            Log.Info($"Asistente: «{who ?? "K-Desktop"}» lee en voz alta {text.Length} letras");
            _output.State(who, "speaking");
            await _output.ReadAloudAsync(who, Guid.NewGuid().ToString("N")[..12], text, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Stopped, or replaced by an order.
        }
        finally
        {
            if (conversation.End(token)) _output.State(who, "idle");
            FreeBrainIfAsked();
        }
    }

    /// <summary>«Para», Escape: stops what that assistant is answering (nothing to say).</summary>
    public void Cancel(string? assistant)
    {
        if (_conversations.TryGetValue(assistant ?? string.Empty, out var conversation)) conversation.Cancel();
    }

    /// <summary>Whether any assistant is answering an order now.</summary>
    public bool IsAnyAnswering => _conversations.Values.Any(c => c.Busy);

    private long _lastActive;

    /// <summary>When an order or a «decir» last started or ended (the brain may have been busy until then).</summary>
    public DateTimeOffset LastActive => new(Interlocked.Read(ref _lastActive), TimeSpan.Zero);

    private void Active() => Interlocked.Exchange(ref _lastActive, _time.GetUtcNow().UtcTicks);

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

    /// <summary>
    /// «Cierra la música y descarga el modelo», «Me refiero a liberar el cerebro. Descargar el cerebro»: the parts of an
    /// order that the rule of freeing the brain understands are done by it (the model leaves once the answer is over: the
    /// rest may still need it), and the rest is answered as usual.
    /// </summary>
    /// <param name="fresh">The order brought files (stage 14.1): it is about them, straight to the AI.</param>
    /// <param name="act">What to do with them without the AI (<see cref="HeardMessage.Act"/>).</param>
    private async Task<TalkAnswer> AnswerAsync(Conversation conversation, AssistantPersona persona, string? who, string said, string turn, bool wasBusy, CancellationToken token,
        bool fresh = false, string? act = null)
    {
        if (fresh || _freeBrain is null || QuickRules.Match(said, [persona.Name]) is not null)
            return await AnswerCoreAsync(conversation, persona, who, said, turn, wasBusy, token, fresh, act).ConfigureAwait(false);
        var parts = ToolChooser.Parts(said);
        var brain = parts.Where(p => QuickRules.Match(p, [persona.Name]) is { Kind: QuickRuleKind.FreeBrain }).ToList();
        if (parts.Count < 2 || brain.Count == 0) return await AnswerCoreAsync(conversation, persona, who, said, turn, wasBusy, token).ConfigureAwait(false);
        var rest = parts.Except(brain).ToList();
        Log.Info($"Asistente: «{said}» pide liberar el cerebro{(rest.Count > 0 ? " y más: " + string.Join(", ", rest) : string.Empty)}");
        if (rest.Count == 0)
            return new TalkAnswer(Phrase(persona, await FreeBrainNowAsync().ConfigureAwait(false) ? "brainFreed" : "brainNotLoaded"), Remember: false);
        var answer = await AnswerCoreAsync(conversation, persona, who, string.Join(", ", rest), turn, wasBusy, token).ConfigureAwait(false);
        FreeBrainAfter(who);
        return Join(answer, new TalkAnswer(Loc.T("tool.free_brain.reply")) { Tools = ["free_brain"] });
    }

    private async Task<TalkAnswer> AnswerCoreAsync(Conversation conversation, AssistantPersona persona, string? who, string said, string turn, bool wasBusy, CancellationToken token,
        bool fresh = false, string? act = null)
    {
        var context = new ToolContext(who, said) { Files = conversation.GivenFiles(_time.GetUtcNow()) };
        // Files given with the order (stage 14.1): opening one needs no AI; the rest is about them, for the AI (a user's command
        // or a rule must not take «resume este archivo»).
        if (fresh && context.Files.Count > 0)
        {
            if (act is HeardMessage.ActOpen or HeardMessage.ActFolder)
                return await ToolAsync(persona, "open_file", new JsonObject { ["path"] = context.Files[0], ["in_folder"] = act == HeardMessage.ActFolder },
                    context, said, acknowledge: true, token).ConfigureAwait(false);
            Log.Info($"Asistente: «{said}» trae {context.Files.Count} archivo(s): a la IA");
            return await AiAsync(conversation, persona, who, said, turn, context, token).ConfigureAwait(false);
        }
        var rule = QuickRules.Match(said, [persona.Name]);
        // The answer to «¿A o B?»: «el primero», «la otra» or one of the names.
        var pending = conversation.TakeChoice(_time.GetUtcNow());
        if (pending is not null && ChoiceOf(pending, rule, said) is { } chosen)
            return await CommandsAsync(persona, [chosen], context, said, token).ConfigureAwait(false);
        switch (rule)
        {
            case { Kind: QuickRuleKind.Cancel }:
                return new TalkAnswer(Phrase(persona, wasBusy ? "cancelled" : "nothingToCancel"), Remember: false);
            case { Kind: QuickRuleKind.RepeatCommand }:
                // «vuelve a repetir el comando anterior»: the last one it ran, not the command called «Anterior».
                if (conversation.LastCommand is not { } again) return new TalkAnswer(Phrase(persona, "nothingToRerun"), Remember: false);
                return await ToolAsync(persona, "run_command", new JsonObject { ["command"] = again }, context with { Decided = true }, said, acknowledge: true, token).ConfigureAwait(false);
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
            // «Recuerda que cuando diga «Kamugi» abras K-Movies» is a phrase for a command (add_voice_phrase): the AI.
            case { Kind: QuickRuleKind.Remember, Text: { Length: > 0 } fact } when _memory is not null && !TalkMind.TeachesPhrase(said):
                return await RememberAsync(persona, who, fact, context, said, token).ConfigureAwait(false);
            case { Kind: QuickRuleKind.Recall } when _memory is not null:
                return Recall(persona);
            case { Kind: QuickRuleKind.Forget, Text: { Length: > 0 } forget } when _memory is not null:
                return await ToolAsync(persona, "forget", new JsonObject { ["fact"] = forget }, context, said, acknowledge: true, token).ConfigureAwait(false);
            case { Kind: QuickRuleKind.FreeBrain } when _freeBrain is not null:
                return new TalkAnswer(Phrase(persona, await FreeBrainNowAsync().ConfigureAwait(false) ? "brainFreed" : "brainNotLoaded"), Remember: false);
            case { Kind: QuickRuleKind.LoadBrain } when _loadBrain is not null:
                var (_, loading) = _brain(who);
                if (loading is null)
                {
                    _output.State(who, "noBrain");
                    return new TalkAnswer(Phrase(persona, "noBrain"), Remember: false, Failed: true);
                }
                _output.State(who, "thinking");
                await _loadBrain().ConfigureAwait(false);
                return new TalkAnswer(Phrase(persona, "brainLoaded"), Remember: false);
            case { Kind: QuickRuleKind.WhatsPlaying }:
                return await ToolAsync(persona, "whats_playing", new JsonObject(), context, said, acknowledge: false, token).ConfigureAwait(false);
            case { Kind: QuickRuleKind.Weather or QuickRuleKind.WeatherTomorrow or QuickRuleKind.WeatherWeek } weather:
                return await ToolAsync(persona, "weather", WeatherArguments(weather), context, said, acknowledge: false, token).ConfigureAwait(false);
            case { Kind: QuickRuleKind.Dictate }:
                return new TalkAnswer(Phrase(persona, "dictating"), Remember: false) { Dictates = true };
            case { Kind: QuickRuleKind.ThinkHard } think:
                // «Piénsalo bien: …» thinks that over; alone, the order before (its wrong answer is in the history).
                var order = think.Text is { Length: > 0 } asked ? asked : conversation.LastOrder;
                if (order is null) return new TalkAnswer(Phrase(persona, "thinkWhat"), Remember: false) { AsksBack = true };
                return await AiAsync(conversation, persona, who, order, turn, context with { Heard = order }, token, thinkFirst: true).ConfigureAwait(false);
            case { Kind: QuickRuleKind.CancelTimer } cancelTimer:
                var which = new JsonObject();
                if (cancelTimer.Text is { Length: > 0 } words) which["reason"] = words;
                return await ToolAsync(persona, "cancel_timer", which, context, said, acknowledge: true, token).ConfigureAwait(false);
        }

        // A phrase of a JSON tool, as its file writes it («buenas noches»): that tool, without the AI, when the phrase is all
        // it needs (stage 9.1; the 4B took «buenas noches» for a greeting and ran nothing).
        if (OwnPhrase(said, persona) is { } own)
        {
            Log.Info($"Asistente: «{said}» es una frase de la herramienta {own.Name}");
            return await ToolAsync(persona, own.Name, new JsonObject(), context, said, acknowledge: !own.ReadsOnly, token).ConfigureAwait(false);
        }

        // «No, eso está mal», «te has equivocado» right after an answer of the AI: it thinks it over, with what it said before
        // and the correction (stage 12). Not the user's commands: the correction is for the AI.
        if (IsWrongAbout(conversation, said))
        {
            Log.Info($"Asistente: «{said}» corrige la respuesta de antes: lo piensa mejor");
            return await AiAsync(conversation, persona, who, said, turn, context, token, thinkFirst: true, doubted: true).ConfigureAwait(false);
        }

        // The user's commands: what clearly names them runs now, in order; two nearly as close are asked about. Not for a
        // question («¿qué hace el comando modo juego?» names it, and must not run it): the AI, with the command tools.
        TalkAnswer? ran = null;
        var forAi = said;
        IReadOnlyCollection<string>? leaveOut = null;
        IReadOnlyList<string>? close = null;
        // Commands surely asked for that a big model was left to run, with the rest of the order.
        IReadOnlyList<string>? certain = null;
        // What is left of the order for the rules about programs and the AI: all of it, or what no command took.
        IReadOnlyList<string> leftover = [said];
        // A phrase the user taught it («cuando diga «la vuelta»…»): the AI, with that memory, before any command that sounds
        // like it («la vuelta» ran «Anterior» at 0.87 and the memory never counted).
        var taught = _memory?.Store.Triggered(said) ?? [];
        if (taught.Count > 0) context = context with { Decided = true };
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
            if (capable && (matched.Unmatched.Count > 0 || !matched.Parts.All(Direct)))
            {
                // It is still told they are sure: Bonsai, lost in «guarda una copia de todo», offered to «apagar la pantalla».
                certain = [.. sure.Select(c => c.Name)];
                sure = [];
            }
            // Not sure of any part, but the whole order is that near one command and well ahead of the rest («Va ser la
            // papelera», Parakeet's «vacía la papelera», 0.82): it is that one. K-Desktop still asks before what is risky.
            // What turns off the PC needs the voice's own bar (5.0.200: «apaga el monitor» turned it off at 0.87).
            if (sure.Count == 0 && matched.Parts is [{ Sure: false, Candidates: [var only, ..] } whole] && only.Score >= CommandTools.Direct
                && (!only.Power || only.Score >= SemanticMatcher.AcceptPower)
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
            // Nothing clear and on its way to the AI: its model, which loads for it anyway, picks with one letter (5.0.200).
            else if (await DecideAsync(who, said, matched, token).ConfigureAwait(false) is { } decided)
            {
                if (decided.Choice == CommandChoice.Run) return await CommandsAsync(persona, [decided.First!], context, said, token).ConfigureAwait(false);
                conversation.AskChoice([decided.First!, decided.Second!], _time.GetUtcNow());
                return new TalkAnswer(Phrase(persona, "which", decided.First!.Name, decided.Second!.Name), Remember: false) { AsksBack = true };
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

        // A small model never asks to think harder and gets sums of several steps wrong («17 cajas de 23 manzanas, regalo un
        // tercio…»): for it, thinking first is cheap (some 2 s with the 4B). A big one asks when it needs to (stage 12).
        var wordProblem = !capable && ran is null && IsWordProblem(forAi);
        if (wordProblem) Log.Info($"Asistente: «{forAi}» parece una cuenta de varios pasos: lo piensa (modelo pequeño)");
        return await AiAsync(conversation, persona, who, forAi, turn, context, token, ran, leaveOut, close, capable, certain, thinkFirst: wordProblem).ConfigureAwait(false);
    }

    /// <summary>
    /// One whole order that named nothing clearly: the assistant's model chooses among the closest commands with one token
    /// (<see cref="CommandDecider"/>). Null for «none», when there is nothing worth asking about, or when the model cannot
    /// say (no AI, a server without probabilities, an error): then the AI goes on as before.
    /// </summary>
    private async Task<CommandDecision?> DecideAsync(string? who, string said, MatchedMessage matched, CancellationToken token)
    {
        if (matched.Parts.Count != 1 || matched.Unmatched.Count > 0) return null;
        var options = CommandDecider.Options(matched, said);
        if (options.Count == 0) return null;
        var (client, model) = _brain(who);
        if (client is null || model is null) return null;
        _output.State(who, "thinking");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyDictionary<char, double>? probabilities;
        try
        {
            probabilities = await client.LettersAsync(model, CommandDecider.Prompt(said, options), CommandDecider.LettersFor(options), token).ConfigureAwait(false);
        }
        catch (Exception ex) when (!token.IsCancellationRequested && ex is ChatModelException or HttpRequestException or JsonException or OperationCanceledException)
        {
            Log.Warn($"Asistente: no se pudo decidir con una letra («{said}»): {ex.Message}");
            return null;
        }
        if (probabilities is null)
        {
            Log.Info("Asistente: el servidor del modelo no da probabilidades: sin decisión de una letra");
            return null;
        }
        var decision = CommandDecider.Decide(probabilities, options);
        Log.Info($"Asistente: «{said}» con una letra ({watch.ElapsedMilliseconds} ms): " + decision.Choice switch
        {
            CommandChoice.Run => $"ejecuta «{decision.First!.Name}» {decision.Probability:0.00}",
            CommandChoice.Ask => $"pregunta «{decision.First!.Name}» o «{decision.Second!.Name}» {decision.Probability:0.00}",
            _ => decision.First is { } best ? $"nada claro («{best.Name}» {decision.Probability:0.00}): a la IA" : $"ninguno {decision.Probability:0.00}: a la IA",
        });
        return decision.Choice == CommandChoice.None ? null : decision;
    }

    /// <summary>
    /// The AI answers <paramref name="forAi"/> (what the rules and the commands left of the order), after what
    /// <paramref name="ran"/> already did. <paramref name="thinkFirst"/>: it thinks harder from the start (stage 12).
    /// </summary>
    private async Task<TalkAnswer> AiAsync(Conversation conversation, AssistantPersona persona, string? who, string forAi, string turn, ToolContext context,
        CancellationToken token, TalkAnswer? ran = null, IReadOnlyCollection<string>? leaveOut = null, IReadOnlyList<string>? close = null,
        bool? capable = null, IReadOnlyList<string>? certain = null, bool thinkFirst = false, bool doubted = false)
    {
        var (client, model) = _brain(who);
        // Not ready: the server may have opened since it was last looked at (Ollama, after K-Desktop at start-up).
        if ((client is null || model is null) && _recheck is not null && await _recheck().ConfigureAwait(false)) (client, model) = _brain(who);
        if (client is null || model is null)
        {
            // The character shows it has nothing to think with, right before it says so.
            _output.State(who, "noBrain");
            return NotDone(persona, ran, forAi, Phrase(persona, "noBrain"));
        }
        conversation.LastOrder = forAi;
        _output.State(who, "thinking");
        // Nothing said yet a moment later (the model loading, a server starting, a long first read): a word, so the user
        // knows it heard. Not when a command already answered.
        var spoken = 0;
        using var quiet = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (ran is null) _ = SayThinkingAsync(conversation, persona, who, turn, () => Volatile.Read(ref spoken) != 0, quiet.Token);
        // Thinking harder takes a while: it says so («déjame pensarlo…»), once, instead of the usual word.
        var hard = 0;
        void ThinkingHard()
        {
            Interlocked.Exchange(ref spoken, 1);
            if (Interlocked.Exchange(ref hard, 1) != 0) return;
            _output.State(who, "thinkingHard");
            if (FreshPhrase(persona, "thinkingHard", conversation) is { Length: > 0 } phrase) _output.Preface(who, turn, phrase);
        }
        // A long answer's whole text so far (stage 14.2), with each line of the chat: K-Desktop's reading sheet grows with it.
        string? longSoFar = null;
        var textSoFar = string.Empty;
        var shown = new PartialText(() =>
        {
            Interlocked.Exchange(ref spoken, 1);
            var sofar = (ran is null ? "" : ran.Text + " ") + Volatile.Read(ref textSoFar);
            _output.Chat(new ChatLineMessage("assistant", sofar, turn, false, who, Volatile.Read(ref longSoFar)));
            // Ahead, the voice says the first sentences only: the rest may end up written down instead.
            _output.SpeakAhead(who, turn, (ran is null ? "" : ran.Text + " ") + LongAnswers.Spoken(Volatile.Read(ref textSoFar)));
        }, _time);
        TalkAnswer answer;
        try
        {
            answer = await _mind.AnswerAsync(client, model, persona, conversation.History.Messages(), conversation.History.LastTools(), forAi, context,
                text => { Volatile.Write(ref textSoFar, text); shown.Show(); }, token,
                leaveOut, close, capable ?? IsCapable(model), certain, _thinker?.Invoke(who), thinkFirst, ThinkingHard, doubted,
                text => { Volatile.Write(ref longSoFar, text); shown.Show(); }).ConfigureAwait(false);
        }
        finally
        {
            await quiet.CancelAsync().ConfigureAwait(false);
        }
        answer = answer with { Text = answer.Text.Length > 0 ? answer.Text : Phrase(persona, "ok"), FromAi = true };
        // One «Recibido.», at the start («Recibido. He ejecutado «Equipo». Afirmativo. He pasado…» had two).
        return ran is null ? answer : Join(ran, answer with { Text = answer.WithoutAck, Ack = null }) with { FromAi = true, Failed = answer.Failed };
    }

    /// <summary>
    /// Whether the order says the answer before was wrong («no, eso está mal», «te has equivocado»,
    /// <c>assistant.words.wrong</c> in any language) and that answer was the AI's, a moment ago.
    /// </summary>
    private bool IsWrongAbout(Conversation conversation, string said) =>
        conversation.LastOrder is not null && conversation.LastAiAt is { } at && _time.GetUtcNow() - at <= CorrectionWithin && UserSignals.IsWrong(said);

    /// <summary>A question with this many numbers at least is taken for a sum of several steps.</summary>
    public const int StepNumbers = 3;

    /// <summary>
    /// Whether the order is a question with <see cref="StepNumbers"/> numbers or more, in figures or in words (a word
    /// problem). «Un», «una» do not count: «una cena», «un tercio».
    /// </summary>
    internal static bool IsWordProblem(string said)
    {
        if (!IsQuestion(said)) return false;
        var words = TextSimilarity.Words(said);
        var count = 0;
        for (var i = 0; i < words.Length;)
        {
            if (words[i].Any(char.IsDigit)) count++;
            else if (SpokenNumbers.TryRead(words, i, out var value, out var end) && end > i)
            {
                if (value != 1) count++;
                i = end;
                continue;
            }
            i++;
        }
        return count >= StepNumbers;
    }

    /// <summary>How long it may think in silence before saying it is on it (1.5 s until 5.0.199: said nearly every time).</summary>
    public static readonly TimeSpan ThinkingAfter = TimeSpan.FromSeconds(2.5);

    /// <summary>
    /// After saying it is on it, it does not say it again for this long (5.0.200: «Un momento.» on every answer was
    /// tiring): the character still shows it is thinking.
    /// </summary>
    public static readonly TimeSpan WaitWordRest = TimeSpan.FromMinutes(3);

    private async Task SayThinkingAsync(Conversation conversation, AssistantPersona persona, string? who, string turn, Func<bool> spoke, CancellationToken cancellationToken)
    {
        try { await Task.Delay(ThinkingAfter, _time, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        if (spoke()) return;
        var now = _time.GetUtcNow();
        if (conversation.WaitedAt is { } at && now - at < WaitWordRest) return;
        if (FreshPhrase(persona, "thinking", conversation) is not { Length: > 0 } phrase) return;
        conversation.WaitedAt = now;
        _output.Preface(who, turn, phrase);
    }

    /// <summary>How many of its last words before an answer it does not repeat (fewer when it has fewer).</summary>
    private const int WaitWordsKept = 3;

    /// <summary>One of the phrases for <paramref name="key"/> that is not one of the last few it said before an answer.</summary>
    private string FreshPhrase(AssistantPersona persona, string key, Conversation conversation)
    {
        var all = persona.PhrasesFor(key);
        if (all.Count == 0) return string.Empty;
        lock (conversation.RecentWaits)
        {
            var fresh = all.Where(p => !conversation.RecentWaits.Contains(p)).ToList();
            var pool = fresh.Count > 0 ? fresh : all;
            var phrase = pool[_random.Next(pool.Count)];
            conversation.RecentWaits.Enqueue(phrase);
            while (conversation.RecentWaits.Count > Math.Min(WaitWordsKept, all.Count - 1)) conversation.RecentWaits.Dequeue();
            return phrase;
        }
    }

    /// <summary>Two answers of one order, one after the other.</summary>
    private static TalkAnswer Join(TalkAnswer first, TalkAnswer then) => first with
    {
        Text = (first.Text + " " + then.Text).Trim(),
        Failed = first.Failed || then.Failed,
        Tools = [.. first.Tools, .. then.Tools.Where(t => !first.Tools.Contains(t))],
        Steps = [.. first.Steps, .. then.Steps],
        Written = first.Written ?? then.Written,
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
            var (tool, arguments) = rule.Kind == QuickRuleKind.OpenApp ? ("open_app", new JsonObject { ["name"] = rule.Text })
                : rule.Op == "kill" ? ("end_process", new JsonObject { ["app"] = rule.Text ?? string.Empty })
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

    /// <summary>
    /// The JSON tool <paramref name="said"/> is one of the phrases of (its examples), if it needs no more than that: no
    /// parameter it must be given, and when it only reads, its own words for the answer (else the AI tells it).
    /// </summary>
    private JsonTool? OwnPhrase(string said, AssistantPersona persona)
    {
        foreach (var tool in _tools.Registry.All)
        {
            if (tool is not JsonTool json) continue;
            var definition = json.Definition;
            if (definition.Parameters?.Any(p => definition.RequiredNames.Contains(p.Key) && p.Value.Default is null) == true) continue;
            if (json.ReadsOnly && string.IsNullOrWhiteSpace(definition.Say?.Ok)) continue;
            if (json.Examples.Any(e => QuickRules.SaysPhrase(said, e, [persona.Name]))) return json;
        }
        return null;
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

    /// <summary>«¿Qué tiempo hará mañana en Bilbao?» → when and, if said, the place.</summary>
    internal static JsonObject WeatherArguments(QuickRule rule)
    {
        var arguments = new JsonObject
        {
            ["when"] = rule.Kind switch { QuickRuleKind.WeatherTomorrow => "tomorrow", QuickRuleKind.WeatherWeek => "week", _ => "now" },
        };
        if (rule.Text?.Trim().TrimEnd('?', '.', '!', '¿', '¡') is { Length: > 0 } place) arguments["place"] = place;
        return arguments;
    }

    /// <summary>The last command an answer ran well, by the name it was asked with; null: none.</summary>
    internal static string? LastCommandOf(IReadOnlyList<ChatMessage> steps)
    {
        string? last = null;
        for (var i = 0; i < steps.Count; i++)
            foreach (var call in steps[i].ToolCalls ?? [])
                if (call.Name == "run_command" && call.Arguments.ValueKind == JsonValueKind.Object && call.Arguments.TryGetProperty("command", out var name)
                    && name.ValueKind == JsonValueKind.String && name.GetString() is { Length: > 0 } command
                    && steps.Skip(i + 1).FirstOrDefault(m => m.Role == ChatRole.Tool && m.ToolCallId == call.Id) is { } result
                    && result.Content.StartsWith("Done", StringComparison.Ordinal))
                    last = command;
        return last;
    }

    private string Phrase(AssistantPersona persona, string key, params object?[] args) => persona.Phrase(key, _random, args);

    private static string LowerFirst(string text) => text.Length > 1 && char.IsUpper(text[0]) && !char.IsUpper(text[1])
        ? char.ToLower(text[0], Loc.Instance.Culture) + text[1..]
        : text;

    /// <summary>One assistant's conversation: its history, its last answer and the order going on now.</summary>
    /// <summary>How long files given to an assistant stay as its context after it was last talked to about them.</summary>
    internal static readonly TimeSpan GivenFor = TimeSpan.FromMinutes(5);

    private sealed class Conversation(TimeProvider time) : IDisposable
    {
        private readonly object _gate = new();
        private CancellationTokenSource? _current;

        public TalkHistory History { get; } = new(time);

        public string? LastAnswer { get; set; }

        /// <summary>When it last said it was on it («Un momento.»), and the last few words it said before an answer (5.0.200).</summary>
        public DateTimeOffset? WaitedAt { get; set; }

        public Queue<string> RecentWaits { get; } = new();

        /// <summary>The last of the user's commands it ran well (by name), for «repite el comando anterior».</summary>
        public string? LastCommand { get; set; }

        /// <summary>The last order, when it did nothing (its correction may teach a voice phrase).</summary>
        public (string Said, DateTimeOffset At)? Missed { get; set; }

        private (IReadOnlyList<string> Files, DateTimeOffset At)? _given;

        /// <summary>The files the user gave it last (stage 14.1); again with each order about them.</summary>
        public void Give(IReadOnlyList<string> files, DateTimeOffset now)
        {
            lock (_gate) _given = ([.. files], now);
        }

        /// <summary>The files given in the last <see cref="GivenFor"/> (none after: «mándalo» then is about something else).</summary>
        public IReadOnlyList<string> GivenFiles(DateTimeOffset now)
        {
            lock (_gate)
            {
                if (_given is not { } given) return [];
                if (now - given.At > GivenFor)
                {
                    _given = null;
                    return [];
                }
                // Talked about, they stay a while longer.
                _given = (given.Files, now);
                return given.Files;
            }
        }

        /// <summary>The last order that went to the AI (what «piénsalo bien» alone thinks over).</summary>
        public string? LastOrder { get; set; }

        /// <summary>When the AI last answered (null: the last answer was not the AI's).</summary>
        public DateTimeOffset? LastAiAt { get; set; }

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
    private sealed class PartialText(Action send, TimeProvider time)
    {
        private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(125);
        private long _last = long.MinValue;

        public void Show()
        {
            var now = time.GetTimestamp();
            if (_last != long.MinValue && time.GetElapsedTime(_last, now) < Every) return;
            _last = now;
            send();
        }
    }
}

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
}

/// <summary>
/// Talking with the model (plan §5.2): the instructions with the persona, the short history and the order; the model may
/// call tools (through the <see cref="ToolRunner"/>, which asks permission and writes the activity), at most
/// <see cref="MaxToolRounds"/> rounds and <see cref="MaxToolCalls"/> calls; then it must answer. Only the tools that fit
/// the order are offered (<see cref="ToolChooser"/>). No thinking, short answers, cleaned for a bubble and a voice.
/// </summary>
public sealed class TalkMind(ToolRunner tools, ToolChooser chooser, TimeProvider time, TalkMemory? memory = null)
{
    /// <summary>Rounds in which the model may call tools; after them it is asked without tools, to answer.</summary>
    public const int MaxToolRounds = 3;

    /// <summary>Tool calls for one order, in all its rounds.</summary>
    public const int MaxToolCalls = 3;

    /// <summary>Longest answer, in tokens (one or two sentences, with room for a tool call).</summary>
    public const int MaxTokens = 220;

    /// <param name="recentTools">The tools used in the last exchange.</param>
    /// <param name="onText">The answer so far, cleaned, while it is written (any thread).</param>
    /// <param name="leaveOut">Tools not to offer for this order (run_command when no command of the user is anything like it).</param>
    /// <param name="commands">The user's commands K-Desktop found close to the order: named in the instructions, and
    /// run_command is offered whatever the order's meaning says (with «abre Steam y Epic Games» it was not, and the model
    /// said it had opened them).</param>
    public async Task<TalkAnswer> AnswerAsync(IChatModel model, string modelName, AssistantPersona persona, IReadOnlyList<ChatMessage> history,
        IReadOnlyCollection<string> recentTools, string said, ToolContext context, Action<string>? onText, CancellationToken cancellationToken,
        IReadOnlyCollection<string>? leaveOut = null, IReadOnlyList<string>? commands = null)
    {
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
        if (aboutMemory) always.AddRange(MemoryTools);
        // «Cuando diga «la vuelta», abre…» came to mind: what it asks for must be at hand.
        if (known.Any(m => QuickRules.Sites(m.Text).Count > 0)) always.Add(OpenWebTool);
        var offered = await chooser.ChooseAsync(available, said, [.. always.Distinct()], order, cancellationToken).ConfigureAwait(false);
        var definitions = offered.Tools.Select(t => new ToolDefinition(t.Name, t.Description, t.ParametersSchemaJson)).ToList();
        var messages = new List<ChatMessage> { ChatMessage.System(TalkPrompt.Compose(persona, time.GetLocalNow(), known, commands)) };
        messages.AddRange(history);
        messages.Add(ChatMessage.User(said));
        var first = messages.Count;

        var calls = 0;
        string? toolReply = null;
        // What the tools that worked say they did (null: one that has no words of its own), and whether one was an action.
        var replies = new List<string?>();
        var acted = false;
        var used = new List<string>();
        var answer = string.Empty;
        try
        {
            for (var round = 0; round <= MaxToolRounds; round++)
            {
                var offer = round < MaxToolRounds && calls < MaxToolCalls;
                var request = new ChatRequest(modelName, messages, offer ? definitions : []) { Temperature = 0.5, MaxTokens = MaxTokens };
                var (text, asked) = await RoundAsync(model, request, onText, cancellationToken).ConfigureAwait(false);
                if (asked.Count == 0)
                {
                    answer = text;
                    break;
                }

                messages.Add(ChatMessage.Assistant(text, asked));
                foreach (var call in asked)
                {
                    string result;
                    if (++calls > MaxToolCalls) result = "Not done: too many tools for one order. Answer now.";
                    else
                    {
                        var outcome = await tools.RunAsync(call.Name, call.Arguments, context, said, "ai", cancellationToken).ConfigureAwait(false);
                        result = outcome.Result is { } done ? done.Text : "Not done: " + outcome.Entry.Did;
                        toolReply = outcome.Result is { Failed: false } ok ? ok.Reply ?? ok.Done ?? toolReply : outcome.Entry.Did;
                        if (outcome.Result is { Failed: false } worked)
                        {
                            if (!used.Contains(call.Name)) used.Add(call.Name);
                            replies.Add(worked.Reply);
                            acted |= worked.Undo is not null || worked.Acted;
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

        var steps = messages.GetRange(first, messages.Count - first);
        // It did something and every tool has its own words for it: those, exact (a small model may well say «in 25
        // minutes» for a timer set for 20). Its own text is for questions and chat.
        if (acted && replies.Count > 0 && replies.All(r => r is not null))
            return new TalkAnswer(persona.Phrase("ok") + " " + string.Join(" ", replies)) { Tools = used, Steps = steps };

        var clean = ReplyText.Clean(answer);
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

    private static readonly string[] NoteTools = ["save_note", "find_notes"];

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

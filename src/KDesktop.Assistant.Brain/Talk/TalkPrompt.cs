using System.Text;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Brain.Talk;

/// <summary>
/// The instructions for the model (plan §5.2), short because they go with every order: who it is (the persona of the
/// assistant it talks for), how it answers (in the user's language, one or two sentences, nothing a voice cannot say),
/// how it uses the tools, and the date and time. In the user's language: small models answer better in the language
/// of their instructions. The tools of this order go apart, as the model's tool definitions. What comes to mind about the
/// user for this order (stage 3.7) goes at the end, to use only if it fits.
/// </summary>
public static class TalkPrompt
{
    /// <param name="commands">The user's commands K-Desktop found close to the order (none: no line about them).</param>
    public static string Compose(AssistantPersona persona, DateTimeOffset now, IReadOnlyList<Core.Assistant.Pipe.MemoryEntry>? known = null,
        IReadOnlyList<string>? commands = null)
    {
        var culture = Loc.Instance.Culture;
        var text = new StringBuilder();
        text.AppendLine(Loc.T("assistant.prompt.who", persona.Name));
        text.AppendLine(persona.Prompt);
        text.AppendLine(Loc.T("assistant.prompt.style"));
        text.AppendLine(Loc.T("assistant.prompt.language"));
        text.AppendLine(Loc.T("assistant.prompt.tools"));
        text.AppendLine(Loc.T("assistant.prompt.act"));
        if (commands is { Count: > 0 }) text.AppendLine(Loc.T("assistant.prompt.commands", string.Join(", ", commands.Select(c => $"«{c}»"))));
        text.Append(Loc.T("assistant.prompt.now", now.ToString("D", culture) + ", " + now.ToString("t", culture)));
        if (known is { Count: > 0 })
        {
            text.AppendLine().Append(Loc.T("assistant.prompt.memory"));
            foreach (var memory in known) text.AppendLine().Append("- ").Append(memory.Text);
        }
        return text.ToString();
    }
}

/// <summary>
/// The last few exchanges with one assistant (plan §5.2: a short history), so «¿y en Madrid?» or «ponle otro» make
/// sense. Forgotten after a while without talking. Thread-safe.
/// </summary>
public sealed class TalkHistory(TimeProvider time)
{
    /// <summary>Exchanges kept (an order and its answer each).</summary>
    public const int Kept = 3;

    /// <summary>After this long without talking, a new order starts a new conversation.</summary>
    public static readonly TimeSpan Forget = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private readonly List<(string Said, string Answer, IReadOnlyList<string> Tools, IReadOnlyList<ChatMessage> Steps)> _exchanges = [];
    private DateTimeOffset _last;

    /// <param name="tools">The tools it used to answer (offered again with the next order).</param>
    /// <param name="steps">The tool calls and their results between the order and the answer: a small model that only
    /// saw «He ejecutado «Pausar».» answered the next order with those words instead of calling the tool.</param>
    public void Add(string said, string answer, IReadOnlyList<string>? tools = null, IReadOnlyList<ChatMessage>? steps = null)
    {
        lock (_gate)
        {
            Expire();
            _exchanges.Add((said, answer, tools ?? [], steps ?? []));
            if (_exchanges.Count > Kept) _exchanges.RemoveRange(0, _exchanges.Count - Kept);
            _last = time.GetUtcNow();
        }
    }

    /// <summary>
    /// It said something on its own after its last answer (a watch that happened, a timer, «Asistente: decir»): it goes
    /// after that answer, so «avísame cuando termine la descarga» again is not taken as already done. Nothing to add it
    /// to: nothing.
    /// </summary>
    public void Said(string text)
    {
        lock (_gate)
        {
            Expire();
            if (_exchanges.Count == 0) return;
            var last = _exchanges[^1];
            _exchanges[^1] = last with { Answer = last.Answer + "\n\n" + text };
        }
    }

    /// <summary>As messages for the model, oldest first.</summary>
    public IReadOnlyList<ChatMessage> Messages()
    {
        lock (_gate)
        {
            Expire();
            return [.. _exchanges.SelectMany(e => e.Steps.Prepend(ChatMessage.User(e.Said)).Append(ChatMessage.Assistant(e.Answer)))];
        }
    }

    /// <summary>The tools used in the last exchange (none when it was forgotten).</summary>
    public IReadOnlyList<string> LastTools()
    {
        lock (_gate)
        {
            Expire();
            return _exchanges.Count > 0 ? _exchanges[^1].Tools : [];
        }
    }

    public void Clear()
    {
        lock (_gate) _exchanges.Clear();
    }

    private void Expire()
    {
        if (_exchanges.Count > 0 && time.GetUtcNow() - _last > Forget) _exchanges.Clear();
    }
}

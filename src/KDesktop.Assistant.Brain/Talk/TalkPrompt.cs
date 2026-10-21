using System.Text;
using KDesktop.Assistant.Brain.Models;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Brain.Talk;

/// <summary>
/// The instructions for the model (plan §5.2), short because they go with every order: who it is (the persona of the
/// assistant it talks for), how it answers (in the user's language, one or two sentences, nothing a voice cannot say),
/// how it uses the tools, and the date and time. In the user's language: small models answer better in the language
/// of their instructions. The tools of this order go apart, as the model's tool definitions.
/// </summary>
public static class TalkPrompt
{
    public static string Compose(AssistantPersona persona, DateTimeOffset now)
    {
        var culture = Loc.Instance.Culture;
        var text = new StringBuilder();
        text.AppendLine(Loc.T("assistant.prompt.who", persona.Name));
        text.AppendLine(persona.Prompt);
        text.AppendLine(Loc.T("assistant.prompt.style"));
        text.AppendLine(Loc.T("assistant.prompt.tools"));
        text.Append(Loc.T("assistant.prompt.now", now.ToString("D", culture) + ", " + now.ToString("t", culture)));
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
    private readonly List<(string Said, string Answer, IReadOnlyList<string> Tools)> _exchanges = [];
    private DateTimeOffset _last;

    /// <param name="tools">The tools it used to answer (offered again with the next order).</param>
    public void Add(string said, string answer, IReadOnlyList<string>? tools = null)
    {
        lock (_gate)
        {
            Expire();
            _exchanges.Add((said, answer, tools ?? []));
            if (_exchanges.Count > Kept) _exchanges.RemoveRange(0, _exchanges.Count - Kept);
            _last = time.GetUtcNow();
        }
    }

    /// <summary>As messages for the model, oldest first.</summary>
    public IReadOnlyList<ChatMessage> Messages()
    {
        lock (_gate)
        {
            Expire();
            return [.. _exchanges.SelectMany(e => new[] { ChatMessage.User(e.Said), ChatMessage.Assistant(e.Answer) })];
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

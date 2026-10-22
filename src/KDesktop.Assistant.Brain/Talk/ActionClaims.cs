using System.Text.RegularExpressions;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Brain.Talk;

/// <summary>
/// What the model says it did, checked against what it really did in that order (5.0.131). «Te he enviado a Telegram…»
/// came after opening the Telegram app and t.me, with nothing sent: a sentence that claims a kind of action (sending,
/// saving, deleting, opening, closing, scheduling) with no tool of that kind having worked is taken out, and an honest one
/// put at the end. The claims are phrases of each language (<c>assistant.claims.&lt;kind&gt;</c>, «|»); the tools of a
/// kind are known by words of their names. A sentence that denies («no he enviado nada») is left as it is.
/// </summary>
public static partial class ActionClaims
{
    /// <summary>Each kind of action, and the words of the names of the tools that do it.</summary>
    private static readonly (string Kind, string[] Tools)[] Kinds =
    [
        ("send", ["send", "notify", "mail", "message", "telegram", "whatsapp", "sms", "post", "share", "tweet", "reply"]),
        ("save", ["note", "remember", "save", "write", "create", "add", "append", "store", "upload", "update", "transform"]),
        ("delete", ["delete", "remove", "forget", "cancel", "clear", "trash", "kill", "end_process", "stop_watching", "run_command", "run_event"]),
        ("open", ["open", "run_command", "run_event", "launch", "start", "play", "control_app"]),
        ("close", ["close", "control_app", "end_process", "kill", "run_command", "run_event", "stop", "quit"]),
        ("schedule", ["timer", "notify_when", "event", "calendar", "schedule", "remind", "alarm"]),
    ];

    /// <summary>
    /// <paramref name="text"/> without the sentences that claim what was not done (<paramref name="worked"/>: the tools that
    /// worked in this order), and the kinds taken out (empty: it was all true).
    /// </summary>
    public static (string Text, IReadOnlyList<string> Unfounded) Check(string text, IReadOnlyCollection<string> worked)
    {
        var sentences = Sentence().Matches(text).Select(m => m.Value).Where(s => s.Trim().Length > 0).ToList();
        var unfounded = new List<string>();
        var kept = new List<string>();
        foreach (var sentence in sentences)
        {
            var claimed = Kinds.FirstOrDefault(k => Claims(sentence, k.Kind) && !worked.Any(t => k.Tools.Any(w => t.Contains(w, StringComparison.OrdinalIgnoreCase))));
            if (claimed.Kind is null || Denies(sentence)) kept.Add(sentence);
            else if (!unfounded.Contains(claimed.Kind)) unfounded.Add(claimed.Kind);
        }
        if (unfounded.Count == 0) return (text, []);
        var honest = unfounded.Select(k => Loc.T("assistant.phrase.claim." + k));
        return (string.Join(" ", kept.Select(s => s.Trim()).Concat(honest)).Trim(), unfounded);
    }

    private static bool Claims(string sentence, string kind)
    {
        var phrases = Phrases(kind);
        if (phrases.Length == 0) return false;
        var folded = " " + sentence.ToLowerInvariant() + " ";
        return phrases.Any(p => Regex.IsMatch(folded, @"(?<![\p{L}])" + Regex.Escape(p) + @"(?![\p{L}])", RegexOptions.None, TimeSpan.FromMilliseconds(50)));
    }

    private static bool Denies(string sentence)
    {
        var words = Loc.T("assistant.claims.denial").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var folded = " " + sentence.ToLowerInvariant() + " ";
        return words.Any(w => Regex.IsMatch(folded, @"(?<![\p{L}])" + Regex.Escape(w) + @"(?![\p{L}])", RegexOptions.None, TimeSpan.FromMilliseconds(50)));
    }

    /// <summary>The phrases that claim a kind of action, in the assistant's language and in English (models mix them).</summary>
    private static string[] Phrases(string kind)
    {
        var key = "assistant.claims." + kind;
        var own = Loc.T(key);
        var english = Loc.Strings(Loc.Fallback, key).GetValueOrDefault(key);
        return [.. new[] { own == key ? null : own, english }.OfType<string>()
            .SelectMany(p => p.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(p => p.ToLowerInvariant()).Distinct()];
    }

    /// <summary>A sentence with what ends it (and the spaces after).</summary>
    [GeneratedRegex(@"[^.!?…]+[.!?…]*\s*")]
    private static partial Regex Sentence();
}

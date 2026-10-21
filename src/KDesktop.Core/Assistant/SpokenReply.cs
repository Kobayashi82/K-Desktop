using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Assistant;

/// <summary>A short answer said aloud to «¿Puedo…?» (stage 5.3).</summary>
public enum SpokenReplyKind { Yes, No, Always }

/// <summary>
/// «Sí», «no, mejor no», «sí, y no vuelvas a preguntar»: the answer to a question of an assistant, said aloud, understood
/// without the AI (stage 5.3) in any installed language. K-Desktop uses it while its little window asks for permission
/// and the order came by voice: the reply answers the window instead of becoming a new order. The language files have
/// <c>assistant.answer.yes</c>, <c>.no</c> and <c>.always</c> (ways of saying it, «|»); <c>assistant.rule.fillers</c> are
/// taken off around it. The whole reply must be one of them, or begin with a one-word yes or no («sí, hazlo ya»).
/// </summary>
public static class SpokenReply
{
    private sealed class Vocabulary
    {
        public readonly List<(SpokenReplyKind Kind, string[] Words)> Phrases = [];
        public readonly List<string[]> Fillers = [];
    }

    private static Vocabulary? _current;
    private static readonly object Gate = new();

    static SpokenReply() => Loc.Instance.Changed += () => { lock (Gate) _current = null; };

    private static Vocabulary Words
    {
        get
        {
            lock (Gate) return _current ??= Build();
        }
    }

    /// <summary>What <paramref name="text"/> answers, or null when it is not a yes or a no.</summary>
    public static SpokenReplyKind? Of(string text)
    {
        var w = Words;
        var said = Trim([.. TextSimilarity.Words(text)], w);
        if (said.Count == 0) return null;

        // The whole of it, the longest way first («sí, y no vuelvas a preguntar» before «sí»).
        foreach (var (kind, words) in w.Phrases)
            if (words.SequenceEqual(said, StringComparer.Ordinal)) return kind;

        // «Sí, hazlo ya», «no, déjalo»: a one-word yes or no that begins a short reply. «Always» needs its own words.
        if (said.Count <= 5)
            foreach (var (kind, words) in w.Phrases)
                if (kind != SpokenReplyKind.Always && words.Length == 1 && words[0] == said[0]) return kind;
        return null;
    }

    private static List<string> Trim(List<string> words, Vocabulary w)
    {
        var changed = true;
        while (changed && words.Count > 0)
        {
            changed = false;
            foreach (var filler in w.Fillers)
            {
                if (filler.Length < words.Count && words.Take(filler.Length).SequenceEqual(filler, StringComparer.Ordinal))
                {
                    words = words[filler.Length..];
                    changed = true;
                    break;
                }

                if (filler.Length < words.Count && words.Skip(words.Count - filler.Length).SequenceEqual(filler, StringComparer.Ordinal))
                {
                    words = words[..^filler.Length];
                    changed = true;
                    break;
                }
            }
        }
        return words;
    }

    private static Vocabulary Build()
    {
        var v = new Vocabulary();
        foreach (var language in Loc.Available())
            foreach (var (key, value) in Loc.Strings(language.Code, "assistant."))
            {
                SpokenReplyKind? kind = key switch
                {
                    "assistant.answer.yes" => SpokenReplyKind.Yes,
                    "assistant.answer.no" => SpokenReplyKind.No,
                    "assistant.answer.always" => SpokenReplyKind.Always,
                    _ => null,
                };
                if (kind is null && key != "assistant.rule.fillers") continue;
                foreach (var words in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(TextSimilarity.Words))
                {
                    if (words.Length == 0) continue;
                    if (kind is { } k) v.Phrases.Add((k, words));
                    else v.Fillers.Add(words);
                }
            }
        // Longest first; for the same length, «always» before the others.
        v.Phrases.Sort((a, b) => b.Words.Length != a.Words.Length ? b.Words.Length - a.Words.Length : (b.Kind == SpokenReplyKind.Always ? 1 : 0) - (a.Kind == SpokenReplyKind.Always ? 1 : 0));
        v.Fillers.Sort((a, b) => b.Length - a.Length);
        return v;
    }
}

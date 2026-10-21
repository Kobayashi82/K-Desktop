using System.Text.RegularExpressions;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Voice;

/// <summary>
/// The words the voice orders understand besides the command names, taken from the language files so every language
/// can teach its own (a new <c>fr.json</c> teaches French to the voice too). All installed languages count at the same
/// time: the user may speak another language than the one of the interface.
/// <list type="bullet">
/// <item><c>voice.words.filler</c>: words that carry no meaning in an order («la», «por favor», «please»);</item>
/// <item><c>voice.words.verbs</c>: generic verbs a command name may have and the order may leave out («abrir», «open»);</item>
/// <item><c>voice.words.doit</c>: words that only say «do it», cheap when said but not in the name («pon», «quiero»);</item>
/// <item><c>voice.words.joiners</c>: what joins several orders, separated by «|» («y luego|y|luego», «and then|and»);</item>
/// <item><c>voice.syn.&lt;concept&gt;</c>: forms of the same thing in each language («cierra cerrar», «close quit»).</item>
/// </list>
/// Lists are separated by spaces (joiners by «|»), lower case; accents do not matter.
/// </summary>
public sealed class VoiceVocabulary
{
    private static VoiceVocabulary? _current;
    private static readonly object Gate = new();

    static VoiceVocabulary() => Loc.Instance.Changed += () => { lock (Gate) _current = null; };

    /// <summary>The vocabulary of the languages installed now (rebuilt when the languages change).</summary>
    public static VoiceVocabulary Current
    {
        get
        {
            lock (Gate) return _current ??= Build();
        }
    }

    public HashSet<string> Filler { get; } = [];
    public HashSet<string> Verbs { get; } = [];
    public HashSet<string> DoIt { get; } = [];

    /// <summary>
    /// <c>voice.words.pronouns</c>: what stands for the thing of the order before («vacía la papelera y luego <b>la</b>
    /// abres», «open <b>it</b>»). Many are articles too: they only count next to a verb and nothing else.
    /// </summary>
    public HashSet<string> Pronouns { get; } = [];

    /// <summary><c>voice.words.enclitics</c>: the same, joined to the end of the verb («ábre<b>la</b>», «chiudi<b>lo</b>»).</summary>
    public HashSet<string> Enclitics { get; } = [];

    /// <summary><c>voice.words.negation</c>: an order that starts with one says what not to do («no, la anterior no»).</summary>
    public HashSet<string> Negation { get; } = [];

    /// <summary>
    /// Verbs of doing something to a thing: <see cref="DoIt"/> and the verbs of opening and closing of the assistants
    /// (<c>assistant.verbs.open</c>, <c>assistant.verbs.close</c>).
    /// </summary>
    public HashSet<string> Actions { get; } = [];

    /// <summary>Folded word → concept («cierra» → "close").</summary>
    public Dictionary<string, string> Synonyms { get; } = new(StringComparer.Ordinal);

    /// <summary>Splits a multiple order.</summary>
    public Regex Joiners { get; private set; } = new("$^");

    /// <summary>Joiners left at the start of a piece («, luego abre…»).</summary>
    public Regex LeadingJoiners { get; private set; } = new("$^");

    private static VoiceVocabulary Build()
    {
        var v = new VoiceVocabulary();
        var joiners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var language in Loc.Available())
        {
            foreach (var (key, value) in Loc.Strings(language.Code, "voice.words."))
            {
                switch (key)
                {
                    case "voice.words.filler": v.Filler.UnionWith(Words(value)); break;
                    case "voice.words.verbs": v.Verbs.UnionWith(Words(value)); break;
                    case "voice.words.doit": v.DoIt.UnionWith(Words(value)); break;
                    case "voice.words.pronouns": v.Pronouns.UnionWith(Words(value)); break;
                    case "voice.words.enclitics": v.Enclitics.UnionWith(Words(value)); break;
                    case "voice.words.negation": v.Negation.UnionWith(Words(value)); break;
                    case "voice.words.joiners":
                        foreach (var j in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            if (j.Length > 0) joiners.Add(j.ToLowerInvariant());
                        break;
                }
            }
            foreach (var (key, value) in Loc.Strings(language.Code, "voice.syn."))
            {
                var concept = key["voice.syn.".Length..];
                v.Synonyms.TryAdd(TextSimilarity.Words(concept).FirstOrDefault() ?? concept, concept);
                foreach (var word in Words(value)) v.Synonyms.TryAdd(word, concept);
            }
            foreach (var key in (string[])["assistant.verbs.open", "assistant.verbs.close"])
                if (Loc.Strings(language.Code, key).GetValueOrDefault(key) is { } verbs) v.Actions.UnionWith(Words(verbs));
        }
        // Verbs are also «do it» words.
        v.DoIt.UnionWith(v.Verbs);
        v.Actions.UnionWith(v.DoIt);

        if (joiners.Count > 0)
        {
            // Longest first, so «y luego» wins over «y»; spaces inside a joiner match any spacing.
            var alternatives = string.Join("|", joiners.OrderByDescending(j => j.Length).Select(j => Regex.Escape(j).Replace("\\ ", "\\s+")));
            const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            v.Joiners = new Regex($@"\s*[,;]\s*|\s+(?:{alternatives})\s+", options);
            v.LeadingJoiners = new Regex($@"^(?:(?:{alternatives})\s+)+", options);
        }
        return v;
    }

    /// <summary>Folded words of a list (lower case, no accents).</summary>
    private static IEnumerable<string> Words(string list) => TextSimilarity.Words(list);
}

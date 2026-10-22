using System.Globalization;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Brain.Talk;

/// <summary>
/// What a fixed rule understood. <see cref="First"/> and <see cref="Second"/> («el primero», «la otra») only count as the
/// answer to «¿A o B?» (stage 4.4). <see cref="Remember"/>, <see cref="Recall"/> and <see cref="Forget"/> are the memory
/// («recuerda que…», «¿qué sabes de mí?», «olvida que…»): a small model took them for notes.
/// </summary>
/// <summary><see cref="Act"/> and <see cref="OpenApp"/> (stage 8.1) only count after the user's own commands.</summary>
public enum QuickRuleKind { Time, Timer, Note, CancelTimer, Cancel, Repeat, Undo, First, Second, OpenWeb, Remember, Recall, Forget, Act, OpenApp, FreeBrain, LoadBrain, RepeatCommand, Dictate, WhatsPlaying, Weather, WeatherTomorrow, WeatherWeek, ThinkHard }

/// <summary>An order a fixed rule understood, with what filled its gaps.</summary>
/// <param name="Seconds">For a timer: how long.</param>
/// <param name="Text">What was said in its <c>{text}</c> or <c>{app}</c> gap, as it was said (the note, the reason of a timer, the program…).</param>
/// <param name="Op">For <see cref="QuickRuleKind.Act"/>: what to do (<c>minimize</c>…, <see cref="KDesktop.Core.Assistant.AssistantActs.Ops"/>).</param>
public sealed record QuickRule(QuickRuleKind Kind, double? Seconds = null, string? Text = null, string? Op = null);

/// <summary>
/// The fixed rules (stage 3.4, plan §4): the orders answered at once, without the AI — the time, a timer, a note,
/// «cancela», «repite», «deshaz». The whole order must fit one of the ways of saying it, in any installed language
/// (like K-Desktop's voice), after taking off the assistant's name, fillers («por favor», «oye») and a polite start
/// («¿puedes…?»). Anything else goes to the AI. The language files have:
/// <list type="bullet">
/// <item><c>assistant.rule.&lt;kind&gt;</c> (<c>time</c>, <c>timer</c>, <c>note</c>, <c>cancelTimer</c>, <c>cancel</c>,
/// <c>repeat</c>, <c>repeatCommand</c> (the last command it ran, again), <c>undo</c>, <c>openWeb</c> —only when its gap names web addresses—, <c>remember</c>, <c>recall</c>,
/// <c>forget</c>, <c>openApp</c>, <c>freeBrain</c> and <c>loadBrain</c> (the model out of the graphics card or into it),
/// <c>thinkHard</c> («piénsalo bien», with the order in its gap or alone for the one before), and <c>act.&lt;op&gt;</c> for each operation on a program —«minimiza {app}»—): ways of saying it, separated by «|», with gaps <c>{dur}</c> (a duration: «diez
/// minutos», «una hora y media») and <c>{text}</c> (some words, kept as said), and <c>[optional words]</c>;</item>
/// <item><c>assistant.rule.fillers</c>, <c>assistant.rule.polite</c>: what is taken off around it and at its start («|»);</item>
/// <item><c>assistant.rule.units</c>: <c>word=seconds</c> separated by spaces («minutos=60»);</item>
/// <item><c>assistant.rule.durations</c>: whole durations, <c>phrase=seconds</c> separated by «|» («media hora=1800»);</item>
/// <item><c>assistant.rule.half</c>: «y media» after a unit (half of it more).</item>
/// </list>
/// </summary>
public static partial class QuickRules
{
    private abstract record Piece;
    private sealed record Word(string Text) : Piece;
    private sealed record Optional(string[] Words) : Piece;
    private sealed record DurationGap : Piece;
    private sealed record TextGap : Piece;

    private sealed record Pattern(QuickRuleKind Kind, Piece[] Pieces, string? Op = null)
    {
        /// <summary>Words it names: the more, the more precise (it wins over a looser one that also fits).</summary>
        public int Weight { get; } = Pieces.Sum(p => p is Word ? 2 : p is Optional o ? o.Words.Length : 0);
    }

    private sealed class Vocabulary
    {
        public readonly List<Pattern> Patterns = [];
        public readonly List<string[]> Fillers = [];
        public readonly List<string[]> Polite = [];
        public readonly List<string[]> Half = [];
        public readonly Dictionary<string, double> Units = new(StringComparer.Ordinal);
        public readonly List<(string[] Words, double Seconds)> Durations = [];
    }

    /// <summary>A word of what was said: folded, and where it is in the original text.</summary>
    private readonly record struct Token(string Folded, int Start, int End);

    /// <summary>Words a program's name may have in an <c>{app}</c> gap at most («Microsoft Flight Simulator 2020»).</summary>
    public const int MaxAppWords = 4;

    private static Vocabulary? _current;
    private static readonly object Gate = new();

    static QuickRules() => Loc.Instance.Changed += () => { lock (Gate) _current = null; };

    private static Vocabulary Words
    {
        get
        {
            lock (Gate) return _current ??= Build();
        }
    }

    /// <summary>
    /// The rule <paramref name="text"/> fits, or null (it goes to the AI). <paramref name="names"/>: what the assistant is
    /// called, taken off the start («Lander, ¿qué hora es?»).
    /// </summary>
    public static QuickRule? Match(string text, IEnumerable<string>? names = null)
    {
        var w = Words;
        // A word said twice in a row is one («qué tienes en en tu memoria»: Parakeet's stutter).
        var tokens = Tokenize(text);
        for (var i = tokens.Count - 1; i > 0; i--)
            if (tokens[i].Folded == tokens[i - 1].Folded) tokens.RemoveAt(i);
        var nameWords = (names ?? []).Select(TextSimilarity.Words).Where(n => n.Length > 0).ToList();
        var bare = Trim(tokens, w, nameWords);
        if (bare.Count == 0) return null;

        // As said, and without its polite start («puedes poner un temporizador…»).
        var tries = new List<List<Token>> { bare };
        foreach (var polite in w.Polite)
            if (StartsWith(bare, polite, 0)) tries.Add(Trim(bare[polite.Length..], w, nameWords));

        (Pattern Pattern, QuickRule Rule)? best = null;
        foreach (var said in tries.Where(t => t.Count > 0))
            foreach (var pattern in w.Patterns)
            {
                if (best is { } b && b.Pattern.Weight >= pattern.Weight) continue;
                var gaps = new Gaps();
                if (!Fits(said, 0, pattern.Pieces, 0, w, gaps)) continue;
                var gap = gaps.Text is { } t ? Original(text, t.From, t.To) : null;
                // «Abre …» is a web only when it names addresses («abre youtube.com»); «abre Steam» is not this rule.
                // And nothing else: «abre trackseries.tv y hazle una foto y dime qué me falta» opened it, said «Ya está» and
                // dropped the rest.
                if (pattern.Kind == QuickRuleKind.OpenWeb && (Sites(gap ?? string.Empty).Count == 0 || !OnlySites(gap!))) continue;
                // A program is named in a few words: «No has abierto equipo. Tenías que mover la ventana… a la derecha» is
                // not «mueve {app} a la derecha».
                // A place too: «¿qué tiempo hace en Bilbao y pon un temporizador…?» is not the weather of all that.
                if (pattern.Kind is QuickRuleKind.Act or QuickRuleKind.OpenApp or QuickRuleKind.Weather or QuickRuleKind.WeatherTomorrow or QuickRuleKind.WeatherWeek
                    && gaps.Text is { } g && said.IndexOf(g.To) - said.IndexOf(g.From) + 1 > MaxAppWords) continue;
                best = (pattern, new QuickRule(pattern.Kind, gaps.Seconds, gap, pattern.Op));
            }
        return best?.Rule;
    }

    /// <summary>
    /// Whether <paramref name="text"/> says <paramref name="phrase"/> and nothing else (without the assistant's name, the
    /// fillers or a polite start; a letter off in a long word): a phrase of a JSON tool («buenas noches»), stage 9.1.
    /// </summary>
    public static bool SaysPhrase(string text, string phrase, IEnumerable<string>? names = null)
    {
        var w = Words;
        var nameWords = (names ?? []).Select(TextSimilarity.Words).Where(n => n.Length > 0).ToList();
        var bare = Trim(Tokenize(text), w, nameWords);
        var wanted = Trim(Tokenize(phrase), w, []).Select(t => t.Folded).ToArray();
        if (bare.Count == 0 || wanted.Length == 0) return false;
        if (bare.Count == wanted.Length && StartsWith(bare, wanted, 0)) return true;
        foreach (var polite in w.Polite)
            if (StartsWith(bare, polite, 0) && Trim(bare[polite.Length..], w, nameWords) is { } rest && rest.Count == wanted.Length && StartsWith(rest, wanted, 0))
                return true;
        return false;
    }

    /// <summary>
    /// The web addresses in what was said, in order and without repeats: «youPorn.com y pornhub.com», «github punto com»
    /// (the word for «dot» of <c>assistant.rule.dot</c>, any language). Not files («steam.exe»).
    /// </summary>
    public static IReadOnlyList<string> Sites(string text)
    {
        foreach (var dot in Dots.Value) text = text.Replace(dot, ".", StringComparison.OrdinalIgnoreCase);
        return [.. Address().Matches(text).Select(m => m.Value.TrimEnd('.', '/'))
            .Where(s => !NotSites.Contains(s[(s.LastIndexOf('.') + 1)..].Split('/')[0]))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Whether <paramref name="text"/> is only web addresses, with fillers and «y» between them («youtube.com y la web de
    /// github.com»): anything else is more of the order, for the AI.
    /// </summary>
    public static bool OnlySites(string text)
    {
        foreach (var dot in Dots.Value) text = text.Replace(dot, ".", StringComparison.OrdinalIgnoreCase);
        var rest = Address().Replace(text, " ");
        var filler = VoiceVocabulary.Current.Filler;
        return TextSimilarity.Words(rest).All(w => filler.Contains(w) || SiteWords.Contains(w));
    }

    /// <summary>Words for «a website», in the languages of K-Desktop.</summary>
    private static readonly HashSet<string> SiteWords = new(StringComparer.Ordinal) { "web", "pagina", "paginas", "site", "sitio", "seite", "sito", "website", "webseite", "page" };

    private static readonly Lazy<string[]> Dots = new(() =>
    [
        .. Loc.Available()
            .Select(l => Loc.Strings(l.Code, "assistant.rule.dot").GetValueOrDefault("assistant.rule.dot") ?? string.Empty)
            .SelectMany(v => v.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(d => " " + d + " ")
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ]);

    /// <summary>Endings of files, not of web addresses.</summary>
    private static readonly HashSet<string> NotSites = new(StringComparer.OrdinalIgnoreCase)
    {
        "exe", "lnk", "bat", "cmd", "msi", "dll", "txt", "log", "ini", "json", "xml", "pdf", "doc", "docx", "xls", "xlsx",
        "ppt", "pptx", "png", "jpg", "jpeg", "gif", "webp", "mp3", "mp4", "mkv", "avi", "wav", "zip", "rar",
    };

    [System.Text.RegularExpressions.GeneratedRegex(@"(?:https?://)?(?:[\p{L}\p{N}-]+\.)+\p{L}{2,}(?:/[^\s,;]*)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex Address();

    private sealed class Gaps
    {
        public double? Seconds;
        public (Token From, Token To)? Text;
    }

    /// <summary>Whether <paramref name="said"/> from <paramref name="i"/> fits the pieces from <paramref name="j"/> to the end.</summary>
    private static bool Fits(List<Token> said, int i, Piece[] pieces, int j, Vocabulary w, Gaps gaps)
    {
        if (j == pieces.Length) return i == said.Count;
        switch (pieces[j])
        {
            case Word word:
                return i < said.Count && Same(said[i].Folded, word.Text) && Fits(said, i + 1, pieces, j + 1, w, gaps);
            case Optional optional:
                return (StartsWith(said, optional.Words, i) && Fits(said, i + optional.Words.Length, pieces, j + 1, w, gaps))
                    || Fits(said, i, pieces, j + 1, w, gaps);
            case DurationGap:
                if (ReadDuration(said, i, w) is not { } duration) return false;
                gaps.Seconds = duration.Seconds;
                return Fits(said, duration.End, pieces, j + 1, w, gaps);
            case TextGap:
                // As few words as let the rest fit («recuérdame que {text} en {dur}»), at least one.
                for (var end = i + 1; end <= said.Count; end++)
                    if (Fits(said, end, pieces, j + 1, w, gaps))
                    {
                        gaps.Text = (said[i], said[end - 1]);
                        return true;
                    }
                return false;
            default:
                return false;
        }
    }

    /// <summary>«diez minutos», «una hora y media», «una hora y diez minutos», «media hora».</summary>
    private static (double Seconds, int End)? ReadDuration(List<Token> said, int i, Vocabulary w)
    {
        var words = said.Select(t => t.Folded).ToList();
        double total = 0;
        var end = i;
        while (true)
        {
            var at = end;
            double part = 0;
            if (w.Durations.FirstOrDefault(d => StartsWith(said, d.Words, at)) is { Words: not null } whole)
            {
                part = whole.Seconds;
                at += whole.Words.Length;
            }
            else if (SpokenNumbers.TryRead(words, at, out var number, out var afterNumber) && afterNumber < words.Count
                && Unit(words[afterNumber], w) is { } unit)
            {
                part = number * unit;
                at = afterNumber + 1;
                if (w.Half.FirstOrDefault(h => StartsWith(said, h, at)) is { } half)
                {
                    part += unit / 2;
                    at += half.Length;
                }
            }
            if (part <= 0) break;
            total += part;
            end = at;
            // «una hora y diez minutos»: one more after «y».
            if (end < words.Count && SpokenNumbers.IsAnd(words[end]) && ReadDuration(said, end + 1, w) is { } more)
            {
                total += more.Seconds;
                end = more.End;
            }
            break;
        }
        return total > 0 ? (total, end) : null;
    }

    private static double? Unit(string word, Vocabulary w) =>
        w.Units.TryGetValue(word, out var seconds) ? seconds
        : w.Units.Where(u => u.Key.Length >= 5 && Same(word, u.Key)).Select(u => (double?)u.Value).FirstOrDefault();

    /// <summary>Equal, or a letter off in a long word (what speech recognition gets wrong).</summary>
    private static bool Same(string said, string written) =>
        said == written || (written.Length >= 5 && Math.Abs(said.Length - written.Length) <= 1 && TextSimilarity.Levenshtein(said, written) <= 1);

    private static bool StartsWith(List<Token> said, string[] words, int at)
    {
        if (at + words.Length > said.Count) return false;
        for (var k = 0; k < words.Length; k++)
            if (!Same(said[at + k].Folded, words[k])) return false;
        return true;
    }

    /// <summary>Without the assistant's name and the fillers at either end.</summary>
    private static List<Token> Trim(List<Token> tokens, Vocabulary w, List<string[]> names)
    {
        var list = tokens.ToList();
        var changed = true;
        while (changed && list.Count > 0)
        {
            changed = false;
            foreach (var cut in names.Concat(w.Fillers))
            {
                if (StartsWith(list, cut, 0) && cut.Length < list.Count)
                {
                    list.RemoveRange(0, cut.Length);
                    changed = true;
                }
                if (list.Count > cut.Length && StartsWith(list, cut, list.Count - cut.Length))
                {
                    list.RemoveRange(list.Count - cut.Length, cut.Length);
                    changed = true;
                }
            }
        }
        return list;
    }

    /// <summary>The words of the text, folded like <see cref="TextSimilarity.Words"/>, with where each one is.</summary>
    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var start = -1;
        var folded = new System.Text.StringBuilder();
        for (var k = 0; k <= text.Length; k++)
        {
            if (k < text.Length && char.IsLetterOrDigit(text[k]))
            {
                if (start < 0) start = k;
                folded.Append(FuzzyMatch.Fold(text[k]));
            }
            else if (start >= 0)
            {
                tokens.Add(new Token(folded.ToString(), start, k));
                folded.Clear();
                start = -1;
            }
        }
        return tokens;
    }

    /// <summary>What was said from one word to another, as it was written.</summary>
    private static string Original(string text, Token from, Token to) => text[from.Start..to.End].Trim();

    private static Vocabulary Build()
    {
        var v = new Vocabulary();
        foreach (var language in Loc.Available())
            foreach (var (key, value) in Loc.Strings(language.Code, "assistant.rule."))
            {
                var name = key["assistant.rule.".Length..];
                switch (name)
                {
                    case "fillers": v.Fillers.AddRange(List(value)); break;
                    case "polite": v.Polite.AddRange(List(value)); break;
                    case "half": v.Half.AddRange(List(value)); break;
                    case "units":
                        foreach (var (words, seconds) in Pairs(value.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
                            foreach (var word in words) v.Units.TryAdd(word, seconds);
                        break;
                    case "durations":
                        v.Durations.AddRange(Pairs(value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
                        break;
                    case var act when act.StartsWith("act.", StringComparison.Ordinal):
                        foreach (var phrase in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            if (Parse(phrase) is { Length: > 0 } pieces) v.Patterns.Add(new Pattern(QuickRuleKind.Act, pieces, act["act.".Length..]));
                        break;
                    default:
                        if (!Enum.TryParse<QuickRuleKind>(name, ignoreCase: true, out var kind)) break;
                        foreach (var phrase in value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            if (Parse(phrase) is { Length: > 0 } pieces) v.Patterns.Add(new Pattern(kind, pieces));
                        break;
                }
            }
        // Longest first: «un cuarto de hora» before «cuarto de hora», «por favor» before «favor».
        foreach (var list in new[] { v.Fillers, v.Polite, v.Half }) list.Sort((a, b) => b.Length - a.Length);
        v.Durations.Sort((a, b) => b.Words.Length - a.Words.Length);
        return v;
    }

    private static IEnumerable<string[]> List(string value) =>
        value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(TextSimilarity.Words).Where(w => w.Length > 0);

    private static IEnumerable<(string[] Words, double Seconds)> Pairs(IEnumerable<string> items)
    {
        foreach (var item in items)
        {
            var equals = item.LastIndexOf('=');
            if (equals <= 0 || !double.TryParse(item[(equals + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)) continue;
            if (TextSimilarity.Words(item[..equals]) is { Length: > 0 } words) yield return (words, seconds);
        }
    }

    /// <summary>«avísame en {dur} [para] {text}» → its pieces.</summary>
    private static Piece[] Parse(string phrase)
    {
        var pieces = new List<Piece>();
        var k = 0;
        while (k < phrase.Length)
        {
            var c = phrase[k];
            if (c is '{' or '[')
            {
                var close = phrase.IndexOf(c == '{' ? '}' : ']', k);
                if (close < 0) return [];
                var inside = phrase[(k + 1)..close];
                if (c == '[')
                {
                    if (TextSimilarity.Words(inside) is { Length: > 0 } words) pieces.Add(new Optional(words));
                }
                else if (inside == "dur") pieces.Add(new DurationGap());
                else if (inside is "text" or "app") pieces.Add(new TextGap());
                else return [];
                k = close + 1;
                continue;
            }
            var next = phrase.IndexOfAny(['{', '['], k);
            if (next < 0) next = phrase.Length;
            pieces.AddRange(TextSimilarity.Words(phrase[k..next]).Select(word => new Word(word)));
            k = next;
        }
        return [.. pieces];
    }
}

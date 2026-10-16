using System.Globalization;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Voice;

public enum MediaOrderKind
{
    Pause, Resume, Next, Previous,
    VolumeUp, VolumeDown, VolumeSet, Mute, Unmute,
    MusicOn, MusicOff,
    /// <summary>Cancels what is counting down or waiting (like Escape); always on, even without the media orders.</summary>
    Cancel,
}

/// <summary>How much «sube el volumen» should move it when no number was said.</summary>
public enum MediaAmount { Normal, Little, Lot }

/// <summary>
/// A built-in voice order for the music and the volume (Settings › Multimedia): no command is needed for it.
/// </summary>
/// <param name="Value">VolumeSet: the level; VolumeUp/Down: the step said («sube el volumen diez»), else null.</param>
public sealed record MediaOrder(MediaOrderKind Kind, int? Value = null, MediaAmount Amount = MediaAmount.Normal)
{
    /// <summary>What the order does, for the OSD and the log («Volumen al 30 %»).</summary>
    public string Name => Kind switch
    {
        MediaOrderKind.VolumeSet => Loc.T("voice.media.name.VolumeSet", Value ?? 0),
        MediaOrderKind.VolumeUp or MediaOrderKind.VolumeDown when Value is { } step => Loc.T($"voice.media.name.{Kind}By", step),
        _ => Loc.T($"voice.media.name.{Kind}"),
    };
}

/// <param name="Phrase">The phrase of the order that fitted best (for the log).</param>
public sealed record MediaCandidate(MediaOrder Order, double Score, string Phrase);

/// <summary>
/// Spoken text → media order. The phrases of each order, the numbers and a few helper words come from the language
/// files (all installed languages at once, like <see cref="VoiceVocabulary"/>):
/// <list type="bullet">
/// <item><c>voice.media.&lt;Kind&gt;</c>: ways of saying it, separated by «|» («pausa|para la música|stop»);</item>
/// <item><c>voice.numbers</c>: number words, <c>word=value</c> separated by spaces; compounds of them are understood
/// («treinta y cinco», «fünfunddreißig», «trentacinque», «quatre-vingt»);</item>
/// <item><c>voice.numbers.levels</c>: words that name a level («máximo=100 mitad=50»);</item>
/// <item><c>voice.numbers.and</c>, <c>voice.numbers.percent</c>: what joins number words, and «por ciento» («|» list);</item>
/// <item><c>voice.media.to</c>: «a», «al»… before a number: a level, not a step («sube el volumen a 80»);</item>
/// <item><c>voice.media.little</c>, <c>voice.media.lot</c>: «un poco», «mucho» («|» lists);</item>
/// <item><c>voice.media.keep</c>: filler words that mean something here («para» in «para la música»);</item>
/// <item><c>voice.media.things</c>: what can be paused or skipped («música vídeo película serie»): any of them stands
/// for the others in pause, resume, next and previous — the media keys work the same for all.</item>
/// </list>
/// </summary>
public static class MediaOrders
{
    private sealed class Vocabulary
    {
        public readonly Dictionary<MediaOrderKind, List<string[]>> Phrases = [];
        public readonly Dictionary<string, int> Numbers = new(StringComparer.Ordinal);
        public readonly HashSet<string> Levels = new(StringComparer.Ordinal);
        public readonly HashSet<string> And = new(StringComparer.Ordinal);
        public readonly HashSet<string> To = new(StringComparer.Ordinal);
        public readonly HashSet<string> Keep = new(StringComparer.Ordinal);
        public readonly HashSet<string> Things = new(StringComparer.Ordinal);
        public readonly List<string[]> Percent = [];
        public readonly List<string[]> Little = [];
        public readonly List<string[]> Lot = [];
    }

    private static Vocabulary? _current;
    private static readonly object Gate = new();

    static MediaOrders() => Loc.Instance.Changed += () => { lock (Gate) _current = null; };

    private static Vocabulary Words
    {
        get
        {
            lock (Gate) return _current ??= Build();
        }
    }

    private static Vocabulary Build()
    {
        var v = new Vocabulary();
        foreach (var kind in Enum.GetValues<MediaOrderKind>()) v.Phrases[kind] = [];
        foreach (var language in Loc.Available())
        {
            foreach (var (key, value) in Loc.Strings(language.Code, "voice.media."))
            {
                var name = key["voice.media.".Length..];
                if (Enum.TryParse<MediaOrderKind>(name, out var kind)) v.Phrases[kind].AddRange(Phrases(value));
                else if (name == "to") v.To.UnionWith(TextSimilarity.Words(value));
                else if (name == "keep") v.Keep.UnionWith(TextSimilarity.Words(value));
                else if (name == "things") v.Things.UnionWith(TextSimilarity.Words(value));
                else if (name == "little") v.Little.AddRange(Phrases(value));
                else if (name == "lot") v.Lot.AddRange(Phrases(value));
            }
            foreach (var (key, value) in Loc.Strings(language.Code, "voice.numbers"))
            {
                switch (key)
                {
                    case "voice.numbers": AddNumbers(v, value, level: false); break;
                    case "voice.numbers.levels": AddNumbers(v, value, level: true); break;
                    case "voice.numbers.and": v.And.UnionWith(TextSimilarity.Words(value)); break;
                    case "voice.numbers.percent": v.Percent.AddRange(Phrases(value)); break;
                }
            }
        }
        // Longest first: «un poco más» before «un poco».
        foreach (var list in v.Phrases.Values.Append(v.Percent).Append(v.Little).Append(v.Lot)) list.Sort((a, b) => b.Length - a.Length);
        return v;
    }

    private static IEnumerable<string[]> Phrases(string list) =>
        list.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(TextSimilarity.Words)
            .Where(w => w.Length > 0);

    private static void AddNumbers(Vocabulary v, string list, bool level)
    {
        foreach (var pair in list.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.LastIndexOf('=');
            if (equals <= 0 || !int.TryParse(pair[(equals + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) continue;
            foreach (var word in TextSimilarity.Words(pair[..equals]))
            {
                v.Numbers.TryAdd(word, number);
                if (level) v.Levels.Add(word);
            }
        }
    }

    /// <summary>The media orders that fit <paramref name="text"/>, best first (at most one per kind).</summary>
    public static List<MediaCandidate> Match(string text)
    {
        var w = Words;
        var said = TextSimilarity.Words(text);
        if (said.Length == 0) return [];

        // The amount and the number only matter to the volume («siete» in «siete la música» is a slip, not a level).
        var words = said.ToList();
        var amount = MediaAmount.Normal;
        if (RemovePhrase(words, w.Little)) amount = MediaAmount.Little;
        else if (RemovePhrase(words, w.Lot)) amount = MediaAmount.Lot;
        var number = TakeNumber(words, w, out var absolute);

        var filler = VoiceVocabulary.Current.Filler;
        string[] Content(IEnumerable<string> list)
        {
            var all = list.ToArray();
            var content = all.Where(x => !filler.Contains(x) || w.Keep.Contains(x)).ToArray();
            return content.Length > 0 ? content : all;
        }
        // «para la película», «siguiente capítulo»: one word for all of them.
        const string Thing = "#thing"; // spoken words never have a #
        string[] Things(string[] list) => list.Select(x => w.Things.Contains(x) ? Thing : x).ToArray();
        var forVolume = Content(words);
        var plain = Content(said);
        var plainThings = Things(plain);

        var result = new List<MediaCandidate>();
        foreach (var (kind, phrases) in w.Phrases)
        {
            if (phrases.Count == 0 || (kind == MediaOrderKind.VolumeSet && number is null)) continue;
            var volume = kind is MediaOrderKind.VolumeUp or MediaOrderKind.VolumeDown or MediaOrderKind.VolumeSet or MediaOrderKind.Mute or MediaOrderKind.Unmute;
            var things = kind is MediaOrderKind.Pause or MediaOrderKind.Resume or MediaOrderKind.Next or MediaOrderKind.Previous;
            var spoken = volume ? forVolume : things ? plainThings : plain;
            if (spoken.Length == 0) continue;
            double PhraseScore(string[] p)
            {
                var written = things ? Things(Content(p)) : Content(p);
                // Both name the thing («para la película» / «para la música»): the verb alone decides, or «siguiente
                // capítulo» would come close to «sigue con la música».
                if (things && spoken.Contains(Thing) && written.Contains(Thing) && spoken.Any(x => x != Thing) && written.Any(x => x != Thing))
                    return IntentMatcher.Score(spoken.Where(x => x != Thing).ToArray(), written.Where(x => x != Thing).ToArray(), synonyms: false);
                return IntentMatcher.Score(spoken, written, synonyms: false);
            }
            var (best, phrase) = phrases.Select(p => (Score: PhraseScore(p), Phrase: p)).MaxBy(p => p.Score);
            if (best <= 0.3) continue;
            var order = kind switch
            {
                MediaOrderKind.VolumeSet => new MediaOrder(kind, Math.Clamp(number!.Value, 0, 100)),
                // «sube el volumen al 80», «baja el volumen a la mitad»: a level.
                MediaOrderKind.VolumeUp or MediaOrderKind.VolumeDown when number is { } n && absolute => new MediaOrder(MediaOrderKind.VolumeSet, Math.Clamp(n, 0, 100)),
                MediaOrderKind.VolumeUp or MediaOrderKind.VolumeDown => new MediaOrder(kind, number is { } step ? Math.Clamp(step, 1, 100) : null, amount),
                _ => new MediaOrder(kind),
            };
            result.Add(new MediaCandidate(order, best, string.Join(' ', phrase)));
        }
        // «sube el volumen al 80» and «volumen al 80» are the same order: once, with its best score.
        return result.GroupBy(c => c.Order.Kind).Select(g => g.MaxBy(c => c.Score)!).OrderByDescending(c => c.Score).ToList();
    }

    /// <summary>Removes the first of <paramref name="phrases"/> found in <paramref name="words"/>.</summary>
    private static bool RemovePhrase(List<string> words, List<string[]> phrases)
    {
        foreach (var phrase in phrases)
        {
            var at = IndexOf(words, phrase, 0);
            if (at < 0) continue;
            words.RemoveRange(at, phrase.Length);
            return true;
        }
        return false;
    }

    private static int IndexOf(List<string> words, string[] phrase, int from)
    {
        for (var i = from; i + phrase.Length <= words.Count; i++)
        {
            var j = 0;
            while (j < phrase.Length && words[i + j] == phrase[j]) j++;
            if (j == phrase.Length) return i;
        }
        return -1;
    }

    /// <summary>
    /// Finds the first number said (digits or words), removes it with its «por ciento» and the «a»/«al» before it, and
    /// returns it. <paramref name="absolute"/>: a level («al 80», «al máximo») rather than an amount («diez más»).
    /// </summary>
    private static int? TakeNumber(List<string> words, Vocabulary w, out bool absolute)
    {
        absolute = false;
        for (var start = 0; start < words.Count; start++)
        {
            if (!TryWord(words[start], w, out var first, out var level)) continue;
            var values = new List<int> { first };
            var end = start + 1;
            while (end < words.Count)
            {
                if (TryWord(words[end], w, out var next, out var l)) { values.Add(next); level |= l; end++; }
                else if (w.And.Contains(words[end]) && end + 1 < words.Count && TryWord(words[end + 1], w, out next, out l))
                {
                    values.Add(next);
                    level |= l;
                    end += 2;
                }
                else break;
            }
            // «por ciento» after it.
            foreach (var p in w.Percent)
                if (IndexOf(words, p, end) == end)
                {
                    end += p.Length;
                    break;
                }
            absolute = level || (start > 0 && w.To.Contains(words[start - 1]));
            if (start > 0 && w.To.Contains(words[start - 1])) start--;
            words.RemoveRange(start, end - start);
            return Combine(values);
        }
        return null;
    }

    /// <summary>«treinta» «cinco» → 35; «one» «hundred» → 100; «quatre» «vingt» «dix» → 90.</summary>
    private static int Combine(List<int> values)
    {
        var total = 0;
        foreach (var v in values)
        {
            if (total is >= 1 and <= 9 && (v == 100 || (v == 20 && total == 4))) total *= v;
            else total += v;
        }
        return total;
    }

    /// <summary>A number word, digits, or a word made of number words («fünfunddreißig», «trentacinque»).</summary>
    private static bool TryWord(string word, Vocabulary w, out int value, out bool level)
    {
        level = w.Levels.Contains(word);
        if (w.Numbers.TryGetValue(word, out value)) return true;
        if (word.All(char.IsAsciiDigit) && word.Length <= 3) return int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        var parts = Split(word, w);
        if (parts is not { Count: >= 2 }) return false;
        value = Combine(parts);
        return true;
    }

    /// <summary>The number words a compound is made of (joiners of three letters or more may sit between them).</summary>
    private static List<int>? Split(string word, Vocabulary w)
    {
        if (word.Length < 5) return null;
        for (var cut = word.Length - 2; cut >= 2; cut--)
        {
            var head = word[..cut];
            if (!w.Numbers.TryGetValue(head, out var value) || w.Levels.Contains(head)) continue;
            var rest = word[cut..];
            if (w.Numbers.TryGetValue(rest, out var last) && !w.Levels.Contains(rest)) return [value, last];
            foreach (var joiner in w.And.Where(j => j.Length >= 3 && rest.StartsWith(j, StringComparison.Ordinal)))
                if (w.Numbers.TryGetValue(rest[joiner.Length..], out last)) return [value, last];
            if (Split(rest, w) is { } more) return [value, .. more];
        }
        return null;
    }
}

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
/// <item>the numbers, with <see cref="SpokenNumbers"/>, and <c>voice.numbers.percent</c>: «por ciento» («|» list);</item>
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
            foreach (var (key, value) in Loc.Strings(language.Code, "voice.numbers.percent"))
                if (key == "voice.numbers.percent") v.Percent.AddRange(Phrases(value));
        }
        // Longest first: «un poco más» before «un poco».
        foreach (var list in v.Phrases.Values.Append(v.Percent).Append(v.Little).Append(v.Lot)) list.Sort((a, b) => b.Length - a.Length);
        return v;
    }

    private static IEnumerable<string[]> Phrases(string list) =>
        list.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(TextSimilarity.Words)
            .Where(w => w.Length > 0);

    /// <summary>The ways of saying each order in one language, as written in its file (for the meaning layer).</summary>
    public static Dictionary<MediaOrderKind, List<string>> Sentences(string language)
    {
        var result = new Dictionary<MediaOrderKind, List<string>>();
        foreach (var (key, value) in Loc.Strings(language, "voice.media."))
            if (Enum.TryParse<MediaOrderKind>(key["voice.media.".Length..], out var kind))
                result[kind] = value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        return result;
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
            if (!SpokenNumbers.TryRead(words, start, out var number, out var end, out var level)) continue;
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
            return number;
        }
        return null;
    }
}

using System.Globalization;
using System.Text;

namespace KDesktop.Assistant.Voice.Kokoro;

/// <summary>
/// Spanish text to the phonemes Kokoro learnt Spanish with, by rules of our own (no espeak-ng, which is GPL): Kokoro's
/// Spanish voices were trained on espeak-ng's «es» (Castilian) through misaki, so these rules write what it writes,
/// checked against it word by word (<c>SpanishPhonemesTests</c>):
/// <list type="bullet">
/// <item>the stress mark goes right before the stressed vowel («kˈasa»); long words take a secondary one on every other
/// syllable from the first up to two before the stressed one («tˌempoɾˌiθaðˈoɾ»); function words have none («de», «la»,
/// «que») or a secondary one («pˌaɾa», «kwˌando»);</item>
/// <item>b, d, g are stops after a pause or a nasal, before a nasal and at the end of a word, and β, ð, ɣ elsewhere, also
/// across words («la βwˈelta»); n becomes m before p, b, m (also across words: «ˈum pˈunto») and ŋ before g and j;</item>
/// <item>falling diphthongs as misaki writes espeak's tied ones: «au» W, «ai» I, «ei» A; the stressed e before an n that
/// closes its syllable is ɛ («tˈɛŋɡo»); c/z θ, j x, ll ʎ, y ʝ, ch ʧ, ñ ɲ, r and rr as espeak; after r and l, «ue» is two
/// syllables («ruˈeða»); words without a vowel are spelled («ctrl»).</item>
/// </list>
/// Numbers and symbols are written out first (<see cref="SpanishNumbers"/>).
/// </summary>
public static class SpanishPhonemes
{
    /// <summary>Words said without stress.</summary>
    private static readonly HashSet<string> Unstressed = new(StringComparer.Ordinal)
    {
        "a", "al", "de", "del", "el", "la", "lo", "los", "las", "le", "les", "me", "te", "se", "nos", "os", "mi", "mis", "tu",
        "tus", "su", "sus", "en", "con", "por", "sin", "que", "y", "e", "o", "u", "si", "tan", "tras",
        "nuestro", "nuestra", "nuestros", "nuestras", "vuestro", "vuestra", "vuestros", "vuestras", "cuyo", "cuya", "cuyos", "cuyas",
    };

    /// <summary>Words said with a secondary stress only.</summary>
    private static readonly HashSet<string> Secondary = new(StringComparer.Ordinal)
    {
        "para", "cuando", "como", "desde", "sobre", "aunque", "mientras", "donde", "hasta", "entre", "hacia", "según",
        "pues", "adonde", "pero", "porque", "bajo",
    };

    /// <summary>The names of the letters, for words without a vowel («ctrl»); «*» marks the stressed vowel.</summary>
    private static readonly Dictionary<char, string> Letters = new()
    {
        ['b'] = "b*e", ['c'] = "θ*e", ['d'] = "d*e", ['f'] = "*efe", ['g'] = "x*e", ['h'] = "*aʧe", ['j'] = "x*ota", ['k'] = "k*a",
        ['l'] = "*ɛle", ['m'] = "*eme", ['n'] = "*ɛne", ['ñ'] = "*eɲe", ['p'] = "p*e", ['q'] = "k*u", ['r'] = "*ere", ['s'] = "*ɛse",
        ['t'] = "t*e", ['v'] = "*uβe", ['w'] = "*uβeðoβle", ['x'] = "*ɛkis", ['y'] = "ʝ*e", ['z'] = "θ*ɛta",
    };

    /// <summary>The phonemes of a text, with its punctuation (as misaki's EspeakG2P gives them for «es»).</summary>
    public static string ToPhonemes(string text)
    {
        var tokens = Tokenize(SpanishNumbers.Expand(text));
        // A function word before a pause is stressed («conectado ˈa», «acerca ðˈe»).
        var words = tokens.Select((t, k) => t.Word is { } w ? Word(w, k + 1 >= tokens.Count || tokens[k + 1].Punctuation is not (null or "-")) : null).ToList();
        var output = new StringBuilder();
        Phone? before = null;
        for (var t = 0; t < tokens.Count; t++)
        {
            if (tokens[t].Punctuation == "-")
            {
                // «K-Desktop»: the two words together, as espeak says them.
                while (output.Length > 0 && output[^1] == ' ') output.Length--;
                continue;
            }
            if (tokens[t].Punctuation is { } mark)
            {
                // Punctuation sticks to the word before it, with a space after; «¿», «¡», quotes: only a pause.
                if (mark.Length > 0)
                {
                    while (output.Length > 0 && output[^1] == ' ') output.Length--;
                    output.Append(mark).Append(' ');
                }
                before = null;
                continue;
            }
            var phones = words[t]!;
            if (phones.Count == 0) continue;
            // The first sound of the next word, for the n that assimilates across words.
            var after = t + 1 < tokens.Count && words[t + 1] is { Count: > 0 } next ? next.FirstOrDefault(p => !p.IsMark) : null;
            Write(output, phones, before, after);
            output.Append(' ');
            before = phones.LastOrDefault(p => !p.IsMark);
        }
        return output.ToString().Trim();
    }

    // ── Words ────────────────────────────────────────────────────────────────

    /// <summary>A sound of a word, or a stress mark (<see cref="IsMark"/>).</summary>
    private sealed record Phone(string Text, PhoneKind Kind)
    {
        public bool IsMark => Kind == PhoneKind.Mark;
    }

    private enum PhoneKind
    {
        Vowel,
        Consonant,

        /// <summary>b, d or g: a stop or its fricative by what comes around it.</summary>
        Soft,

        /// <summary>A soft one that is a stop whatever comes before (before a nasal).</summary>
        Stop,

        /// <summary>A soft one that ends its word: a stop unless the next word starts with a vowel.</summary>
        Final,

        Nasal,
        Mark,
    }

    /// <summary>A letter of the word once read: a vowel (strong a e o, or weak i u; accented or not) or a consonant sound.</summary>
    private sealed class Unit(string sound, bool vowel, bool weak = false, bool accented = false, PhoneKind kind = PhoneKind.Consonant)
    {
        public string Sound { get; } = sound;
        public bool IsVowel { get; } = vowel;
        public bool Weak { get; } = weak;
        public bool Accented { get; } = accented;
        public PhoneKind Kind { get; set; } = kind;

        /// <summary>A y said as a vowel («muy», «hay»).</summary>
        public bool FromY { get; init; }

        /// <summary>A glide after its nucleus («ai»: the i).</summary>
        public bool Offglide { get; set; }
    }

    private static readonly Dictionary<string, List<Phone>> Cache = new(StringComparer.Ordinal);
    private static readonly object CacheGate = new();

    private static List<Phone> Word(string word, bool beforePause)
    {
        var key = beforePause && Unstressed.Contains(word) && word is not ("y" or "e" or "o" or "u") ? word + "|" : word;
        lock (CacheGate)
        {
            if (Cache.TryGetValue(key, out var known)) return known;
        }
        var phones = Build(word, key != word);
        lock (CacheGate)
        {
            if (Cache.Count > 5000) Cache.Clear();
            Cache[key] = phones;
        }
        return phones;
    }

    private static List<Phone> Build(string word, bool stress = false)
    {
        // «exactamente»: the adjective with its own stress, and «mˈente».
        if (word.Length > 7 && word.EndsWith("mente", StringComparison.Ordinal) && Read(word[..^5]).Any(u => u.IsVowel))
            return [.. Build(word[..^5]), new Phone("m", PhoneKind.Nasal), new Phone("ˈ", PhoneKind.Mark), new Phone("e", PhoneKind.Vowel),
                new Phone("n", PhoneKind.Nasal), new Phone("t", PhoneKind.Consonant), new Phone("e", PhoneKind.Vowel)];
        var units = Read(word);
        if (units.Count == 0) return [];
        var nuclei = Syllables(units);
        if (nuclei.Count == 0) return Spell(word);

        // The stressed syllable: the accented vowel; else the one before last if it ends in a vowel, n or s; else the last.
        var accented = nuclei.FindIndex(i => units[i].Accented);
        int stressed;
        if (accented >= 0) stressed = accented;
        else
        {
            var penult = "aeiouns".Contains(word[^1], StringComparison.Ordinal);
            stressed = nuclei.Count >= 2 && penult ? nuclei.Count - 2 : nuclei.Count - 1;
        }
        var level = new char[nuclei.Count];
        if (Unstressed.Contains(word) && !stress) { }
        else if (Secondary.Contains(word)) level[stressed] = 'ˌ';
        else
        {
            level[stressed] = 'ˈ';
            for (var s = 0; s <= stressed - 2; s += 2) level[s] = 'ˌ';
        }

        var phones = new List<Phone>();
        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            if (!unit.IsVowel)
            {
                var kind = unit.Kind;
                // b, d, g before a consonant other than l or r: a stop («ˌadmin», «inkˈoɡnito», «sˌubkaɾpˈetas»); at the end of the word, unless a vowel follows
                // («rˈed», «la mitˈað iθkjˈeɾða»).
                if (kind == PhoneKind.Soft && i < units.Count - 1 && !units[i + 1].IsVowel && units[i + 1].Sound is not ("l" or "ɾ" or "r")) kind = PhoneKind.Stop;
                else if (kind == PhoneKind.Soft && i == units.Count - 1) kind = PhoneKind.Final;
                phones.Add(new Phone(unit.Sound, kind));
                continue;
            }
            var syllable = nuclei.IndexOf(i);
            if (syllable < 0)
            {
                // A glide before its nucleus («bien», «cuando»): j, w; after it, it went with the nucleus.
                if (!unit.Offglide) phones.Add(new Phone(unit.Sound == "i" ? "j" : "w", PhoneKind.Consonant));
                continue;
            }
            if (level[syllable] != default) phones.Add(new Phone(level[syllable].ToString(), PhoneKind.Mark));
            var sound = unit.Sound;
            // A falling diphthong: as misaki writes espeak's tied pair.
            if (i + 1 < units.Count && units[i + 1].Offglide)
                sound = (sound, units[i + 1].Sound) switch
                {
                    ("a", "i") => "I",
                    ("a", "u") => "W",
                    ("e", "i") => "A",
                    ("e", "u") => "eʊ",
                    ("o", "i") => "oɪ",
                    ("o", "u") => "ow",
                    ("u", "i") => "uj",
                    (var s, var g) => s + g,
                };
            // The stressed e before an n that closes its syllable: ɛ.
            else if (sound == "e" && level[syllable] == 'ˈ' && i + 2 < units.Count && units[i + 1].Sound == "n" && !units[i + 2].IsVowel) sound = "ɛ";
            phones.Add(new Phone(sound, PhoneKind.Vowel));
        }
        return phones;
    }

    /// <summary>A word without a vowel, letter by letter: a secondary stress on each but the last («θˌetˌeˌereˈɛle»).</summary>
    private static List<Phone> Spell(string word)
    {
        var phones = new List<Phone>();
        var letters = word.Where(Letters.ContainsKey).ToList();
        for (var i = 0; i < letters.Count; i++)
        {
            var name = Letters[letters[i]];
            var star = name.IndexOf('*', StringComparison.Ordinal);
            if (star > 0) phones.Add(new Phone(name[..star], PhoneKind.Consonant));
            phones.Add(new Phone(i == letters.Count - 1 ? "ˈ" : "ˌ", PhoneKind.Mark));
            phones.Add(new Phone(name[(star + 1)..], PhoneKind.Vowel));
        }
        return phones;
    }

    /// <summary>The letters of a word as sounds.</summary>
    private static List<Unit> Read(string w)
    {
        var units = new List<Unit>();
        for (var i = 0; i < w.Length; i++)
        {
            var c = w[i];
            var next = i + 1 < w.Length ? w[i + 1] : '\0';
            var afterNext = i + 2 < w.Length ? w[i + 2] : '\0';
            switch (c)
            {
                case 'a' or 'e' or 'o': units.Add(new Unit(c.ToString(), true, kind: PhoneKind.Vowel)); break;
                case 'i' or 'u': units.Add(new Unit(c.ToString(), true, weak: true, kind: PhoneKind.Vowel)); break;
                case 'á': units.Add(new Unit("a", true, accented: true, kind: PhoneKind.Vowel)); break;
                case 'é': units.Add(new Unit("e", true, accented: true, kind: PhoneKind.Vowel)); break;
                case 'ó': units.Add(new Unit("o", true, accented: true, kind: PhoneKind.Vowel)); break;
                case 'í': units.Add(new Unit("i", true, accented: true, kind: PhoneKind.Vowel)); break;
                case 'ú': units.Add(new Unit("u", true, accented: true, kind: PhoneKind.Vowel)); break;
                case 'ü': units.Add(new Unit("u", true, weak: true, kind: PhoneKind.Vowel)); break;
                case 'b' or 'v': units.Add(new Unit("b", false, kind: PhoneKind.Soft)); break;
                case 'd': units.Add(new Unit("d", false, kind: PhoneKind.Soft)); break;
                case 'g':
                    if (next is 'e' or 'i' or 'é' or 'í') units.Add(new Unit("x", false));
                    else
                    {
                        units.Add(new Unit("ɡ", false, kind: PhoneKind.Soft));
                        // «gue», «gui»: the u is not said.
                        if (next == 'u' && afterNext is 'e' or 'i' or 'é' or 'í') i++;
                    }
                    break;
                case 'c':
                    if (next == 'h')
                    {
                        units.Add(new Unit("ʧ", false));
                        i++;
                    }
                    else if (next == 'k') { }
                    else if (next is 'e' or 'i' or 'é' or 'í') units.Add(new Unit("θ", false));
                    else units.Add(new Unit("k", false));
                    break;
                case 'q':
                    units.Add(new Unit("k", false));
                    if (next == 'u') i++;
                    break;
                case 'k': units.Add(new Unit("k", false)); break;
                case 'z': units.Add(new Unit("θ", false)); break;
                case 'j': units.Add(new Unit("x", false)); break;
                case 'h': break;
                case 'ñ': units.Add(new Unit("ɲ", false)); break;
                case 'l':
                    if (next == 'l')
                    {
                        units.Add(new Unit("ʎ", false));
                        i++;
                    }
                    else units.Add(new Unit("l", false));
                    break;
                case 'r':
                    if (next == 'r')
                    {
                        units.Add(new Unit("r", false));
                        i++;
                    }
                    // At the start of the word or after n, l, s: the trill.
                    else if (i == 0 || (units.Count > 0 && units[^1].Sound is "n" or "l" or "s")) units.Add(new Unit("r", false));
                    else units.Add(new Unit("ɾ", false));
                    break;
                case 'n' or 'm': units.Add(new Unit(c.ToString(), false, kind: PhoneKind.Nasal)); break;
                case 'p':
                    // «captura»: espeak lengthens a p before t.
                    units.Add(new Unit(next == 't' ? "pː" : "p", false));
                    break;
                case 's':
                    if (next == 'h' && (i == 0 || i + 2 == w.Length))
                    {
                        units.Add(new Unit("ʃ", false));
                        i++;
                    }
                    else units.Add(new Unit("s", false));
                    break;
                case 'x':
                    if (i == 0) units.Add(new Unit("s", false));
                    else
                    {
                        units.Add(new Unit("k", false));
                        units.Add(new Unit("s", false));
                    }
                    break;
                case 'y':
                    // A vowel when alone, at the end of a word or before a consonant («y», «hay», «muy»); else ʝ («yo», «mayo»).
                    var vowelNext = next != '\0' && "aeiouáéíóú".Contains(next, StringComparison.Ordinal);
                    if (!vowelNext && (w.Length == 1 || i > 0)) units.Add(new Unit("i", true, weak: true, kind: PhoneKind.Vowel) { FromY = true });
                    else units.Add(new Unit("ʝ", false));
                    break;
                case 'w': units.Add(new Unit("u", true, weak: true, kind: PhoneKind.Vowel)); break;
                case 'f' or 't': units.Add(new Unit(c.ToString(), false)); break;
            }
        }
        return units;
    }

    /// <summary>
    /// Which vowels are nuclei of syllables, in order: strong and accented ones always; of weak ones together, the last
    /// («cuidado», «ciudad»), except a final y («muy»). The rest are glides (j, w before; I, W, A after). After r and l, a u
    /// before a strong vowel is a syllable of its own, as espeak says it («ruˈeða», «luˈeɣo»), and an i after rr («kˌoriˈɛnte»).
    /// </summary>
    private static List<int> Syllables(List<Unit> units)
    {
        var nuclei = new List<int>();
        var i = 0;
        while (i < units.Count)
        {
            if (!units[i].IsVowel)
            {
                i++;
                continue;
            }
            var end = i;
            while (end + 1 < units.Count && units[end + 1].IsVowel) end++;
            var run = Enumerable.Range(i, end - i + 1).ToList();
            var strong = run.Where(k => !units[k].Weak || units[k].Accented).ToList();
            if (strong.Count == 0) strong = [units[run[^1]].FromY && run.Count > 1 ? run[^2] : run[^1]];
            if (run.Count > 1 && units[i].Weak && i > 0 && !strong.Contains(i) && strong.Contains(i + 1)
                && (units[i].Sound == "u" ? units[i - 1].Sound is "r" or "ɾ" or "l" or "ʎ" : units[i - 1].Sound == "r"))
                strong.Insert(0, i);
            foreach (var k in run)
                if (!strong.Contains(k) && k > i && strong.Contains(k - 1)) units[k].Offglide = true;
            nuclei.AddRange(strong);
            i = end + 1;
        }
        return nuclei;
    }

    // ── Across words ─────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a word's phones: b, d, g as stops after a pause or a nasal and fricatives elsewhere; n as m before p, b, m
    /// (also the next word's first sound) and ŋ before g and j.
    /// </summary>
    private static void Write(StringBuilder output, List<Phone> phones, Phone? before, Phone? after)
    {
        var previous = before;
        for (var i = 0; i < phones.Count; i++)
        {
            var phone = phones[i];
            if (phone.IsMark)
            {
                output.Append(phone.Text);
                continue;
            }
            var text = phone.Text;
            if (phone.Kind is PhoneKind.Soft or PhoneKind.Stop or PhoneKind.Final)
            {
                var stop = phone.Kind == PhoneKind.Stop || previous is null || previous.Kind == PhoneKind.Nasal
                    || (phone.Kind == PhoneKind.Final && after?.Kind != PhoneKind.Vowel);
                text = text switch
                {
                    "b" => stop ? "b" : "β",
                    "d" => stop ? "d" : "ð",
                    _ => stop ? "ɡ" : "ɣ",
                };
            }
            else if (phone.Text == "n")
            {
                // In the word: m before p, b, m and ŋ before g, j; before the next word only before p, b and g.
                var inWord = phones.Skip(i + 1).FirstOrDefault(p => !p.IsMark);
                text = inWord is not null
                    ? inWord.Text switch { "p" or "pː" or "b" or "m" => "m", "ɡ" or "x" => "ŋ", _ => "n" }
                    : after?.Text switch { "p" or "pː" or "b" => "m", "ɡ" => "ŋ", _ => "n" };
            }
            output.Append(text);
            previous = phone;
        }
    }

    // ── Tokens ───────────────────────────────────────────────────────────────

    private sealed record Token(string? Word, string? Punctuation);

    /// <summary>A letter Spanish does not use, without its accent («à» → «a», «ç» → «c»).</summary>
    private static char Fold(char c) =>
        "áéíóúüñ".Contains(c, StringComparison.Ordinal) || c < 128 ? c : c.ToString().Normalize(NormalizationForm.FormD)[0];

    /// <summary>Words in lower case, and the punctuation that is kept (as misaki keeps it) or that is a pause.</summary>
    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var word = new StringBuilder();
        void Flush()
        {
            if (word.Length > 0) tokens.Add(new Token(word.ToString(), null));
            word.Clear();
        }
        for (var k = 0; k < text.Length; k++)
        {
            var c = char.ToLower(text[k], CultureInfo.InvariantCulture);
            if (char.IsLetter(c)) word.Append(Fold(c));
            else if (c == '-' && word.Length > 0 && k + 1 < text.Length && char.IsLetter(text[k + 1]))
            {
                Flush();
                tokens.Add(new Token(null, "-"));
            }
            else if (c is '\'' or '’' && word.Length > 0) { }
            else
            {
                Flush();
                switch (c)
                {
                    case ',' or '.' or ';' or ':' or '!' or '?' or '…' or '—':
                        tokens.Add(new Token(null, c.ToString()));
                        break;
                    case '¿' or '¡' or '(' or ')' or '"' or '«' or '»' or '“' or '”':
                        // A pause without a mark of its own in Kokoro's vocabulary.
                        tokens.Add(new Token(null, string.Empty));
                        break;
                }
            }
        }
        Flush();
        return tokens;
    }
}

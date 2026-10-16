using System.Text;

namespace KDesktop.Core.Voice;

/// <summary>
/// Spanish "sounds like" key, to compare how a word was transcribed: «KDesktop», «Ka desktop», «Cadesktop» and
/// «ca de esktop» end up close. Lower case, no accents or spaces; c/k/qu → k, z/ce/ci → s, v/w → b, silent h out,
/// ll → y, ge/gi → j, x → ks, ph → f, repeated letters → one. A lone letter («K desktop») becomes its spoken name.
/// </summary>
public static class PhoneticKey
{
    /// <summary>How each letter is read out in Spanish (a, e, o, u and y are words on their own and stay).</summary>
    private static readonly Dictionary<char, string> LetterNames = new()
    {
        ['b'] = "be", ['c'] = "ce", ['d'] = "de", ['f'] = "efe", ['g'] = "ge", ['h'] = "hache", ['i'] = "i",
        ['j'] = "jota", ['k'] = "ka", ['l'] = "ele", ['m'] = "eme", ['n'] = "ene", ['p'] = "pe", ['q'] = "cu",
        ['r'] = "erre", ['s'] = "ese", ['t'] = "te", ['v'] = "uve", ['w'] = "uvedoble", ['x'] = "equis", ['z'] = "zeta",
    };

    /// <summary>Key of a whole text (its words joined).</summary>
    public static string Of(string text)
    {
        var joined = new StringBuilder();
        foreach (var word in TextSimilarity.Words(text))
            joined.Append(word.Length == 1 && LetterNames.TryGetValue(word[0], out var name) ? name : word);
        return Encode(joined.ToString());
    }

    /// <summary>Key of text already folded (lower case, no accents, letters and digits only).</summary>
    public static string Encode(string s)
    {
        var o = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            var next = i + 1 < s.Length ? s[i + 1] : '\0';
            var after = i + 2 < s.Length ? s[i + 2] : '\0';
            switch (c)
            {
                case 'q':
                    o.Append('k');
                    if (next == 'u' && after is 'e' or 'i') i++; // que, qui: the u is silent
                    break;
                case 'c':
                    if (next == 'h') { o.Append('X'); i++; } // ch: its own sound
                    else o.Append(next is 'e' or 'i' ? 's' : 'k');
                    break;
                case 'z': o.Append('s'); break;
                case 'v' or 'w': o.Append('b'); break;
                case 'h': break; // silent
                case 'l':
                    if (next == 'l') { o.Append('y'); i++; }
                    else o.Append('l');
                    break;
                case 'y':
                    // «hoy», «rey»: a y that ends a syllable sounds like i.
                    o.Append(next is 'a' or 'e' or 'i' or 'o' or 'u' ? 'y' : 'i');
                    break;
                case 'g':
                    if (next is 'e' or 'i') o.Append('j');
                    else
                    {
                        o.Append('g');
                        if (next == 'u' && after is 'e' or 'i') i++; // gue, gui
                    }
                    break;
                case 'x': o.Append("ks"); break;
                case 'p':
                    if (next == 'h') { o.Append('f'); i++; }
                    else o.Append('p');
                    break;
                default: o.Append(c); break;
            }
        }
        // Repeated sounds count once («kk», «ss», «rr»…).
        var key = new StringBuilder(o.Length);
        foreach (var c in o.ToString())
            if (key.Length == 0 || key[^1] != c) key.Append(c);
        return key.ToString();
    }

    /// <summary>1 when two keys are equal, 0 when nothing matches (normalised edit distance).</summary>
    public static double Similarity(string a, string b) => TextSimilarity.Ratio(a, b);
}

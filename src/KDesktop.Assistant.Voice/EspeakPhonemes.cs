using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace KDesktop.Assistant.Voice;

/// <summary>
/// Text to phonemes with espeak-ng: for Kokoro in the languages without rules of our own (English, French, Italian,
/// Portuguese), as misaki does it (<see cref="ToPhonemes"/>), and for every Piper voice, as piper-phonemize does it
/// (<see cref="Ipa"/>). espeak-ng is GPL: it is not part of K-Desktop; the user downloads it apart, like a model
/// (<c>EspeakFiles.Folder</c>), and it is loaded from there only when it is needed. One process-wide library, used under
/// a lock (it keeps global state).
/// </summary>
internal static partial class EspeakPhonemes
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitializeFn(int output, int bufferLength, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int options);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetVoiceByNameFn([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr TextToPhonemesFn(ref IntPtr text, int textMode, int phonemeMode);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int TerminateFn();

    private static readonly object Gate = new();
    private static string? _folder;
    private static SetVoiceByNameFn? _setVoice;
    private static TextToPhonemesFn? _toPhonemes;
    private static string? _voice;
    private static IntPtr _library;

    /// <summary>IPA (bit 1), with a tie (bit 7) U+0361 inside phonemes of several letters, as phonemizer asks for it.</summary>
    private const int PhonemeMode = 0x02 | 0x80 | ('͡' << 8);

    private const int Utf8 = 1;

    /// <summary>
    /// The voice files of the languages Kokoro speaks through espeak (phonemizer picks the first one of each); any other
    /// voice (Piper's «es-419», «en-gb-x-rp»…) is the file of that name in <c>espeak-ng-data\lang</c>.
    /// </summary>
    private static readonly Dictionary<string, string> Files = new(StringComparer.Ordinal)
    {
        ["en-us"] = "gmw/en-US",
        ["en-gb"] = "gmw/en",
        ["fr-fr"] = "roa/fr",
        ["it"] = "roa/it",
        ["pt-br"] = "roa/pt-BR",
    };

    /// <summary>IPA (bit 1) without ties: what piper-phonemize asks for.</summary>
    private const int PlainIpa = 0x02;

    /// <summary>The marks misaki keeps; between them, espeak says each piece (phonemizer's «preserve_punctuation»).</summary>
    private const string Marks = ";:,.!?¡¿—…\"«»“”()";

    /// <summary>The phonemes of a text in an espeak voice («en-us», «fr-fr», «it», «pt-br»), as misaki writes them for Kokoro.</summary>
    /// <param name="folder">Where espeak-ng was unpacked (its DLL and <c>espeak-ng-data</c>).</param>
    public static string ToPhonemes(string text, string voice, string folder)
    {
        var english = voice.StartsWith("en", StringComparison.Ordinal);
        // misaki: «» to curly quotes, () to «» (kept as marks).
        text = text.Replace('«', '“').Replace('»', '”');
        var output = new StringBuilder();
        lock (Gate)
        {
            Load(folder);
            Use(voice, folder);
            var piece = new StringBuilder();
            foreach (var c in text)
            {
                if (Marks.Contains(c, StringComparison.Ordinal))
                {
                    Flush();
                    while (output.Length > 0 && output[^1] == ' ') output.Length--;
                    output.Append(c).Append(' ');
                }
                else piece.Append(c);
            }
            Flush();

            void Flush()
            {
                var said = piece.ToString().Trim();
                piece.Clear();
                if (said.Length == 0) return;
                output.Append(Clauses(said, PhonemeMode)).Append(' ');
            }
        }
        var ps = output.ToString().Trim();
        return english ? English(ps, british: voice == "en-gb") : Other(ps);
    }

    /// <summary>The marks that end a clause for Piper, as espeak's clause terminators (an ellipsis is a stop, a dash a comma).</summary>
    private static char ClauseMark(char c) => c switch
    {
        ',' or ';' or ':' or '.' or '!' or '?' => c,
        '…' => '.',
        '—' or '–' => ',',
        _ => '\0',
    };

    /// <summary>Quotes and brackets: a mark before one of them still ends its clause («"Hola."»).</summary>
    private const string Quotes = "\"«»“”‘’()[]{}";

    /// <summary>
    /// The phonemes of a text in an espeak voice («es», «en-us», «es-419»…) as piper-phonemize gives them to a Piper voice:
    /// IPA without ties, decomposed (NFD), each clause followed by its mark (a comma, colon or semicolon with a space).
    /// A mark only ends a clause before a space or the end, as for espeak («3,5» and «3.5» stay numbers); everything else
    /// goes to espeak as it is (its quotes are pauses too: «Visual» starts with b, not β).
    /// </summary>
    /// <param name="folder">Where espeak-ng was unpacked (its DLL and <c>espeak-ng-data</c>).</param>
    public static string Ipa(string text, string voice, string folder)
    {
        var output = new StringBuilder();
        lock (Gate)
        {
            Load(folder);
            Use(voice, folder);
            var piece = new StringBuilder();
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                var mark = ClauseMark(c);
                if (mark != '\0' && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1]) || Quotes.Contains(text[i + 1], StringComparison.Ordinal)
                                     || ClauseMark(text[i + 1]) != '\0'))
                {
                    Flush(mark);
                    // «...» or «?!» is one mark.
                    while (i + 1 < text.Length && ClauseMark(text[i + 1]) != '\0') i++;
                }
                else piece.Append(c);
            }
            Flush('\0');

            void Flush(char mark)
            {
                var said = piece.ToString().Trim();
                piece.Clear();
                if (said.Length > 0) output.Append(Clauses(said, PlainIpa));
                while (output.Length > 0 && output[^1] == ' ') output.Length--;
                if (mark != '\0' && output.Length > 0) output.Append(mark);
                output.Append(' ');
            }
        }
        return Spaces().Replace(output.ToString(), " ").Trim().Normalize(NormalizationForm.FormD);
    }

    /// <summary>
    /// Selects a voice: Kokoro's by their file, as phonemizer does (this build does not take «fr-fr» as a name); the others
    /// by name, as piper-phonemize does, or else by their file. Under the gate.
    /// </summary>
    private static void Use(string voice, string folder)
    {
        if (_voice == voice) return;
        var chosen = Files.TryGetValue(voice, out var file)
            ? _setVoice!(file) == 0
            : _setVoice!(voice) == 0 || (FileOf(voice, folder) is { } found && _setVoice(found) == 0);
        if (!chosen) throw new InvalidOperationException($"espeak-ng no tiene la voz «{voice}»");
        _voice = voice;
    }

    /// <summary>The file of a voice in <c>espeak-ng-data\lang</c> («es-419» → «roa/es-419»), or null.</summary>
    private static string? FileOf(string voice, string folder)
    {
        var lang = Path.Combine(folder, "espeak-ng-data", "lang");
        if (!Directory.Exists(lang)) return null;
        var found = Directory.EnumerateFiles(lang, "*", SearchOption.AllDirectories)
            .FirstOrDefault(f => Path.GetFileName(f).Equals(voice, StringComparison.OrdinalIgnoreCase));
        return found is null ? null : Path.GetRelativePath(lang, found).Replace('\\', '/');
    }

    /// <summary>espeak's phonemes of a piece without marks, clause after clause, without its language switches «(en)».</summary>
    private static string Clauses(string text, int mode)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            var pointer = buffer;
            var parts = new List<string>();
            while (pointer != IntPtr.Zero)
            {
                var result = _toPhonemes!(ref pointer, Utf8, mode);
                if (result != IntPtr.Zero && Marshal.PtrToStringUTF8(result) is { Length: > 0 } phonemes) parts.Add(phonemes.Trim());
            }
            var line = string.Join(' ', parts).Replace('\n', ' ');
            line = LanguageFlag().Replace(line, string.Empty);
            return Spaces().Replace(line, " ").Trim().Replace('͡', '^');
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static readonly (string From, string To)[] OtherTies =
    [
        ("a^ɪ", "I"), ("a^ʊ", "W"), ("d^z", "ʣ"), ("d^ʒ", "ʤ"), ("e^ɪ", "A"), ("o^ʊ", "O"), ("ə^ʊ", "Q"), ("s^s", "S"), ("t^s", "ʦ"),
        ("t^ʃ", "ʧ"), ("ɔ^ɪ", "Y"),
    ];

    /// <summary>misaki's EspeakG2P (Kokoro 1.0): the tied pairs as single symbols, no ties or hyphens left.</summary>
    private static string Other(string ps)
    {
        foreach (var (from, to) in OtherTies.OrderBy(t => t.From, StringComparer.Ordinal)) ps = ps.Replace(from, to, StringComparison.Ordinal);
        return ps.Replace("^", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
    }

    private static readonly (string From, string To)[] EnglishTies =
    [
        .. new (string, string)[]
        {
            ("ʔˌn̩", "ʔn"), ("ʔn̩", "ʔn"), ("a^ɪ", "I"), ("a^ʊ", "W"), ("d^ʒ", "ʤ"), ("e^ɪ", "A"), ("e", "A"), ("t^ʃ", "ʧ"),
            ("ɔ^ɪ", "Y"), ("ə^l", "ᵊl"), ("ʲo", "jo"), ("ʲə", "jə"), ("ʲ", ""), ("ɚ", "əɹ"), ("r", "ɹ"), ("x", "k"), ("ç", "k"),
            ("ɐ", "ə"), ("ɬ", "l"), ("̃", ""),
        }.OrderByDescending(t => t.Item1.Length),
    ];

    /// <summary>misaki's EspeakFallback for English (Kokoro 1.0).</summary>
    private static string English(string ps, bool british)
    {
        foreach (var (from, to) in EnglishTies) ps = ps.Replace(from, to, StringComparison.Ordinal);
        ps = Syllabic().Replace(ps, "ᵊ$1").Replace("̩", "", StringComparison.Ordinal);
        if (british)
        {
            ps = ps.Replace("e^ə", "ɛː", StringComparison.Ordinal).Replace("iə", "ɪə", StringComparison.Ordinal).Replace("ə^ʊ", "Q", StringComparison.Ordinal);
        }
        else
        {
            ps = ps.Replace("o^ʊ", "O", StringComparison.Ordinal).Replace("ɜːɹ", "ɜɹ", StringComparison.Ordinal).Replace("ɜː", "ɜɹ", StringComparison.Ordinal)
                .Replace("ɪə", "iə", StringComparison.Ordinal).Replace("ː", "", StringComparison.Ordinal);
        }
        ps = ps.Replace('o', 'ɔ').Replace("ɾ", "T", StringComparison.Ordinal).Replace("ʔ", "t", StringComparison.Ordinal);
        return ps.Replace("^", "", StringComparison.Ordinal);
    }

    private static void Load(string folder)
    {
        if (_folder == folder) return;
        Unload();
        var library = NativeLibrary.Load(Path.Combine(folder, "espeak-ng.dll"));
        _library = library;
        var initialize = Marshal.GetDelegateForFunctionPointer<InitializeFn>(NativeLibrary.GetExport(library, "espeak_Initialize"));
        _setVoice = Marshal.GetDelegateForFunctionPointer<SetVoiceByNameFn>(NativeLibrary.GetExport(library, "espeak_SetVoiceByName"));
        _toPhonemes = Marshal.GetDelegateForFunctionPointer<TextToPhonemesFn>(NativeLibrary.GetExport(library, "espeak_TextToPhonemes"));
        // Synchronous output (no audio device), the data next to the DLL: the folder that holds «espeak-ng-data» (the build
        // Piper's voices learnt with only takes that; the newer one also takes the data folder itself).
        if (initialize(0x02, 0, folder, 0) <= 0) throw new InvalidOperationException("espeak-ng no ha arrancado");
        _folder = folder;
        _voice = null;
    }

    /// <summary>Closes espeak-ng and lets its DLL go (K-Desktop is about to delete it).</summary>
    public static void Release()
    {
        lock (Gate) Unload();
    }

    private static void Unload()
    {
        if (_library == IntPtr.Zero) return;
        try
        {
            if (NativeLibrary.TryGetExport(_library, "espeak_Terminate", out var terminate))
                Marshal.GetDelegateForFunctionPointer<TerminateFn>(terminate)();
        }
        finally
        {
            NativeLibrary.Free(_library);
            _library = IntPtr.Zero;
            _folder = null;
            _voice = null;
            _setVoice = null;
            _toPhonemes = null;
        }
    }

    [GeneratedRegex(@"\([a-z\-]{2,8}\)")]
    private static partial Regex LanguageFlag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"(\S)̩")]
    private static partial Regex Syllabic();
}

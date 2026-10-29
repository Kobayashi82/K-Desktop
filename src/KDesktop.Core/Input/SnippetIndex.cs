using KDesktop.Core.Model;

namespace KDesktop.Core.Input;

/// <summary>
/// An abbreviation of «Textos» just typed. <paramref name="Typed"/> is the abbreviation as it was typed (its case says
/// how to write the text); <paramref name="Erase"/> how many characters the program got (the last one was swallowed);
/// <paramref name="EndVk"/> the space, Tab or Enter that ended it (swallowed too), 0 when it went off on its own.
/// </summary>
public sealed record SnippetHit(TextSnippet Snippet, string Typed, int Erase, int EndVk, char Last, ForegroundWindow Window);

/// <summary>
/// The enabled texts of the expander, ready for the input hook. An abbreviation goes off as soon as it is typed, unless a
/// longer one that works in the same program starts the same («;fe» and «;fecha»): then it waits for a space, Tab or
/// Enter. A text limited to some programs wins there over one for everywhere with the same abbreviation, and works in
/// them even when they are in «Desactivar en»; one for everywhere does not work in those.
/// </summary>
public sealed class SnippetIndex
{
    public static readonly SnippetIndex Empty = new([], string.Empty, []);

    /// <summary>The program «in front» when any will do (K-Desktop's box to try them): every text works.</summary>
    public const string AnyProgram = "*";

    /// <summary>Per abbreviation, the texts with it: the ones limited to programs first, then in the order of the list.</summary>
    private readonly Dictionary<string, List<TextSnippet>> _byAbbreviation = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per abbreviation, the longer ones that start the same.</summary>
    private readonly Dictionary<string, List<string>> _longer = new(StringComparer.OrdinalIgnoreCase);
    private readonly int[] _lengths;
    private readonly HashSet<string> _disabledIn;

    public string Prefix { get; }

    public bool IsEmpty => _byAbbreviation.Count == 0;

    /// <summary>The longest text that has to be remembered (one more character, to see what is in front without a prefix).</summary>
    public int Keep { get; }

    public SnippetIndex(IEnumerable<TextSnippet> snippets, string prefix, IEnumerable<string> disabledIn)
    {
        Prefix = prefix.Trim();
        foreach (var snippet in snippets.Where(s => s.Enabled && s.Text.Length > 0).OrderBy(s => s.Processes.Count == 0))
        {
            var abbreviation = TextSnippet.Clean(snippet.Abbreviation, Prefix);
            if (abbreviation.Length == 0) continue;
            if (!_byAbbreviation.TryGetValue(abbreviation, out var list)) _byAbbreviation[abbreviation] = list = [];
            list.Add(snippet);
        }
        foreach (var a in _byAbbreviation.Keys)
        {
            var longer = _byAbbreviation.Keys.Where(b => b.Length > a.Length && b.StartsWith(a, StringComparison.OrdinalIgnoreCase)).ToList();
            if (longer.Count > 0) _longer[a] = longer;
        }
        _lengths = _byAbbreviation.Keys.Select(k => k.Length).Distinct().OrderDescending().ToArray();
        Keep = Prefix.Length + (_lengths.Length > 0 ? _lengths[0] : 0) + 1;
        _disabledIn = disabledIn.Select(ProcessNames.Normalize).ToHashSet();
    }

    public SnippetIndex(AppConfig config, bool paused)
        : this(paused || !config.Settings.SnippetsEnabled ? [] : config.Snippets, config.Settings.SnippetPrefix, config.Settings.DisabledIn)
    {
    }

    /// <summary>
    /// A character typed: the abbreviation that ends with it and goes off right away (none waits for an end).
    /// <paramref name="process"/> is asked only when an abbreviation is there.
    /// </summary>
    public TextSnippet? Typed(string text, Func<string> process, out string typed)
    {
        var found = Find(text, process, out typed, out var front);
        if (found is null) return null;
        var longer = _longer.GetValueOrDefault(typed);
        return longer is not null && longer.Any(l => Usable(l, front!) is not null) ? null : found;
    }

    /// <summary>A space, Tab or Enter typed after <paramref name="text"/>: the abbreviation it ends.</summary>
    public TextSnippet? Ended(string text, Func<string> process, out string typed) => Find(text, process, out typed, out _);

    /// <summary>The longest abbreviation at the end of <paramref name="text"/>, with the prefix in front, that works there.</summary>
    private TextSnippet? Find(string text, Func<string> process, out string typed, out string? front)
    {
        typed = string.Empty;
        front = null;
        foreach (var length in _lengths)
        {
            var start = text.Length - length - Prefix.Length;
            if (start < 0) continue;
            if (Prefix.Length > 0 && string.CompareOrdinal(text, start, Prefix, 0, Prefix.Length) != 0) continue;
            // Without a prefix, only a whole word: «ok» does not go off inside «book».
            if (Prefix.Length == 0 && start > 0 && char.IsLetterOrDigit(text[start - 1])) continue;
            var candidate = text.Substring(start + Prefix.Length, length);
            if (!_byAbbreviation.ContainsKey(candidate)) continue;
            front ??= process();
            if (Usable(candidate, front) is not { } snippet) continue;
            typed = candidate;
            return snippet;
        }
        return null;
    }

    /// <summary>The text of <paramref name="abbreviation"/> that works with <paramref name="process"/> in front, if any.</summary>
    private TextSnippet? Usable(string abbreviation, string process)
    {
        if (!_byAbbreviation.TryGetValue(abbreviation, out var list)) return null;
        if (process == AnyProgram) return list[0];
        var disabled = _disabledIn.Contains(process);
        return list.FirstOrDefault(s => s.Processes.Count > 0 ? s.WorksIn(process) : !disabled);
    }
}

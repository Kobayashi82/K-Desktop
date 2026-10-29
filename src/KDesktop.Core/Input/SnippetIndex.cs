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
/// longer one starts the same («;fe» and «;fecha»): then it waits for a space, Tab or Enter.
/// </summary>
public sealed class SnippetIndex
{
    public static readonly SnippetIndex Empty = new([], string.Empty, []);

    private readonly Dictionary<string, TextSnippet> _byAbbreviation = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _waits = new(StringComparer.OrdinalIgnoreCase);
    private readonly int[] _lengths;
    private readonly HashSet<string> _disabledIn;

    public string Prefix { get; }

    public bool IsEmpty => _byAbbreviation.Count == 0;

    /// <summary>The longest text that has to be remembered (one more character, to see what is in front without a prefix).</summary>
    public int Keep { get; }

    public SnippetIndex(IEnumerable<TextSnippet> snippets, string prefix, IEnumerable<string> disabledIn)
    {
        Prefix = prefix.Trim();
        foreach (var snippet in snippets)
        {
            if (!snippet.Enabled || snippet.Text.Length == 0) continue;
            var abbreviation = TextSnippet.Clean(snippet.Abbreviation, Prefix);
            if (abbreviation.Length > 0) _byAbbreviation.TryAdd(abbreviation, snippet);
        }
        foreach (var a in _byAbbreviation.Keys)
            if (_byAbbreviation.Keys.Any(b => b.Length > a.Length && b.StartsWith(a, StringComparison.OrdinalIgnoreCase))) _waits.Add(a);
        _lengths = _byAbbreviation.Keys.Select(k => k.Length).Distinct().OrderDescending().ToArray();
        Keep = Prefix.Length + (_lengths.Length > 0 ? _lengths[0] : 0) + 1;
        _disabledIn = disabledIn.Select(ProcessNames.Normalize).ToHashSet();
    }

    public SnippetIndex(AppConfig config, bool paused)
        : this(paused || !config.Settings.SnippetsEnabled ? [] : config.Snippets, config.Settings.SnippetPrefix, config.Settings.DisabledIn)
    {
    }

    public bool IsDisabledIn(string process) => _disabledIn.Contains(process);

    /// <summary>A character typed: the abbreviation that ends with it and goes off right away (none waits for an end).</summary>
    public TextSnippet? Typed(string text, out string typed)
    {
        var found = Find(text, out typed);
        return found is not null && !_waits.Contains(typed) ? found : null;
    }

    /// <summary>A space, Tab or Enter typed after <paramref name="text"/>: the abbreviation it ends.</summary>
    public TextSnippet? Ended(string text, out string typed) => Find(text, out typed);

    /// <summary>The longest abbreviation at the end of <paramref name="text"/>, with the prefix in front.</summary>
    private TextSnippet? Find(string text, out string typed)
    {
        typed = string.Empty;
        foreach (var length in _lengths)
        {
            var start = text.Length - length - Prefix.Length;
            if (start < 0) continue;
            if (Prefix.Length > 0 && string.CompareOrdinal(text, start, Prefix, 0, Prefix.Length) != 0) continue;
            // Without a prefix, only a whole word: «ok» does not go off inside «book».
            if (Prefix.Length == 0 && start > 0 && char.IsLetterOrDigit(text[start - 1])) continue;
            var candidate = text.Substring(start + Prefix.Length, length);
            if (!_byAbbreviation.TryGetValue(candidate, out var snippet)) continue;
            typed = candidate;
            return snippet;
        }
        return null;
    }
}

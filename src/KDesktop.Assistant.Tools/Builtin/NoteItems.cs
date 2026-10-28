using System.Text.RegularExpressions;
using KDesktop.Core.Engine;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// A note seen as a list (B1, 5.0.343), to add or take out things: one per line («- leche»), or in one line after a title
/// («Compra: leche, huevos y pan»). A note that is no list is one item. Written back the way it was.
/// </summary>
public sealed partial class NoteItems
{
    /// <summary>«and» in the six languages, between the last two of a list in one line.</summary>
    private static readonly string[] Ands = [" y ", " e ", " and ", " et ", " und ", " ed "];

    private readonly string _header;
    private readonly bool _lines;
    private readonly string _bullet;
    private readonly bool _numbered;
    private readonly string? _and;

    private NoteItems(string header, bool lines, string bullet, bool numbered, string? and, List<string> items)
    {
        _header = header;
        _lines = lines;
        _bullet = bullet;
        _numbered = numbered;
        _and = and;
        Items = items;
    }

    public List<string> Items { get; }

    public static NoteItems Parse(string text)
    {
        text = text.Trim();
        if (text.Contains('\n', StringComparison.Ordinal))
        {
            var rows = text.Split('\n').Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
            var header = string.Empty;
            // A first line that is a title («Compra:», or the only one without a bullet).
            if (rows.Count > 1 && (rows[0].EndsWith(':') || (!Bullet().IsMatch(rows[0]) && Bullet().IsMatch(rows[1]))))
            {
                header = rows[0];
                rows.RemoveAt(0);
            }
            var first = rows.Count > 0 ? Bullet().Match(rows[0]) : Match.Empty;
            var numbered = first.Success && char.IsDigit(first.Value[0]);
            var bullet = first.Success && !numbered ? first.Value : string.Empty;
            return new NoteItems(header, true, bullet, numbered, null, [.. rows.Select(r => Bullet().Replace(r, string.Empty, 1).Trim())]);
        }

        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var head = colon > 0 && colon < 60 && !text[..colon].Contains("//", StringComparison.Ordinal) ? text[..(colon + 1)] : string.Empty;
        var body = text[head.Length..].Trim();
        var pieces = body.Split([',', ';'], StringSplitOptions.TrimEntries).ToList();
        string? and = null;
        if (pieces.Count > 0)
        {
            var last = pieces[^1];
            var at = Ands.Select(a => (And: a, At: last.LastIndexOf(a, StringComparison.OrdinalIgnoreCase))).Where(p => p.At > 0).OrderByDescending(p => p.At).FirstOrDefault();
            if (at.And is not null)
            {
                and = last.Substring(at.At, at.And.Length);
                pieces[^1] = last[..at.At].Trim();
                pieces.Add(last[(at.At + at.And.Length)..].Trim());
            }
        }
        var items = pieces.Select(p => p.TrimEnd('.').Trim()).Where(p => p.Length > 0).ToList();
        return new NoteItems(head, false, string.Empty, false, and, items);
    }

    /// <summary>The item with every word of <paramref name="phrase"/> (the short ones too when there are no others); -1: none.</summary>
    public int IndexOf(string phrase)
    {
        var wanted = Words(phrase);
        var strong = wanted.Where(w => w.Length > 2).ToArray();
        if (strong.Length > 0) wanted = strong;
        if (wanted.Length == 0) return -1;
        var exact = Items.FindIndex(i => string.Join(' ', Words(i)) == string.Join(' ', Words(phrase)));
        if (exact >= 0) return exact;
        return Items.FindIndex(i =>
        {
            var have = Words(i);
            return wanted.All(w => have.Any(h => h.StartsWith(w, StringComparison.Ordinal) || (w.StartsWith(h, StringComparison.Ordinal) && h.Length > 3)));
        });
    }

    /// <summary>The note written back the way it was.</summary>
    public string Text
    {
        get
        {
            if (_lines)
            {
                var rows = Items.Select((item, i) => (_numbered ? $"{i + 1}. " : _bullet) + item);
                return string.Join('\n', (_header.Length > 0 ? new[] { _header } : []).Concat(rows));
            }
            var list = Items.Count >= 2 && _and is not null
                ? string.Join(", ", Items.Take(Items.Count - 1)) + _and + Items[^1]
                : string.Join(", ", Items);
            return (_header + " " + list).Trim();
        }
    }

    /// <summary>Only a title is left (or nothing).</summary>
    public bool Empty => Items.Count == 0;

    public bool HasHeader => _header.Length > 0;

    private static string[] Words(string text) =>
        new string(text.Select(c => char.IsLetterOrDigit(c) ? FuzzyMatch.Fold(c) : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    [GeneratedRegex(@"^(\d+[.)]\s+|[-*•·]\s+|\[[ xX]?\]\s*)")]
    private static partial Regex Bullet();
}

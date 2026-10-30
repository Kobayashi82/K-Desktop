using System.Text.RegularExpressions;

namespace KDesktop.Core.Assistant;

/// <summary>
/// The pictures of a line of the chat with an assistant: which files are pictures, the ones the tools made during an order
/// (collected per order, as it flows through awaits) and the ones named in an answer («![captura](C:\…\a.png)» or a path).
/// </summary>
public static partial class ChatImages
{
    /// <summary>The kinds of picture shown in the chat.</summary>
    private static readonly string[] Kinds = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];

    /// <summary>At most this many pictures in one line.</summary>
    public const int MaxPerLine = 8;

    private static readonly AsyncLocal<List<string>?> Collected = new();

    /// <summary>Whether <paramref name="path"/> is a picture file that is there (by its extension).</summary>
    public static bool IsPicture(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        try { return Kinds.Contains(Path.GetExtension(path).ToLowerInvariant()) && File.Exists(path); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Collects what the tools make from here on (the order being answered, and what it awaits) until the result is
    /// disposed; <see cref="Gathering.Taken"/> gives them.
    /// </summary>
    public static Gathering Collect()
    {
        var before = Collected.Value;
        var list = new List<string>();
        Collected.Value = list;
        return new Gathering(list, before);
    }

    /// <summary>A tool made these pictures (only kept while an order collects them).</summary>
    public static void Made(IEnumerable<string> paths)
    {
        if (Collected.Value is not { } list) return;
        lock (list) list.AddRange(paths.Where(IsPicture));
    }

    public sealed class Gathering(List<string> list, List<string>? before) : IDisposable
    {
        public IReadOnlyList<string> Taken
        {
            get { lock (list) return [.. list.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxPerLine)]; }
        }

        public void Dispose() => Collected.Value = before;
    }

    /// <summary>
    /// The pictures an answer names (Markdown «![…](path)» or a full path of a picture that is there), and the text
    /// without the Markdown of them (their words stay, if they had any).
    /// </summary>
    public static (string Text, IReadOnlyList<string> Pictures) FromText(string text)
    {
        var found = new List<string>();
        var clean = Markdown().Replace(text, m =>
        {
            var target = m.Groups["src"].Value.Trim('<', '>');
            if (!IsPicture(target)) return m.Value;
            found.Add(target);
            return m.Groups["alt"].Value;
        });
        foreach (Match m in Bare().Matches(clean))
            if (IsPicture(m.Value)) found.Add(m.Value);
        return (clean.Trim(), [.. found.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxPerLine)]);
    }

    [GeneratedRegex(@"!\[(?<alt>[^\]]*)\]\((?<src>[^)\r\n]+)\)")]
    private static partial Regex Markdown();

    [GeneratedRegex(@"[A-Za-z]:\\[^\r\n""<>|*?«»]+?\.(?:png|jpe?g|gif|bmp|webp)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Bare();
}

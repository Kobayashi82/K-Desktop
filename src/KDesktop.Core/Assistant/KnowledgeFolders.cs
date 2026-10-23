namespace KDesktop.Core.Assistant;

/// <summary>
/// The folders of documents the assistants may know (stage 19): chosen by the user, never a whole disk, nor Windows, the
/// programs or the whole user folder (thousands of files that are not the user's documents). One inside another counts once.
/// </summary>
public static class KnowledgeFolders
{
    /// <summary>
    /// Why <paramref name="path"/> cannot be one: <c>missing</c>, <c>root</c> (a whole disk), <c>system</c> (Windows, the
    /// programs, the whole user folder or its AppData), <c>already</c> (it or a folder above it is there); null when it can.
    /// </summary>
    public static string? Problem(string path, IEnumerable<string> chosen)
    {
        string full;
        try { full = Full(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return "missing"; }
        if (!Directory.Exists(full)) return "missing";
        if (string.Equals(Path.GetPathRoot(full)?.TrimEnd('\\'), full.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return "root";
        if (IsSystem(full)) return "system";
        if (chosen.Any(c => Contains(Full(c), full))) return "already";
        return null;
    }

    /// <summary>The chosen ones that count: full paths, no repeats, none inside another.</summary>
    public static IReadOnlyList<string> Clean(IEnumerable<string> chosen)
    {
        var all = chosen.Select(c => { try { return Full(c); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; } })
            .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return [.. all.Where(f => !all.Any(other => !string.Equals(other, f, StringComparison.OrdinalIgnoreCase) && Contains(other, f)))];
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or inside it.</summary>
    public static bool Contains(string folder, string path) =>
        string.Equals(folder, path, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static string Full(string path) => Path.GetFullPath(path.Trim().Trim('"')).TrimEnd('\\') is { Length: > 2 } trimmed ? trimmed : Path.GetFullPath(path.Trim());

    private static bool IsSystem(string full)
    {
        string[] whole =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        ];
        if (whole.Any(w => w.Length > 0 && string.Equals(w.TrimEnd('\\'), full, StringComparison.OrdinalIgnoreCase))) return true;
        string[] inside =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ];
        return inside.Any(i => i.Length > 0 && Contains(i.TrimEnd('\\'), full));
    }
}

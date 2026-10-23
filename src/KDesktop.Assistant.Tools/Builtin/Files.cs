using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>A file or folder found on the PC.</summary>
public sealed record FoundFile(string Path, DateTime Modified, DateTime Created, long Size, bool IsFolder, bool InContent = false)
{
    public string Name => System.IO.Path.GetFileName(Path);

    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    /// <summary>When it arrived or last changed: a copied or downloaded file keeps an old modified date but is created now.</summary>
    public DateTime When => Created > Modified ? Created : Modified;
}

/// <summary>What to look for: words of its name (or content), a kind, a folder, how recent; all optional.</summary>
public sealed record FileQuery(string? Text, string? Kind, string? Folder, int? Days, bool Newest)
{
    public static FileQuery Of(JsonElement arguments) => new(
        arguments.GetString("text")?.Trim(), arguments.GetString("kind")?.Trim().ToLowerInvariant(), arguments.GetString("folder")?.Trim(),
        arguments.GetNumber("days") is { } days and > 0 ? (int)Math.Ceiling(days) : null,
        string.Equals(arguments.GetString("sort")?.Trim(), "newest", StringComparison.OrdinalIgnoreCase));

    /// <summary>The words that count (folded; one or two letters are articles and the like).</summary>
    /// <summary>
    /// The words that count, folded; one or two letters are articles and the like, and what is written joined stays one
    /// word («k-desktop» = kdesktop, which the name «K-Desktop 5.0» contains).
    /// </summary>
    public IReadOnlyList<string> Words =>
        (Text ?? string.Empty).Split([' ', '\t', '\'', '’'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => string.Concat(TextSimilarity.Words(token)))
            .Where(w => w.Length > 2 || w.Length > 0 && w.All(char.IsDigit)).Distinct().ToList();

    /// <summary>For the index, which splits names at hyphens and dots: the words and their parts.</summary>
    public IReadOnlyList<string> Terms =>
        Words.Concat(TextSimilarity.Words(Text ?? string.Empty).Where(w => w.Length > 2 || w.All(char.IsDigit))).Distinct().ToList();

    /// <summary>Its extensions (with the dot), or null for any kind; a folder is <see cref="FileKinds.Folder"/>.</summary>
    public IReadOnlySet<string>? Extensions => Kind is { Length: > 0 } kind && FileKinds.All.TryGetValue(kind, out var set) ? set : null;

    public bool WantsFolders => string.Equals(Kind, FileKinds.Folder, StringComparison.Ordinal);
}

/// <summary>The kinds of file the model may ask for, by extension.</summary>
public static class FileKinds
{
    public const string Folder = "folder";

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> All = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
    {
        ["document"] = Set(".pdf .doc .docx .odt .rtf .txt .md .xls .xlsx .xlsm .ods .csv .ppt .pptx .odp .epub"),
        ["pdf"] = Set(".pdf"),
        ["word"] = Set(".doc .docx .odt .rtf"),
        ["spreadsheet"] = Set(".xls .xlsx .xlsm .ods .csv"),
        ["presentation"] = Set(".ppt .pptx .odp"),
        ["text"] = Set(".txt .md .log .json .xml .ini .csv"),
        ["image"] = Set(".jpg .jpeg .png .gif .bmp .webp .heic .heif .tif .tiff .svg .raw .cr2 .nef .arw .dng .psd"),
        ["video"] = Set(".mp4 .mkv .avi .mov .wmv .webm .m4v .mpg .mpeg .flv .ts"),
        ["music"] = Set(".mp3 .flac .wav .ogg .m4a .aac .opus .wma .alac .aiff"),
        ["archive"] = Set(".zip .rar .7z .tar .gz .bz2 .xz .iso"),
        ["program"] = Set(".exe .msi .msix .appx"),
        [Folder] = Set(""),
    };

    private static HashSet<string> Set(string extensions) =>
        new(extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What opening would run instead of showing (a program, a script, a shortcut): never opened, only shown in its
    /// folder (plan §6.4: the AI does not run things).
    /// </summary>
    public static readonly IReadOnlySet<string> Runs = Set(
        ".exe .com .bat .cmd .ps1 .psm1 .vbs .vbe .js .jse .wsf .wsh .msi .msp .msix .appx .appxbundle .scr .pif .lnk .url .reg .hta .cpl .jar .appref-ms .application .gadget .inf .sys .dll .ocx .scf .library-ms .settingcontent-ms .py .pyw .sh");
}

/// <summary>The user's usual folders, by the names the model is given.</summary>
public static partial class UserFolders
{
    public static readonly IReadOnlyList<string> Names = ["desktop", "downloads", "documents", "pictures", "videos", "music"];

    /// <summary>The folder a name means: one of <see cref="Names"/> or an existing path; null otherwise.</summary>
    public static string? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var clean = name.Trim().Trim('"', '\'');
        if (Path.IsPathFullyQualified(clean) && Directory.Exists(clean)) return clean;
        return clean.ToLowerInvariant() switch
        {
            "desktop" => Known(new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"), Environment.SpecialFolder.DesktopDirectory),
            "downloads" or "download" => Known(new Guid("374DE290-123F-4565-9164-39C4925E467B"), null),
            "documents" => Known(new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7"), Environment.SpecialFolder.MyDocuments),
            "pictures" or "photos" => Known(new Guid("33E28130-4E1E-4676-835A-98395C3BC3BB"), Environment.SpecialFolder.MyPictures),
            "videos" => Known(new Guid("18989B1D-99B5-455B-841C-AB7C74E4DDFC"), Environment.SpecialFolder.MyVideos),
            "music" => Known(new Guid("4BD8D571-6D19-48D3-BE97-422220080E43"), Environment.SpecialFolder.MyMusic),
            _ => null,
        };
    }

    /// <summary>All of them that exist (the same one twice only once: Videos may be inside Pictures, Downloads on another drive).</summary>
    public static IReadOnlyList<string> All() =>
        Names.Select(Find).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Where the user has moved it (Downloads on D:, as the user has), or the usual place.</summary>
    private static string? Known(Guid id, Environment.SpecialFolder? fallback)
    {
        try
        {
            if (SHGetKnownFolderPath(id, 0, 0, out var pointer) == 0)
            {
                try
                {
                    if (Marshal.PtrToStringUni(pointer) is { Length: > 0 } path && Directory.Exists(path)) return path;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pointer);
                }
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // never on Windows
        }
        var usual = fallback is { } special ? Environment.GetFolderPath(special)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(usual) ? usual : null;
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);
}

/// <summary>Somewhere that already knows the files (the Windows Search index).</summary>
public interface IFileIndex
{
    /// <summary>What the index has for <paramref name="query"/> under <paramref name="scope"/> (null: everything it indexes).</summary>
    Task<IReadOnlyList<FoundFile>> QueryAsync(FileQuery query, string? scope, CancellationToken cancellationToken);
}

/// <summary>
/// The Windows Search index (stage 8.2), through its OLE DB provider: in milliseconds, by name and by content, but only
/// where Windows indexes (the user's profile, unless they added more).
/// </summary>
public sealed class WindowsSearchIndex : IFileIndex
{
    private const int Top = 80;

    public Task<IReadOnlyList<FoundFile>> QueryAsync(FileQuery query, string? scope, CancellationToken cancellationToken) => Task.Run<IReadOnlyList<FoundFile>>(() =>
    {
        var found = new List<FoundFile>();
        using var connection = new System.Data.OleDb.OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
        connection.Open();
        using var command = new System.Data.OleDb.OleDbCommand(Sql(query, scope, DateTime.UtcNow), connection) { CommandTimeout = 4 };
        using var reader = command.ExecuteReader();
        var content = query.Words.Count > 0;
        while (reader.Read() && !cancellationToken.IsCancellationRequested)
        {
            if (reader.GetValue(0) is not string url || PathOf(url) is not { } path) continue;
            var folder = reader.GetValue(4) is string type && type == "Directory";
            found.Add(new FoundFile(path, Date(reader.GetValue(1)), Date(reader.GetValue(2)), reader.GetValue(3) is { } size and not DBNull ? Convert.ToInt64(size, CultureInfo.InvariantCulture) : 0, folder, content));
        }
        return found;
    }, cancellationToken);

    /// <summary>
    /// The query in Windows Search's SQL: the words anywhere (name or content) or as the start of a word of the name;
    /// the kind by extension; recent by date (in UTC, as the index keeps them).
    /// </summary>
    public static string Sql(FileQuery query, string? scope, DateTime utcNow)
    {
        var where = new List<string> { $"SCOPE='file:{Quote(scope?.Replace('\\', '/') ?? string.Empty)}'" };
        if (query.Terms is { Count: > 0 } words)
            where.Add($"(FREETEXT(*, '{Quote(string.Join(' ', words))}') OR CONTAINS(System.FileName, '{Quote(string.Join(" OR ", words.Select(w => $"\"{w}*\"")))}'))");
        if (query.WantsFolders) where.Add("System.ItemType = 'Directory'");
        else if (query.Extensions is { Count: > 0 } extensions)
            where.Add("(" + string.Join(" OR ", extensions.Select(e => $"System.ItemType = '{Quote(e)}'")) + ")");
        if (query.Days is { } days)
            where.Add($"System.DateModified >= '{(utcNow - TimeSpan.FromDays(days)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}'");
        var order = query.Words.Count > 0 && !query.Newest ? "System.Search.Rank DESC" : "System.DateModified DESC";
        return $"SELECT TOP {Top} System.ItemUrl, System.DateModified, System.DateCreated, System.Size, System.ItemType FROM SystemIndex WHERE {string.Join(" AND ", where)} ORDER BY {order}";
    }

    private static string Quote(string text) => text.Replace("'", "''", StringComparison.Ordinal);

    private static string? PathOf(string url) =>
        url.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? url[5..].Replace('/', '\\') : null;

    /// <summary>
    /// The index keeps its dates in UTC and gives them without saying so (<c>Unspecified</c>): read as local, everything it
    /// found was two hours older than what the walk found, and «lo último de Descargas» mixed the two.
    /// </summary>
    internal static DateTime Date(object value) => value is DateTime date
        ? (date.Kind == DateTimeKind.Local ? date : DateTime.SpecifyKind(date, DateTimeKind.Utc).ToLocalTime())
        : DateTime.MinValue;
}

/// <summary>
/// Finding files (stage 8.2): the Windows Search index and, since it only knows what Windows indexes (the user's
/// Downloads may be on another drive), a quick walk through the usual folders, shallow first and with a budget. Never
/// in hidden or system folders nor in the program's folders (node_modules, .git…). Keeps the last list of each assistant
/// so «abre el segundo» can follow.
/// </summary>
public sealed class FileSearch(IFileIndex? index = null, Func<IReadOnlyList<string>>? folders = null, TimeProvider? time = null)
{
    /// <summary>How many go back to the model.</summary>
    public const int Shown = 8;

    /// <summary>The walk through the folders: how many entries and how long, at most.</summary>
    private const int WalkEntries = 40_000;
    private static readonly TimeSpan WalkTime = TimeSpan.FromSeconds(2.5);
    private const int WalkDepth = 5;

    /// <summary>How long a list can be referred to by number.</summary>
    public static readonly TimeSpan ListLife = TimeSpan.FromMinutes(15);

    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".svn", ".hg", ".vs", ".idea", "bin", "obj", "__pycache__", ".venv", "venv", "site-packages", "$RECYCLE.BIN",
        "System Volume Information", "AppData", "packages", "pkgs", "Miniconda3", "anaconda3", "miniforge3", "scoop", "go", "vcpkg",
        "Program Files", "Program Files (x86)", "Windows",
    };

    private readonly IFileIndex _index = index ?? new WindowsSearchIndex();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyList<FoundFile> Files)> _last = new(StringComparer.Ordinal);

    /// <summary>The best matches, best first (newest first when asked or when there are no words).</summary>
    public async Task<IReadOnlyList<FoundFile>> FindAsync(FileQuery query, CancellationToken cancellationToken) =>
        (await SearchAsync(query, cancellationToken).ConfigureAwait(false)).Files;

    /// <summary>The best <see cref="Shown"/> matches and how many there were in all (at least those).</summary>
    public async Task<(IReadOnlyList<FoundFile> Files, int Total)> SearchAsync(FileQuery query, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var scope = UserFolders.Find(query.Folder);
        var walked = scope is not null ? [scope] : (folders ?? UserFolders.All)();
        var fromIndex = Task.Run(async () =>
        {
            try
            {
                return await _index.QueryAsync(query, scope, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warn($"Asistente: el índice de búsqueda de Windows no contesta: {ex.Message}");
                return (IReadOnlyList<FoundFile>)[];
            }
        }, cancellationToken);
        var fromWalk = Task.Run(() => Walk(walked, query, cancellationToken), cancellationToken);
        var all = (await fromIndex.ConfigureAwait(false)).Where(f => !Hidden(f.Path)).Concat(await fromWalk.ConfigureAwait(false));
        var words = query.Words;
        var cutoff = query.Days is { } days ? _time.GetLocalNow().DateTime - TimeSpan.FromDays(days) : DateTime.MinValue;
        var ranked = all
            // Found by both: what the walk read from the disk now (the index may be behind), and whether the index found
            // it by its content.
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last() with { InContent = g.Any(f => f.InContent) })
            .Where(f => f.When >= cutoff && KindFits(f, query))
            .Select(f => (File: f, Score: Score(f, words)))
            .Where(p => words.Count == 0 || p.Score >= 0.5 || p.File.InContent)
            .OrderByDescending(p => query.Newest || words.Count == 0 ? 0 : Math.Round(p.Score, 1))
            .ThenByDescending(p => p.File.When)
            .Select(p => p.File)
            .ToList();
        var best = ranked.Take(Shown).ToList();
        Log.Info($"Asistente: archivos «{query.Text}» {query.Kind} {query.Folder} {query.Days} → {best.Count} de {ranked.Count} en {watch.ElapsedMilliseconds} ms");
        return (best, ranked.Count);
    }

    /// <summary>What a folder holds at its top level, at most.</summary>
    public const int Listed = 15;

    /// <summary>Entries of a folder inside a listed one that are counted, at most (a big one is «more than…»).</summary>
    private const int CountedInside = 2000;

    /// <summary>
    /// «¿Qué tengo en Descargas?»: what is directly in <paramref name="folder"/> (not inside its folders), newest first, at
    /// most <see cref="Listed"/>; how many folders and files it has in all; and, for each folder listed, what is in it
    /// («8 videos, 1 folder»).
    /// </summary>
    public static (IReadOnlyList<FoundFile> Entries, int Folders, int Files, IReadOnlyDictionary<string, string> Inside) List(string folder)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System, ReturnSpecialDirectories = false };
        var entries = new List<FoundFile>();
        try
        {
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options))
            {
                if (IsNoise(entry.Name)) continue;
                entries.Add(new FoundFile(entry.FullName, entry.LastWriteTime, entry.CreationTime, entry is FileInfo info ? info.Length : 0, entry is DirectoryInfo));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn($"Asistente: no se ha podido mirar {folder}: {ex.Message}");
        }
        var shown = entries.OrderByDescending(e => e.When).Take(Listed).ToList();
        var inside = shown.Where(e => e.IsFolder).ToDictionary(e => e.Path, e => Contents(e.Path, options), StringComparer.OrdinalIgnoreCase);
        return (shown, entries.Count(e => e.IsFolder), entries.Count(e => !e.IsFolder), inside);
    }

    /// <summary>What is in a folder, for the model: «8 video, 1 text, 1 folder» (empty: «empty»).</summary>
    private static string Contents(string folder, EnumerationOptions options)
    {
        var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
        var seen = 0;
        try
        {
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options))
            {
                if (IsNoise(entry.Name)) continue;
                if (++seen > CountedInside) break;
                var kind = entry is DirectoryInfo ? FileKinds.Folder
                    : FileKinds.All.Where(k => k.Key is not ("pdf" or "word" or "text" or FileKinds.Folder)).FirstOrDefault(k => k.Value.Contains(entry.Extension)).Key ?? "other";
                kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return "cannot be read";
        }
        if (kinds.Count == 0) return "empty";
        var text = string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Value} {k.Key}"));
        return seen > CountedInside ? "more than " + CountedInside + ": " + text : text;
    }

    /// <summary>Keeps <paramref name="files"/> as <paramref name="assistant"/>'s last list.</summary>
    public void Remember(string? assistant, IReadOnlyList<FoundFile> files) => _last[assistant ?? string.Empty] = (_time.GetUtcNow(), files);

    /// <summary>Number <paramref name="number"/> (from 1) of its last list, if it is recent.</summary>
    public FoundFile? Numbered(string? assistant, int number) =>
        _last.TryGetValue(assistant ?? string.Empty, out var last) && _time.GetUtcNow() - last.At < ListLife && number >= 1 && number <= last.Files.Count
            ? last.Files[number - 1] : null;

    /// <summary>How well the words fit its name (and, half as much, its folder's): 0–1.</summary>
    public static double Score(FoundFile file, IReadOnlyList<string> words)
    {
        if (words.Count == 0) return 1;
        var name = NameWords(file.IsFolder ? file.Name : Path.GetFileNameWithoutExtension(file.Name));
        var joined = string.Concat(name);
        var parent = NameWords(Path.GetFileName(file.Folder) ?? string.Empty);
        double total = 0;
        foreach (var word in words)
        {
            var inName = name.Length == 0 ? 0 : name.Max(n => TextSimilarity.WordSimilarity(word, n));
            // «facturaluz2026», «FacturaLuz»: the word inside a name written all together.
            if (word.Length >= 4 && joined.Contains(word, StringComparison.Ordinal)) inName = 1;
            var inFolder = parent.Length == 0 ? 0 : parent.Max(n => TextSimilarity.WordSimilarity(word, n)) * 0.5;
            var best = Math.Max(inName, inFolder);
            total += best >= 0.8 ? best : 0;
        }
        return total / words.Count;
    }

    /// <summary>The words of a file name, also where it is written all together («FacturaLuz2026» = factura, luz, 2026).</summary>
    public static string[] NameWords(string name)
    {
        var spaced = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && (char.IsLower(name[i - 1]) && char.IsUpper(name[i]) || char.IsLetter(name[i - 1]) != char.IsLetter(name[i]) && char.IsLetterOrDigit(name[i - 1]) && char.IsLetterOrDigit(name[i])))
                spaced.Append(' ');
            spaced.Append(name[i]);
        }
        return TextSimilarity.Words(spaced.ToString());
    }

    private static bool KindFits(FoundFile file, FileQuery query) =>
        query.WantsFolders ? file.IsFolder
        : query.Extensions is { } extensions ? !file.IsFolder && extensions.Contains(Path.GetExtension(file.Path))
        : true;

    private static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string Profiles = Path.GetDirectoryName(Profile) ?? Profile;

    /// <summary>
    /// The index also has the Start menu, the program data, other users' profiles and what programs keep in the user's
    /// (Miniconda's packages, dot folders): not what the user looks for.
    /// </summary>
    private static bool Hidden(string path)
    {
        if (path.StartsWith(Profiles + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !path.StartsWith(Profile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Start Menu\", StringComparison.OrdinalIgnoreCase) || path.Contains(@"\ProgramData\", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\$Recycle.Bin", StringComparison.OrdinalIgnoreCase))
            return true;
        if (IsNoise(Path.GetFileName(path))) return true;
        var folders = path.Split(Path.DirectorySeparatorChar);
        for (var i = 1; i < folders.Length - 1; i++)
            if (IsProgramsFolder(folders[i])) return true;
        return false;
    }

    /// <summary>Settings and leftovers of programs (.claude.json, ~$informe.docx, desktop.ini), not the user's files.</summary>
    private static bool IsNoise(string name) =>
        name.StartsWith('.') || name.StartsWith('~') || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || name.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase);

    /// <summary>Where programs keep their own things: never walked into, and nothing from there out of the index.</summary>
    private static bool IsProgramsFolder(string name) => name.StartsWith('.') || Skipped.Contains(name);

    /// <summary>Through the folders, shallow first, within the budget; what fits the kind, the date and (if any) the words.</summary>
    private static List<FoundFile> Walk(IReadOnlyList<string> roots, FileQuery query, CancellationToken cancellationToken)
    {
        var found = new List<FoundFile>();
        var words = query.Words;
        var watch = Stopwatch.StartNew();
        var seen = 0;
        var queue = new Queue<(DirectoryInfo Folder, int Depth)>(roots.Select(r => (new DirectoryInfo(r), 0)));
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System, ReturnSpecialDirectories = false };
        while (queue.Count > 0 && seen < WalkEntries && watch.Elapsed < WalkTime && !cancellationToken.IsCancellationRequested)
        {
            var (folder, depth) = queue.Dequeue();
            IEnumerable<FileSystemInfo> entries;
            try { entries = folder.EnumerateFileSystemInfos("*", options); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { continue; }
            try
            {
                foreach (var entry in entries)
                {
                    if (++seen > WalkEntries) break;
                    if (entry is DirectoryInfo sub)
                    {
                        if (IsProgramsFolder(sub.Name) || sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                        if (depth + 1 < WalkDepth) queue.Enqueue((sub, depth + 1));
                    }
                    if (IsNoise(entry.Name)) continue;
                    var file = new FoundFile(entry.FullName, entry.LastWriteTime, entry.CreationTime, entry is FileInfo info ? info.Length : 0, entry is DirectoryInfo);
                    if (!KindFits(file, query)) continue;
                    if (words.Count > 0 && Score(file, words) < 0.5) continue;
                    found.Add(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return found;
    }

    /// <summary>Opens a file with its program, or shows it selected in its folder (as the user: this process is not elevated).</summary>
    public static bool Open(FoundFile file, bool inFolder)
    {
        try
        {
            using var _ = inFolder
                ? Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file.Path}\"") { UseShellExecute = true })
                : Process.Start(new ProcessStartInfo(file.Path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            Log.Warn($"Asistente: no se ha podido abrir {file.Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>The list for the model, numbered.</summary>
    public static string Describe(IReadOnlyList<FoundFile> files)
    {
        var lines = files.Select((f, i) =>
            $"{i + 1}. {f.Name}{(f.IsFolder ? " (folder)" : string.Empty)} — in {f.Folder} — {(f.When == DateTime.MinValue ? "?" : f.When.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))}{(f.IsFolder ? string.Empty : " — " + SizeOf(f.Size))}");
        return string.Join('\n', lines);
    }

    public static string SizeOf(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB".Replace(',', '.'),
        < 1024L * 1024 * 1024 => (bytes / 1024.0 / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
        _ => (bytes / 1024.0 / 1024 / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " GB",
    };
}

/// <summary>«Búscame el PDF de la factura de la luz», «¿dónde guardé las fotos de la boda?» (stage 8.2): only finds.</summary>
public sealed class FindFilesTool(FileSearch search) : ITool
{
    public string Name => "find_files";

    public string Description => "Finds files or folders on the PC by words of their name or content, kind, folder and date. Only finds and lists them (numbered); to open one use open_file.";

    public string ParametersSchemaJson { get; } = Schema(withNumber: false);

    internal static string Schema(bool withNumber) =>
        "{\"type\":\"object\",\"properties\":{"
        + (withNumber ? "\"number\":{\"type\":\"integer\",\"description\":\"The number of a file in the last find_files list\"}," : string.Empty)
        + "\"text\":{\"type\":\"string\",\"description\":\"A few key words of its name or content, without articles (e.g. factura luz)\"},"
        + $"\"kind\":{{\"type\":\"string\",\"enum\":[{string.Join(",", FileKinds.All.Keys.Select(k => $"\"{k}\""))}]}},"
        + $"\"folder\":{{\"type\":\"string\",\"description\":\"Only when the user says where: one of {string.Join(", ", UserFolders.Names)}, or a full path; empty: anywhere\"}},"
        + "\"days\":{\"type\":\"integer\",\"description\":\"Only what changed or arrived in the last N days\"},"
        + "\"sort\":{\"type\":\"string\",\"enum\":[\"best\",\"newest\"],\"description\":\"newest for the latest ones (\\\"the last thing I downloaded\\\": folder downloads, newest)\"}"
        + (withNumber ? ",\"in_folder\":{\"type\":\"boolean\",\"description\":\"Show it selected in its folder instead of opening it (\\\"show me where it is\\\", \\\"in its folder\\\")\"}" : string.Empty)
        + "}}";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => Loc.T("tool.find_files.intent", What(FileQuery.Of(arguments)));

    /// <summary>What is looked for, in words for people: the words, else the kind, else «lo último».</summary>
    internal static string What(FileQuery query) =>
        ToolText.Clean(query.Text) is { Length: > 0 } text ? text
        : query.Kind is { Length: > 0 } kind && FileKinds.All.ContainsKey(kind) ? Loc.T("tool.files.kind." + kind.ToLowerInvariant())
        : Loc.T("tool.files.latest");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var query = FileQuery.Of(arguments);
        if (query.Folder is { Length: > 0 } folder && UserFolders.Find(folder) is null)
            return ToolResult.Fail($"There is no folder \"{folder}\": use one of {string.Join(", ", UserFolders.Names)} or a full path.");
        // «¿Qué tengo en Descargas?»: what is in it, not everything inside its folders newest first (it said «tienes 8
        // cosas»: six folders of a series and the episodes inside two of them, cut at eight).
        if (query is { Folder: { Length: > 0 } listed, Words.Count: 0, Kind: null, Days: null } && UserFolders.Find(listed) is { } path)
            return Listing(path, context);
        var (files, total) = await search.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        // Small models name a folder the user did not say («la carpeta de K-Desktop» → desktop): then anywhere, saying so.
        var elsewhere = string.Empty;
        if (files.Count == 0 && query.Folder is { Length: > 0 } given && (query.Words.Count > 0 || query.Kind is not null))
        {
            (files, total) = await search.SearchAsync(query with { Folder = null }, cancellationToken).ConfigureAwait(false);
            if (files.Count > 0) elsewhere = $"Nothing in {given}; elsewhere: ";
        }
        search.Remember(context.Assistant, files);
        var done = Loc.T("tool.find_files.done", What(query), files.Count);
        if (files.Count == 0)
            return new ToolResult("Nothing found. Say so (you may suggest other words or another folder).") { Done = done, Reply = Loc.T("tool.find_files.none") };
        var count = total > files.Count ? $"{total} found, these are the first {files.Count}" : $"Found {files.Count}";
        return new ToolResult($"{elsewhere}{count}{(query.Newest || query.Words.Count == 0 ? ", newest first" : ", best first")}:\n{FileSearch.Describe(files)}\nTell the user briefly what you found (names, not full paths){(total > files.Count ? $"; there are {total} in all, not {files.Count}" : string.Empty)}. To open one, call open_file with its number.")
        {
            Done = done,
        };
    }

    /// <summary>What is directly in a folder, with its totals and what each folder in it holds.</summary>
    private ToolResult Listing(string path, ToolContext context)
    {
        var (entries, folders, files, inside) = FileSearch.List(path);
        search.Remember(context.Assistant, entries);
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : path;
        var done = Loc.T("tool.find_files.listed", name, folders + files);
        Log.Info($"Asistente: carpeta {path} → {folders} carpeta(s) y {files} archivo(s)");
        if (entries.Count == 0)
            return new ToolResult($"{path} is empty. Say so; do not look for or open anything elsewhere instead.") { Done = done, Reply = Loc.T("tool.find_files.empty", name) };
        var lines = entries.Select((e, i) => e.IsFolder
            ? $"{i + 1}. {e.Name} (folder: {inside.GetValueOrDefault(e.Path, "?")}) — {e.When.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}"
            : $"{i + 1}. {e.Name} — {e.When.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} — {FileSearch.SizeOf(e.Size)}");
        var more = folders + files > entries.Count ? $" These are the newest {entries.Count}." : string.Empty;
        return new ToolResult($"Directly in {path} (not counting what is inside its folders) there are {folders} folder(s) and {files} file(s), newest first.{more}\n{string.Join('\n', lines)}\nTell the user how many there are and what they are, briefly (group alike ones: «six seasons of a series»); names, not full paths. To open one, call open_file with its number.")
        {
            Done = done,
        };
    }
}

/// <summary>
/// «Abre lo último que descargué», «abre el segundo», «enséñamelo en la carpeta» (stage 8.2): opens a file with its
/// program or shows it in its folder. Programs, scripts and shortcuts are never opened, only shown (plan §6.4).
/// </summary>
public sealed class OpenFileTool(FileSearch search, Func<FoundFile, bool, bool>? open = null) : ITool
{
    public string Name => "open_file";

    public string Description => "Opens a file or folder of the PC (a document, manual, photo, video…) with its program, or shows it in its folder: a number from the last find_files list, or search words (it opens the best match). It never runs programs or scripts. Not for programs from the Start menu (open_app) nor web pages.";

    public string ParametersSchemaJson { get; } = FindFilesTool.Schema(withNumber: true);

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) =>
        arguments.GetNumber("number") is { } number ? Loc.T("tool.open_file.intentNumber", (int)number)
        : Loc.T("tool.open_file.intent", FindFilesTool.What(FileQuery.Of(arguments)));

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        FoundFile? file;
        if (arguments.GetNumber("number") is { } number)
        {
            file = search.Numbered(context.Assistant, (int)Math.Round(number));
            if (file is null) return ToolResult.Fail("There is no such number in a recent list: search with find_files first, or give search words.");
        }
        else
        {
            var query = FileQuery.Of(arguments);
            if (query.Words.Count == 0 && query.Kind is null && query.Folder is null && query.Days is null)
                return ToolResult.Fail("Say which file: a number from the last list or words of its name.");
            if (query.Folder is { Length: > 0 } folder && UserFolders.Find(folder) is null)
                return ToolResult.Fail($"There is no folder \"{folder}\": use one of {string.Join(", ", UserFolders.Names)} or a full path.");
            var files = await search.FindAsync(query, cancellationToken).ConfigureAwait(false);
            if (files.Count == 0)
                return new ToolResult("Nothing found to open. Say so.") { Done = Loc.T("tool.find_files.done", FindFilesTool.What(query), 0), Reply = Loc.T("tool.find_files.none"), Failed = true };
            search.Remember(context.Assistant, files);
            file = files[0];
        }
        if (!file.IsFolder && !File.Exists(file.Path) || file.IsFolder && !Directory.Exists(file.Path))
            return ToolResult.Fail($"{file.Name} is not there any more.");
        var runs = !file.IsFolder && FileKinds.Runs.Contains(Path.GetExtension(file.Path));
        var inFolder = runs || string.Equals(arguments.GetString("in_folder"), "true", StringComparison.OrdinalIgnoreCase);
        context.Show?.Invoke(ToolGesture.WaitForWindow(inFolder ? "explorer" : null));
        if (!(open ?? FileSearch.Open)(file, inFolder)) return ToolResult.Fail($"{file.Name} did not open.");
        var said = runs ? $"Shown {file.Name} in its folder: it is a program or script and you never run those. Say so."
            : inFolder ? $"Shown {file.Name} selected in its folder ({file.Folder})."
            : $"Opened {file.Name} ({file.Folder}).";
        return new ToolResult(said)
        {
            Done = Loc.T(inFolder ? "tool.open_file.doneFolder" : "tool.open_file.done", file.Name),
            Reply = Loc.T(runs ? "tool.open_file.replyRuns" : inFolder ? "tool.open_file.replyFolder" : "tool.open_file.reply", file.Name),
            Acted = true,
        };
    }
}

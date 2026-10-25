using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using KDesktop.Core.Model;
using KDesktop.Core.Native;

namespace KDesktop.Core.Platform;

/// <summary>What «Archivos y carpetas» has to do, with the paths already expanded ({fecha}, {hora}).</summary>
public sealed record FileJob(
    FileOperation Operation,
    IReadOnlyList<string> Sources,
    string Destination,
    bool Subfolders = false,
    string NewName = "",
    FileExists Exists = FileExists.IfNewer,
    bool Recycle = true,
    BackupMode BackupMode = BackupMode.Mirror,
    bool MirrorDelete = false,
    bool BackupZip = false,
    int Keep = 5,
    DateTime? Now = null);

/// <summary>How it went. <paramref name="Refused"/>: a language key (with <paramref name="RefusedArg"/>) when it did nothing on purpose.</summary>
public sealed record FileJobResult(int Done, int Skipped, int Failed, long Bytes, string? Refused = null, string? RefusedArg = null, string? Output = null);

/// <summary>Bytes done of the total, and the file in hand.</summary>
public readonly record struct FileJobProgress(long Bytes, long Total, string Current);

/// <summary>
/// The work of «Archivos y carpetas». Safe with nesting: the destination (and what it creates) is never walked as a
/// source, links and junctions are not followed (no loops), and what would copy, move or mirror a folder onto itself is
/// refused before touching anything. Each file that fails is counted and logged; the rest go on.
/// </summary>
public static partial class FileJobs
{
    private const int Buffer = 1 << 20;

    /// <summary>One file to handle: where it is, and its path below the destination («Fotos\2024\a.jpg»).</summary>
    private sealed record Item(string Path, string Relative, long Length, DateTime Written);

    /// <summary>What the sources are once read: files (with their folders) and the folders given whole.</summary>
    private sealed class Plan
    {
        public List<Item> Files { get; } = [];
        public List<string> Dirs { get; } = [];
        public List<(string Path, string Name)> Folders { get; } = [];
        public List<string> Missing { get; } = [];
        public long Total => Files.Sum(f => f.Length);
    }

    private sealed class Counter
    {
        public int Done, Skipped, Failed;

        /// <summary>Bytes really written; <see cref="Seen"/> also counts what was left as it was (for the progress).</summary>
        public long Bytes, Seen;
    }

    public static Task<FileJobResult> RunAsync(FileJob job, IProgress<FileJobProgress>? progress, CancellationToken token) =>
        Task.Run(() => Run(job, progress, token), token);

    private static FileJobResult Run(FileJob job, IProgress<FileJobProgress>? progress, CancellationToken token)
    {
        var dest = Full(job.Destination);
        return job.Operation switch
        {
            FileOperation.CreateFolder => CreateFolder(dest),
            FileOperation.Delete => Delete(job, token),
            FileOperation.Rename => Rename(job, token),
            FileOperation.Unzip => Unzip(job, dest, progress, token),
            FileOperation.Zip => Zip(job, dest, null, progress, token),
            FileOperation.Backup when job.BackupMode == BackupMode.Dated => Dated(job, dest, progress, token),
            _ => CopyOrMove(job, dest, progress, token),
        };
    }

    // ── reading the sources ──────────────────────────────────────────────

    private static string? Full(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')))); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    /// <summary>«840 KB», «1,2 GB» in <paramref name="culture"/>.</summary>
    public static string Size(long bytes, CultureInfo culture)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return string.Create(culture, $"{value.ToString(unit == 0 || value >= 100 ? "0" : "0.#", culture)} {units[unit]}");
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or something inside it.</summary>
    public static bool IsInside(string path, string folder) =>
        path.Equals(folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsPattern(string path) => Path.GetFileName(path).IndexOfAny(['*', '?']) >= 0;

    private static Plan Read(IEnumerable<string> sources, bool subfolders, IReadOnlyCollection<string> excluded, CancellationToken token)
    {
        var plan = new Plan();
        foreach (var raw in sources)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var text = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
            if (IsPattern(text))
            {
                var folder = Full(Path.GetDirectoryName(text));
                if (folder is null || !Directory.Exists(folder))
                {
                    plan.Missing.Add(raw);
                    continue;
                }
                Walk(plan, folder, string.Empty, Path.GetFileName(text), subfolders, Below(excluded, folder), token);
                continue;
            }
            var full = Full(text);
            if (full is null) plan.Missing.Add(raw);
            else if (Directory.Exists(full))
            {
                var name = Path.GetFileName(full);
                if (name.Length == 0) name = full.TrimEnd(':', '\\').Replace(":", "", StringComparison.Ordinal); // a drive: «D»
                plan.Folders.Add((full, name));
                // The destination itself as a source: listed (so it is refused), never read.
                if (excluded.Any(e => e.Equals(full, StringComparison.OrdinalIgnoreCase))) continue;
                plan.Dirs.Add(name);
                Walk(plan, full, name, "*", true, Below(excluded, full), token);
            }
            else if (File.Exists(full)) Add(plan, new FileInfo(full), Path.GetFileName(full));
            else plan.Missing.Add(raw);
        }
        return plan;
    }

    /// <summary>
    /// The excluded folders that hang from <paramref name="root"/>: the loop happens only when the destination is inside
    /// what is being read (a source inside the destination is read whole).
    /// </summary>
    private static string[] Below(IReadOnlyCollection<string> excluded, string root) =>
        [.. excluded.Where(e => IsInside(e, root) && !e.Equals(root, StringComparison.OrdinalIgnoreCase))];

    /// <summary>The files of <paramref name="folder"/> that match, going down if asked; never into an excluded folder or a link.</summary>
    private static void Walk(Plan plan, string folder, string relative, string pattern, bool deep, IReadOnlyCollection<string> excluded, CancellationToken token)
    {
        var pending = new Stack<(string Path, string Relative)>();
        pending.Push((folder, relative));
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var (dir, rel) = pending.Pop();
            DirectoryInfo info;
            try { info = new DirectoryInfo(dir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { continue; }
            try
            {
                foreach (var file in info.EnumerateFiles(pattern))
                    if (!excluded.Any(e => IsInside(file.FullName, e))) Add(plan, file, Path.Combine(rel, file.Name));
                if (!deep) continue;
                foreach (var sub in info.EnumerateDirectories())
                {
                    if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint) || excluded.Any(e => IsInside(sub.FullName, e))) continue;
                    var subRel = Path.Combine(rel, sub.Name);
                    if (pattern == "*") plan.Dirs.Add(subRel);
                    pending.Push((sub.FullName, subRel));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Archivos: no se pudo leer {dir}: {ex.Message}");
            }
        }
    }

    private static void Add(Plan plan, FileInfo file, string relative) =>
        plan.Files.Add(new Item(file.FullName, relative, file.Length, file.LastWriteTimeUtc));

    private static FileJobResult Missing(Plan plan, Counter count)
    {
        foreach (var missing in plan.Missing) Log.Warn($"Archivos: no existe {missing}");
        return new FileJobResult(count.Done, count.Skipped, count.Failed + plan.Missing.Count, count.Bytes);
    }

    // ── copy, move, mirror ───────────────────────────────────────────────

    private static FileJobResult CopyOrMove(FileJob job, string? dest, IProgress<FileJobProgress>? progress, CancellationToken token)
    {
        if (dest is null) return new FileJobResult(0, 0, 0, 0, "files.noDestination");
        var move = job.Operation == FileOperation.Move;
        var mirror = job.Operation == FileOperation.Backup;
        var plan = Read(job.Sources, job.Subfolders, [dest], token);

        // A folder onto itself, or moved/mirrored around what contains the destination: nothing is touched.
        foreach (var (path, name) in plan.Folders)
        {
            if (Path.Combine(dest, name).Equals(path, StringComparison.OrdinalIgnoreCase) || dest.Equals(path, StringComparison.OrdinalIgnoreCase))
                return new FileJobResult(0, 0, 0, 0, "files.sameFolder", path);
            if (move && IsInside(dest, path)) return new FileJobResult(0, 0, 0, 0, "files.moveInside", path);
        }

        var count = new Counter();
        var total = plan.Total;
        Directory.CreateDirectory(dest);
        foreach (var dir in plan.Dirs) Try(count, dir, () => Directory.CreateDirectory(Path.Combine(dest, dir)), counts: false);
        var exists = mirror ? FileExists.IfNewer : job.Exists;
        foreach (var item in plan.Files)
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(dest, item.Relative);
            if (target.Equals(item.Path, StringComparison.OrdinalIgnoreCase))
            {
                count.Skipped++;
                continue;
            }
            Try(count, item.Path, () =>
            {
                target = Resolve(target, item, exists);
                if (target.Length == 0)
                {
                    count.Skipped++;
                    count.Seen += item.Length;
                    return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (move && MoveFast(item.Path, target))
                {
                    count.Bytes += item.Length;
                    count.Seen += item.Length;
                }
                else
                {
                    Copy(item, target, count, total, progress, token);
                    if (move) File.Delete(item.Path);
                }
                count.Done++;
                progress?.Report(new FileJobProgress(count.Seen, total, item.Relative));
            });
        }

        if (move)
            foreach (var (path, _) in plan.Folders) DeleteEmptyFolders(path);
        if (mirror && job.MirrorDelete)
            foreach (var (path, name) in plan.Folders) Clean(path, Path.Combine(dest, name), count, token);
        return Missing(plan, count);
    }

    /// <summary>
    /// Where the file goes after looking at what is already there: the same path, another name («a (2).txt»), or ""
    /// to leave it (skip, or not newer).
    /// </summary>
    private static string Resolve(string target, Item item, FileExists exists)
    {
        if (!File.Exists(target)) return target;
        switch (exists)
        {
            case FileExists.Skip:
                return string.Empty;
            case FileExists.IfNewer:
                var there = new FileInfo(target);
                return item.Written > there.LastWriteTimeUtc.AddSeconds(2) || item.Length != there.Length ? target : string.Empty;
            case FileExists.KeepBoth:
                return Unique(target);
            default:
                return target;
        }
    }

    /// <summary>«a.txt» → «a (2).txt», «a (3).txt»… the first that is free.</summary>
    public static string Unique(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, string.Create(CultureInfo.InvariantCulture, $"{name} ({i}){ext}"));
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    private static bool MoveFast(string from, string to)
    {
        if (!string.Equals(Path.GetPathRoot(from), Path.GetPathRoot(to), StringComparison.OrdinalIgnoreCase)) return false;
        File.Move(from, to, overwrite: true);
        return true;
    }

    /// <summary>Copies by blocks (progress, and Esc stops it); a file left half way is removed.</summary>
    private static void Copy(Item item, string target, Counter count, long total, IProgress<FileJobProgress>? progress, CancellationToken token)
    {
        var done = false;
        try
        {
            using (var source = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, Buffer, FileOptions.SequentialScan))
            using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, Buffer))
            {
                var buffer = new byte[Buffer];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    count.Bytes += read;
                    count.Seen += read;
                    progress?.Report(new FileJobProgress(count.Seen, total, item.Relative));
                }
            }
            File.SetLastWriteTimeUtc(target, item.Written);
            done = true;
        }
        finally
        {
            if (!done) try { File.Delete(target); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Takes out of <paramref name="mirror"/> what is no longer in <paramref name="source"/> (only inside the mirror).</summary>
    private static void Clean(string source, string mirror, Counter count, CancellationToken token)
    {
        if (!Directory.Exists(mirror)) return;
        foreach (var file in Directory.EnumerateFiles(mirror, "*", SearchOption.AllDirectories).ToList())
        {
            token.ThrowIfCancellationRequested();
            if (!IsInside(file, mirror)) continue;
            var original = Path.Combine(source, Path.GetRelativePath(mirror, file));
            if (!File.Exists(original)) Try(count, file, () => File.Delete(file), counts: false);
        }
        foreach (var dir in Directory.EnumerateDirectories(mirror, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
        {
            var original = Path.Combine(source, Path.GetRelativePath(mirror, dir));
            if (!Directory.Exists(original) && IsInside(dir, mirror)) Try(count, dir, () => Directory.Delete(dir, recursive: true), counts: false);
        }
    }

    private static void DeleteEmptyFolders(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var dir in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).Append(folder).ToList())
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
    }

    // ── dated backups ────────────────────────────────────────────────────

    private static FileJobResult Dated(FileJob job, string? dest, IProgress<FileJobProgress>? progress, CancellationToken token)
    {
        if (dest is null) return new FileJobResult(0, 0, 0, 0, "files.noDestination");
        var now = job.Now ?? DateTime.Now;
        var first = job.Sources.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) is { } s && Full(IsPattern(s) ? Path.GetDirectoryName(s) : s) is { } p
            ? Path.GetFileName(p) : string.Empty;
        var name = first.Length > 0 ? first : "Backup";
        var stamp = now.ToString("yyyy-MM-dd HH.mm", CultureInfo.InvariantCulture);
        var output = Path.Combine(dest, $"{name} {stamp}");
        if (File.Exists(output + ".zip") || Directory.Exists(output)) output = Path.Combine(dest, $"{name} {now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture)}");

        var inner = job with { Exists = FileExists.Replace, Operation = job.BackupZip ? FileOperation.Zip : FileOperation.Copy };
        var result = job.BackupZip
            ? Zip(inner, output + ".zip", dest, progress, token)
            : CopyInto(inner with { Destination = output }, Full(output), dest, progress, token);
        if (result.Refused is null && result.Done > 0 && job.Keep > 0) Prune(dest, name, job.Keep);
        return result with { Output = job.BackupZip ? output + ".zip" : output };
    }

    // CopyOrMove with the whole backup folder left out (the earlier copies are not copied again).
    private static FileJobResult CopyInto(FileJob job, string? dest, string excludedFolder, IProgress<FileJobProgress>? progress, CancellationToken token)
    {
        if (dest is null) return new FileJobResult(0, 0, 0, 0, "files.noDestination");
        var plan = Read(job.Sources, job.Subfolders, [dest, excludedFolder], token);
        foreach (var (path, _) in plan.Folders)
            if (excludedFolder.Equals(path, StringComparison.OrdinalIgnoreCase)) return new FileJobResult(0, 0, 0, 0, "files.sameFolder", path);
        var count = new Counter();
        var total = plan.Total;
        Directory.CreateDirectory(dest);
        foreach (var dir in plan.Dirs) Try(count, dir, () => Directory.CreateDirectory(Path.Combine(dest, dir)), counts: false);
        foreach (var item in plan.Files)
        {
            token.ThrowIfCancellationRequested();
            Try(count, item.Path, () =>
            {
                var target = Path.Combine(dest, item.Relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Copy(item, target, count, total, progress, token);
                count.Done++;
            });
        }
        return Missing(plan, count);
    }

    [GeneratedRegex(@"^ \d{4}-\d{2}-\d{2} \d{2}\.\d{2}(\.\d{2})?(\.zip)?$", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Stamp();

    /// <summary>Keeps the newest <paramref name="keep"/> dated copies of <paramref name="name"/> (only those it made).</summary>
    private static void Prune(string dest, string name, int keep)
    {
        var copies = Directory.EnumerateFileSystemEntries(dest)
            .Where(e => Path.GetFileName(e) is var n && n.StartsWith(name, StringComparison.OrdinalIgnoreCase) && Stamp().IsMatch(n[name.Length..]))
            .OrderByDescending(e => Path.GetFileName(e), StringComparer.OrdinalIgnoreCase)
            .Skip(keep)
            .ToList();
        foreach (var old in copies)
            try
            {
                if (Directory.Exists(old)) Directory.Delete(old, recursive: true);
                else File.Delete(old);
                Log.Info($"Archivos: copia antigua quitada {old}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn($"Archivos: no se pudo quitar {old}: {ex.Message}");
            }
    }

    // ── zip ──────────────────────────────────────────────────────────────

    private static FileJobResult Zip(FileJob job, string? dest, string? excludedFolder, IProgress<FileJobProgress>? progress, CancellationToken token)
    {
        if (dest is null) return new FileJobResult(0, 0, 0, 0, "files.noDestination");
        var plan = Read(job.Sources, job.Subfolders, excludedFolder is null ? [] : [excludedFolder], token);
        if (!dest.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var name = plan.Folders.Count > 0 ? plan.Folders[0].Name : plan.Files.Count > 0 ? Path.GetFileNameWithoutExtension(plan.Files[0].Path) : "Archivo";
            dest = Path.Combine(dest, name + ".zip");
        }
        if (File.Exists(dest))
        {
            if (job.Exists == FileExists.Skip) return new FileJobResult(0, 1, 0, 0, Output: dest);
            if (job.Exists == FileExists.KeepBoth) dest = Unique(dest);
        }
        var count = new Counter();
        var total = plan.Total;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var temporary = dest + ".part";
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var dir in plan.Dirs) zip.CreateEntry(dir.Replace('\\', '/') + "/");
                foreach (var item in plan.Files)
                {
                    token.ThrowIfCancellationRequested();
                    if (item.Path.Equals(dest, StringComparison.OrdinalIgnoreCase) || item.Path.Equals(temporary, StringComparison.OrdinalIgnoreCase)) continue;
                    Try(count, item.Path, () =>
                    {
                        var entry = zip.CreateEntry(item.Relative.Replace('\\', '/'), CompressionLevel.Optimal);
                        entry.LastWriteTime = item.Written.ToLocalTime();
                        using var source = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, Buffer);
                        using var output = entry.Open();
                        var buffer = new byte[Buffer];
                        int read;
                        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            token.ThrowIfCancellationRequested();
                            output.Write(buffer, 0, read);
                            count.Bytes += read;
                            count.Seen += read;
                            progress?.Report(new FileJobProgress(count.Seen, total, item.Relative));
                        }
                        count.Done++;
                    });
                }
            }
            File.Move(temporary, dest, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) try { File.Delete(temporary); } catch (IOException) { }
        }
        return Missing(plan, count) with { Output = dest };
    }

    private static FileJobResult Unzip(FileJob job, string? dest, IProgress<FileJobProgress>? progress, CancellationToken token)
    {
        if (dest is null) return new FileJobResult(0, 0, 0, 0, "files.noDestination");
        var plan = Read(job.Sources, job.Subfolders, [], token);
        var count = new Counter();
        var total = plan.Files.Sum(f => f.Length);
        foreach (var file in plan.Files.Where(f => f.Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
        {
            token.ThrowIfCancellationRequested();
            Try(count, file.Path, () =>
            {
                using var zip = ZipFile.OpenRead(file.Path);
                foreach (var entry in zip.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    var target = Path.GetFullPath(Path.Combine(dest, entry.FullName));
                    // A name with «..» never writes outside the destination.
                    if (!IsInside(target, dest)) continue;
                    if (entry.FullName.EndsWith('/'))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    var item = new Item(file.Path, entry.FullName, entry.Length, entry.LastWriteTime.UtcDateTime);
                    var to = Resolve(target, item, job.Exists);
                    if (to.Length == 0)
                    {
                        count.Skipped++;
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    entry.ExtractToFile(to, overwrite: true);
                    count.Done++;
                    count.Bytes += entry.Length;
                }
                count.Seen += file.Length;
                progress?.Report(new FileJobProgress(count.Seen, total, file.Relative));
            });
        }
        return Missing(plan, count);
    }

    // ── rename, delete, create ───────────────────────────────────────────

    private static FileJobResult Rename(FileJob job, CancellationToken token)
    {
        var name = job.NewName.Trim();
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return new FileJobResult(0, 0, 0, 0, "files.badName", name);
        var plan = Read(job.Sources, job.Subfolders, [], token);
        var count = new Counter();
        var targets = plan.Folders.Select(f => f.Path).Concat(plan.Files.Where(f => !plan.Folders.Any(d => IsInside(f.Path, d.Path))).Select(f => f.Path));
        foreach (var path in targets)
        {
            token.ThrowIfCancellationRequested();
            Try(count, path, () =>
            {
                // Without an extension of its own, a file keeps the one it had.
                var newName = Directory.Exists(path) || Path.HasExtension(name) ? name : name + Path.GetExtension(path);
                var target = Path.Combine(Path.GetDirectoryName(path)!, newName);
                if (target.Equals(path, StringComparison.OrdinalIgnoreCase)) return;
                target = Unique(target);
                if (Directory.Exists(path)) Directory.Move(path, target);
                else File.Move(path, target);
                count.Done++;
            });
        }
        return Missing(plan, count);
    }

    private static FileJobResult Delete(FileJob job, CancellationToken token)
    {
        var plan = Read(job.Sources, job.Subfolders, [], token);
        // Never a whole drive.
        if (plan.Folders.FirstOrDefault(f => Path.GetPathRoot(f.Path) is { } root && Path.TrimEndingDirectorySeparator(root).Equals(f.Path, StringComparison.OrdinalIgnoreCase)) is { Path: { } drive })
            return new FileJobResult(0, 0, 0, 0, "files.drive", drive);
        var count = new Counter();
        var targets = plan.Folders.Select(f => f.Path).Concat(plan.Files.Where(f => !plan.Folders.Any(d => IsInside(f.Path, d.Path))).Select(f => f.Path)).ToList();
        foreach (var path in targets)
        {
            token.ThrowIfCancellationRequested();
            Try(count, path, () =>
            {
                if (job.Recycle) ToRecycleBin(path);
                else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                else File.Delete(path);
                count.Done++;
            });
        }
        return Missing(plan, count);
    }

    private static void ToRecycleBin(string path)
    {
        var op = new Win32.SHFILEOPSTRUCT
        {
            wFunc = Win32.FO_DELETE,
            pFrom = path + "\0\0",
            fFlags = (ushort)(Win32.FOF_ALLOWUNDO | Win32.FOF_NOCONFIRMATION | Win32.FOF_SILENT | Win32.FOF_NOERRORUI),
        };
        var error = Win32.SHFileOperation(ref op);
        if (error != 0 || op.fAnyOperationsAborted) throw new IOException($"SHFileOperation 0x{error:X}");
    }

    private static FileJobResult CreateFolder(string? dest)
    {
        if (dest is null) return new FileJobResult(0, 0, 0, 0, "files.noDestination");
        if (Directory.Exists(dest)) return new FileJobResult(0, 1, 0, 0, Output: dest);
        Directory.CreateDirectory(dest);
        return new FileJobResult(1, 0, 0, 0, Output: dest);
    }

    /// <summary>Runs one step; a failure is logged and counted (unless <paramref name="counts"/> is false) and the job goes on.</summary>
    private static void Try(Counter count, string what, Action step, bool counts = true)
    {
        try { step(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            Log.Warn($"Archivos: {what}: {ex.Message}");
            if (counts) count.Failed++;
        }
    }
}

using System.Text;
using System.Text.Json;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// What write_file, copy_file, create_folder, zip_files and unzip share (B4, 5.0.346): where things go, and putting what
/// they made in the recycle bin when undone. None of them ever writes over something that is there.
/// </summary>
internal static class FileWork
{
    public const string FolderSchema = "{\"type\":\"string\",\"description\":\"Where: one of desktop, downloads, documents, pictures, videos, music, or a full path of a folder (default: desktop)\"}";

    /// <summary>The folder meant (a user folder or an existing full path); empty: the desktop.</summary>
    public static string? Folder(string? asked) =>
        string.IsNullOrWhiteSpace(asked) ? UserFolders.Find("desktop") : UserFolders.Find(asked);

    public static ToolResult NoFolder(string? asked) =>
        ToolResult.Fail($"There is no folder \"{asked}\": use one of {string.Join(", ", UserFolders.Names)} or the full path of an existing folder.");

    /// <summary>A name without folders or characters Windows does not take; empty when nothing is left.</summary>
    public static string CleanName(string? name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string((Path.GetFileName((name ?? string.Empty).Trim().Trim('"')) ?? string.Empty).Where(c => !bad.Contains(c)).ToArray()).Trim().TrimEnd('.');
        return clean;
    }

    /// <summary>The folder's name for people: a user folder in their language («Escritorio», «Desktop» on disk), else the last part of its path.</summary>
    public static string Shown(string folder)
    {
        foreach (var (name, key) in Known)
            if (UserFolders.Find(name) is { } known && string.Equals(Path.TrimEndingDirectorySeparator(known), Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase))
                return Loc.T(key);
        return Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } leaf ? leaf : folder;
    }

    private static readonly (string Name, string Key)[] Known =
        [("desktop", "folder.Desktop"), ("documents", "folder.Documents"), ("downloads", "folder.Downloads"), ("pictures", "folder.Pictures"), ("music", "folder.Music"), ("videos", "folder.Videos")];

    public static ToolUndo Made(string tool, string path) => new(tool, JsonSerializer.Serialize(new[] { path }));

    /// <summary>What a tool made goes to the recycle bin (never deleted for good).</summary>
    public static bool Unmake(ToolUndo undo, Func<string, bool>? recycle = null) =>
        JsonSerializer.Deserialize<string[]>(undo.Data) is [var path] && GivenFiles.Exists(path) && (recycle ?? RecycleBin.Send)(path);

    /// <summary>A list of paths given by the model (an array, one text, or nothing: the files the user gave).</summary>
    public static List<string> Paths(JsonElement arguments, ToolContext context)
    {
        var listed = NoteTools.List(arguments, "paths").Select(p => p.Trim('"')).ToList();
        if (listed.Count == 0 && GivenFiles.Resolve(arguments, context) is { } one) listed.Add(one);
        if (listed.Count == 0) listed.AddRange(context.Files);
        return [.. listed.Select(p => Path.IsPathFullyQualified(p) ? p : context.Files.FirstOrDefault(f => Path.GetFileName(f).Equals(p, StringComparison.OrdinalIgnoreCase)) ?? p)];
    }
}

/// <summary>«Escribe en un archivo la lista de la compra» (B4): a new text document; one that is there is never replaced unasked.</summary>
public sealed class WriteFileTool(Func<string, bool>? recycle = null) : ITool
{
    /// <summary>The kinds of document it writes: text only (never a script or a program that could be run later).</summary>
    public static readonly IReadOnlyList<string> Kinds = [".txt", ".md", ".csv", ".tsv", ".json", ".xml", ".html", ".htm", ".css", ".log", ".yaml", ".yml", ".srt", ".ics", ".vcf"];

    public const int MaxText = 200_000;

    /// <summary>What a replaced file had, kept to undo it, up to this size.</summary>
    private const int MaxKept = 1 << 20;

    public string Name => "write_file";

    public string Description => "Writes a new text document on the PC (txt by default; also md, csv, json, html…) with the text you give, on the desktop unless the user says where. It never replaces a file that is there unless the user said yes to replacing it (then call it again with replace true).";

    public string ParametersSchemaJson =>
        "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"description\":\"The file's name (shopping list.txt); without an extension it is a .txt\"},"
        + "\"text\":{\"type\":\"string\",\"description\":\"All of its text\"},\"folder\":" + FileWork.FolderSchema
        + ",\"replace\":{\"type\":\"boolean\",\"description\":\"Only when the user said yes to replacing the file that is there\"}},\"required\":[\"name\",\"text\"]}";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    /// <summary>Replacing a file always asks on the PC (what it had is gone otherwise).</summary>
    public ToolRisk RiskFor(JsonElement arguments) => Replaces(arguments) ? ToolRisk.AskAlways : ToolRisk.Safe;

    private static bool Replaces(JsonElement arguments) => arguments.GetString("replace") is "true" or "True";

    public string Intent(JsonElement arguments) => Loc.T("tool.write_file.intent", FileWork.CleanName(arguments.GetString("name")) is { Length: > 0 } name ? name : "?");

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var name = FileWork.CleanName(arguments.GetString("name"));
        if (name.Length == 0) return Task.FromResult(ToolResult.Fail("Give the file a name."));
        if (Path.GetExtension(name).Length == 0) name += ".txt";
        var kind = Path.GetExtension(name).ToLowerInvariant();
        if (!Kinds.Contains(kind))
            return Task.FromResult(ToolResult.Fail($"Only text documents ({string.Join(", ", Kinds)}), never {kind}: say so to the user."));
        var text = arguments.GetString("text") ?? string.Empty;
        if (text.Length > MaxText) return Task.FromResult(ToolResult.Fail("The text is too long for one file."));
        if (FileWork.Folder(arguments.GetString("folder")) is not { } folder) return Task.FromResult(FileWork.NoFolder(arguments.GetString("folder")));

        var path = Path.Combine(folder, name);
        if (Directory.Exists(path)) return Task.FromResult(ToolResult.Fail($"There is a folder called {name} in {folder}: choose another name."));
        var exists = File.Exists(path);
        if (exists && !Replaces(arguments))
        {
            var start = Start(path);
            return Task.FromResult(new ToolResult($"There is already a file {name} in {folder}{(start is null ? string.Empty : $" (it begins: «{start}»)")}. Nothing was written. Ask the user whether to replace it; if they say yes, call write_file again with replace true, or use another name.")
            {
                Done = Loc.T("tool.write_file.existsDone", name),
                Reply = Loc.T("tool.write_file.exists", name),
            });
        }

        byte[]? before = null;
        try
        {
            if (exists && new FileInfo(path).Length <= MaxKept) before = File.ReadAllBytes(path);
            // A .csv with a BOM opens well in Excel; the rest without one.
            File.WriteAllText(path, text.ReplaceLineEndings("\r\n"), new UTF8Encoding(kind == ".csv"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido escribir {path}: {ex.Message}");
            return Task.FromResult(ToolResult.Fail($"{name} could not be written ({ex.Message}). Say so."));
        }
        var shown = FileWork.Shown(folder);
        return Task.FromResult(new ToolResult($"{(exists ? "Replaced" : "Wrote")} {path}.")
        {
            Done = Loc.T(exists ? "tool.write_file.replaced" : "tool.write_file.done", name, shown),
            Reply = Loc.T("tool.write_file.reply", name, shown),
            Undo = new ToolUndo(Name, JsonSerializer.Serialize(new Written(path, exists, before is null ? null : Convert.ToBase64String(before)))),
            Acted = true,
        });
    }

    private sealed record Written(string Path, bool Replaced, string? Before);

    /// <summary>A new one goes to the recycle bin; a replaced one gets back what it had (when it was small enough to keep).</summary>
    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken)
    {
        if (JsonSerializer.Deserialize<Written>(undo.Data) is not { } written || !File.Exists(written.Path)) return Task.FromResult(false);
        if (!written.Replaced) return Task.FromResult((recycle ?? RecycleBin.Send)(written.Path));
        if (written.Before is null) return Task.FromResult(false);
        try
        {
            File.WriteAllBytes(written.Path, Convert.FromBase64String(written.Before));
            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido devolver {written.Path} a como estaba: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    private static string? Start(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            var buffer = new char[160];
            var read = reader.Read(buffer, 0, buffer.Length);
            var start = new string(buffer, 0, read).ReplaceLineEndings(" ").Trim();
            return start.Length == 0 ? null : start + (read == buffer.Length ? "…" : string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}

/// <summary>«Copia este archivo a documentos» (B4): never over another; a second one is «nombre (2)».</summary>
public sealed class CopyFileTool(Func<string, bool>? recycle = null) : ITool
{
    public string Name => "copy_file";

    public string Description => "Copies a file or folder to another folder. Never replaces anything: if one of that name is there, the copy gets «(2)».";

    public string ParametersSchemaJson { get; } = "{\"type\":\"object\",\"properties\":{" + GivenFiles.PathSchema + ",\"to\":" + FileWork.FolderSchema + "},\"required\":[\"to\"]}";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.copy_file.intent", GivenFiles.NameOf(arguments.GetString("path")), arguments.GetString("to") ?? "?");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (GivenFiles.Resolve(arguments, context) is not { } path) return ToolResult.Fail("Which file? Give its full path.");
        var name = GivenFiles.NameOf(path);
        if (!GivenFiles.Exists(path)) return ToolResult.Fail($"{name} is not there.");
        if (FileWork.Folder(arguments.GetString("to")) is not { } folder) return FileWork.NoFolder(arguments.GetString("to"));
        if (Directory.Exists(path) && FileJobs.IsInside(folder, path)) return ToolResult.Fail("A folder cannot be copied inside itself.");

        var target = FileJobs.Unique(Path.Combine(folder, Path.GetFileName(path.TrimEnd('\\'))));
        try
        {
            await Task.Run(() =>
            {
                if (Directory.Exists(path)) CopyFolder(path, target, cancellationToken);
                else File.Copy(path, target, overwrite: false);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido copiar {path} a {target}: {ex.Message}");
            return ToolResult.Fail($"{name} could not be copied ({ex.Message}). Say so.");
        }
        var copy = Path.GetFileName(target);
        var shown = FileWork.Shown(folder);
        return new ToolResult($"Copied to {target}.{(copy != name ? $" There was one of that name already, so the copy is called {copy}." : string.Empty)}")
        {
            Done = Loc.T("tool.copy_file.done", name, shown),
            Reply = Loc.T("tool.copy_file.reply", copy, shown),
            Undo = FileWork.Made(Name, target),
            Acted = true,
        };
    }

    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken) => Task.FromResult(FileWork.Unmake(undo, recycle));

    /// <summary>A whole folder, into a new one (links and junctions are not followed: no loops, nothing from elsewhere).</summary>
    private static void CopyFolder(string from, string to, CancellationToken token)
    {
        Directory.CreateDirectory(to);
        foreach (var entry in new DirectoryInfo(from).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            var target = Path.Combine(to, entry.Name);
            if (entry is DirectoryInfo) CopyFolder(entry.FullName, target, token);
            else File.Copy(entry.FullName, target, overwrite: false);
        }
    }
}

/// <summary>«Crea una carpeta en el escritorio que se llame Facturas» (B4).</summary>
public sealed class CreateFolderTool : ITool
{
    public string Name => "create_folder";

    public string Description => "Creates a new folder (on the desktop unless the user says where).";

    public string ParametersSchemaJson { get; } = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"description\":\"The new folder's name\"},\"in\":" + FileWork.FolderSchema + "},\"required\":[\"name\"]}";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.create_folder.intent", FileWork.CleanName(arguments.GetString("name")) is { Length: > 0 } name ? name : "?");

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var name = FileWork.CleanName(arguments.GetString("name"));
        if (name.Length == 0) return Task.FromResult(ToolResult.Fail("Give the folder a name."));
        if (FileWork.Folder(arguments.GetString("in")) is not { } folder) return Task.FromResult(FileWork.NoFolder(arguments.GetString("in")));
        var path = Path.Combine(folder, name);
        var shown = FileWork.Shown(folder);
        if (Directory.Exists(path))
            return Task.FromResult(new ToolResult($"The folder {path} was already there: nothing to do.") { Done = Loc.T("tool.create_folder.known", name, shown), Reply = Loc.T("tool.create_folder.replyKnown", name) });
        if (File.Exists(path)) return Task.FromResult(ToolResult.Fail($"There is a file called {name} in {folder}: choose another name."));
        try { Directory.CreateDirectory(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(ToolResult.Fail($"The folder could not be created ({ex.Message}). Say so."));
        }
        return Task.FromResult(new ToolResult($"Created the folder {path}.")
        {
            Done = Loc.T("tool.create_folder.done", name, shown),
            Reply = Loc.T("tool.create_folder.reply", name, shown),
            Undo = FileWork.Made(Name, path),
            Acted = true,
        });
    }

    /// <summary>Only while it is still empty (what the user put in it since is not thrown away).</summary>
    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken)
    {
        if (JsonSerializer.Deserialize<string[]>(undo.Data) is not [var path] || !Directory.Exists(path) || Directory.EnumerateFileSystemEntries(path).Any())
            return Task.FromResult(false);
        try
        {
            Directory.Delete(path);
            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Task.FromResult(false); }
    }
}

/// <summary>«Comprime estas fotos» (B4): a .zip next to them (or where the user says), never over another one.</summary>
public sealed class ZipFilesTool(Func<string, bool>? recycle = null) : ITool
{
    public string Name => "zip_files";

    public string Description => "Compresses files or folders into a new .zip (next to the first one unless the user says where). Never replaces a zip that is there.";

    public string ParametersSchemaJson { get; } =
        "{\"type\":\"object\",\"properties\":{\"paths\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"Full paths of the files or folders (empty: the ones the user gave you)\"},"
        + "\"name\":{\"type\":\"string\",\"description\":\"The zip's name (optional)\"},\"to\":" + FileWork.FolderSchema.Replace("default: desktop", "default: where the first one is", StringComparison.Ordinal) + "}}";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.zip_files.intent");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var paths = FileWork.Paths(arguments, context);
        if (paths.Count == 0) return ToolResult.Fail("Which files? Give their full paths.");
        var missing = paths.Where(p => !Path.IsPathFullyQualified(p) || !GivenFiles.Exists(p)).ToList();
        if (missing.Count > 0) return ToolResult.Fail($"These are not there: {string.Join(", ", missing)}.");
        var to = arguments.GetString("to");
        var folder = string.IsNullOrWhiteSpace(to) ? Path.GetDirectoryName(paths[0].TrimEnd('\\')) : UserFolders.Find(to);
        if (folder is null) return FileWork.NoFolder(to);
        var name = FileWork.CleanName(arguments.GetString("name"));
        if (name.Length == 0) name = (paths.Count == 1 ? Path.GetFileNameWithoutExtension(paths[0].TrimEnd('\\')) : FileWork.Shown(folder)) + ".zip";
        else if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name += ".zip";
        var zip = FileJobs.Unique(Path.Combine(folder, name));

        var done = await FileJobs.RunAsync(new FileJob(FileOperation.Zip, paths, zip, Subfolders: true, Exists: FileExists.KeepBoth), null, cancellationToken).ConfigureAwait(false);
        if (done.Refused is not null || !File.Exists(zip) || done.Done == 0)
            return ToolResult.Fail($"The zip could not be made ({done.Failed} files failed). Say so.");
        var made = Path.GetFileName(zip);
        return new ToolResult($"Made {zip} with {done.Done} files{(done.Failed > 0 ? $" ({done.Failed} could not be added: in use?)" : string.Empty)}, {FileJobs.Size(new FileInfo(zip).Length, System.Globalization.CultureInfo.InvariantCulture)}.")
        {
            Done = Loc.T("tool.zip_files.done", made),
            Reply = Loc.T("tool.zip_files.reply", made, FileWork.Shown(folder)),
            Undo = FileWork.Made(Name, zip),
            Acted = true,
        };
    }

    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken) => Task.FromResult(FileWork.Unmake(undo, recycle));
}

/// <summary>«Descomprime el zip» (B4): always into a new folder (never over what is there; nothing written outside it).</summary>
public sealed class UnzipTool(Func<string, bool>? recycle = null) : ITool
{
    public string Name => "unzip";

    public string Description => "Extracts a .zip into a new folder (next to it, named like it, unless the user says where). Never replaces anything.";

    public string ParametersSchemaJson { get; } = "{\"type\":\"object\",\"properties\":{" + GivenFiles.PathSchema + ",\"to\":"
        + FileWork.FolderSchema.Replace("default: desktop", "the new folder goes inside it; default: next to the zip", StringComparison.Ordinal) + "}}";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.unzip.intent", GivenFiles.NameOf(arguments.GetString("path")));

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (GivenFiles.Resolve(arguments, context) is not { } path) return ToolResult.Fail("Which zip? Give its full path.");
        var name = GivenFiles.NameOf(path);
        if (!File.Exists(path)) return ToolResult.Fail($"{name} is not there.");
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return ToolResult.Fail($"{name} is not a .zip (only zips can be extracted).");
        var to = arguments.GetString("to");
        var folder = string.IsNullOrWhiteSpace(to) ? Path.GetDirectoryName(path) : UserFolders.Find(to);
        if (folder is null) return FileWork.NoFolder(to);
        var target = FileJobs.Unique(Path.Combine(folder, Path.GetFileNameWithoutExtension(path)));

        var done = await FileJobs.RunAsync(new FileJob(FileOperation.Unzip, [path], target, Exists: FileExists.KeepBoth), null, cancellationToken).ConfigureAwait(false);
        if (done.Refused is not null || !Directory.Exists(target))
            return ToolResult.Fail($"{name} could not be extracted (damaged, or not a zip). Say so.");
        var made = Path.GetFileName(target);
        return new ToolResult($"Extracted {done.Done} files into {target}{(done.Failed > 0 ? $" ({done.Failed} failed)" : string.Empty)}.")
        {
            Done = Loc.T("tool.unzip.done", name, made),
            Reply = Loc.T("tool.unzip.reply", made, FileWork.Shown(folder)),
            Undo = FileWork.Made(Name, target),
            Acted = true,
        };
    }

    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken) => Task.FromResult(FileWork.Unmake(undo, recycle));
}

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// The files the user gave an assistant (stage 14.1: dropped on it, or the one it was told about): which one a tool means.
/// A path given by the model wins; else, by name among the given ones; else, the first one given.
/// </summary>
internal static class GivenFiles
{
    public const string PathSchema = "\"path\":{\"type\":\"string\",\"description\":\"The full path of the file or folder (the one the user gave you, when they gave one); empty: the first one they gave you\"}";

    public static string? Resolve(JsonElement arguments, ToolContext context)
    {
        var asked = arguments.GetString("path")?.Trim().Trim('"');
        if (asked is { Length: > 0 } && Path.IsPathFullyQualified(asked)) return asked;
        if (asked is { Length: > 0 })
            return context.Files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), asked, StringComparison.OrdinalIgnoreCase))
                   ?? context.Files.FirstOrDefault(f => Path.GetFileName(f).Contains(asked, StringComparison.OrdinalIgnoreCase));
        return context.Files.Count > 0 ? context.Files[0] : null;
    }

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>Its name, for people.</summary>
    public static string NameOf(string? path) => path is null ? Loc.T("tool.given.it") : Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path;
}

/// <summary>
/// read_file (stage 14.1): what a file says, to summarize it, answer about it or read it aloud. Long ones (stage 14.3) give
/// the parts that matter for the question (by words and meaning) or, without one, parts spread across it. Pictures: the
/// model's vision when it sees, else the OCR of Windows. Folders: what is in them.
/// </summary>
public sealed class ReadFileTool(IVision? vision = null, IScreenReader? reader = null, ITextMeaning? meaning = null) : ITool
{
    /// <summary>The most text the model gets of a file (the context is 16 K tokens).</summary>
    public const int MaxText = 9000;

    public string Name => "read_file";

    public string Description => "Reads a file of the PC (a document, PDF, Word, text, code, a picture) to summarize it, answer about it, translate it or read it aloud; a folder: what is in it. Give the question when the user asks something about it: a long file then gives the parts that answer it.";

    public string ParametersSchemaJson { get; } = "{\"type\":\"object\",\"properties\":{" + GivenFiles.PathSchema
        + ",\"question\":{\"type\":\"string\",\"description\":\"What the user wants to know from it (e.g. the total amount, the deadline); empty to read it all or summarize it\"}}}";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public bool ReadsOnly => true;

    public string Intent(JsonElement arguments) => Loc.T("tool.read_file.intent", GivenFiles.NameOf(arguments.GetString("path")));

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (GivenFiles.Resolve(arguments, context) is not { } path) return ToolResult.Fail("Which file? Give its full path (the user has not given you any).");
        if (!GivenFiles.Exists(path)) return ToolResult.Fail($"{path} is not there.");
        var name = GivenFiles.NameOf(path);
        var question = arguments.GetString("question")?.Trim() ?? string.Empty;
        if (Directory.Exists(path)) return Folder(path, name);
        if (FileText.IsPicture(path)) return await PictureAsync(path, name, question, context, cancellationToken).ConfigureAwait(false);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var content = await Task.Run(() => FileText.Read(path), cancellationToken).ConfigureAwait(false);
        if (content is null)
            return new ToolResult($"{name} is not a file with text that can be read (a program, a video, a compressed file…). Say so.") { Done = Loc.T("tool.read_file.cannot", name), Failed = true };
        var text = content.Text.Trim();
        Log.Info($"Asistente: {name} leído en {watch.ElapsedMilliseconds} ms ({text.Length} letras, {content.Pages} páginas)");
        if (text.Length == 0)
            return new ToolResult($"{name} has no text (a scanned PDF is only pictures). Say so.") { Done = Loc.T("tool.read_file.empty", name), Failed = true };
        if (text.Length <= MaxText)
            return new ToolResult($"The text of {name}:\n---\n{text}\n---\nAnswer the user from this, in their language.") { Done = Loc.T("tool.read_file.done", name) };

        // Too long for the context: the parts that answer the question, or parts across it for a summary.
        var chunks = LongText.Chunks(content);
        var key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
        var parts = question.Length > 0
            ? await LongText.BestAsync(chunks, question, meaning, key, MaxText, cancellationToken).ConfigureAwait(false)
            : LongText.Spread(chunks, MaxText);
        Log.Info($"Asistente: {name} es largo ({chunks.Count} trozos): {parts.Count} {(question.Length > 0 ? "por la pregunta" : "repartidos")}");
        var pages = content.Pages > 1;
        var about = question.Length > 0
            ? $"Only the parts that matter for «{question}» are here"
            : "Only parts spread across it are here (for a summary); to look for something in it, call read_file again with a question";
        return new ToolResult($"{name} is long ({text.Length} characters{(pages ? $", {content.Pages} pages" : "")}). {about}{(pages ? ", each with its page" : "")}:\n---\n{LongText.Describe(parts, pages)}\n---\nAnswer the user from this, in their language{(pages ? ", saying the page where it comes from" : "")}.")
        {
            Done = Loc.T("tool.read_file.done", name),
        };
    }

    private static ToolResult Folder(string path, string name)
    {
        var entries = new DirectoryInfo(path).EnumerateFileSystemInfos().Where(e => !e.Attributes.HasFlag(FileAttributes.Hidden))
            .OrderByDescending(e => e is DirectoryInfo).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var lines = entries.Take(60).Select(e => e is DirectoryInfo ? $"{e.Name} (folder)" : $"{e.Name} — {FileSearch.SizeOf(((FileInfo)e).Length)}");
        return new ToolResult($"The folder {name} ({path}) has {entries.Count} items{(entries.Count > 60 ? ", the first 60" : "")}:\n{string.Join('\n', lines)}")
        {
            Done = Loc.T("tool.read_file.folder", name),
        };
    }

    private Task<ToolResult> PictureAsync(string path, string name, string question, ToolContext context, CancellationToken cancellationToken) =>
        PictureFile.DescribeAsync(vision, reader, path, name, question, context, cancellationToken);
}

/// <summary>A picture file as JPEG (for the model's vision) and as pixels (for the OCR), no bigger than a side.</summary>
internal static class PictureFile
{
    /// <summary>
    /// What a picture file shows (read_file, read_clipboard): its text by the OCR of Windows when that is what is wanted or the
    /// model cannot see, else the model's vision. <paramref name="name"/>: how it is called for the model.
    /// </summary>
    public static async Task<ToolResult> DescribeAsync(IVision? vision, IScreenReader? reader, string path, string name, string question, ToolContext context,
        CancellationToken cancellationToken)
    {
        var sees = vision?.CanSee(context.Assistant) == true;
        var reads = reader?.CanRead == true;
        if (!sees && !reads)
            return new ToolResult($"{name} is a picture, and the model in use cannot see pictures nor can Windows read text. Say so: a model that sees can be chosen in Options → Brain.")
            {
                Done = Loc.T("tool.look_at_screen.blind"), Failed = true,
            };
        var picture = await PictureFile.LoadAsync(path, 1600, cancellationToken).ConfigureAwait(false);
        if (picture is null) return ToolResult.Fail($"{name} could not be opened as a picture.");
        var wantsText = !sees || LookAtScreenTool.WantsText(question, context.Heard);
        if (wantsText && reads)
        {
            var text = await reader!.ReadAsync(picture.Value.Pixels, cancellationToken).ConfigureAwait(false);
            if (text is { Text.Length: > 0 })
                return new ToolResult($"The text of the picture {name}, read by OCR:\n---\n{text.Text}\n---\nAnswer the user from this, in their language.") { Done = Loc.T("tool.read_file.done", name) };
            if (!sees) return new ToolResult($"No text could be read in {name}, and the model in use cannot see pictures. Say so.") { Done = Loc.T("tool.read_file.empty", name) };
        }
        var asked = question.Length > 0 ? question : "What is in this picture?";
        var seen = await vision!.LookAsync(context.Assistant, picture.Value.Jpeg,
            $"The user asks about this picture ({name}): «{asked}». Look at it and answer: say what it shows and copy the important visible text word for word.", cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(seen)) return ToolResult.Fail("The model did not say what it saw.");
        return new ToolResult($"In the picture {name}: {seen.Trim()}\nAnswer the user from this, in their language.") { Done = Loc.T("tool.read_file.done", name) };
    }

    public static async Task<(byte[] Jpeg, BgraImage Pixels)?> LoadAsync(string path, uint maxSide, CancellationToken cancellationToken)
    {
        try
        {
            using var file = new InMemoryRandomAccessStream();
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            await file.WriteAsync(bytes.AsBuffer()).AsTask(cancellationToken).ConfigureAwait(false);
            file.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(file).AsTask(cancellationToken).ConfigureAwait(false);
            var scale = Math.Min(1.0, maxSide / (double)Math.Max(decoder.OrientedPixelWidth, decoder.OrientedPixelHeight));
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.OrientedPixelWidth * scale)),
                ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.OrientedPixelHeight * scale)),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(cancellationToken).ConfigureAwait(false);
            var pixels = data.DetachPixelData();
            using var output = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output).AsTask(cancellationToken).ConfigureAwait(false);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform.ScaledWidth, transform.ScaledHeight, 96, 96, pixels);
            await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
            var jpeg = new byte[output.Size];
            output.Seek(0);
            await output.ReadAsync(jpeg.AsBuffer(), (uint)jpeg.Length, InputStreamOptions.None).AsTask(cancellationToken).ConfigureAwait(false);
            return (jpeg, new BgraImage((int)transform.ScaledWidth, (int)transform.ScaledHeight, pixels));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ArgumentException)
        {
            Log.Warn($"Asistente: no se ha podido abrir la imagen {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// delete_file (stage 14.1): to the recycle bin, never for good. It always asks first, whatever the user chose (Options →
/// Assistant → «Preguntar antes»): a file gone is worse than a question.
/// </summary>
public sealed class DeleteFileTool(Func<string, bool>? recycle = null) : ITool
{
    public string Name => "delete_file";

    public string Description => "Sends a file or folder to the recycle bin (the user is asked first). Only when the user asks to delete it.";

    public string ParametersSchemaJson { get; } = "{\"type\":\"object\",\"properties\":{" + GivenFiles.PathSchema + "}}";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.AskAlways;

    public bool AlwaysAsks => true;

    public string Intent(JsonElement arguments) => Loc.T("tool.delete_file.intent", GivenFiles.NameOf(arguments.GetString("path")));

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (GivenFiles.Resolve(arguments, context) is not { } path) return Task.FromResult(ToolResult.Fail("Which file? Give its full path."));
        var name = GivenFiles.NameOf(path);
        if (!GivenFiles.Exists(path)) return Task.FromResult(ToolResult.Fail($"{name} is not there any more."));
        if (!(recycle ?? RecycleBin.Send)(path)) return Task.FromResult(ToolResult.Fail($"{name} could not be deleted (in use, or no permission). Say so."));
        return Task.FromResult(new ToolResult($"{name} is in the recycle bin now (it can be restored from there).")
        {
            Done = Loc.T("tool.delete_file.done", name),
            Reply = Loc.T("tool.delete_file.reply", name),
            Acted = true,
        });
    }
}

/// <summary>move_file (stage 14.1): to another folder, never over a file of the same name; it always asks first; undone by moving it back.</summary>
public sealed class MoveFileTool : ITool
{
    public string Name => "move_file";

    public string Description => "Moves a file or folder to another folder (the user is asked first). Never overwrites anything.";

    public string ParametersSchemaJson { get; } = "{\"type\":\"object\",\"properties\":{" + GivenFiles.PathSchema
        + $",\"to\":{{\"type\":\"string\",\"description\":\"Where: one of {string.Join(", ", UserFolders.Names)}, or a full path of a folder\"}}}},\"required\":[\"to\"]}}";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.AskAlways;

    public bool AlwaysAsks => true;

    public string Intent(JsonElement arguments) =>
        Loc.T("tool.move_file.intent", GivenFiles.NameOf(arguments.GetString("path")), arguments.GetString("to") ?? "?");

    public Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (GivenFiles.Resolve(arguments, context) is not { } path) return Task.FromResult(ToolResult.Fail("Which file? Give its full path."));
        var name = GivenFiles.NameOf(path);
        if (!GivenFiles.Exists(path)) return Task.FromResult(ToolResult.Fail($"{name} is not there any more."));
        var to = arguments.GetString("to")?.Trim().Trim('"') ?? string.Empty;
        var folder = UserFolders.Find(to) ?? (Path.IsPathFullyQualified(to) ? to : null);
        if (folder is null) return Task.FromResult(ToolResult.Fail($"There is no folder \"{to}\": use one of {string.Join(", ", UserFolders.Names)} or a full path."));
        var target = Path.Combine(folder, Path.GetFileName(path.TrimEnd('\\')));
        if (GivenFiles.Exists(target)) return Task.FromResult(ToolResult.Fail($"There is already a {name} in {folder}: nothing was moved. Say so."));
        if (!Move(path, target)) return Task.FromResult(ToolResult.Fail($"{name} could not be moved (in use, or no permission). Say so."));
        return Task.FromResult(new ToolResult($"Moved {name} to {folder}.")
        {
            Done = Loc.T("tool.move_file.done", name, folder),
            Reply = Loc.T("tool.move_file.reply", name, Path.GetFileName(folder.TrimEnd('\\')) is { Length: > 0 } leaf ? leaf : folder),
            Undo = new ToolUndo(Name, JsonSerializer.Serialize(new[] { path, target })),
            Acted = true,
        });
    }

    public Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken)
    {
        var paths = JsonSerializer.Deserialize<string[]>(undo.Data);
        return Task.FromResult(paths is [var from, var to] && GivenFiles.Exists(to) && !GivenFiles.Exists(from) && Move(to, from));
    }

    private static bool Move(string from, string to)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            if (Directory.Exists(from)) Directory.Move(from, to);
            else File.Move(from, to, overwrite: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Asistente: no se ha podido mover {from} a {to}: {ex.Message}");
            return false;
        }
    }
}

/// <summary>Sending to the recycle bin (SHFileOperation with undo, no dialogs).</summary>
internal static class RecycleBin
{
    public static bool Send(string path)
    {
        var operation = new ShFileOperation
        {
            Func = 3, // FO_DELETE
            From = path + "\0\0",
            Flags = 0x0040 | 0x0010 | 0x0004 | 0x0400, // FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
        };
        var result = SHFileOperation(ref operation);
        if (result != 0 || operation.AnyOperationsAborted) Log.Warn($"Asistente: no se ha podido mandar {path} a la papelera ({result})");
        return result == 0 && !operation.AnyOperationsAborted && !GivenFiles.Exists(path);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOperation
    {
        public IntPtr Hwnd;
        public uint Func;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref ShFileOperation operation);
}

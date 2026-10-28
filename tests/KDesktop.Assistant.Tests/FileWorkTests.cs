using System.IO.Compression;
using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core.Assistant;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// B4 (5.0.346): writing a document, copying, a new folder, zipping and unzipping. Nothing is ever written over what is
/// there, replacing a file always asks, and everything is undone (to a recycle bin of the test, never the real one).
/// </summary>
public sealed class FileWorkTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-filework-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<string> _recycled = [];

    public FileWorkTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    /// <summary>The test's recycle bin: what is sent is deleted for good, inside the test folder only.</summary>
    private bool Recycle(string path)
    {
        Assert.StartsWith(_folder, path, StringComparison.OrdinalIgnoreCase);
        _recycled.Add(path);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        else File.Delete(path);
        return true;
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private string Escaped => _folder.Replace("\\", "\\\\", StringComparison.Ordinal);

    private static readonly ToolContext Dot = new("dot");

    [Fact]
    public async Task ADocumentIsWrittenNeverReplacedUnaskedAndUndone()
    {
        var write = new WriteFileTool(Recycle);
        var made = await write.RunAsync(Json($$"""{"name":"compra","text":"leche\nhuevos","folder":"{{Escaped}}"}"""), Dot, default);
        var path = Path.Combine(_folder, "compra.txt");
        Assert.False(made.Failed);
        Assert.Equal("leche\r\nhuevos", await File.ReadAllTextAsync(path));

        // There already: nothing is written, the user is asked.
        var again = await write.RunAsync(Json($$"""{"name":"compra.txt","text":"otra cosa","folder":"{{Escaped}}"}"""), Dot, default);
        Assert.Contains("replace true", again.Text, StringComparison.Ordinal);
        Assert.Equal("leche\r\nhuevos", await File.ReadAllTextAsync(path));

        // Replacing always asks on the PC; undone, it gets back what it had.
        Assert.Equal(ToolRisk.AskAlways, write.RiskFor(Json("""{"replace":true}""")));
        Assert.Equal(ToolRisk.Safe, write.RiskFor(Json("""{"name":"a"}""")));
        var replaced = await write.RunAsync(Json($$"""{"name":"compra.txt","text":"otra cosa","folder":"{{Escaped}}","replace":true}"""), Dot, default);
        Assert.Equal("otra cosa", await File.ReadAllTextAsync(path));
        Assert.True(await write.UndoAsync(replaced.Undo!, default));
        Assert.Equal("leche\r\nhuevos", await File.ReadAllTextAsync(path));
        Assert.True(await write.UndoAsync(made.Undo!, default));
        Assert.False(File.Exists(path));
        Assert.Single(_recycled);

        // Never a script or a program.
        var script = await write.RunAsync(Json($$"""{"name":"x.bat","text":"del *","folder":"{{Escaped}}"}"""), Dot, default);
        Assert.True(script.Failed);
        Assert.False(File.Exists(Path.Combine(_folder, "x.bat")));
    }

    [Fact]
    public async Task ACopyNeverReplacesAndAFolderIsMadeAndUndone()
    {
        var source = Path.Combine(_folder, "origen");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        await File.WriteAllTextAsync(Path.Combine(source, "a.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(source, "sub", "b.txt"), "b");
        var into = Path.Combine(_folder, "destino");

        var folder = new CreateFolderTool();
        var created = await folder.RunAsync(Json($$"""{"name":"destino","in":"{{Escaped}}"}"""), Dot, default);
        Assert.True(Directory.Exists(into));
        var twice = await folder.RunAsync(Json($$"""{"name":"destino","in":"{{Escaped}}"}"""), Dot, default);
        Assert.Null(twice.Undo);

        var copy = new CopyFileTool(Recycle);
        var to = into.Replace("\\", "\\\\", StringComparison.Ordinal);
        var first = await copy.RunAsync(Json($$"""{"to":"{{to}}"}"""), Dot with { Files = [Path.Combine(source, "a.txt")] }, default);
        var second = await copy.RunAsync(Json($$"""{"to":"{{to}}"}"""), Dot with { Files = [Path.Combine(source, "a.txt")] }, default);
        Assert.True(File.Exists(Path.Combine(into, "a.txt")));
        Assert.True(File.Exists(Path.Combine(into, "a (2).txt")));
        var whole = await copy.RunAsync(Json($$"""{"to":"{{to}}"}"""), Dot with { Files = [source] }, default);
        Assert.Equal("b", await File.ReadAllTextAsync(Path.Combine(into, "origen", "sub", "b.txt")));
        // Not into itself.
        Assert.True((await copy.RunAsync(Json($$"""{"to":"{{Path.Combine(source, "sub").Replace("\\", "\\\\", StringComparison.Ordinal)}}"}"""), Dot with { Files = [source] }, default)).Failed);

        // A folder with things in it is not taken away; empty again, it is.
        Assert.False(await folder.UndoAsync(created.Undo!, default));
        foreach (var undo in new[] { first, second, whole }) Assert.True(await copy.UndoAsync(undo.Undo!, default));
        Assert.True(await folder.UndoAsync(created.Undo!, default));
        Assert.False(Directory.Exists(into));
        Assert.True(File.Exists(Path.Combine(source, "a.txt")));
    }

    [Fact]
    public async Task ZippedAndUnzippedIntoNewPlacesOnly()
    {
        var photos = Path.Combine(_folder, "fotos");
        Directory.CreateDirectory(photos);
        await File.WriteAllTextAsync(Path.Combine(photos, "1.jpg"), "uno");
        await File.WriteAllTextAsync(Path.Combine(photos, "2.jpg"), "dos");

        var zip = new ZipFilesTool(Recycle);
        var zipped = await zip.RunAsync(Json("{}"), Dot with { Files = [photos] }, default);
        var file = Path.Combine(_folder, "fotos.zip");
        Assert.False(zipped.Failed);
        using (var archive = ZipFile.OpenRead(file)) Assert.Equal(2, archive.Entries.Count(e => e.Length > 0));
        // Another one: a new name, the first is left alone.
        await zip.RunAsync(Json("{}"), Dot with { Files = [photos] }, default);
        Assert.True(File.Exists(Path.Combine(_folder, "fotos (2).zip")));

        var unzip = new UnzipTool(Recycle);
        var opened = await unzip.RunAsync(Json("{}"), Dot with { Files = [file] }, default);
        // «fotos» is there: a new folder, «fotos (2)».
        var extracted = Path.Combine(_folder, "fotos (2)");
        Assert.Equal("dos", await File.ReadAllTextAsync(Directory.EnumerateFiles(extracted, "2.jpg", SearchOption.AllDirectories).Single()));
        Assert.True(Directory.Exists(photos)); // the folder of the same name was not touched
        Assert.True(await unzip.UndoAsync(opened.Undo!, default));
        Assert.True(await zip.UndoAsync(zipped.Undo!, default));
        Assert.False(File.Exists(file));
        Assert.Equal(2, Directory.GetFiles(photos).Length);
    }
}

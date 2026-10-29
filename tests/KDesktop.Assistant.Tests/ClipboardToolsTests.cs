using System.Text.Json;
using KDesktop.Assistant.Tools;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Assistant.Tools.Mcp;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Assistant.Sharing;

namespace KDesktop.Assistant.Tests;

/// <summary>B5 (5.0.347): reading and setting the clipboard through K-Desktop (a clipboard of the test, never the real one).</summary>
public sealed class ClipboardToolsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "kdesktop-clip-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>K-Desktop's side of <c>text</c> over a clipboard in memory.</summary>
    private sealed class Clipboard : ICommandBridge
    {
        public string Text { get; set; } = string.Empty;

        public List<string> Ops { get; } = [];

        public IReadOnlyList<string>? Files { get; set; }

        public string? Image { get; set; }

        /// <summary>The history K-Desktop keeps (null: off for the assistants).</summary>
        public List<ClipText>? History { get; set; }

        public Task<TextDoneMessage?> TextAsync(string op, string? assistant, string source, string text, string mode, long hwnd, CancellationToken cancellationToken)
        {
            Ops.Add($"{op}:{source}{mode}");
            if (op == "history")
            {
                if (History is null) return Task.FromResult<TextDoneMessage?>(new TextDoneMessage("r", false, Reason: "off"));
                var found = History.Where(h => h.Text.Contains(source, StringComparison.OrdinalIgnoreCase)).ToList();
                return Task.FromResult<TextDoneMessage?>(found.Count == 0 ? new TextDoneMessage("r", false, Reason: "empty") : new TextDoneMessage("r", true, History: found));
            }
            if (op == "put")
            {
                Text = text;
                return Task.FromResult<TextDoneMessage?>(new TextDoneMessage("r", true, From: "copied"));
            }
            if (Text.Length > 0) return Task.FromResult<TextDoneMessage?>(new TextDoneMessage("r", true, Text, "clipboard"));
            if (source == AssistantText.ClipboardAll && Files is { } files) return Task.FromResult<TextDoneMessage?>(new TextDoneMessage("r", false, Reason: "files", Files: files));
            if (source == AssistantText.ClipboardAll && Image is { } image) return Task.FromResult<TextDoneMessage?>(new TextDoneMessage("r", false, Reason: "image", Image: image));
            return Task.FromResult<TextDoneMessage?>(new TextDoneMessage("r", false, Reason: "empty"));
        }

        public Task<MatchedMessage?> MatchAsync(string text, CancellationToken cancellationToken) => Task.FromResult<MatchedMessage?>(null);

        public Task<EndMessage?> RunAsync(string command, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<EndMessage?> MediaAsync(string order, int? value, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);

        public Task<DescribedMessage?> DescribeAsync(string command, CancellationToken cancellationToken) => Task.FromResult<DescribedMessage?>(null);

        public Task<EndMessage?> RunEventAsync(string name, string? assistant, CancellationToken cancellationToken) => Task.FromResult<EndMessage?>(null);
    }

    [Fact]
    public async Task WhatIsCopiedIsReadFromTheClipboardOnly()
    {
        var clipboard = new Clipboard();
        var read = new ReadClipboardTool(clipboard);
        var empty = await read.RunAsync(Json("{}"), new ToolContext("dot"), default);
        Assert.False(empty.Failed);
        Assert.Contains("no text", empty.Text, StringComparison.Ordinal);

        clipboard.Text = "Calle Mayor 5, Madrid";
        var found = await read.RunAsync(Json("{}"), new ToolContext("dot"), default);
        Assert.Contains("Calle Mayor 5, Madrid", found.Text, StringComparison.Ordinal);
        // Never Ctrl+C on the window in front: the clipboard itself.
        Assert.All(clipboard.Ops, op => Assert.Equal("grab:" + AssistantText.ClipboardAll, op));
    }

    private sealed class Eyes : IVision
    {
        public string? Asked { get; private set; }

        public bool CanSee(string? assistant) => true;

        public Task<string?> LookAsync(string? assistant, byte[] jpeg, string question, CancellationToken cancellationToken)
        {
            Asked = question;
            return Task.FromResult<string?>(jpeg.Length > 0 ? "Un punto rojo y uno azul." : null);
        }
    }

    /// <summary>C2 (5.0.367): what was copied before, from K-Desktop's history, never from the clipboard itself.</summary>
    [Fact]
    public async Task WhatWasCopiedBeforeComesFromTheHistory()
    {
        var clipboard = new Clipboard
        {
            Text = "lo de ahora",
            History =
            [
                new ClipText("https://ejemplo.com/reunion", "chrome", DateTime.Today.AddHours(9), false),
                new ClipText("Calle Falsa 123", "notepad", DateTime.Today.AddDays(-2), true),
            ],
        };
        var tool = new ClipboardHistoryTool(clipboard);

        var all = await tool.RunAsync(Json("{}"), new ToolContext("dot"), default);
        Assert.Contains("1. [", all.Text, StringComparison.Ordinal);
        Assert.Contains("ejemplo.com/reunion", all.Text, StringComparison.Ordinal);
        Assert.Contains("pinned", all.Text, StringComparison.Ordinal);
        Assert.Equal(["history:"], clipboard.Ops); // never grab: the clipboard is not touched

        var link = await tool.RunAsync(Json("""{"query":"ejemplo"}"""), new ToolContext("dot"), default);
        Assert.DoesNotContain("Calle Falsa", link.Text, StringComparison.Ordinal);

        var none = await tool.RunAsync(Json("""{"query":"nada"}"""), new ToolContext("dot"), default);
        Assert.False(string.IsNullOrEmpty(none.Reply));

        clipboard.History = null;
        var off = await tool.RunAsync(Json("{}"), new ToolContext("dot"), default);
        Assert.Contains("off", off.Text, StringComparison.Ordinal);
    }

    /// <summary>5.0.348: files copied are told by their paths; a copied picture is looked at, and its temporary file goes.</summary>
    [Fact]
    public async Task FilesAndPicturesCopiedAreToldToo()
    {
        Directory.CreateDirectory(_folder);
        var clipboard = new Clipboard { Files = [@"C:\Fotos\playa.jpg"] };
        var eyes = new Eyes();
        var read = new ReadClipboardTool(clipboard, eyes);
        var files = await read.RunAsync(Json("{}"), new ToolContext("dot"), default);
        Assert.Contains(@"C:\Fotos\playa.jpg", files.Text, StringComparison.Ordinal);
        Assert.Contains("read_file", files.Text, StringComparison.Ordinal);

        // A 2×1 picture as K-Desktop leaves it (a .bmp from the DIB).
        var dib = new byte[40 + 8];
        BitConverter.TryWriteBytes(dib.AsSpan(0), 40);
        BitConverter.TryWriteBytes(dib.AsSpan(4), 2);
        BitConverter.TryWriteBytes(dib.AsSpan(8), 1);
        BitConverter.TryWriteBytes(dib.AsSpan(12), (ushort)1);
        BitConverter.TryWriteBytes(dib.AsSpan(14), (ushort)32);
        new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 }.CopyTo(dib, 40);
        var image = Path.Combine(_folder, "copiada.bmp");
        await File.WriteAllBytesAsync(image, Core.Platform.ClipboardSnapshot.BmpFromDib(dib));
        var picture = await new ReadClipboardTool(new Clipboard { Image = image }, eyes)
            .RunAsync(Json("""{"question":"¿de qué color son?"}"""), new ToolContext("dot"), default);
        Assert.Contains("Un punto rojo y uno azul.", picture.Text, StringComparison.Ordinal);
        Assert.Contains("¿de qué color son?", eyes.Asked, StringComparison.Ordinal);
        Assert.False(File.Exists(image));
    }

    [Fact]
    public async Task ACopyIsUndoneBackToWhatWasThere()
    {
        var clipboard = new Clipboard { Text = "lo de antes" };
        var set = new SetClipboardTool(clipboard);
        var copied = await set.RunAsync(Json("""{"text":"600 123 456"}"""), new ToolContext("dot"), default);
        Assert.Equal("600 123 456", clipboard.Text);
        Assert.True(copied.Acted);
        // What was there is not written anywhere: only a key.
        Assert.DoesNotContain("antes", copied.Undo!.Data, StringComparison.Ordinal);
        Assert.True(await set.UndoAsync(copied.Undo, default));
        Assert.Equal("lo de antes", clipboard.Text);
        Assert.False(await set.UndoAsync(copied.Undo, default));

        // Nothing copied before: nothing to undo to.
        var fresh = new Clipboard();
        Assert.Null((await new SetClipboardTool(fresh).RunAsync(Json("""{"text":"x"}"""), new ToolContext("dot"), default)).Undo);
    }

    /// <summary>What is copied may be a password: another program never gets it with the «read» scope.</summary>
    [Fact]
    public void TheClipboardIsNotSharedForReading()
    {
        var registry = new ToolRegistry();
        CommandTools.AddTo(registry, new Clipboard());
        var runner = new ToolRunner(registry, () => new HashSet<string>(StringComparer.Ordinal), (_, _) => Task.FromResult("no"), _ => { }, Path.Combine(_folder, "undo.json"));
        var shared = new SharedTools(runner) { Scope = McpShare.ScopeRead };
        Assert.DoesNotContain(shared.Shared, t => t.Name is "read_clipboard" or "set_clipboard" or "clipboard_history");
        Assert.Contains(shared.Shared, t => t.Name == "describe_command");
    }
}

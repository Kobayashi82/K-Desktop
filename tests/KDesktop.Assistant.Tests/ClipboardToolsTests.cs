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

        public Task<TextDoneMessage?> TextAsync(string op, string? assistant, string source, string text, string mode, long hwnd, CancellationToken cancellationToken)
        {
            Ops.Add($"{op}:{source}{mode}");
            if (op == "put")
            {
                Text = text;
                return Task.FromResult<TextDoneMessage?>(new TextDoneMessage("r", true, From: "copied"));
            }
            return Task.FromResult<TextDoneMessage?>(Text.Length == 0
                ? new TextDoneMessage("r", false, Reason: "empty")
                : new TextDoneMessage("r", true, Text, "clipboard"));
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
        Assert.All(clipboard.Ops, op => Assert.Equal("grab:clipboard", op));
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
        Assert.DoesNotContain(shared.Shared, t => t.Name is "read_clipboard" or "set_clipboard");
        Assert.Contains(shared.Shared, t => t.Name == "describe_command");
    }
}

using System.Collections.Concurrent;
using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// «¿Qué tengo copiado?» (B5, 5.0.347): the text in the clipboard, taken by K-Desktop (<c>text</c> <c>grab</c> from the
/// clipboard, never Ctrl+C). Not «only reads» on purpose: what is copied may be a password, so another program using the
/// shared tools (MCP) never gets it in the «read» scope, and with «all» it asks on the PC first. Without text (5.0.348): the
/// files copied in the Explorer are told by their paths (read_file reads them), and a picture is looked at like read_file
/// does (vision, or its text by the OCR of Windows).
/// </summary>
public sealed class ReadClipboardTool(ICommandBridge bridge, IVision? vision = null, IScreenReader? reader = null) : ITool
{
    /// <summary>The most given to the model.</summary>
    public const int MaxGiven = 6000;

    public string Name => "read_clipboard";

    public string Description => "Reads what the user has copied (the clipboard): its text, the files copied in the Explorer, or what a copied picture shows. Only when they mean what they copied.";

    public string ParametersSchemaJson => ToolText.Optional(("question", "string", "For a copied picture: what the user wants to know from it (empty: what it shows)"));

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.read_clipboard.intent");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var taken = await bridge.TextAsync("grab", context.Assistant, Core.Assistant.AssistantText.ClipboardAll, string.Empty, string.Empty, 0, cancellationToken).ConfigureAwait(false);
        if (taken is null) return ToolResult.Fail("K-Desktop did not answer.");
        if (taken is { Reason: "files", Files: { Count: > 0 } files })
        {
            var shown = files.Take(30).ToList();
            return new ToolResult($"The user copied {files.Count} file{(files.Count == 1 ? "" : "s")} or folder{(files.Count == 1 ? "" : "s")} (not text){(files.Count > shown.Count ? $", the first {shown.Count}" : "")}:\n{string.Join("\n", shown)}\nTo know what one says, use read_file with its full path.")
            {
                Done = Loc.T("tool.read_clipboard.files", files.Count),
            };
        }
        if (taken is { Reason: "image", Image: { } image })
        {
            try
            {
                return await PictureFile.DescribeAsync(vision, reader, image, Loc.T("tool.read_clipboard.picture"), arguments.GetString("question")?.Trim() ?? string.Empty,
                    context, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                try { File.Delete(image); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Core.Log.Warn($"Asistente: no se ha podido borrar {image}: {ex.Message}"); }
            }
        }
        if (!taken.Ok)
        {
            var reason = taken.Reason is { Length: > 0 } r ? r : "busy";
            var why = reason == "empty" ? Loc.T("tool.read_clipboard.empty") : Loc.T("tool.transform_text.reason." + reason);
            var said = reason == "empty" ? "Nothing is copied (no text, files nor picture)." : $"The clipboard could not be read ({reason}).";
            return new ToolResult(said + " Say so.") { Done = why, Reply = why, Failed = reason != "empty" };
        }
        var text = taken.Text;
        var cut = text.Length > MaxGiven;
        return new ToolResult($"The clipboard has this text ({text.Length} characters{(cut ? $", the first {MaxGiven} here" : string.Empty)}):\n---\n{(cut ? text[..MaxGiven] : text)}\n---")
        {
            Done = Loc.T("tool.read_clipboard.done"),
        };
    }
}

/// <summary>
/// «Cópiame el teléfono del fontanero» (B5, 5.0.347): a text left in the clipboard, ready to paste. Undone, what was there
/// comes back; that is kept in memory only (never in undo.json: it may be private), so after a restart it cannot be undone.
/// </summary>
public sealed class SetClipboardTool(ICommandBridge bridge) : ITool
{
    public const int MaxText = 20_000;

    private readonly ConcurrentDictionary<string, string> _before = new(StringComparer.Ordinal);

    public string Name => "set_clipboard";

    public string Description => "Puts a text in the clipboard (copies it), so the user can paste it. Only when they ask to copy something.";

    public string ParametersSchemaJson => ToolText.OneString("text", "The text to copy, exactly");

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.set_clipboard.intent", ToolText.Clean(arguments.GetString("text")));

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var text = arguments.GetString("text") ?? string.Empty;
        if (text.Trim().Length == 0) return ToolResult.Fail("There is no text to copy.");
        if (text.Length > MaxText) return ToolResult.Fail("The text is too long to copy at once.");
        // What was copied, to put it back if undone.
        var before = await bridge.TextAsync("grab", context.Assistant, "clipboard", string.Empty, string.Empty, 0, cancellationToken).ConfigureAwait(false);
        var put = await bridge.TextAsync("put", context.Assistant, string.Empty, text, "copy", 0, cancellationToken).ConfigureAwait(false);
        if (put is not { Ok: true })
        {
            var why = Loc.T("tool.transform_text.reason." + (put?.Reason is { Length: > 0 } r ? r : "busy"));
            return ToolResult.Fail($"It could not be copied ({put?.Reason ?? "no answer"}). Say so.") with { Reply = why, Done = why };
        }
        ToolUndo? undo = null;
        if (before is { Ok: true, Text.Length: > 0 })
        {
            var key = Guid.NewGuid().ToString("N");
            _before[key] = before.Text;
            undo = new ToolUndo(Name, key);
        }
        var shown = ToolText.Clean(text.ReplaceLineEndings(" "));
        return new ToolResult($"Copied: \"{shown}\". The user can paste it now.")
        {
            Done = Loc.T("tool.set_clipboard.done", shown),
            Reply = Loc.T("tool.set_clipboard.reply"),
            Undo = undo,
            Acted = true,
        };
    }

    public async Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken)
    {
        if (!_before.TryRemove(undo.Data, out var text)) return false;
        var put = await bridge.TextAsync("put", null, string.Empty, text, "copy", 0, cancellationToken).ConfigureAwait(false);
        return put is { Ok: true };
    }
}

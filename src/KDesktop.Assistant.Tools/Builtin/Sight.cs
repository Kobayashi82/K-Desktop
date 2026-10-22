using System.Text.Json;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using KDesktop.Core.Voice;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>What a picture of the screen is for: the model looking at it (JPEG), reading its text (the pixels), or both.</summary>
[Flags]
public enum ScreenUse { See = 1, Read = 2 }

/// <summary>
/// A picture of the screen: what it shows for the model («the window «X» of code»), the picture as JPEG (empty when it
/// was not asked to <see cref="ScreenUse.See"/>), the pixels at full size (when asked to <see cref="ScreenUse.Read"/>)
/// and, for people, the window's title (null: a monitor).
/// </summary>
public sealed record ScreenPicture(byte[] Jpeg, string What, int Width, int Height, string? Window = null, BgraImage? Pixels = null);

/// <summary>What the host lends the tools to see the desktop (stage 8.3): pictures of it and the page open in the browser.</summary>
public interface IScreen
{
    /// <summary>The window in front (the user's, never K-Desktop's) or, with <paramref name="wholeScreen"/>, the monitor with the mouse; null when it cannot.</summary>
    Task<ScreenPicture?> CaptureAsync(bool wholeScreen, ScreenUse use, CancellationToken cancellationToken);

    /// <summary>The address of the page open in the browser in front (Chrome, Edge, Firefox…), or null.</summary>
    Task<string?> BrowserAddressAsync(CancellationToken cancellationToken);
}

/// <summary>The model looking at a picture (stage 8.3): the assistant's own model when it sees.</summary>
public interface IVision
{
    /// <summary>Whether the model <paramref name="assistant"/> talks with can see pictures now.</summary>
    bool CanSee(string? assistant);

    /// <summary>What the model says about <paramref name="jpeg"/> for <paramref name="question"/>; null when it did not answer.</summary>
    Task<string?> LookAsync(string? assistant, byte[] jpeg, string question, CancellationToken cancellationToken);
}

/// <summary>Reading the text of a picture without the model (stage 10.1): the OCR of Windows.</summary>
public interface IScreenReader
{
    /// <summary>Whether Windows can read text (some OCR language is installed).</summary>
    bool CanRead { get; }

    /// <summary>The text of <paramref name="image"/>, in the assistant's language when its OCR is installed; null: it cannot.</summary>
    Task<ReadText?> ReadAsync(BgraImage image, CancellationToken cancellationToken);
}

/// <summary>The OCR of Windows, in the language of the flow (<see cref="Loc.Use"/>: the assistant's).</summary>
public sealed class WindowsScreenReader : IScreenReader
{
    public bool CanRead => ScreenText.Available;

    public Task<ReadText?> ReadAsync(BgraImage image, CancellationToken cancellationToken) =>
        ScreenText.ReadAsync(image, Loc.Instance.Language, cancellationToken: cancellationToken);
}

/// <summary>
/// «¿Qué pone aquí?», «¿qué error me sale?», «¿qué ves en la pantalla?» (stages 8.3 and 10.1): the window in front (or
/// the monitor). To read its text (or when the model does not see) the OCR of Windows reads it, exactly and in a moment,
/// and the text goes back; to understand what it shows (pictures, colours, where things are), or when the OCR finds no
/// text, a picture goes to the assistant's model, which must see, and what it says goes back. Nothing is kept.
/// </summary>
public sealed class LookAtScreenTool(IScreen? screen, IVision? vision, IScreenReader? reader = null) : ITool
{
    /// <summary>The most text of the screen the model gets (a whole window of code is more).</summary>
    public const int MaxText = 6000;

    public string Name => "look_at_screen";

    public string Description => "Looks at the user's screen (the window in front, or the whole monitor): reads its text exactly (an error message, what it says, a number) or tells what it shows (a picture, where things are). Use it when the user asks about what is on their screen (\"what does it say here\", \"what is this\"), or to copy the text of the screen.";

    public string ParametersSchemaJson =>
        """{"type":"object","properties":{"question":{"type":"string","description":"What to look for or answer about the screen, e.g. read the error message"},"read_text":{"type":"boolean","description":"true to read the text (what it says, an error, a number, to copy it); false to understand pictures, colours or layout"},"copy":{"type":"boolean","description":"Also copy the text read to the clipboard, when the user asks to copy it"},"whole_screen":{"type":"boolean","description":"The whole monitor instead of the window in front"}}}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.look_at_screen.intent");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var sees = vision?.CanSee(context.Assistant) == true;
        var reads = reader?.CanRead == true;
        if (screen is null || (!sees && !reads))
            return new ToolResult("You cannot see the screen: the model in use has no vision and Windows has no text recognition. Say so: a model that sees can be chosen in Options → Brain.")
            {
                Done = Loc.T("tool.look_at_screen.blind"), Reply = Loc.T("tool.look_at_screen.blind"), Failed = true,
            };
        var whole = Flag(arguments, "whole_screen") == true;
        var copy = Flag(arguments, "copy") == true;
        var question = arguments.GetString("question")?.Trim() is { Length: > 0 } asked ? asked : "What is on the screen?";
        var read = reads && (!sees || copy || (Flag(arguments, "read_text") ?? WantsText(question, context.Heard)));
        var use = (read ? ScreenUse.Read : 0) | (sees ? ScreenUse.See : 0);
        if (await screen.CaptureAsync(whole, use, cancellationToken).ConfigureAwait(false) is not { } picture)
            return ToolResult.Fail("The screen could not be captured.");
        var where = picture.Window is { Length: > 0 } title ? ToolText.Clean(title) : Loc.T("tool.look_at_screen.monitor");

        if (read && reader is not null && picture.Pixels is { } pixels)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var text = await reader.ReadAsync(pixels, cancellationToken).ConfigureAwait(false);
            Log.Info($"Asistente: texto de la pantalla ({picture.What}, {pixels.Width}×{pixels.Height}) leído en {watch.ElapsedMilliseconds} ms: {text?.Lines ?? 0} líneas, {text?.Text.Length ?? 0} letras");
            if (text is { Text.Length: > 0 })
            {
                var copied = copy && ClipboardSnapshot.SetText(text.Text);
                var shown = text.Text.Length > MaxText ? text.Text[..MaxText] + "\n[…]" : text.Text;
                var cut = text.Text.Length > MaxText ? $" (only the first {MaxText} characters of {text.Text.Length})" : "";
                return new ToolResult($"The text on the screen ({picture.What}), read exactly by OCR, line by line{cut}; pictures, colours and layout are not seen:\n---\n{shown}\n---\n"
                                      + (copied ? "It is now copied to the clipboard: say so. " : "")
                                      + "Answer the user from this, in their language, quoting what they ask for exactly.")
                {
                    Done = Loc.T(copied ? "tool.look_at_screen.copied" : "tool.look_at_screen.read", where),
                };
            }
            if (!sees)
                return new ToolResult($"No text could be read on the screen ({picture.What}), and the model in use cannot see pictures. Say so.")
                {
                    Done = Loc.T("tool.look_at_screen.noText", where),
                };
        }
        if (picture.Jpeg.Length == 0) return ToolResult.Fail("The screen could not be captured.");

        // The question quoted and the task said apart: a 4B model given the question bare only repeated it.
        var prompt = $"The user asks about this screenshot of {picture.What}: «{question}». Look at the picture and answer: say what is shown and copy the important visible text word for word.";
        var seeing = System.Diagnostics.Stopwatch.StartNew();
        var seen = await vision!.LookAsync(context.Assistant, picture.Jpeg, prompt, cancellationToken).ConfigureAwait(false);
        Log.Info($"Asistente: pantalla ({picture.What}, {picture.Width}×{picture.Height}, {picture.Jpeg.Length / 1024} KB) vista en {seeing.ElapsedMilliseconds} ms");
        if (string.IsNullOrWhiteSpace(seen)) return ToolResult.Fail("The model did not say what it saw.");
        return new ToolResult($"On the screen ({picture.What}): {seen.Trim()}\nAnswer the user from this, in their language.")
        {
            Done = Loc.T("tool.look_at_screen.done", where),
        };
    }

    /// <summary>A boolean argument (the models send true, "true" or nothing); null when not given.</summary>
    private static bool? Flag(JsonElement arguments, string name) => arguments.GetString(name)?.Trim().ToLowerInvariant() switch
    {
        "true" or "1" or "yes" => true,
        "false" or "0" or "no" => false,
        _ => null,
    };

    /// <summary>
    /// Whether the question or the order is about the text (<c>tool.look_at_screen.textWords</c>, this language and
    /// English): «¿qué pone?», «léeme el error», «copia el texto», for when the model does not say.
    /// </summary>
    public static bool WantsText(string question, string? heard)
    {
        var said = TextSimilarity.Words(question + " " + heard).ToHashSet(StringComparer.Ordinal);
        return new[] { Loc.T("tool.look_at_screen.textWords"), Loc.Strings(Loc.Fallback, "tool.look_at_screen.").GetValueOrDefault("tool.look_at_screen.textWords") }
            .OfType<string>().SelectMany(p => p.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .SelectMany(TextSimilarity.Words).Any(said.Contains);
    }
}

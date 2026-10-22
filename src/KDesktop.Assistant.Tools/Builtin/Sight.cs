using System.Text.Json;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>A picture of the screen: what it shows for the model («the window «X» of code»), the picture as JPEG and, for people, the window's title (null: a monitor).</summary>
public sealed record ScreenPicture(byte[] Jpeg, string What, int Width, int Height, string? Window = null);

/// <summary>What the host lends the tools to see the desktop (stage 8.3): pictures of it and the page open in the browser.</summary>
public interface IScreen
{
    /// <summary>The window in front (the user's, never K-Desktop's) or, with <paramref name="wholeScreen"/>, the monitor with the mouse; null when it cannot.</summary>
    Task<ScreenPicture?> CaptureAsync(bool wholeScreen, CancellationToken cancellationToken);

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

/// <summary>
/// «¿Qué pone aquí?», «¿qué error me sale?», «¿qué ves en la pantalla?» (stage 8.3): a picture of the window in front
/// (or of the monitor) to the assistant's model, which must see; what it says goes back as text. Nothing is kept.
/// </summary>
public sealed class LookAtScreenTool(IScreen? screen, IVision? vision) : ITool
{
    public string Name => "look_at_screen";

    public string Description => "Looks at the user's screen (the window in front, or the whole monitor) and tells what is there: reads its text, an error message, what a picture shows. Use it when the user asks about what is on their screen (\"what does it say here\", \"what is this\").";

    public string ParametersSchemaJson =>
        """{"type":"object","properties":{"question":{"type":"string","description":"What to look for or answer about the screen, e.g. read the error message"},"whole_screen":{"type":"boolean","description":"The whole monitor instead of the window in front"}}}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.look_at_screen.intent");

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        if (screen is null || vision is null || !vision.CanSee(context.Assistant))
            return new ToolResult("You cannot see the screen: the model in use has no vision. Say so: a model that sees can be chosen in Options → Brain.")
            {
                Done = Loc.T("tool.look_at_screen.blind"), Reply = Loc.T("tool.look_at_screen.blind"), Failed = true,
            };
        var whole = string.Equals(arguments.GetString("whole_screen"), "true", StringComparison.OrdinalIgnoreCase);
        if (await screen.CaptureAsync(whole, cancellationToken).ConfigureAwait(false) is not { } picture)
            return ToolResult.Fail("The screen could not be captured.");
        var question = arguments.GetString("question")?.Trim() is { Length: > 0 } asked ? asked : "What is on the screen?";
        // The question quoted and the task said apart: a 4B model given the question bare only repeated it.
        var prompt = $"The user asks about this screenshot of {picture.What}: «{question}». Look at the picture and answer: say what is shown and copy the important visible text word for word.";
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var seen = await vision.LookAsync(context.Assistant, picture.Jpeg, prompt, cancellationToken).ConfigureAwait(false);
        Log.Info($"Asistente: pantalla ({picture.What}, {picture.Width}×{picture.Height}, {picture.Jpeg.Length / 1024} KB) vista en {watch.ElapsedMilliseconds} ms");
        if (string.IsNullOrWhiteSpace(seen)) return ToolResult.Fail("The model did not say what it saw.");
        return new ToolResult($"On the screen ({picture.What}): {seen.Trim()}\nAnswer the user from this, in their language.")
        {
            Done = Loc.T("tool.look_at_screen.done", picture.Window is { Length: > 0 } title ? ToolText.Clean(title) : Loc.T("tool.look_at_screen.monitor")),
        };
    }
}

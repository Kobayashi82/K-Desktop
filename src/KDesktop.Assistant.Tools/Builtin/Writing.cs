using System.Globalization;
using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>The assistant's model rewriting a text (stage 8, <c>transformar_texto</c>): a request of its own, apart from the conversation.</summary>
public interface IWriter
{
    /// <summary>Whether <paramref name="assistant"/> has a model to write with now.</summary>
    bool CanWrite(string? assistant);

    /// <summary>The text done as <paramref name="instruction"/> says (English, for the model); null when it did not answer.</summary>
    Task<string?> RewriteAsync(string? assistant, string instruction, string text, CancellationToken cancellationToken);
}

/// <summary>
/// «tradúceme esto al inglés», «corrige la ortografía», «resúmelo», «ponlo más formal», «hazlo una lista» (stage 8,
/// <c>transformar_texto</c>): K-Desktop takes the selection of the window in front (or what is copied), the model
/// rewrites it, and K-Desktop pastes it in its place (or leaves it copied); a summary is said, and left copied. The
/// clipboard is left as it was, except when the result is left copied.
/// </summary>
public sealed class TransformTextTool(ICommandBridge bridge, IWriter? writer) : ITool
{
    public string Name => "transform_text";

    public string Description => "Works on the text the user has selected (or else copied): translate, correct (spelling and grammar), summarize, formal, casual, list (bullet points), shorter, longer, or other (say how in details). The result replaces the selection, or is left copied; a summary is said to the user. Use it for \"this\", \"the text\", \"what I selected / copied\". Not for a text the user says in the order itself: do that yourself.";

    public static readonly IReadOnlyList<string> Actions = ["translate", "correct", "summarize", "formal", "casual", "list", "shorter", "longer", "other"];

    public string ParametersSchemaJson { get; } =
        $$$$"""{"type":"object","properties":{"action":{"type":"string","enum":[{{{{string.Join(",", Actions.Select(a => $"\"{a}\""))}}}}]},"language":{"type":"string","description":"For translate: the language to translate into, in English (e.g. English, French)"},"details":{"type":"string","description":"For other, or anything more the user asked (e.g. keep it under 50 words), in English"},"deliver":{"type":"string","enum":["auto","paste","copy","say"],"description":"auto (default): replace the selection, or copy it if it came from the clipboard; a summary is said"}},"required":["action"]}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    // Pasting only replaces what the user selected and asked to change.
    public ToolRisk Risk => ToolRisk.Safe;

    public string Intent(JsonElement arguments) => Loc.T("tool.transform_text." + ActionOf(arguments));

    private static string ActionOf(JsonElement arguments) =>
        arguments.GetString("action")?.Trim().ToLowerInvariant() switch
        {
            { } action when Actions.Contains(action) => action,
            "summary" or "summarise" => "summarize",
            "fix" or "spelling" or "proofread" => "correct",
            "bullets" or "bullet_points" => "list",
            "shorten" => "shorter",
            "expand" => "longer",
            _ => "other",
        };

    /// <summary>
    /// The text the order itself brings, after a colon or between quotes («tradúceme al inglés: me duele la cabeza»), or
    /// null. The model is answering it: what is selected or copied has nothing to do with it.
    /// </summary>
    public static string? InOrder(string? heard)
    {
        if (string.IsNullOrWhiteSpace(heard)) return null;
        string? text = null;
        var colon = heard.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0) text = heard[(colon + 1)..];
        else
        {
            var open = heard.IndexOfAny(['«', '"', '“']);
            var close = open < 0 ? -1 : heard.IndexOfAny(['»', '"', '”'], open + 1);
            if (close > open) text = heard[(open + 1)..close];
        }
        text = text?.Trim().Trim('«', '»', '"', '“', '”', ' ');
        return text is { Length: > 0 } && text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2 ? text : null;
    }

    /// <summary>What to ask the model, in English.</summary>
    public static string Instruction(string action, string? language, string? details)
    {
        var instruction = action switch
        {
            "translate" => $"Translate it into {(string.IsNullOrWhiteSpace(language) ? "English" : language.Trim())}.",
            "correct" => "Correct its spelling, grammar and punctuation; change nothing else.",
            // It is said to the user: in their language, whatever the text is in (a Spanish text came back in English).
            "summarize" => $"Summarize it in {new CultureInfo(Loc.Instance.Language).EnglishName}, in a few short sentences.",
            "formal" => "Rewrite it in a more formal, polite tone.",
            "casual" => "Rewrite it in a more casual, friendly tone.",
            "list" => "Turn it into a list of short bullet points.",
            "shorter" => "Make it shorter, keeping what matters.",
            "longer" => "Expand it a little, in the same tone.",
            _ => string.Empty,
        };
        return $"{instruction} {details?.Trim()}".Trim();
    }

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var action = ActionOf(arguments);
        var instruction = Instruction(action, arguments.GetString("language"), arguments.GetString("details"));
        if (instruction.Length == 0) return ToolResult.Fail("Say what to do with the text (details).");
        // «tradúceme al inglés: me duele la cabeza» would have translated whatever was selected or copied.
        if (InOrder(context.Heard) is { } inOrder)
            return ToolResult.Fail($"The text is in the order itself (\"{inOrder}\"), not selected or copied: do it yourself and answer with the result, without tools.");
        if (writer is null || !writer.CanWrite(context.Assistant)) return ToolResult.Fail("There is no model to write with.");
        var intent = Intent(arguments);

        var taken = await bridge.TextAsync("grab", context.Assistant, "auto", string.Empty, string.Empty, 0, cancellationToken).ConfigureAwait(false);
        if (taken is null) return ToolResult.Fail("K-Desktop did not answer.");
        if (!taken.Ok)
        {
            var why = Loc.T("tool.transform_text.reason." + (taken.Reason is { Length: > 0 } r ? r : "busy"));
            return ToolResult.Fail($"No text to work on ({taken.Reason}). Say so.") with { Reply = why, Done = why };
        }

        var result = await writer.RewriteAsync(context.Assistant, instruction, taken.Text, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result)) return ToolResult.Fail("The model gave no text back.");

        var deliver = arguments.GetString("deliver")?.Trim().ToLowerInvariant() switch
        {
            { } given when given is "paste" or "copy" or "say" => given,
            _ when action == "summarize" => "say",
            _ => taken.From == "selection" ? "paste" : "copy",
        };
        if (deliver == "paste" && taken.Hwnd == 0) deliver = "copy";
        // Its figure types into the window the text goes back to.
        if (deliver == "paste") context.Show?.Invoke(ToolGesture.Typing(taken.Hwnd));
        var put = await bridge.TextAsync("put", context.Assistant, string.Empty, result, deliver == "paste" ? "paste" : "copy", taken.Hwnd, cancellationToken).ConfigureAwait(false);
        if (put is not { Ok: true })
        {
            var why = Loc.T("tool.transform_text.reason." + (put?.Reason is { Length: > 0 } r ? r : "busy"));
            return ToolResult.Fail($"The result could not be put back ({put?.Reason}): {Excerpt(result)}") with { Reply = why };
        }

        if (deliver == "say")
            return new ToolResult($"The result (also left copied): \"{Excerpt(result)}\". Tell it to the user.")
            {
                Done = Loc.T("tool.transform_text.doneCopied", intent),
                Reply = Excerpt(result),
                Acted = true,
            };
        var pasted = put.From == "pasted";
        return new ToolResult($"Done: the result was {(pasted ? $"pasted in place of the selection in \"{taken.Window}\"" : "left in the clipboard")}{(put.Reason == "moved" ? " (another window was in front)" : "")}. It begins: \"{Excerpt(result, 200)}\". Say it briefly; do not read the whole text.")
        {
            Done = pasted ? Loc.T("tool.transform_text.donePasted", intent, ToolText.Clean(taken.Window)) : Loc.T("tool.transform_text.doneCopied", intent),
            Reply = Loc.T(pasted ? "tool.transform_text.replyPasted" : put.Reason == "moved" ? "tool.transform_text.replyMoved" : "tool.transform_text.replyCopied"),
            Acted = true,
        };
    }

    private static string Excerpt(string text, int max = 700)
    {
        var clean = text.Trim();
        return clean.Length <= max ? clean : clean[..max].TrimEnd() + "…";
    }
}

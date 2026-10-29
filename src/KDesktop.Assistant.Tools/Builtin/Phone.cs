using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// «Mándame esta dirección al móvil», «pásame el pdf al teléfono» (B11, 5.0.353): a text, a link and/or a small file to the
/// paired phones and browsers, through K-Desktop (<see cref="SendPhoneMessage"/>), kept until each of them has it. It asks
/// the first time (what is sent leaves the PC, to the user's own devices).
/// </summary>
public sealed class SendToPhoneTool(ICommandBridge bridge) : ITool
{
    public string Name => "send_to_phone";

    public string Description => "Sends a text, a link or a small file (25 MB at most) to the user's phone (the paired K-Desktop app or web app), as a notification. All their devices unless they name one.";

    public string ParametersSchemaJson { get; } =
        """{"type":"object","properties":{"text":{"type":"string","description":"What to send, the text itself (an address, a phone number, a list…)"},"title":{"type":"string","description":"A short title (optional)"},"url":{"type":"string","description":"A web link to send"},"file":{"type":"string","description":"The full path of a file to send"},"device":{"type":"string","description":"Part of the device's name when the user names one (the tablet); empty: all"}}}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.AskOnce;

    public string Intent(JsonElement arguments) => Loc.T("tool.send_to_phone.intent", What(arguments));

    /// <summary>What it sends, for people: the title, the text, the link or the file's name.</summary>
    private static string What(JsonElement arguments)
    {
        var what = new[] { arguments.GetString("title"), arguments.GetString("text"), arguments.GetString("url"), Path.GetFileName(arguments.GetString("file") ?? string.Empty) }
            .Select(ToolText.Clean).FirstOrDefault(t => t.Length > 0) ?? "…";
        return what.Length > 50 ? what[..50] + "…" : what;
    }

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var file = arguments.GetString("file")?.Trim().Trim('"');
        if (file is { Length: > 0 } && !File.Exists(file)) return ToolResult.Fail($"There is no file \"{file}\": find it first (find_files).");
        var request = new SendPhoneMessage(string.Empty, context.Assistant, arguments.GetString("title")?.Trim() ?? string.Empty, arguments.GetString("text")?.Trim() ?? string.Empty,
            arguments.GetString("url"), file is { Length: > 0 } ? file : null, arguments.GetString("device") is { Length: > 0 } device ? device.Trim() : null);
        if (request.Title.Length == 0 && request.Text.Length == 0 && request.Url is null && request.File is null) return ToolResult.Fail("Say what to send (text, url or file).");
        var sent = await bridge.SendToPhoneAsync(request, cancellationToken).ConfigureAwait(false);
        if (sent is null) return ToolResult.Fail("K-Desktop did not answer: nothing was sent.");
        if (!sent.Ok)
        {
            return sent.Reason switch
            {
                "noDevice" when sent.Devices is { Count: > 0 } devices => ToolResult.Fail($"No paired device is called \"{request.Device}\". The devices are: {string.Join(", ", devices)}.")
                    with { Reply = Loc.T("tool.send_to_phone.noSuchDevice", request.Device ?? string.Empty) },
                "noDevice" => ToolResult.Fail("No phone or browser is paired with K-Desktop: nothing was sent. Tell the user to pair one in Options → Web app.")
                    with { Reply = Loc.T("tool.send_to_phone.noDevice") },
                "fileTooBig" => ToolResult.Fail("The file is too big (25 MB at most): nothing was sent.") with { Reply = Loc.T("tool.send_to_phone.tooBig") },
                _ => ToolResult.Fail($"It could not be sent ({sent.Reason})."),
            };
        }
        var names = string.Join(", ", sent.Names);
        return new ToolResult($"Sent to {names}. It shows there as a notification, or when they open the app.")
        {
            Done = Loc.T("tool.send_to_phone.done", What(arguments), names),
            Reply = Loc.T("tool.send_to_phone.reply", names),
            Acted = true,
        };
    }
}

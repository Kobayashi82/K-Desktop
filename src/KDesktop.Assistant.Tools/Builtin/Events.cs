using System.Text.Json;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>
/// «Cuando abra Steam, ejecuta Modo juego», «cada vez que conecte los cascos dime la batería», «si me voy 10 minutos, bloquea
/// el PC» (B8, 5.0.351): an event of any kind made and saved at once (<see cref="AssistantEvents"/>), with the user's
/// commands and/or an order for the assistant at that time (approved now, like <c>schedule</c>). It asks like the riskiest
/// thing it will do; then it tells what it made, and «deshaz» removes it. It shows in Eventos, marked with who made it.
/// </summary>
public sealed class CreateEventTool(ICommandBridge bridge, ToolRegistry registry) : ITool
{
    public string Name => "create_event";

    public string Description => "Makes and saves a K-Desktop event: when something happens (a time, a program opens or comes to the front, the PC is left idle, locked or wakes up, a file appears in a folder, a drive, device, headphones or screen is connected, the network changes, the battery or the processor passes a %, sunrise or sunset, K-Desktop starts…), it runs the user's commands and/or gives the assistant an order. "
                                 + "For «when X, do Y» and «every time…». A plain order at a time is schedule.";

    public string ParametersSchemaJson { get; } =
        $$$"""{"type":"object","properties":{"when":{"type":"string","enum":[{{{string.Join(",", AssistantEvents.Kinds.Select(k => $"\"{k}\""))}}}],"description":"What fires it (start: when K-Desktop starts with the PC; every: every N minutes; idle: N minutes without touching the PC; device: a controller, keyboard or USB gadget)"},"time":{"type":"string","description":"time: HH:MM"},"days":{"type":"string","description":"time: daily, workdays, weekend, or days in English with commas"},"date":{"type":"string","description":"time: YYYY-MM-DD for only that day"},"minutes":{"type":"integer","description":"every, idle: minutes; load: how long it must last"},"programs":{"type":"array","items":{"type":"string"},"description":"program_opens, program_in_front: the programs as the user names them"},"what":{"type":"string","description":"file: created, changed, deleted, renamed or any; audio_device: connected or default; load: cpu, gpu, memory or vram"},"match":{"type":"string","description":"Part of the name of the network, device, drive, headphones or screen (empty: any); remote_call: the name to call it by"},"folder":{"type":"string","description":"file: desktop, downloads, documents, pictures, videos, music or a full path"},"filter":{"type":"string","description":"file: like *.pdf (empty: any)"},"percent":{"type":"integer","description":"load, battery: the %"},"above":{"type":"boolean","description":"load, battery: above the % (true) or below (false)"},"offset":{"type":"integer","description":"sunrise, sunset: minutes after (negative: before)"},"commands":{"type":"array","items":{"type":"string"},"description":"Exact names of the user's K-Desktop commands to run"},"end_commands":{"type":"array","items":{"type":"string"},"description":"Commands to run when it ends (the program closes, the user comes back, the device is removed…)"},"order":{"type":"string","description":"What to ask the assistant then, in the user's words, when no command does it"},"tools":{"type":"array","items":{"type":"string"},"description":"With order: the tools it will use then"},"name":{"type":"string","description":"A short name for the event (empty: made from what it does)"},"once":{"type":"boolean","description":"Only once, then it is deleted"},"only_from":{"type":"string","description":"Only between this time (HH:MM)…"},"only_to":{"type":"string","description":"…and this one"},"only_days":{"type":"string","description":"Only on these days (like days)"},"only_if_running":{"type":"string","description":"Only while this program is open"}},"required":["when"]}""";

    public IReadOnlyList<string> Examples => ToolText.Examples(Name);

    public ToolRisk Risk => ToolRisk.AskOnce;

    /// <summary>As risky as what it will do: the user's commands ask once, the order's tools by their own risk; nothing that acts, nothing asked.</summary>
    public ToolRisk RiskFor(JsonElement arguments)
    {
        var commands = ScheduleTool.Strings(arguments, "commands").Concat(ScheduleTool.Strings(arguments, "end_commands")).Any();
        return ScheduleTool.RiskOf(registry, ScheduleTool.Strings(arguments, "tools"), commands);
    }

    public string Intent(JsonElement arguments)
    {
        var spec = Spec(arguments, out _);
        var when = AssistantEvents.Trigger(spec, DateTime.Now, ProcessNames.Normalize, out _)?.Summary ?? spec.When;
        return Loc.T("tool.create_event.intent", when, string.Join(", ", Runs(spec)));
    }

    private static IEnumerable<string> Runs(EventSpec spec) =>
        (spec.Commands ?? []).Select(c => $"«{c}»").Concat(spec.Order is { Length: > 0 } order ? [$"«{order}»"] : []);

    /// <summary>The event as the model gives it; the folder of «file» as a full path (null <paramref name="folderError"/>: fine).</summary>
    private EventSpec Spec(JsonElement arguments, out ToolResult? folderError)
    {
        folderError = null;
        var when = arguments.GetString("when")?.Trim().ToLowerInvariant() ?? string.Empty;
        string? folder = null;
        if (when == "file")
        {
            var asked = arguments.GetString("folder");
            folder = FileWork.Folder(string.IsNullOrWhiteSpace(asked) ? "downloads" : asked);
            if (folder is null) folderError = FileWork.NoFolder(asked);
        }
        var tools = ScheduleTool.Strings(arguments, "tools").Where(t => registry.FindAny(t) is not null).ToList();
        int? Number(string name) => arguments.GetNumber(name) is { } n ? (int)Math.Round(n) : null;
        bool? Flag(string name) => arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
        List<string>? List(string name) => ScheduleTool.Strings(arguments, name).ToList() is { Count: > 0 } items ? items : null;
        return new EventSpec(when, ToolText.Clean(arguments.GetString("name")) is { Length: > 0 } name ? name : null,
            arguments.GetString("time"), arguments.GetString("days"), arguments.GetString("date"), Number("minutes"),
            List("programs"), arguments.GetString("what"), arguments.GetString("match"), folder, arguments.GetString("filter"),
            Number("percent"), Flag("above"), Number("offset"), List("commands"), List("end_commands"),
            ToolText.Clean(arguments.GetString("order")) is { Length: > 0 } order ? order : null, tools.Count > 0 ? tools : null, Flag("once"),
            arguments.GetString("only_from"), arguments.GetString("only_to"), arguments.GetString("only_days"), arguments.GetString("only_if_running"));
    }

    public async Task<ToolResult> RunAsync(JsonElement arguments, ToolContext context, CancellationToken cancellationToken)
    {
        var spec = Spec(arguments, out var folderError);
        if (folderError is not null) return folderError;
        // From text of third parties (telegram, phase 4.3): its order gets no tools approved.
        if (!ScheduleTool.MayApprove(context)) spec = spec with { Tools = null };
        if (!AssistantEvents.Kinds.Contains(spec.When)) return ToolResult.Fail("Say what fires it (when): one of " + string.Join(", ", AssistantEvents.Kinds) + ".");
        var made = await bridge.MakeEventAsync(new MakeEventMessage(string.Empty, "add", context.Assistant, spec), cancellationToken).ConfigureAwait(false);
        if (made is null) return ToolResult.Fail("K-Desktop did not answer: no event was made.");
        if (!made.Ok)
            return made.Reason switch
            {
                "time" => ToolResult.Fail("Say the time as HH:MM: no event was made."),
                "past" => ToolResult.Fail("That day and time have already passed: no event was made.") with { Reply = Loc.T("tool.schedule.past") },
                "programs" => ToolResult.Fail("Say which program (programs): no event was made."),
                "folder" => ToolResult.Fail("That folder does not exist: no event was made."),
                "nothing" => ToolResult.Fail("Say what it does: commands of the user that exist, or an order. No event was made."
                                             + (made.Unknown is { Count: > 0 } u ? $" These are not commands of the user: {string.Join(", ", u)}." : string.Empty)),
                "tooMany" => ToolResult.Fail("There are too many events made by assistants: no event was made.") with { Reply = Loc.T("tool.create_event.tooMany") },
                _ => ToolResult.Fail("It could not be made (" + made.Reason + ")."),
            };
        var runs = string.Join(", ", made.Runs ?? []);
        var unknown = made.Unknown ?? [];
        var text = $"Made and saved the event \"{made.Name}\": {made.Summary} → {runs}."
                   + (made.Programs is { Count: > 0 } p ? $" It watches the process {string.Join(", ", p)}." : string.Empty)
                   + (unknown.Count > 0 ? $" These are not commands of the user and were left out: {string.Join(", ", unknown)}. Tell the user." : string.Empty)
                   + " Tell the user what it does in a short sentence.";
        return new ToolResult(text)
        {
            Done = Loc.T("tool.create_event.done", made.Name ?? string.Empty),
            Reply = Loc.T("tool.create_event.reply", made.Summary ?? string.Empty, runs)
                    + (unknown.Count > 0 ? " " + Loc.T("tool.schedule.unknown", string.Join(", ", unknown)) : string.Empty),
            Acted = true,
            Undo = new ToolUndo(Name, made.EventId ?? string.Empty),
        };
    }

    /// <summary>Removes it again.</summary>
    public async Task<bool> UndoAsync(ToolUndo undo, CancellationToken cancellationToken) =>
        undo.Data.Length > 0 && await bridge.MakeEventAsync(new MakeEventMessage(string.Empty, "remove", Remove: undo.Data), cancellationToken).ConfigureAwait(false) is { Ok: true };
}

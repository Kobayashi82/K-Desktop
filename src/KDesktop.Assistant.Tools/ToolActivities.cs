using KDesktop.Assistant.Sdk;
using KDesktop.Assistant.Tools.Declarative;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools;

/// <summary>What the assistant is busy with while a tool runs (stage 13.4), for its character and the panel.</summary>
public static class ToolActivities
{
    private static readonly Dictionary<string, AssistantActivity> Builtin = new(StringComparer.Ordinal)
    {
        ["run_command"] = AssistantActivity.Command,
        ["run_event"] = AssistantActivity.Command,
        ["find_command"] = AssistantActivity.Command,
        ["describe_command"] = AssistantActivity.Command,
        ["add_voice_phrase"] = AssistantActivity.Command,
        ["control_app"] = AssistantActivity.Apps,
        ["open_app"] = AssistantActivity.Apps,
        ["end_process"] = AssistantActivity.Apps,
        ["look_up_web"] = AssistantActivity.SearchingWeb,
        ["search_web"] = AssistantActivity.SearchingWeb,
        ["read_web"] = AssistantActivity.ReadingWeb,
        ["open_web"] = AssistantActivity.OpeningWeb,
        ["find_files"] = AssistantActivity.Files,
        ["open_file"] = AssistantActivity.Files,
        ["read_file"] = AssistantActivity.Files,
        ["delete_file"] = AssistantActivity.Files,
        ["move_file"] = AssistantActivity.Files,
        ["save_note"] = AssistantActivity.Notes,
        ["find_notes"] = AssistantActivity.Notes,
        ["remember"] = AssistantActivity.Memory,
        ["forget"] = AssistantActivity.Memory,
        ["weather"] = AssistantActivity.Weather,
        ["whats_playing"] = AssistantActivity.Music,
        ["control_media"] = AssistantActivity.Music,
        ["set_timer"] = AssistantActivity.Timer,
        ["cancel_timer"] = AssistantActivity.Timer,
        ["notify_when"] = AssistantActivity.Watching,
        ["stop_watching"] = AssistantActivity.Watching,
        ["look_at_screen"] = AssistantActivity.Screen,
        ["transform_text"] = AssistantActivity.Writing,
        ["calculate"] = AssistantActivity.Calculating,
        ["convert"] = AssistantActivity.Calculating,
        ["date_math"] = AssistantActivity.Calculating,
        ["get_time"] = AssistantActivity.Calculating,
        ["pc_status"] = AssistantActivity.System,
        ["free_brain"] = AssistantActivity.System,
    };

    /// <summary>The activity of a tool: the built-in ones by name, a JSON tool that runs a command as one; the rest, «other».</summary>
    public static AssistantActivity Of(ITool tool) =>
        Builtin.TryGetValue(tool.Name, out var activity) ? activity : tool is CommandJsonTool ? AssistantActivity.Command : AssistantActivity.Other;

    /// <summary>The activity of a tool by its name (one that is not there: «other»).</summary>
    public static AssistantActivity Of(string tool) => Builtin.GetValueOrDefault(tool, AssistantActivity.Other);

    /// <summary>The key of its words: <c>assistant.activity.searchingWeb</c>…</summary>
    public static string Id(AssistantActivity activity) => activity switch
    {
        AssistantActivity.None => "none",
        _ => char.ToLowerInvariant(activity.ToString()[0]) + activity.ToString()[1..],
    };

    /// <summary>The activity a <see cref="Id"/> names; «none» for one not known.</summary>
    public static AssistantActivity Parse(string? id) =>
        id is { Length: > 0 } && Enum.TryParse<AssistantActivity>(id, ignoreCase: true, out var activity) && Enum.IsDefined(activity) ? activity : AssistantActivity.None;

    /// <summary>
    /// A few words saying it in the language in use («Buscando en internet»), null for none. «Other» names the tool (its
    /// title when it has one).
    /// </summary>
    public static string? Text(AssistantActivity activity, string? tool = null)
    {
        if (activity == AssistantActivity.None) return null;
        if (activity != AssistantActivity.Other) return Loc.T("assistant.activity." + Id(activity));
        return tool is { Length: > 0 } ? Loc.T("assistant.activity.otherTool", TitleOf(tool)) : Loc.T("assistant.activity.other");
    }

    /// <summary>The title of a tool for people («Buscar en internet»), or its name.</summary>
    private static string TitleOf(string tool)
    {
        var key = "tool." + tool + ".title";
        var title = Loc.T(key);
        return title == key ? tool : title;
    }
}

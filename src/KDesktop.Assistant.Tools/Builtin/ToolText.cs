using System.Text.Json;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Tools.Builtin;

/// <summary>Pieces of text for asking, for the activity and for the model.</summary>
internal static class ToolText
{
    /// <summary>How much of what the model wrote (a note, a search…) is shown.</summary>
    public const int MaxShown = 80;

    /// <summary>On one line, without extra spaces or quotes, and cut.</summary>
    public static string Clean(string? text)
    {
        var clean = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim().Trim('"', '«', '»', '“', '”');
        return clean.Length <= MaxShown ? clean : clean[..MaxShown].TrimEnd() + "…";
    }

    /// <summary>Its example phrases in the user's language (<c>tool.&lt;name&gt;.examples</c>, separated by «|»), or none.</summary>
    public static IReadOnlyList<string> Examples(string tool)
    {
        var key = $"tool.{tool}.examples";
        var text = Loc.T(key);
        return text == key ? [] : text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>The time as the user's language writes it («17:42»).</summary>
    public static string Clock(DateTimeOffset at) => at.ToLocalTime().ToString("t", Loc.Instance.Culture);

    /// <summary>«10 min», «1 h 5 min», «30 s», «2 días».</summary>
    public static string Left(TimeSpan left) => left.TotalSeconds switch
    {
        < 60 => Loc.T("tool.left.seconds", Math.Max(1, (int)Math.Round(left.TotalSeconds))),
        < 3600 => Loc.T("tool.left.minutes", (int)Math.Round(left.TotalMinutes)),
        < 86400 => left.Minutes == 0 ? Loc.T("tool.left.hoursOnly", (int)left.TotalHours) : Loc.T("tool.left.hours", (int)left.TotalHours, left.Minutes),
        _ => Loc.T("tool.left.days", (int)Math.Round(left.TotalDays)),
    };

    /// <summary>Several parameters, all optional (name, JSON type, description for the model).</summary>
    public static string Optional(params (string Name, string Type, string Description)[] parameters) =>
        "{\"type\":\"object\",\"properties\":{"
        + string.Join(",", parameters.Select(p => $"\"{p.Name}\":{{\"type\":\"{p.Type}\",\"description\":\"{JsonEncodedText.Encode(p.Description)}\"}}"))
        + "}}";

    /// <summary>One required text parameter.</summary>
    public static string OneString(string name, string description) =>
        $$$"""{"type":"object","properties":{"{{{name}}}":{"type":"string","description":"{{{JsonEncodedText.Encode(description)}}}"}},"required":["{{{name}}}"]}""";

    public const string NoParameters = """{"type":"object","properties":{}}""";
}

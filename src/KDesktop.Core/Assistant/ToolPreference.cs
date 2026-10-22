namespace KDesktop.Core.Assistant;

/// <summary>
/// What the user chose for one tool in Options → Tools (stage 9.3): switched off (the assistants never get it), or asking
/// otherwise than it says (<c>safe</c>, <c>askOnce</c>, <c>askAlways</c>; null: as it says).
/// </summary>
public sealed record ToolPreference(bool Enabled = true, string? Risk = null)
{
    public static string Name(ToolRisk risk) => risk switch
    {
        ToolRisk.AskOnce => "askOnce",
        ToolRisk.AskAlways => "askAlways",
        _ => "safe",
    };

    public static ToolRisk? Parse(string? risk) => risk switch
    {
        "safe" => ToolRisk.Safe,
        "askOnce" => ToolRisk.AskOnce,
        "askAlways" => ToolRisk.AskAlways,
        _ => null,
    };
}

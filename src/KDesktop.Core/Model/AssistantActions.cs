using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Model;

/// <summary>
/// «Asistente: acción»: one of the extras an assistant declares in its manifest («Lander: modo manual»). K-Desktop only
/// sends it (message <c>extra</c>); the assistant does it. One that is not on the desktop comes out first.
/// </summary>
public partial class AssistantExtraAction : CommandAction
{
    /// <summary>The assistant's id («lander»).</summary>
    [ObservableProperty] private string _assistant = string.Empty;

    /// <summary>The extra's id in its manifest («lander.manual»).</summary>
    [ObservableProperty] private string _extra = string.Empty;

    /// <summary>Display-only: «Lander: Modo manual» (resolved from the installed manifest), null when it is not installed.</summary>
    [ObservableProperty]
    [property: JsonIgnore] private string? _extraName;

    /// <summary>Both ids in one value («lander/lander.manual»), for the editor's list.</summary>
    [JsonIgnore]
    public string Target
    {
        get => Extra.Length == 0 ? string.Empty : $"{Assistant}/{Extra}";
        set
        {
            var parts = (value ?? string.Empty).Split('/', 2);
            Assistant = parts.Length == 2 ? parts[0] : string.Empty;
            Extra = parts.Length == 2 ? parts[1] : string.Empty;
        }
    }

    partial void OnAssistantChanged(string value) => OnPropertyChanged(nameof(Target));

    partial void OnExtraChanged(string value) => OnPropertyChanged(nameof(Target));

    public override ActionKind Kind => ActionKind.AssistantExtra;

    public override string Summary =>
        ExtraName ?? (Extra.Length == 0 ? Loc.T("summary.noExtra") : Loc.T("summary.extraMissing", Assistant, Extra));

    public override bool IsTransient(string? property) => property is nameof(ExtraName) or nameof(Target) || base.IsTransient(property);
}

/// <summary>
/// «Asistente: preguntar»: an order for an assistant, as if it had been said to it («¿qué tenía apuntado?»), with the
/// same path as the voice and the chat (message <c>heard</c>, source <c>command</c>). Without text, the box to write it
/// opens (so a hotkey can open it).
/// </summary>
public partial class AssistantAskAction : CommandAction
{
    /// <summary>The assistant's id; empty: the one Options → Assistants says for orders to nobody in particular.</summary>
    [ObservableProperty] private string _assistant = string.Empty;

    /// <summary>The order; empty: the box to write it opens.</summary>
    [ObservableProperty] private string _text = string.Empty;

    public override ActionKind Kind => ActionKind.AssistantAsk;

    public override string Summary => Text.Trim().Length == 0 ? Loc.T("summary.assistantAskBox") : $"«{Text.Trim()}»";
}

/// <summary>What «Asistente: cerebro» does: free the graphics card, or load the model before talking.</summary>
public enum AssistantBrainOp { Unload, Load }

/// <summary>
/// «Asistente: cerebro»: the assistants' model leaves the graphics card (before a game, a render…) or loads now (the
/// first answer does not wait for it). Message <c>brain</c> <c>unload</c> / <c>load</c>; without the module running there
/// is nothing loaded by it.
/// </summary>
public partial class AssistantBrainAction : CommandAction
{
    [ObservableProperty] private AssistantBrainOp _op;

    public override ActionKind Kind => ActionKind.AssistantBrain;

    public override string Summary => Loc.T("summary.assistantBrain." + Op.ToString().ToLowerInvariant());
}

/// <summary>
/// «Asistente: decir»: an assistant says something (message <c>say</c>), in its bubble and in the chat, without the AI:
/// an event («the copy finished»), a reminder in a command. Without a body (nobody on the desktop), K-Desktop's OSD.
/// </summary>
public partial class AssistantSayAction : CommandAction
{
    /// <summary>The assistant's id; empty: the one Options → Assistants says for what is addressed to nobody.</summary>
    [ObservableProperty] private string _assistant = string.Empty;

    [ObservableProperty] private string _text = string.Empty;

    public override ActionKind Kind => ActionKind.AssistantSay;

    public override string Summary => Text.Trim().Length == 0 ? Loc.T("summary.assistantSayEmpty") : $"«{Text.Trim()}»";
}

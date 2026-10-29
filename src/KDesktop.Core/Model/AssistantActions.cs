using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Model;

/// <summary>
/// «Asistente: acción»: one of the extras an assistant declares in its manifest («Lander: modo manual»), or putting it
/// on the desktop or hiding it. K-Desktop only sends an extra (message <c>extra</c>); the assistant does it. One that is
/// not on the desktop comes out first, or (without <see cref="BringOut"/>) the action is not done.
/// </summary>
public partial class AssistantExtraAction : CommandAction
{
    /// <summary>The <see cref="Extra"/> that puts the assistant on the desktop (no manifest's extra starts with «@»).</summary>
    public const string ShowExtra = "@show";

    /// <summary>The <see cref="Extra"/> that hides it.</summary>
    public const string HideExtra = "@hide";

    /// <summary>The assistant's id («lander»).</summary>
    [ObservableProperty] private string _assistant = string.Empty;

    /// <summary>The extra's id in its manifest («lander.manual»), or <see cref="ShowExtra"/> / <see cref="HideExtra"/>.</summary>
    [ObservableProperty] private string _extra = string.Empty;

    /// <summary>An extra for an assistant that is not on the desktop: it comes out to do it (otherwise, nothing is done).</summary>
    [ObservableProperty] private bool _bringOut = true;

    /// <summary>Putting it on the desktop or hiding it (not one of its extras).</summary>
    [JsonIgnore] public bool IsShowOrHide => Extra is ShowExtra or HideExtra;

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

    partial void OnExtraChanged(string value)
    {
        // A list without it (another assistant chosen) writes null for a moment.
        if (value is null)
        {
            Extra = string.Empty;
            return;
        }
        OnPropertyChanged(nameof(Target));
        OnPropertyChanged(nameof(IsShowOrHide));
    }

    public override ActionKind Kind => ActionKind.AssistantExtra;

    public override string Summary =>
        ExtraName ?? (Extra.Length == 0 ? Loc.T("summary.noExtra")
            : Loc.T("summary.extraMissing", Assistant, IsShowOrHide ? Loc.T(Extra == ShowExtra ? "assistantExtra.show" : "assistantExtra.hide") : Extra));

    public override bool IsTransient(string? property) => property is nameof(ExtraName) or nameof(Target) or nameof(IsShowOrHide) || base.IsTransient(property);
}

/// <summary>What «Asistente: preguntar» does with the answer (D1, 5.0.370).</summary>
public enum AssistantAnswer
{
    /// <summary>The assistant says it, in its bubble and with its voice (as always); the command does not wait for it.</summary>
    Say,

    /// <summary>Pasted over the selection, in the window the command was run over.</summary>
    Paste,

    Copy,

    /// <summary>On the assistant's reading sheet.</summary>
    Sheet,

    /// <summary>Only in {resultado} (and its variable), for the next actions.</summary>
    Keep,
}

/// <summary>
/// «Asistente: preguntar»: an order for an assistant, as if it had been said to it («¿qué tenía apuntado?»), with the
/// same path as the voice and the chat (message <c>heard</c>, source <c>command</c>). Without text, the box to write it
/// opens (so a hotkey can open it). Except <see cref="AssistantAnswer.Say"/>, the command waits for the answer, quietly
/// (it neither speaks nor shows its bubble), and gives it to the next actions.
/// </summary>
public partial class AssistantAskAction : CommandAction
{
    /// <summary>The assistant's id (K-Desktop when new); empty, from before 5.0.113: none, and it is not run.</summary>
    [ObservableProperty] private string _assistant = global::KDesktop.Core.Assistant.AssistantTargeting.KDesktop;

    /// <summary>The order; empty: the box to write it opens.</summary>
    [ObservableProperty] private string _text = string.Empty;

    [ObservableProperty] private AssistantAnswer _answer;

    /// <summary>One that is not on the desktop comes out to answer (otherwise, nothing is done).</summary>
    [ObservableProperty] private bool _bringOut = true;

    public override ActionKind Kind => ActionKind.AssistantAsk;

    public override bool GivesResult => Answer != AssistantAnswer.Say;

    partial void OnAnswerChanged(AssistantAnswer value) => OnPropertyChanged(nameof(GivesResult));

    public override string Summary
    {
        get
        {
            var asked = Text.Trim().Length == 0 ? Loc.T("summary.assistantAskBox") : $"«{Text.Trim()}»";
            return Answer == AssistantAnswer.Say ? asked : Loc.T("summary.assistantAnswer." + Answer, asked);
        }
    }
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
    /// <summary>The assistant's id (K-Desktop when new); empty, from before 5.0.113: none, and it is not run.</summary>
    [ObservableProperty] private string _assistant = global::KDesktop.Core.Assistant.AssistantTargeting.KDesktop;

    [ObservableProperty] private string _text = string.Empty;

    /// <summary>Before saying it, it catches the eye (goes towards the mouse); otherwise it says it where it is.</summary>
    [ObservableProperty] private bool _catchEye;

    public override ActionKind Kind => ActionKind.AssistantSay;

    public override string Summary => Text.Trim().Length == 0 ? Loc.T("summary.assistantSayEmpty") : $"«{Text.Trim()}»";
}

/// <summary>What «Asistente: chat» does with the chat window.</summary>
public enum AssistantChatMode { Toggle, Open, Close }

/// <summary>
/// «Asistente: chat» (D3, 5.0.371): the chat window, apart from the main one (the same conversation as Panel → Asistente),
/// opened, closed or one or the other. It starts with <see cref="Assistant"/>, or (empty) the last one it had.
/// </summary>
public partial class AssistantChatAction : CommandAction
{
    [ObservableProperty] private AssistantChatMode _mode;

    /// <summary>The assistant it opens with; empty: the last one used in the window.</summary>
    [ObservableProperty] private string _assistant = string.Empty;

    public override ActionKind Kind => ActionKind.AssistantChat;

    public override string Summary => Loc.T("summary.assistantChat." + Mode);
}

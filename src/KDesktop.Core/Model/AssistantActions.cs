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

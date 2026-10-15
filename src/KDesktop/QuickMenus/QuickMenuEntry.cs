using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Services;

namespace KDesktop.QuickMenus;

/// <summary>View model of one row of a QuickMenu level.</summary>
public partial class QuickMenuEntry : ObservableObject
{
    public QuickMenuEntry(QuickMenuItem item, AppConfig config)
    {
        Item = item;
        Command = item.Kind == QuickMenuItemKind.Command ? config.FindCommand(item.CommandId) : null;
        Label = item.Label ?? Command?.Name ?? (item.Kind == QuickMenuItemKind.Command ? Loc.T("qm.deletedCommand") : string.Empty);
        Icon = item.IsSeparator ? null : IconProvider.ForMenuItem(item, Command);
        Children = item.Children.ToList();
    }

    public QuickMenuItem Item { get; }
    public Command? Command { get; }
    public string Label { get; }
    public ImageSource? Icon { get; }
    public IReadOnlyList<QuickMenuItem> Children { get; }

    public bool IsSeparator => Item.Kind == QuickMenuItemKind.Separator;
    public bool IsTitle => Item.Kind == QuickMenuItemKind.Title;
    public bool HasChildren => Children.Count > 0;
    public bool IsRunnable => Command is { Enabled: true };
    public bool IsDisabled => Item.Kind == QuickMenuItemKind.Command && !IsRunnable;
    public bool IsSelectable => !IsSeparator && (IsRunnable || HasChildren);
    public bool HasIcon => Icon is not null;

    [ObservableProperty] private bool _isHighlighted;
    [ObservableProperty] private bool _isExpanded;

    /// <summary>Set briefly to play the "executed" flash animation.</summary>
    [ObservableProperty] private bool _isFlashing;
}

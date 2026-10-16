using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Services;

namespace KDesktop.Views.QuickMenuEditor;

public enum DropHint { None, Before, After, Into }

/// <summary>One row of the visual editor (wraps a <see cref="QuickMenuItem"/>).</summary>
public partial class QmRow : ObservableObject
{
    public QmRow(QuickMenuItem item, AppConfig config, QmColumn column)
    {
        Item = item;
        Column = column;
        Command = item.Kind == QuickMenuItemKind.Command ? config.FindCommand(item.CommandId) : null;
        IsMissing = item.Kind == QuickMenuItemKind.Command && Command is null;
        Label = item.Label ?? Command?.Name ?? item.Kind switch
        {
            QuickMenuItemKind.Command => Loc.T("qm.deletedCommand"),
            QuickMenuItemKind.Title => Loc.T("qm.newTitle"),
            QuickMenuItemKind.Submenu => Loc.T("qm.newSubmenu"),
            _ => string.Empty,
        };
        Icon = item.IsSeparator ? null : IconProvider.ForMenuItem(item, Command);
        _editText = Label;
    }

    public QuickMenuItem Item { get; }
    public QmColumn Column { get; }
    public Command? Command { get; }
    public string Label { get; }
    public ImageSource? Icon { get; }

    public bool IsSeparator => Item.Kind == QuickMenuItemKind.Separator;
    public bool IsTitle => Item.Kind == QuickMenuItemKind.Title;
    public bool IsSubmenu => Item.Kind == QuickMenuItemKind.Submenu;
    public bool HasChildren => Item.Children.Count > 0;
    public bool IsMissing { get; }
    public bool HasIcon => Icon is not null;
    public bool HasCustomLabel => Item.Label is not null && Command is not null;
    public bool IsDisabledCommand => Command is { Enabled: false };

    /// <summary>Small caption under the label: the command name when the item is renamed.</summary>
    public string? Caption => HasCustomLabel && Command!.Name != Label ? Command.Name : null;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isOnPath;

    /// <summary>Arrived here from «QuickMenus: …» of a command: the row flashes so it is easy to spot.</summary>
    [ObservableProperty] private bool _isFound;
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private DropHint _dropHint;
    [ObservableProperty] private string _editText;
}

/// <summary>One level shown in the editor canvas.</summary>
public sealed class QmColumn
{
    public QmColumn(int level, QuickMenuItem? owner, ObservableCollection<QuickMenuItem> list, string title)
    {
        Level = level;
        Owner = owner;
        List = list;
        Title = title;
    }

    public int Level { get; }
    public QuickMenuItem? Owner { get; }
    public ObservableCollection<QuickMenuItem> List { get; }
    public string Title { get; }
    public List<QmRow> Rows { get; } = [];
    public bool IsEmpty => Rows.Count == 0;
}

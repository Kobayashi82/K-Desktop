using System.Windows;
using System.Windows.Controls;

namespace KDesktop.Views;

/// <summary>What a row of the Options tree is: a group that folds, a section, or something inside a section (an assistant).</summary>
public enum NavKind { Group, Section, Sub }

/// <summary>
/// A row of the Options tree (5.0.100): its key («brain», «assistants/lander»), whether it is the section shown now
/// (<see cref="IsCurrent"/>: the highlight, apart from the tree's selection, which is only where the keyboard is) and
/// what the search looks in.
/// </summary>
public sealed class NavItem : TreeViewItem
{
    public static readonly DependencyProperty IsCurrentProperty =
        DependencyProperty.Register(nameof(IsCurrent), typeof(bool), typeof(NavItem), new PropertyMetadata(false));

    /// <summary>The keyboard is on it (not the mouse): a ring shows where.</summary>
    public static readonly DependencyProperty ShowFocusProperty =
        DependencyProperty.Register(nameof(ShowFocus), typeof(bool), typeof(NavItem), new PropertyMetadata(false));

    public NavItem(NavKind kind, string key)
    {
        Kind = kind;
        Key = key;
    }

    public NavKind Kind { get; }

    public string Key { get; }

    public bool IsCurrent
    {
        get => (bool)GetValue(IsCurrentProperty);
        set => SetValue(IsCurrentProperty, value);
    }

    public bool ShowFocus
    {
        get => (bool)GetValue(ShowFocusProperty);
        set => SetValue(ShowFocusProperty, value);
    }

    /// <summary>The words of its title and hint (and, for a section, of its page), folded, for the search.</summary>
    public string SearchText { get; set; } = string.Empty;

    /// <summary>The chevron of a row that folds (clicking it folds; elsewhere, it opens the section).</summary>
    public FrameworkElement? Chevron { get; set; }

    /// <summary>The number of sections, shown on a folded group.</summary>
    public FrameworkElement? CountChip { get; set; }

    /// <summary>The group (or section) it is in.</summary>
    public NavItem? Owner => ItemsControlFromItemContainer(this) as NavItem;

    public IEnumerable<NavItem> Children => Items.OfType<NavItem>();

    protected override DependencyObject GetContainerForItemOverride() => new NavItem(NavKind.Sub, string.Empty);

    protected override bool IsItemItsOwnContainerOverride(object item) => item is NavItem;
}

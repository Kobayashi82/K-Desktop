using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace KDesktop.Views;

/// <summary>Right-click menus of the lists (commands, macros, K-Board remaps).</summary>
internal static class ListContextMenu
{
    /// <summary>
    /// The row a right click landed on, selected and focused (so the menu acts on it); null outside the rows.
    /// Marks the click handled when there is a row.
    /// </summary>
    public static ListBoxItem? SelectRow(ListBox list, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is not ListBoxItem row) return null;
        e.Handled = true;
        if (!row.IsSelected)
        {
            if (list.SelectionMode == SelectionMode.Single) list.SelectedItem = row.DataContext;
            else
            {
                list.SelectedItems.Clear();
                row.IsSelected = true;
            }
        }
        row.Focus();
        return row;
    }

    public static MenuItem Entry(string header, SymbolRegular symbol, Action click)
    {
        var item = new MenuItem { Header = header, Icon = new SymbolIcon { Symbol = symbol } };
        item.Click += (_, _) => click();
        return item;
    }

    /// <summary>«Eliminar», in red (the theme's danger colour), as in every right-click menu.</summary>
    public static MenuItem Delete(Action click) => Danger(Entry(Core.Localization.Loc.T("common.delete"), SymbolRegular.Delete24, click));

    /// <summary>Paints a menu entry (its text and its icon) in the danger colour.</summary>
    public static MenuItem Danger(MenuItem item)
    {
        item.SetResourceReference(Control.ForegroundProperty, "Kd.Danger");
        if (item.Icon is SymbolIcon icon) icon.SetResourceReference(Control.ForegroundProperty, "Kd.Danger");
        return item;
    }

    /// <summary>Opens a menu at the mouse with <paramref name="items"/> (menu items and separators).</summary>
    public static void Open(UIElement target, params Control[] items)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.MousePoint };
        foreach (var item in items) menu.Items.Add(item);
        menu.IsOpen = true;
    }
}

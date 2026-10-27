using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KDesktop.Views;

/// <summary>
/// <c>v:ComboHover.NoScroll="True"</c>: the open list of a combo does not jump when the mouse goes over an item only
/// half in view (WPF brings it into view, and the list scrolls under the mouse). The keys still scroll to the item.
/// It also opens without an item cut at the top: from the start when the chosen one fits, else with the chosen one first.
/// </summary>
public static class ComboHover
{
    public static readonly DependencyProperty NoScrollProperty = DependencyProperty.RegisterAttached(
        "NoScroll", typeof(bool), typeof(ComboHover), new PropertyMetadata(false, OnChanged));

    public static bool GetNoScroll(DependencyObject d) => (bool)d.GetValue(NoScrollProperty);

    public static void SetNoScroll(DependencyObject d, bool value) => d.SetValue(NoScrollProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox combo) return;
        if ((bool)e.NewValue)
        {
            combo.AddHandler(FrameworkElement.RequestBringIntoViewEvent, (RequestBringIntoViewEventHandler)OnBringIntoView);
            combo.DropDownOpened += OnOpened;
        }
        else
        {
            combo.RemoveHandler(FrameworkElement.RequestBringIntoViewEvent, (RequestBringIntoViewEventHandler)OnBringIntoView);
            combo.DropDownOpened -= OnOpened;
        }
    }

    /// <summary>Once the list is laid out, it is placed so the chosen item and the ones on top show whole.</summary>
    private static void OnOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox combo) return;
        combo.Dispatcher.BeginInvoke(() =>
        {
            if (!combo.IsDropDownOpen || combo.ItemContainerGenerator.ContainerFromItem(combo.SelectedItem) is not FrameworkElement item
                || Ancestor<ScrollViewer>(item) is not { } scroller) return;
            var top = item.TranslatePoint(new Point(0, 0), scroller).Y + scroller.VerticalOffset;
            scroller.ScrollToVerticalOffset(top + item.ActualHeight <= scroller.ViewportHeight ? 0 : top);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>The list's scroller: the nearest ScrollViewer above the item.</summary>
    private static T? Ancestor<T>(DependencyObject item) where T : DependencyObject
    {
        for (var d = System.Windows.Media.VisualTreeHelper.GetParent(item); d is not null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is T found) return found;
        return null;
    }

    private static void OnBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (sender is ComboBox { IsDropDownOpen: true } && e.TargetObject is ComboBoxItem
            && InputManager.Current.MostRecentInputDevice is MouseDevice)
            e.Handled = true;
    }
}

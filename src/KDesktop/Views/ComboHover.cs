using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KDesktop.Views;

/// <summary>
/// <c>v:ComboHover.NoScroll="True"</c>: the open list of a combo does not jump when the mouse goes over an item only
/// half in view (WPF brings it into view, and the list scrolls under the mouse). The keys still scroll to the item.
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
        if ((bool)e.NewValue) combo.AddHandler(FrameworkElement.RequestBringIntoViewEvent, (RequestBringIntoViewEventHandler)OnBringIntoView);
        else combo.RemoveHandler(FrameworkElement.RequestBringIntoViewEvent, (RequestBringIntoViewEventHandler)OnBringIntoView);
    }

    private static void OnBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (sender is ComboBox { IsDropDownOpen: true } && e.TargetObject is ComboBoxItem
            && InputManager.Current.MostRecentInputDevice is MouseDevice)
            e.Handled = true;
    }
}

using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Input;
using KDesktop.Core.Model;

namespace KDesktop.Views.Pages;

public partial class QuickMenusPage : UserControl
{
    public QuickMenusPage()
    {
        InitializeComponent();
        MenuBox.ItemsSource = App.Current.Config.QuickMenus;
        MenuBox.SelectedIndex = 0;
    }

    public void Select(string menuId) =>
        MenuBox.SelectedItem = App.Current.Config.FindMenu(menuId);

    private void OnTest(object sender, RoutedEventArgs e)
    {
        if (MenuBox.SelectedItem is QuickMenu menu) App.Current.QuickMenus.Show(menu.Id, ForegroundWindow.Capture());
    }
}

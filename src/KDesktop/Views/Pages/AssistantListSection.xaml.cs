using System.IO;
using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using Microsoft.Win32;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → Assistants (stage 1.8): the installed assistants with their icon, version and author;
/// «Add assistant» opens a package's card, and each one can be put on the desktop, hidden or removed (asking
/// whether to keep its data). Its own settings come in stage 1.9.
/// </summary>
public partial class AssistantListSection : UserControl
{
    private readonly App _app = App.Current;

    public AssistantListSection()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _app.AssistantChanged += Refresh;
            Refresh();
        };
        Unloaded += (_, _) => _app.AssistantChanged -= Refresh;
    }

    private void Refresh()
    {
        var rows = AssistantLook.Rows(_app.Config.Settings.AssistantActive);
        AssistantList.ItemsSource = rows;
        EmptyPanel.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ModuleOffText.Visibility = rows.Count > 0 && !_app.Config.Settings.AssistantEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("assistantOptions.addTitle"),
            Filter = Loc.T("assistant.fileType") + $"|*{AssistantPackages.Extension}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } home
                ? Path.Combine(home, "Downloads") : string.Empty,
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) _app.OpenAssistantPackage(dlg.FileName);
    }

    private void OnActivate(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AssistantRow row }) _app.ActivateAssistant(row.Id);
    }

    private void OnHide(object sender, RoutedEventArgs e) => _app.ActivateAssistant(null);

    private async void OnRemove(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AssistantRow row } button) return;
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("assistantOptions.removeTitle", row.Name),
            Width = 520,
            MaxWidth = 520,
            Content = Loc.T("assistantOptions.removeQuestion", row.Name),
            PrimaryButtonText = Loc.T("assistantOptions.removeKeep"),
            SecondaryButtonText = Loc.T("assistantOptions.removeAll"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        var answer = await box.ShowDialogAsync();
        if (answer is not (Wpf.Ui.Controls.MessageBoxResult.Primary or Wpf.Ui.Controls.MessageBoxResult.Secondary)) return;

        // Leaving the desktop takes a moment (its exit): the row waits meanwhile.
        button.IsEnabled = false;
        try
        {
            await _app.UninstallAssistantAsync(row.Id, keepData: answer == Wpf.Ui.Controls.MessageBoxResult.Primary);
            _app.Ui.ShowOsd(Loc.T("assistantOptions.removed", row.Name), OsdKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _app.Ui.ShowOsd(Loc.T("assistantOptions.removeFailed", row.Name, ex.Message), OsdKind.Warning);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
}

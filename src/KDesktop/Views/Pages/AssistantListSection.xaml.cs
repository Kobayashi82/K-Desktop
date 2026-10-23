using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using Microsoft.Win32;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → Assistants (stage 1.8): K-Desktop (5.0.112, of the system: made active or not, never removed) and
/// the installed assistants in compact rows with their icon, version and how they are called; «Add assistant» opens a
/// package's card, and each one can be put on the desktop (several at once, stage 1.11), hidden or removed (asking whether
/// to keep its data). A row opens its card in place of the list (stage 1.9; 5.0.113: its word, key and model are in the
/// card's «General»).
/// </summary>
public partial class AssistantListSection : UserControl
{
    private readonly App _app = App.Current;
    /// <summary>What the rows show: the list is made again only when it changes.</summary>
    private string? _listShown;

    public AssistantListSection()
    {
        InitializeComponent();
        SettingsView.Closed += ShowList;
        Loaded += (_, _) =>
        {
            _app.AssistantChanged += Refresh;
            _app.AssistantUpdatesChanged += Refresh;
            _listShown = null;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantChanged -= Refresh;
            _app.AssistantUpdatesChanged -= Refresh;
        };
    }

    private void Refresh()
    {
        // The assistant whose settings are open was removed or updated.
        if (SettingsView.AssistantId is { } open && SettingsView.IsVisible && !Core.Assistant.AssistantTargeting.IsSystem(open))
        {
            if (AssistantPackages.Find(open) is { } installed) SettingsView.Refresh(installed);
            else ShowList();
        }
        var rows = AssistantLook.Rows(_app);
        var key = string.Join('|', rows.Select(r => $"{r.Id}:{r.Name}:{r.Byline}:{r.IsOnDesktop}:{r.Line}")) + _app.Config.Settings.AssistantEnabled;
        if (key == _listShown) return;
        _listShown = key;
        AssistantList.ItemsSource = rows;
        // Nothing installed: K-Desktop is there, and a word on where the others come from.
        EmptyPanel.Visibility = rows.All(r => r.IsSystem) ? Visibility.Visible : Visibility.Collapsed;
        ModuleOffText.Visibility = !_app.Config.Settings.AssistantEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The list (null) or an assistant's settings (its id) is shown now: the tree of Options follows.</summary>
    public event Action<string?>? Shown;

    /// <summary>Shows an assistant's card in place of the list (<c>--page settings/assistants/&lt;id&gt;</c>).</summary>
    public void ShowSettings(string id)
    {
        if (Core.Assistant.AssistantTargeting.IsSystem(id)) SettingsView.ShowSystem();
        else if (AssistantPackages.Find(id) is { } installed) SettingsView.Show(installed);
        else
        {
            ShowList();
            return;
        }
        ListCard.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        Shown?.Invoke(id);
    }

    /// <summary>The assistant shown goes to its «Personalidad y voz».</summary>
    public void ShowPersonal()
    {
        if (SettingsView.Visibility == Visibility.Visible) SettingsView.ShowPersonal();
    }

    /// <summary>The assistant shown goes to its «Probar» (stage 13.9).</summary>
    public void ShowTry()
    {
        if (SettingsView.Visibility == Visibility.Visible) SettingsView.ShowTry();
    }

    public void ShowList()
    {
        SettingsView.Visibility = Visibility.Collapsed;
        ListCard.Visibility = Visibility.Visible;
        Shown?.Invoke(null);
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AssistantRow row }) ShowSettings(row.Id);
    }

    /// <summary>A click on a row, but on its buttons, opens its card.</summary>
    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null && d != sender; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ButtonBase) return;
        if (sender is FrameworkElement { DataContext: AssistantRow row }) ShowSettings(row.Id);
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
        if (sender is FrameworkElement { DataContext: AssistantRow row }) _app.ShowAssistant(row.Id);
    }

    private void OnHide(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AssistantRow row }) _app.HideAssistant(row.Id);
    }

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

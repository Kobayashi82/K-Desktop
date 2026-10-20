using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Assistant;

/// <summary>
/// The settings of one installed assistant (stage 1.9, §11.8), drawn from its settings.json with K-Desktop's own
/// controls: no code of the assistant is loaded here. Changes are saved and reach the assistant at once while it is
/// on the desktop.
/// </summary>
public partial class AssistantSettingsView : UserControl
{
    private readonly App _app = App.Current;
    private AssistantSettingsModel? _model;
    private InstalledAssistant? _shown;

    public AssistantSettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _app.AssistantChanged += UpdateLive;
            _app.AssistantPanelChanged += OnPanelChanged;
            UpdateLive();
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantChanged -= UpdateLive;
            _app.AssistantPanelChanged -= OnPanelChanged;
            _model?.Flush();
        };
    }

    /// <summary>«Back» was pressed: the list shows again.</summary>
    public event Action? Closed;

    /// <summary>The id of the assistant shown, if any.</summary>
    public string? AssistantId => _shown?.Id;

    /// <summary>Shows an installed assistant's settings (its first page).</summary>
    public void Show(InstalledAssistant assistant)
    {
        _model?.Flush();
        _shown = assistant;
        var layout = SettingsLayout.Load(assistant);
        _model = layout is null ? null : new AssistantSettingsModel(assistant, layout);
        DataContext = _model;

        TitleText.Text = Loc.T("assistantSettings.title", assistant.Manifest.Name);
        var icon = AssistantLook.Icon(assistant);
        HeroIcon.Source = icon;
        HeroFallback.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;
        NonePanel.Visibility = _model is null ? Visibility.Visible : Visibility.Collapsed;
        NoneText.Text = Loc.T("assistantSettings.none", assistant.Manifest.Name);
        ResetButton.Visibility = _model is null ? Visibility.Collapsed : Visibility.Visible;
        PageTabs.Visibility = _model is { HasTabs: true } ? Visibility.Visible : Visibility.Collapsed;
        UpdateLive();
    }

    /// <summary>The assistants changed: a new version of the one shown is shown again (its settings may be others).</summary>
    public void Refresh(InstalledAssistant installed)
    {
        if (_shown is null || _shown.Id != installed.Id || _shown.Folder != installed.Folder) Show(installed);
        else UpdateLive();
    }

    /// <summary>Whether the changes reach the assistant now, or when it is put on the desktop.</summary>
    private void UpdateLive()
    {
        if (_model is null) return;
        var name = _model.Assistant.Manifest.Name;
        var live = _app.IsAssistantLoaded(_model.Assistant.Id);
        LiveText.Text = Loc.T(live ? "assistantSettings.live" : "assistantSettings.notLive", name);
        LiveDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, live ? "Kd.Success" : "TextFillColorTertiaryBrush");
    }

    private void OnPanelChanged(string id)
    {
        if (_model?.Assistant.Id == id) _model.PanelChanged();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        _model?.Flush();
        Closed?.Invoke();
    }

    private async void OnReset(object sender, RoutedEventArgs e)
    {
        if (_model is not { } model) return;
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("assistantSettings.reset"),
            Content = Loc.T("assistantSettings.resetQuestion", model.Assistant.Manifest.Name),
            PrimaryButtonText = Loc.T("assistantSettings.reset"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        model.Reset();
        _app.Ui.ShowOsd(Loc.T("assistantSettings.resetDone", model.Assistant.Manifest.Name), OsdKind.Success);
    }

    // ── controls that need more than a binding ──────────────────────────

    private async void OnButton(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ButtonSetting button } element || _model is not { } model) return;
        var item = button.Item;
        if (item.Extra is { } extra)
        {
            _app.RunAssistantExtra(model.Assistant.Id, extra);
            return;
        }
        if (!item.ResetData) return;

        var name = model.Assistant.Manifest.Name;
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = button.Label,
            Content = item.Confirm is { } confirm ? AssistantLook.Text(confirm) : Loc.T("assistantSettings.resetDataQuestion", name),
            PrimaryButtonText = Loc.T("assistantSettings.resetDataYes"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        element.IsEnabled = false;
        try
        {
            await _app.ResetAssistantDataAsync(model.Assistant.Id);
            _app.Ui.ShowOsd(Loc.T("assistantSettings.resetDataDone", name), OsdKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _app.Ui.ShowOsd(Loc.T("assistantSettings.resetDataFailed", name, ex.Message), OsdKind.Warning);
        }
        finally
        {
            element.IsEnabled = true;
        }
    }

    private void OnCaptureHotkey(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HotkeySetting hotkey }) hotkey.Capture(Window.GetWindow(this));
    }

    private void OnClearHotkey(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HotkeySetting hotkey }) hotkey.Clear();
    }

    /// <summary>The command behind a hotkey, in the command editor (to add it to a QuickMenu, give it phrases…).</summary>
    private void OnOpenHotkeyCommand(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: HotkeySetting { Command: { } command } })
            (Window.GetWindow(this) as MainWindow)?.OpenCommand(command.Id);
    }

    private void OnGalleryTile(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GalleryTile tile, Tag: GallerySetting gallery }) gallery.Choose(tile);
    }

    private void OnPickColor(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string hex } button) return;
        // The popup's content keeps the color model as the DataContext of the template that holds it.
        if (FindModel<ColorSetting>(button) is { } color) color.Pick(hex);
        if (FindParent<Popup>(button) is { } popup) popup.IsOpen = false;
    }

    private static AppsSetting? AppsOf(object sender) => sender is FrameworkElement { DataContext: AppsSetting apps } ? apps : null;

    private void OnAppsDropDown(object? sender, EventArgs e)
    {
        if (sender is ComboBox box && AppsOf(box) is { } apps) ProcessCombo.Fill(box, apps.Apps);
    }

    private void OnAppPicked(object? sender, EventArgs e)
    {
        if (sender is ComboBox box && AppsOf(box) is { } apps) ProcessCombo.AddPicked(box, apps.Apps);
    }

    private void OnAppKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not ComboBox box || AppsOf(box) is not { } apps) return;
        ProcessCombo.AddTyped(box, apps.Apps);
        e.Handled = true;
    }

    private void OnAddApp(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement button || AppsOf(button) is not { } apps) return;
        if ((button.Parent as Panel)?.Children.OfType<ComboBox>().FirstOrDefault() is { } box) ProcessCombo.AddTyped(box, apps.Apps);
    }

    private void OnPickApp(object sender, RoutedEventArgs e)
    {
        if (AppsOf(sender) is not { } apps) return;
        WindowPicker.Pick(name =>
        {
            if (!apps.Apps.Contains(name)) apps.Apps.Add(name);
        });
    }

    private void OnRemoveApp(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AppsSetting apps, DataContext: string name }) apps.Apps.Remove(name);
    }

    private static T? FindModel<T>(DependencyObject from) where T : class
    {
        for (DependencyObject? d = from; d is not null; d = LogicalTreeHelper.GetParent(d) ?? System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is FrameworkElement { DataContext: T model }) return model;
        return null;
    }

    private static T? FindParent<T>(DependencyObject from) where T : DependencyObject
    {
        for (DependencyObject? d = from; d is not null; d = LogicalTreeHelper.GetParent(d) ?? System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is T found) return found;
        return null;
    }
}

/// <summary>A Fluent symbol as the icon of a WPF-UI button.</summary>
public sealed class SymbolIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SymbolRegular symbol ? new SymbolIcon { Symbol = symbol } : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible when the text has something.</summary>
public sealed class TextToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

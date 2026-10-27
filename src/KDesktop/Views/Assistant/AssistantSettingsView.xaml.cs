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
/// on the desktop. Every assistant (K-Desktop too, 5.0.112) has K-Desktop's «General» first and «Personalidad y voz» last.
/// </summary>
public partial class AssistantSettingsView : UserControl
{
    private readonly App _app = App.Current;
    private AssistantSettingsModel? _model;

    public AssistantSettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _app.AssistantChanged += UpdateLive;
            _app.AssistantPanelChanged += OnPanelChanged;
            UpdateLive();
            FitWidth();
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantChanged -= UpdateLive;
            _app.AssistantPanelChanged -= OnPanelChanged;
            _model?.Flush();
            PersonalView.Flush();
        };
    }

    private ScrollViewer? _page;

    /// <summary>
    /// As wide as the page lets it, up to the most of the sections (they are as wide as their content): the same on every tab,
    /// and never wider than the window (it used to be 720 at least, and in a narrow window its right side was cut).
    /// </summary>
    private void FitWidth()
    {
        if (_page is not null) return;
        DependencyObject? at = this;
        while (at is not null and not ScrollViewer) at = System.Windows.Media.VisualTreeHelper.GetParent(at);
        if (at is not ScrollViewer page || page.Content is not FrameworkElement content) return;
        _page = page;
        void Fit()
        {
            if (!content.IsAncestorOf(this)) return;
            var most = double.IsInfinity(content.MaxWidth) ? double.MaxValue : content.MaxWidth;
            var left = TransformToAncestor(content).Transform(new Point(0, 0)).X;
            Width = Math.Max(0, Math.Min(most, page.ViewportWidth) - left);
        }
        page.SizeChanged += (_, _) => Fit();
        page.ScrollChanged += (_, e) => { if (e.OriginalSource == page && e.ViewportWidthChange != 0) Fit(); };
        Fit();
    }

    /// <summary>«Back» was pressed: the list shows again.</summary>
    public event Action? Closed;

    /// <summary>The id of the assistant shown, if any.</summary>
    public string? AssistantId => _model?.Id;

    /// <summary>K-Desktop's settings: its «General» and «Personalidad y voz».</summary>
    public void ShowSystem() => ShowModel(new AssistantSettingsModel(null, new SettingsLayout([])));

    /// <summary>Shows an installed assistant's settings (its «General» first).</summary>
    public void Show(InstalledAssistant assistant) =>
        // Without a settings.json it still has K-Desktop's pages.
        ShowModel(new AssistantSettingsModel(assistant, SettingsLayout.Load(assistant) ?? new SettingsLayout([])));

    private void ShowModel(AssistantSettingsModel model)
    {
        _model?.Flush();
        PersonalView.Flush();
        if (_model is not null) _model.PropertyChanged -= OnModelChanged;
        _model = model;
        _model.PropertyChanged += OnModelChanged;
        DataContext = _model;
        GeneralView.Show(model.Assistant);
        if (model.Assistant is { } shown) TryView.Show(shown.Id);
        if (model.Assistant is { } assistant) PersonalView.Show(assistant);
        else PersonalView.ShowSystem();

        TitleText.Text = Loc.T("assistantSettings.title", model.Name);
        var icon = AssistantLook.IconOf(model.Id);
        HeroIcon.Source = icon;
        HeroFallback.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;
        NoneText.Text = Loc.T("assistantSettings.none", model.Name);
        OwnTabs.Visibility = _model.HasOwnSettings ? Visibility.Visible : Visibility.Collapsed;
        ShowPage();
        UpdateLive();
    }

    /// <summary>
    /// Opens one of K-Desktop's pages by its name (<c>--page settings/assistants/&lt;id&gt;/personal</c>, <c>voice</c>,
    /// <c>try</c> or <c>general</c>); false for another name.
    /// </summary>
    public bool ShowPage(string name)
    {
        Func<SettingsPageModel, bool>? which = name switch
        {
            "general" => p => p.IsGeneral,
            "personal" => p => p.IsPersonal,
            "voice" => p => p.IsVoice,
            "try" => p => p.IsTry,
            _ => null,
        };
        if (which is null) return false;
        if (_model?.Pages.FirstOrDefault(which) is { } page) _model.CurrentPage = page;
        return true;
    }

    /// <summary>The changes reach it now (it is on the desktop, or K-Desktop is active with the module running).</summary>
    private bool IsLive(string id) => Core.Assistant.AssistantTargeting.IsSystem(id)
        ? _app.Assistant is { IsRunning: true } && _app.IsOnDesktop(id)
        : _app.IsAssistantLoaded(id);

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssistantSettingsModel.CurrentPage)) ShowPage();
    }

    /// <summary>
    /// «Básico», «Personalidad», «Voz», «Probar» or a page of its settings.json («Restablecer» of the top only undoes those of its
    /// settings.json).
    /// </summary>
    private void ShowPage()
    {
        if (_model is null) return;
        var general = _model.CurrentPage is { IsGeneral: true };
        var voice = _model.CurrentPage is { IsVoice: true };
        var personal = _model.CurrentPage is { IsPersonal: true } || voice;
        var trying = _model.CurrentPage is { IsTry: true };
        GeneralView.Visibility = general ? Visibility.Visible : Visibility.Collapsed;
        PersonalView.Visibility = personal ? Visibility.Visible : Visibility.Collapsed;
        if (personal) PersonalView.ShowPart(voice);
        TryView.Visibility = trying ? Visibility.Visible : Visibility.Collapsed;
        SectionsList.Visibility = general || personal || trying ? Visibility.Collapsed : Visibility.Visible;
        ResetButton.Visibility = _model.HasOwnSettings && !general && !personal && !trying ? Visibility.Visible : Visibility.Collapsed;
        NonePanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>The assistants changed: a new version of the one shown is shown again (its settings may be others).</summary>
    public void Refresh(InstalledAssistant installed)
    {
        if (_model?.Assistant is not { } shown || shown.Id != installed.Id || shown.Folder != installed.Folder) Show(installed);
        else UpdateLive();
    }

    /// <summary>Whether the changes reach the assistant now, or when it is put on the desktop; and putting it there or hiding it.</summary>
    private void UpdateLive()
    {
        if (AssistantId is not { } id) return;
        var name = App.AssistantName(id);
        var system = Core.Assistant.AssistantTargeting.IsSystem(id);
        var live = IsLive(id);
        LiveText.Text = system && !live ? Loc.T("assistantState.systemInactive", name) : Loc.T(live ? "assistantSettings.live" : "assistantSettings.notLive", name);
        LiveDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, live ? "Kd.Success" : "TextFillColorTertiaryBrush");
        var onDesktop = _app.IsOnDesktop(id);
        PutButton.Content = Loc.T(system ? "assistantSystem.activate" : "assistantOptions.putOnDesktop");
        HideButton.Content = Loc.T(system ? "assistantSystem.deactivate" : "assistantOptions.hide");
        PutButton.Visibility = onDesktop ? Visibility.Collapsed : Visibility.Visible;
        HideButton.Visibility = onDesktop ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPut(object sender, RoutedEventArgs e)
    {
        if (AssistantId is { } id) _app.ShowAssistant(id);
    }

    private void OnHide(object sender, RoutedEventArgs e)
    {
        if (AssistantId is { } id) _app.HideAssistant(id);
    }

    private void OnPanelChanged(string id)
    {
        if (_model?.Id == id) _model.PanelChanged();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        _model?.Flush();
        PersonalView.Flush();
        Closed?.Invoke();
    }

    private async void OnReset(object sender, RoutedEventArgs e)
    {
        if (_model is not { } model) return;
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("assistantSettings.reset"),
            Content = Loc.T("assistantSettings.resetQuestion", model.Name),
            PrimaryButtonText = Loc.T("assistantSettings.reset"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        model.Reset();
        _app.Ui.ShowNotice(Loc.T("assistantSettings.resetDone", model.Name), OsdKind.Success);
    }

    // ── controls that need more than a binding ──────────────────────────

    private async void OnButton(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ButtonSetting button } element || _model is not { } model) return;
        var item = button.Item;
        if (item.Extra is { } extra)
        {
            _app.RunAssistantExtra(model.Id, extra);
            return;
        }
        if (!item.ResetData) return;

        var name = model.Name;
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
            await _app.ResetAssistantDataAsync(model.Id);
            _app.Ui.ShowNotice(Loc.T("assistantSettings.resetDataDone", name), OsdKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _app.Ui.ShowNotice(Loc.T("assistantSettings.resetDataFailed", name, ex.Message), OsdKind.Warning);
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

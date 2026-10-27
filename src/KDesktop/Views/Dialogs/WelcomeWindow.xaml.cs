using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using KDesktop.Core;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Voice;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Dialogs;

/// <summary>
/// The welcome of a fresh install: language and theme → what K-Desktop does → start with Windows and the hotkeys →
/// voice (optional, the models downloaded here) → assistants (optional) → ready. Each step changes the real setting at
/// once; «Saltar» closes it. Opened again from «Acerca de» or with <c>--welcome</c>.
/// </summary>
public partial class WelcomeWindow : FluentWindow
{
    private sealed record LanguageOption(string? Code, string Name);

    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction Pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };

    private readonly App _app = App.Current;
    private readonly StackPanel[] _steps;
    private int _step;
    private bool _filling;
    /// <summary>What to do in the main window once this one is closed (open the catalog, a new command…).</summary>
    private Action<MainWindow>? _then;

    private WelcomeWindow()
    {
        InitializeComponent();
        _steps = [Step0, Step1, Step2, Step3, Step4, Step5];
        for (var i = 0; i < _steps.Length; i++)
            StepDots.Children.Add(new Ellipse { Width = 8, Height = 8, Margin = new Thickness(5, 0, 0, 0) });

        var s = _app.Config.Settings;
        FillLanguages();
        foreach (var pill in ThemePills.Children.OfType<RadioButton>())
            pill.IsChecked = (string)pill.Tag == s.Theme.ToString();
        StartupToggle.IsChecked = s.StartWithWindows;
        VoiceToggle.IsChecked = s.VoiceEnabled;
        AssistantToggle.IsChecked = s.AssistantEnabled;
        UpdateHotkeys();
        UpdateVoice();
        UpdateAssistant();

        void OnModels() => Dispatcher.BeginInvoke(UpdateVoice);
        void OnLanguage() => Dispatcher.BeginInvoke(() =>
        {
            FillLanguages();
            ShowStep(_step, animate: false);
            UpdateHotkeys();
            UpdateVoice();
        });
        _app.Voice.Models.Changed += OnModels;
        Loc.Instance.Changed += OnLanguage;
        Closed += (_, _) =>
        {
            _app.Voice.Models.Changed -= OnModels;
            Loc.Instance.Changed -= OnLanguage;
            HotkeyCapture.CancelActive();
            if (_then is { } then) _app.ShowMainWindow(then);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };

        ShowStep(0, animate: false);
        Loaded += (_, _) => PopIn(LogoScale, 0.6);
    }

    /// <summary>Shows the welcome over <paramref name="owner"/> (the main window), unless it is open already.</summary>
    public static void Open(Window owner)
    {
        if (Application.Current.Windows.OfType<WelcomeWindow>().FirstOrDefault() is { } open)
        {
            open.Activate();
            return;
        }
        new WelcomeWindow { Owner = owner }.Show();
    }

    // ── steps ────────────────────────────────────────────────────────────

    private void ShowStep(int step, bool animate = true)
    {
        _step = step;
        for (var i = 0; i < _steps.Length; i++) _steps[i].Visibility = i == step ? Visibility.Visible : Visibility.Collapsed;
        StepTitle.Text = Loc.T($"welcome.step{step}");
        for (var i = 0; i < StepDots.Children.Count; i++)
        {
            var dot = (Ellipse)StepDots.Children[i];
            dot.SetResourceReference(Shape.FillProperty, i == step ? "Kd.Accent" : i < step ? "Kd.AccentSoft" : "ControlStrokeColorDefaultBrush");
            dot.BeginAnimation(WidthProperty, new DoubleAnimation(i == step ? 20 : 8, TimeSpan.FromMilliseconds(animate ? 200 : 0)) { EasingFunction = EaseOut });
        }
        var last = step == _steps.Length - 1;
        NextButton.Content = Loc.T(step == 0 ? "welcome.start" : last ? "welcome.finish" : "wake.next");
        BackButton.Visibility = step > 0 ? Visibility.Visible : Visibility.Collapsed;
        SkipButton.Visibility = last ? Visibility.Collapsed : Visibility.Visible;

        if (!animate) return;
        StepHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(180)));
        StepShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
        if (step == 1) StaggerFeatures();
        if (last) PopIn(DoneScale, 0.3);
    }

    /// <summary>The features of «Qué es K-Desktop» come in one after another.</summary>
    private void StaggerFeatures()
    {
        var i = 0;
        foreach (var card in Features.Children.OfType<FrameworkElement>())
        {
            var delay = TimeSpan.FromMilliseconds(60 * i++);
            var shift = new TranslateTransform();
            card.RenderTransform = shift;
            card.Opacity = 0;
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = delay });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(320)) { BeginTime = delay, EasingFunction = EaseOut });
        }
    }

    private static void PopIn(ScaleTransform scale, double from)
    {
        var grow = new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = Pop };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_step < _steps.Length - 1) ShowStep(_step + 1);
        else Close();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_step > 0) ShowStep(_step - 1);
    }

    private void OnSkip(object sender, RoutedEventArgs e) => Close();

    /// <summary>Closes the welcome and then does <paramref name="then"/> in the main window.</summary>
    private void CloseThen(Action<MainWindow> then)
    {
        _then = then;
        Close();
    }

    // ── 0 · language and theme ───────────────────────────────────────────

    private void FillLanguages()
    {
        _filling = true;
        var options = new[] { new LanguageOption(null, Loc.T("settings.languageSystem")) }
            .Concat(Loc.Available().Select(l => new LanguageOption(l.Code, l.Name))).ToList();
        LanguageBox.ItemsSource = options;
        // By the item: a null SelectedValue («Sistema») selects nothing.
        LanguageBox.SelectedItem = options.FirstOrDefault(o => o.Code == _app.Config.Settings.Language) ?? options[0];
        _filling = false;
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || LanguageBox.SelectedItem is not LanguageOption option || option.Code == _app.Config.Settings.Language) return;
        _app.Config.Settings.Language = option.Code;
        _app.ApplySettings();
    }

    private void OnThemePicked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !Enum.TryParse<AppTheme>(tag, out var theme) || theme == _app.Config.Settings.Theme) return;
        _app.Config.Settings.Theme = theme;
        _app.ApplySettings();
    }

    // ── 2 · start with Windows and hotkeys ───────────────────────────────

    private void OnStartupToggle(object sender, RoutedEventArgs e)
    {
        var enable = StartupToggle.IsChecked == true;
        _app.Config.Settings.StartWithWindows = enable;
        // A test copy (--data) never touches the scheduled task of the normal one.
        if (AppPaths.InstanceSuffix.Length > 0) return;
        StartupTask.Apply(enable);
        StartupToggle.IsChecked = StartupTask.IsRegistered();
    }

    private void UpdateHotkeys()
    {
        var s = _app.Config.Settings;
        ShowHotkey(LauncherKeyButton, LauncherKeyClear, s.LauncherHotkey);
        ShowHotkey(VoiceKeyButton, VoiceKeyClear, s.VoiceHotkey);
        var show = s.ShowWindowHotkey;
        ShowKeyText.Text = string.IsNullOrWhiteSpace(show) ? Loc.T("settings.voiceNoHotkey") : show;
    }

    private static void ShowHotkey(Wpf.Ui.Controls.Button button, Wpf.Ui.Controls.Button clear, string hotkey)
    {
        var none = string.IsNullOrWhiteSpace(hotkey);
        button.Content = none ? Loc.T("welcome.pickKey") : hotkey;
        clear.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Capture(Wpf.Ui.Controls.Button button, Action<string> set)
    {
        button.Content = Loc.T("commands.pressHotkey");
        HotkeyCapture.Start(
            preview => button.Content = preview + "…",
            hotkey =>
            {
                if (hotkey is not null) set(Hotkey.Normalize(hotkey));
                UpdateHotkeys();
            },
            this);
    }

    private void OnCaptureLauncherKey(object sender, RoutedEventArgs e) => Capture(LauncherKeyButton, hk => _app.Config.Settings.LauncherHotkey = hk);

    private void OnClearLauncherKey(object sender, RoutedEventArgs e)
    {
        _app.Config.Settings.LauncherHotkey = string.Empty;
        UpdateHotkeys();
    }

    private void OnCaptureVoiceKey(object sender, RoutedEventArgs e) => Capture(VoiceKeyButton, hk => _app.Config.Settings.VoiceHotkey = hk);

    private void OnClearVoiceKey(object sender, RoutedEventArgs e)
    {
        _app.Config.Settings.VoiceHotkey = string.Empty;
        UpdateHotkeys();
    }

    // ── 3 · voice ────────────────────────────────────────────────────────

    private static string Megabytes(long bytes) => (bytes / 1048576.0).ToString("N0", Loc.Instance.Culture) + " MB";

    private void OnVoiceToggle(object sender, RoutedEventArgs e)
    {
        var on = VoiceToggle.IsChecked == true;
        _app.Config.Settings.VoiceEnabled = on;
        if (!on && _app.Voice.Models.IsDownloading) _app.Voice.Models.Cancel();
        _app.ApplySettings();
        UpdateVoice();
    }

    private void UpdateVoice()
    {
        var on = _app.Config.Settings.VoiceEnabled;
        VoicePanel.IsEnabled = on;
        VoicePanel.Opacity = on ? 1 : 0.5;

        var download = _app.Voice.Models;
        var complete = VoiceModels.Complete;
        var missing = Math.Max(1048576, ModelDownloader.TotalSize - ModelDownloader.InstalledSize());
        ModelsText.Text = download.IsDownloading
            ? Loc.T("settings.voiceDownloading", Megabytes(download.Done), Megabytes(ModelDownloader.TotalSize))
            : complete ? Loc.T("welcome.voiceReady")
            : Loc.T("welcome.voiceMissing", Megabytes(missing)) + (download.Error is { } error ? " " + Loc.T("settings.voiceLastError", error) : "");
        ModelsIcon.Symbol = complete ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.ArrowDownload24;
        ModelsIcon.Filled = complete;
        ModelsIcon.SetResourceReference(ForegroundProperty, complete ? "Kd.Success" : "Kd.AccentText");
        ModelsProgress.Visibility = download.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        ModelsProgress.Value = (double)download.Done / ModelDownloader.TotalSize;
        DownloadButton.Visibility = !download.IsDownloading && !complete ? Visibility.Visible : Visibility.Collapsed;
        CancelDownloadButton.Visibility = download.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDownload(object sender, RoutedEventArgs e)
    {
        _app.Voice.Models.Start();
        UpdateVoice();
    }

    private void OnCancelDownload(object sender, RoutedEventArgs e)
    {
        _app.Voice.Models.Cancel();
        UpdateVoice();
    }

    // ── 4 · assistants ───────────────────────────────────────────────────

    private void OnAssistantToggle(object sender, RoutedEventArgs e)
    {
        _app.Config.Settings.AssistantEnabled = AssistantToggle.IsChecked == true;
        _app.ApplySettings();
        UpdateAssistant();
    }

    private void UpdateAssistant()
    {
        var on = _app.Config.Settings.AssistantEnabled;
        CatalogBox.IsEnabled = on;
        CatalogBox.Opacity = on ? 1 : 0.5;
    }

    private void OnOpenCatalog(object sender, RoutedEventArgs e) => CloseThen(w => w.Navigate("settings/catalog"));

    // ── 5 · ready ────────────────────────────────────────────────────────

    private void OnFirstCommand(object sender, RoutedEventArgs e) => CloseThen(w => w.NewCommand());

    private void OnOpenSettings(object sender, RoutedEventArgs e) => CloseThen(w => w.Navigate("settings"));
}

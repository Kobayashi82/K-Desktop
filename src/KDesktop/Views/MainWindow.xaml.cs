using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KDesktop.Core.Localization;
using Wpf.Ui.Controls;

namespace KDesktop.Views;

public partial class MainWindow : FluentWindow
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private readonly Dictionary<string, FrameworkElement> _pages = [];
    private string _current = "commands";

    public MainWindow()
    {
        InitializeComponent();
        Logo.Source = Services.IconProvider.Asset("KDesktop");
        ApplySavedBounds(App.Current.Config.Settings.WindowBounds);
        Closing += (_, _) =>
        {
            var settings = App.Current.Config.Settings;
            settings.WindowBounds = SaveBounds();
            settings.WindowPage = _current;
            if (Pages.SettingsPage.LastSection is { } section) settings.SettingsSection = section;
        };
        Loaded += (_, _) =>
        {
            UpdateStatus();
            // Where it was left (unless something already asked for a page, like --page).
            if (NavList.SelectedItem is null)
            {
                var saved = App.Current.Config.Settings.WindowPage;
                var item = NavList.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == saved && i.Visibility == Visibility.Visible);
                NavList.SelectedItem = item ?? NavList.Items[0];
            }
            ShowBanners();
        };
        Loc.Instance.Changed += OnLanguageChanged;
        App.Current.PausedChanged += OnPausedChanged;
        App.Current.AssistantChanged += UpdateStatus;
        SourceInitialized += (_, _) => FileDrop.Attach(this, OnFilesDropped);
        // A QuickMenu open over this window (Probar, or its hotkey) closes with a click here, as it does anywhere else.
        PreviewMouseDown += (_, _) =>
        {
            if (App.Current.QuickMenus.IsOpen) App.Current.QuickMenus.CloseAll(restoreFocus: false);
        };
        Closed += (_, _) =>
        {
            Loc.Instance.Changed -= OnLanguageChanged;
            App.Current.PausedChanged -= OnPausedChanged;
            App.Current.AssistantChanged -= UpdateStatus;
            foreach (var page in _pages.Values.OfType<IDisposable>()) page.Dispose();
        };
    }

    private readonly System.Windows.Threading.DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(4) };

    /// <summary>A short message at the bottom of the window, for a few seconds (UI thread).</summary>
    public void Notice(string text, Core.Engine.OsdKind kind)
    {
        NoticeText.Text = text;
        (NoticeIcon.Symbol, var brush) = kind switch
        {
            Core.Engine.OsdKind.Warning => (SymbolRegular.Warning24, "Kd.Warning"),
            Core.Engine.OsdKind.Success => (SymbolRegular.CheckmarkCircle24, "Kd.Success"),
            _ => (SymbolRegular.Info24, "Kd.AccentText"),
        };
        NoticeIcon.SetResourceReference(ForegroundProperty, brush);
        NoticeBox.SetResourceReference(Border.BorderBrushProperty, kind == Core.Engine.OsdKind.Warning ? "Kd.Warning" : "Kd.MenuBorder");
        NoticeBox.Visibility = Visibility.Visible;
        NoticeBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        NoticeShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
        _noticeTimer.Stop();
        _noticeTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(2 + text.Length / 25.0, 3, 8));
        _noticeTimer.Tick -= OnNoticeDone;
        _noticeTimer.Tick += OnNoticeDone;
        _noticeTimer.Start();
    }

    private void OnNoticeDone(object? sender, EventArgs e)
    {
        _noticeTimer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            if (!_noticeTimer.IsEnabled) NoticeBox.Visibility = Visibility.Collapsed;
        };
        NoticeBox.BeginAnimation(OpacityProperty, fade);
    }

    public void OpenCommand(string commandId)
    {
        Navigate("commands");
        if (_pages.GetValueOrDefault("commands") is Pages.CommandsPage page) page.Select(commandId);
    }

    /// <summary>Panel → Comandos with a new command (the welcome's «Crear mi primer comando»).</summary>
    public void NewCommand()
    {
        Navigate("commands");
        (_pages.GetValueOrDefault("commands") as Pages.CommandsPage)?.NewCommand();
    }

    public void OpenEvent(string? eventId)
    {
        Navigate("events");
        if (eventId is not null && _pages.GetValueOrDefault("events") is Pages.EventsPage page) page.Select(eventId);
    }

    /// <param name="commandId">The command to point at inside the menu (opens its submenus and flashes it).</param>
    public void OpenQuickMenu(string? menuId, string? commandId = null)
    {
        Navigate("quickmenus");
        if (menuId is not null && _pages.GetValueOrDefault("quickmenus") is Pages.QuickMenusPage page) page.Select(menuId, commandId);
    }

    public void OpenSettings(string section)
    {
        Navigate("settings");
        (_pages.GetValueOrDefault("settings") as Pages.SettingsPage)?.ShowSection(section);
    }

    // ── position and size ────────────────────────────────────────────────

    /// <summary>Opens where it was closed, if that place is still on a screen (a monitor may be gone).</summary>
    private void ApplySavedBounds(string? saved)
    {
        var parts = saved?.Split(',');
        if (parts is not { Length: >= 4 }) return;
        var n = new double[4];
        for (var i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out n[i])) return;
        var rect = new Rect(n[0], n[1], Math.Max(MinWidth, n[2]), Math.Max(MinHeight, n[3]));
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visible = Rect.Intersect(rect, screen);
        if (visible.IsEmpty || visible.Width < 200 || visible.Height < 120) return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = rect.Left;
        Top = rect.Top;
        Width = rect.Width;
        Height = rect.Height;
        if (parts.Length > 4 && parts[4] == "max") WindowState = WindowState.Maximized;
    }

    private string SaveBounds()
    {
        // Maximized or minimized: keep the normal-state rectangle so restoring it later looks right.
        var r = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return string.Join(",", new[] { r.Left, r.Top, r.Width, r.Height }.Select(v => Math.Round(v).ToString(inv)))
               + (WindowState == WindowState.Maximized ? ",max" : "");
    }

    // ── status icons ─────────────────────────────────────────────────────

    public void UpdateStatus()
    {
        var app = App.Current;
        var s = app.Config.Settings;

        // Paused wins (it is the user's choice); otherwise red when the hotkeys are not being detected.
        var hooks = app.Router.IsRunning;
        SetStatus(PauseStatus, PauseIcon, s.Paused ? SymbolRegular.Pause24 : SymbolRegular.Power24,
            s.Paused ? "Kd.Warning" : hooks ? "Kd.Success" : "Kd.Danger",
            Loc.T(s.Paused ? "status.paused" : hooks ? "status.active" : app.HooksDisabled ? "status.hooksArg" : "status.hooksFailed"),
            Loc.T("status.clickOptions"));

        var udpOn = s.UdpEnabled && app.UdpAllowed;
        SetStatus(UdpStatus, UdpIcon, udpOn ? SymbolRegular.PlugConnected24 : SymbolRegular.PlugDisconnected24,
            !udpOn ? "TextFillColorTertiaryBrush" : app.Udp.IsRunning ? "Kd.Success" : "Kd.Danger",
            !udpOn ? Loc.T("status.udpOff") : app.Udp.IsRunning ? Loc.T("status.udpOn", s.UdpPort) : Loc.T("status.udpError", s.UdpPort),
            Loc.T("status.clickOptions"));

        var webOn = s.WebEnabled && app.WebAllowed;
        var web = app.Web.State;
        SetStatus(WebStatus, WebIcon, SymbolRegular.Phone24,
            !webOn ? "TextFillColorTertiaryBrush"
            : web == Core.Remote.Web.WebServerState.Running ? "Kd.Success"
            : web == Core.Remote.Web.WebServerState.LocalOnly ? "Kd.Warning" : "Kd.Danger",
            !webOn ? Loc.T("status.webOff") : web switch
            {
                Core.Remote.Web.WebServerState.Running => Loc.T("status.webOn", s.WebPort),
                Core.Remote.Web.WebServerState.LocalOnly => Loc.T("status.webLocal", s.WebPort),
                _ => Loc.T("status.webError", s.WebPort),
            },
            Loc.T("status.clickOptions"));

        // Voice: listening (accent), ready (green), off or without its models (grey).
        var voiceReady = app.Voice.Ready;
        SetStatus(VoiceStatus, VoiceIcon, voiceReady ? SymbolRegular.Mic24 : SymbolRegular.MicOff24,
            app.Voice.IsListening ? "Kd.Accent" : voiceReady ? "Kd.Success" : "TextFillColorTertiaryBrush",
            app.Voice.IsListening ? Loc.T("status.voiceListening")
            : !s.VoiceEnabled ? Loc.T("status.voiceOff")
            : !voiceReady ? Loc.T("status.voiceNoModels")
            : (string.IsNullOrWhiteSpace(s.VoiceHotkey) ? Loc.T("status.voiceNoHotkey") : Loc.T("status.voiceReady", s.VoiceHotkey))
              + (app.WakeListener.IsRunning
                  ? Environment.NewLine + Loc.T("status.voiceWake", string.Join(", ", app.Voice.WakeWords.Active.Where(w => w.Detector is not null).Select(w => $"«{w.Name}»")))
                  : ""),
            Loc.T("status.clickOptions"));

        // The assistant: its page and its icon only while the module is on.
        var assistantOn = app.Assistant is not null;
        AssistantNav.Visibility = AssistantStatus.Visibility = assistantOn ? Visibility.Visible : Visibility.Collapsed;
        StatusGrid.Columns = assistantOn ? 5 : 4;
        if (assistantOn)
        {
            var (text, brush) = AssistantLook.Summary(app);
            SetStatus(AssistantStatus, AssistantIcon, SymbolRegular.Bot24, brush, Loc.T("status.assistant", text), Loc.T("status.clickPanel"));
        }
    }

    /// <summary>Icon, colour and tooltip of one status button.</summary>
    private static void SetStatus(System.Windows.Controls.Button button, SymbolIcon icon, SymbolRegular symbol, string brush, string text, string? hint)
    {
        icon.Symbol = symbol;
        icon.SetResourceReference(ForegroundProperty, brush);
        button.ToolTip = hint is null ? text : text + "\n" + hint;
        System.Windows.Automation.AutomationProperties.SetName(button, text);
    }

    private void OnPauseStatus(object sender, RoutedEventArgs e) => OpenSettings("general");

    private void OnUdpStatus(object sender, RoutedEventArgs e) => OpenSettings("remote");

    private void OnWebStatus(object sender, RoutedEventArgs e) => OpenSettings("web");

    private void OnVoiceStatus(object sender, RoutedEventArgs e) => OpenSettings("voice");

    private void OnAssistantStatus(object sender, RoutedEventArgs e) => Navigate("assistant");

    /// <summary>Files dropped on the window: assistant packages open their card; anything else is not ours.</summary>
    private void OnFilesDropped(IReadOnlyList<string> files)
    {
        var packages = files.Where(f => f.EndsWith(Core.Assistant.Packages.AssistantPackages.Extension, StringComparison.OrdinalIgnoreCase)).ToList();
        if (packages.Count == 0)
        {
            App.Current.Ui.ShowNotice(Loc.T("assistant.dropOnlyPackages"), Core.Engine.OsdKind.Info);
            return;
        }
        foreach (var package in packages) App.Current.OpenAssistantPackage(package);
    }

    private void OnPausedChanged()
    {
        UpdateStatus();
        ShowBanners();
    }

    private void OnNavigate(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is ListBoxItem { Tag: string key }) Navigate(key, fromList: true);
    }

    public void OpenMacro(string? macroId)
    {
        Navigate("macros");
        if (macroId is not null && _pages.GetValueOrDefault("macros") is Pages.MacrosPage page) page.Select(macroId);
    }

    public void OpenKBoard(string? profileId)
    {
        Navigate("kboard");
        if (profileId is not null && _pages.GetValueOrDefault("kboard") is Pages.KBoardPage page) page.Select(profileId);
    }

    public void Navigate(string key, bool fromList = false)
    {
        // "settings/android": a section of the settings (--page settings/android).
        if (key.StartsWith("settings/", StringComparison.Ordinal))
        {
            OpenSettings(key["settings/".Length..]);
            return;
        }
        // "assistant/dot": Panel → Assistant showing that one (several may be on the desktop).
        if (key.StartsWith("assistant/", StringComparison.Ordinal))
        {
            Navigate("assistant");
            (_pages.GetValueOrDefault("assistant") as Pages.AssistantPage)?.Show(key["assistant/".Length..]);
            return;
        }
        if (key == "stats")
        {
            Navigate("achievements");
            (_pages.GetValueOrDefault("achievements") as Pages.AchievementsPage)?.ShowStats();
            return;
        }
        if (!fromList)
        {
            var item = NavList.Items.OfType<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == key);
            if (item is not null && !ReferenceEquals(NavList.SelectedItem, item))
            {
                NavList.SelectedItem = item; // re-enters through OnNavigate
                return;
            }
        }
        if (_current == key && PageHost.Content is not null) return;
        _current = key;

        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "quickmenus" => new Pages.QuickMenusPage(),
                "events" => new Pages.EventsPage(),
                "macros" => new Pages.MacrosPage(),
                "kboard" => new Pages.KBoardPage(),
                "achievements" => new Pages.AchievementsPage(),
                "history" => new Pages.HistoryPage(),
                "assistant" => new Pages.AssistantPage(),
                "settings" => new Pages.SettingsPage(),
                "about" => new Pages.AboutPage(),
                _ => new Pages.CommandsPage(),
            };
            _pages[key] = page;
        }
        PageHost.Content = page;

        var duration = new Duration(TimeSpan.FromMilliseconds(260));
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200))));
        PageShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(16, 0, duration) { EasingFunction = EaseOut });
    }

    // Pages build some texts in code: rebuild them so everything follows the new language.
    private void OnLanguageChanged() => Dispatcher.BeginInvoke(RebuildPages);

    /// <summary>
    /// Throws away the cached pages and shows the current one again: after a language change, or when the whole
    /// configuration is replaced (imports), since the pages hold on to the collections of the old one.
    /// </summary>
    public void RebuildPages()
    {
        foreach (var page in _pages.Values.OfType<IDisposable>()) page.Dispose();
        _pages.Clear();
        var key = _current;
        PageHost.Content = null;
        Navigate(key, fromList: true);
        ShowBanners();
        UpdateStatus();
    }

    /// <summary>
    /// An info bar with a button on its right (the InfoBar does not show its own content). Closing the bar hides
    /// the button too.
    /// </summary>
    private void AddBanner(InfoBar bar, FrameworkElement button)
    {
        // The bar's template ignores Padding: once laid out, make its text block leave room for the button.
        bar.Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (FindText(bar, bar.Message) is { } text &&
                System.Windows.Media.VisualTreeHelper.GetParent(text) is FrameworkElement panel)
                panel.Margin = new Thickness(panel.Margin.Left, panel.Margin.Top, button.ActualWidth + 20, panel.Margin.Bottom);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
        button.HorizontalAlignment = HorizontalAlignment.Right;
        button.VerticalAlignment = VerticalAlignment.Center;
        button.Margin = new Thickness(0, 0, bar.IsClosable ? 52 : 12, 0);
        var host = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        host.Children.Add(bar);
        host.Children.Add(button);
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(InfoBar.IsOpenProperty, typeof(InfoBar))
            .AddValueChanged(bar, (_, _) => host.Visibility = bar.IsOpen ? Visibility.Visible : Visibility.Collapsed);
        Banners.Children.Add(host);
    }

    private static System.Windows.Controls.TextBlock? FindText(DependencyObject root, string? text)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.TextBlock tb && tb.Text == text) return tb;
            if (FindText(child, text) is { } found) return found;
        }
        return null;
    }

    /// <summary>Info bars: hotkeys off, paused, new versions.</summary>
    public void ShowBanners()
    {
        Banners.Children.Clear();
        var app = App.Current;

        if (!app.Router.IsRunning)
        {
            Banners.Children.Add(new InfoBar
            {
                Title = Loc.T("banner.hooksTitle"),
                Message = Loc.T(app.HooksDisabled ? "banner.hooksArg" : "banner.hooksFailed"),
                Severity = InfoBarSeverity.Error,
                IsOpen = true,
                IsClosable = true,
                Margin = new Thickness(0, 0, 0, 8),
            });
        }

        if (app.Config.Settings.Paused)
        {
            var resume = new Wpf.Ui.Controls.Button
            {
                Content = Loc.T("pause.resume"),
                Icon = new SymbolIcon(SymbolRegular.Play24),
                Appearance = ControlAppearance.Primary,
            };
            resume.Click += (_, _) => app.SetPaused(false);
            AddBanner(new InfoBar
            {
                Title = Loc.T("banner.pausedTitle"),
                Message = Loc.T("banner.pausedMessage"),
                Severity = InfoBarSeverity.Warning,
                IsOpen = true,
                IsClosable = false,
            }, resume);
        }

        if (app.Updates.Available is { } update && app.Updates.State is Core.Updates.UpdateState.Available
                or Core.Updates.UpdateState.Downloading or Core.Updates.UpdateState.Ready)
        {
            var state = app.Updates.State;
            var install = new Wpf.Ui.Controls.Button
            {
                Content = Loc.T(Core.Updates.UpdateService.CanInstall ? "updates.install" : "updates.openDownload"),
                Icon = new SymbolIcon(SymbolRegular.ArrowDownload24),
                Appearance = ControlAppearance.Primary,
                IsEnabled = state != Core.Updates.UpdateState.Downloading,
            };
            install.Click += (_, _) => _ = app.InstallUpdateAsync();
            AddBanner(new InfoBar
            {
                Title = Loc.T("banner.updateTitle", update.Version.ToString(3)),
                Message = Loc.T(state switch
                {
                    _ when !Core.Updates.UpdateService.CanInstall => "updates.notPortable",
                    Core.Updates.UpdateState.Downloading => "banner.updateDownloading",
                    Core.Updates.UpdateState.Ready => "banner.updateReady",
                    _ => "banner.updateMessage",
                }),
                Severity = InfoBarSeverity.Informational,
                IsOpen = true,
                IsClosable = true,
            }, install);
        }

        // New versions of assistants (stage 9.5): one is opened with its card; several, in Options → Updates.
        if (app.PendingAssistantUpdates is { Count: > 0 } assistants)
        {
            var one = assistants.Count == 1 ? assistants[0] : null;
            var see = new Wpf.Ui.Controls.Button
            {
                Content = Loc.T(one is null ? "assistantUpdates.see" : "assistantUpdates.update"),
                Icon = new SymbolIcon(SymbolRegular.ArrowDownload24),
                Appearance = ControlAppearance.Primary,
                IsEnabled = one is null || !app.IsDownloadingAssistantUpdate(one.Id),
            };
            see.Click += (_, _) =>
            {
                if (one is not null) _ = app.OpenAssistantUpdateAsync(one.Id);
                else Navigate("settings/updates");
            };
            AddBanner(new InfoBar
            {
                Title = one is not null ? Loc.T("assistantUpdates.bannerOne", one.Name, one.Latest!) : Loc.T("assistantUpdates.bannerMany", assistants.Count),
                Message = one is not null
                    ? one.Changes?.In(Loc.Instance.Language) is { Length: > 0 } changes ? changes : Loc.T("assistantUpdates.bannerMessage")
                    : string.Join(", ", assistants.Select(a => $"{a.Name} {a.Latest}")),
                Severity = InfoBarSeverity.Informational,
                IsOpen = true,
                IsClosable = true,
            }, see);
        }
    }
}

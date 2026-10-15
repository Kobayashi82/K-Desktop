using System.Windows;
using System.Windows.Controls;
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
        Closing += (_, _) => App.Current.Config.Settings.WindowBounds = SaveBounds();
        Loaded += (_, _) =>
        {
            NavList.SelectedIndex = 0;
            ShowBanners();
            UpdateStatus();
        };
        Loc.Instance.Changed += OnLanguageChanged;
        App.Current.PausedChanged += OnPausedChanged;
        Closed += (_, _) =>
        {
            Loc.Instance.Changed -= OnLanguageChanged;
            App.Current.PausedChanged -= OnPausedChanged;
            foreach (var page in _pages.Values.OfType<IDisposable>()) page.Dispose();
        };
    }

    public void OpenCommand(string commandId)
    {
        Navigate("commands");
        if (_pages.GetValueOrDefault("commands") is Pages.CommandsPage page) page.Select(commandId);
    }

    public void OpenQuickMenu(string? menuId)
    {
        Navigate("quickmenus");
        if (menuId is not null && _pages.GetValueOrDefault("quickmenus") is Pages.QuickMenusPage page) page.Select(menuId);
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

        // Voice: listening (accent), ready (green) or without its models (grey).
        var voiceReady = Core.Voice.VoiceModels.Ready;
        SetStatus(VoiceStatus, VoiceIcon, voiceReady ? SymbolRegular.Mic24 : SymbolRegular.MicOff24,
            app.Voice.IsListening ? "Kd.Accent" : voiceReady ? "Kd.Success" : "TextFillColorTertiaryBrush",
            app.Voice.IsListening ? Loc.T("status.voiceListening")
            : !voiceReady ? Loc.T("status.voiceNoModels")
            : string.IsNullOrWhiteSpace(s.VoiceHotkey) ? Loc.T("status.voiceNoHotkey")
            : Loc.T("status.voiceReady", s.VoiceHotkey),
            Loc.T("status.clickOptions"));
    }

    /// <summary>Icon, colour (icon and dot) and tooltip of one status button.</summary>
    private static void SetStatus(System.Windows.Controls.Button button, SymbolIcon icon, SymbolRegular symbol, string brush, string text, string? hint)
    {
        icon.Symbol = symbol;
        icon.SetResourceReference(ForegroundProperty, brush == "TextFillColorTertiaryBrush" ? "TextFillColorSecondaryBrush" : brush);
        button.SetResourceReference(TagProperty, brush);
        button.ToolTip = hint is null ? text : text + "\n" + hint;
        System.Windows.Automation.AutomationProperties.SetName(button, text);
    }

    private void OnPauseStatus(object sender, RoutedEventArgs e) => OpenSettings("general");

    private void OnUdpStatus(object sender, RoutedEventArgs e) => OpenSettings("remote");

    private void OnWebStatus(object sender, RoutedEventArgs e) => OpenSettings("web");

    private void OnVoiceStatus(object sender, RoutedEventArgs e) => OpenSettings("voice");

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
                "macros" => new Pages.MacrosPage(),
                "kboard" => new Pages.KBoardPage(),
                "achievements" => new Pages.AchievementsPage(),
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

    private void OnLanguageChanged()
    {
        // Pages build some texts in code: rebuild them so everything follows the new language.
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var page in _pages.Values.OfType<IDisposable>()) page.Dispose();
            _pages.Clear();
            var key = _current;
            PageHost.Content = null;
            Navigate(key, fromList: true);
            ShowBanners();
            UpdateStatus();
        });
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

    /// <summary>Info bars: hotkeys off, paused, K-Desktop 4 still running, import summary.</summary>
    private void ShowBanners()
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
                Content = Loc.T("pause.resumeShort"),
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

        var legacy = app.LegacyInstances;
        if (legacy.Count > 0)
        {
            var bar = new InfoBar
            {
                Title = Loc.T("banner.legacyTitle"),
                Message = Loc.T("banner.legacyMessage"),
                Severity = InfoBarSeverity.Warning,
                IsOpen = true,
                IsClosable = true,
            };
            var close = new Wpf.Ui.Controls.Button { Content = Loc.T("banner.legacyClose"), Appearance = ControlAppearance.Caution };
            close.Click += (_, _) =>
            {
                foreach (var p in app.LegacyInstances)
                {
                    try { p.Kill(); } catch { /* ignore */ }
                    p.Dispose();
                }
                bar.IsOpen = false;
            };
            AddBanner(bar, close);
            foreach (var p in legacy) p.Dispose();
        }

        if (app.ImportReport is { } report)
        {
            Banners.Children.Add(new InfoBar
            {
                Title = Loc.T("banner.importTitle"),
                Message = Loc.T("banner.importMessage", report.Commands, report.Actions, report.QuickMenus, report.MenuItems, report.Icons) +
                          (report.Skipped.Count > 0 ? " " + Loc.T("banner.importSkipped", report.Skipped.Count) : ""),
                Severity = InfoBarSeverity.Success,
                IsOpen = true,
                IsClosable = true,
                Margin = new Thickness(0, 0, 0, 8),
            });
        }
    }
}

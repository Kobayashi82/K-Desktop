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
        Loaded += (_, _) =>
        {
            NavList.SelectedIndex = 0;
            ShowBanners();
            UpdateStatus();
        };
        Loc.Instance.Changed += OnLanguageChanged;
        Closed += (_, _) =>
        {
            Loc.Instance.Changed -= OnLanguageChanged;
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

    public void UpdateStatus()
    {
        var app = App.Current;
        var s = app.Config.Settings;
        HookDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, app.Router.IsRunning ? "Kd.Success" : "Kd.Danger");
        HookText.Text = Loc.T(app.Router.IsRunning ? "status.hooks" : "status.hooksOff");
        UdpText.Text = !s.UdpEnabled || !app.UdpAllowed ? Loc.T("status.udpOff")
            : app.Udp.IsRunning ? Loc.T("status.udpOn", s.UdpPort) : Loc.T("status.udpError", s.UdpPort);
        UdpDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
            !s.UdpEnabled ? "TextFillColorTertiaryBrush" : app.Udp.IsRunning ? "Kd.Success" : "Kd.Danger");

        var web = app.Web.State;
        WebText.Text = !s.WebEnabled || !app.WebAllowed ? Loc.T("status.webOff")
            : web switch
            {
                Core.Remote.Web.WebServerState.Running => Loc.T("status.webOn", s.WebPort),
                Core.Remote.Web.WebServerState.LocalOnly => Loc.T("status.webLocal", s.WebPort),
                _ => Loc.T("status.webError", s.WebPort),
            };
        WebDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
            !s.WebEnabled || !app.WebAllowed ? "TextFillColorTertiaryBrush"
            : web == Core.Remote.Web.WebServerState.Running ? "Kd.Success"
            : web == Core.Remote.Web.WebServerState.LocalOnly ? "Kd.Warning" : "Kd.Danger");
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

    /// <summary>Info bars: K-Desktop 4 still running, import summary.</summary>
    private void ShowBanners()
    {
        Banners.Children.Clear();
        var app = App.Current;

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
                Margin = new Thickness(0, 0, 0, 8),
            };
            var close = new Wpf.Ui.Controls.Button { Content = Loc.T("banner.legacyClose"), Appearance = ControlAppearance.Caution, Margin = new Thickness(0, 6, 0, 0) };
            close.Click += (_, _) =>
            {
                foreach (var p in app.LegacyInstances)
                {
                    try { p.Kill(); } catch { /* ignore */ }
                    p.Dispose();
                }
                bar.IsOpen = false;
            };
            bar.Content = close;
            Banners.Children.Add(bar);
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

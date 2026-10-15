using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KDesktop.Core.Achievements;
using KDesktop.Core.Localization;
using KDesktop.Services;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using Image = System.Windows.Controls.Image;

namespace KDesktop.Views.Pages;

/// <summary>Achievements (grouped cards with progress) and statistics (headline numbers, most used commands, origins).</summary>
public partial class AchievementsPage : UserControl, IDisposable
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private readonly App _app = App.Current;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private StatsSnapshot _snapshot;

    public AchievementsPage()
    {
        InitializeComponent();
        _snapshot = _app.Achievements.Snapshot();
        NotifyToggle.IsChecked = _app.Config.Settings.AchievementNotifications;
        SoundToggle.IsChecked = _app.Config.Settings.AchievementSound;
        _refresh.Tick += (_, _) =>
        {
            _refresh.Stop();
            _snapshot = _app.Achievements.Snapshot();
            BuildSummary(animate: false);
            BuildGroups(animate: false);
            if (StatsView.IsVisible) BuildStats(animate: false);
        };
        _app.Achievements.Changed += OnChanged;
        Loaded += (_, _) =>
        {
            BuildSummary(animate: true);
            BuildGroups(animate: true);
        };
    }

    public void Dispose()
    {
        _app.Achievements.Changed -= OnChanged;
        _refresh.Stop();
    }

    /// <summary>Opens the statistics tab directly.</summary>
    public void ShowStats() => TabStats.IsChecked = true;

    private void OnChanged() => Dispatcher.BeginInvoke(() =>
    {
        if (!_refresh.IsEnabled) _refresh.Start();
    });

    // ── tabs ─────────────────────────────────────────────────────────────

    private void OnTab(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || StatsView is null) return;
        var stats = TabStats.IsChecked == true;
        var show = stats ? (FrameworkElement)StatsView : AchievementsView;
        AchievementsView.Visibility = stats ? Visibility.Collapsed : Visibility.Visible;
        StatsView.Visibility = stats ? Visibility.Visible : Visibility.Collapsed;
        if (stats)
        {
            _snapshot = _app.Achievements.Snapshot();
            BuildStats(animate: true);
        }
        show.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        var shift = new TranslateTransform();
        show.RenderTransform = shift;
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = EaseOut });
    }

    // ── achievements ─────────────────────────────────────────────────────

    private void BuildSummary(bool animate)
    {
        var s = _snapshot;
        var culture = Loc.Instance.Culture;
        UnlockedText.Text = s.UnlockedCount.ToString("N0", culture);
        TotalText.Text = "/ " + s.TotalCount.ToString("N0", culture);
        var fraction = s.TotalCount == 0 ? 0 : (double)s.UnlockedCount / s.TotalCount;
        PercentText.Text = fraction.ToString("P0", culture);
        TotalBar.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(animate ? 0 : TotalBar.ScaleX, fraction, TimeSpan.FromMilliseconds(animate ? 900 : 300)) { EasingFunction = EaseOut });

        var last = s.Data.Unlocked.OrderByDescending(u => u.Value).Select(u => (Def: AchievementCatalog.Get(u.Key), Date: u.Value)).FirstOrDefault(u => u.Def is not null);
        LastText.Text = last.Def is { } def ? Loc.T("ach.last", def.Name, last.Date.ToString("g", culture)) : Loc.T("ach.none");

        var days = Math.Max(0, (int)(DateTime.Now - s.Data.FirstUse).TotalDays);
        SinceText.Text = Loc.T("ach.since", s.Data.FirstUse.ToString("D", culture), days);
    }

    private void BuildGroups(bool animate)
    {
        var data = _snapshot.Data;
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        var items = AchievementCatalog.All.Select(d => new AchievementItem(d, data)).ToList();

        Groups.Children.Clear();
        var index = 0;
        foreach (var category in AchievementCatalog.Categories)
        {
            var all = items.Where(i => i.Def.Category == category).ToList();
            var visible = all.Where(i => PassesFilter(i) && i.Matches(query))
                             .OrderByDescending(i => i.Unlocked).ToList(); // stable: catalog order inside each half
            if (visible.Count == 0) continue;

            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 10) };
            header.Children.Add(new SymbolIcon
            {
                Symbol = Enum.TryParse<SymbolRegular>(AchievementCatalog.CategorySymbol(category), out var sym) ? sym : SymbolRegular.Star24,
                FontSize = 16,
                Foreground = (Brush)FindResource("Kd.AccentText"),
                Margin = new Thickness(0, 0, 8, 0),
            });
            header.Children.Add(new TextBlock { Text = Loc.T($"achcat.{category}"), Style = (Style)FindResource("SectionTitle"), Margin = new Thickness(0) });
            header.Children.Add(new TextBlock
            {
                Text = $"{all.Count(i => i.Unlocked)} / {all.Count}",
                Margin = new Thickness(10, 0, 0, 0),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextFillColorTertiaryBrush"),
            });
            Groups.Children.Add(header);

            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 12), ItemWidth = CardWidth() };
            foreach (var item in visible)
            {
                var card = new ContentPresenter { Content = item, ContentTemplate = (DataTemplate)FindResource("AchievementCard") };
                if (animate) Stagger(card, index++);
                wrap.Children.Add(card);
            }
            Groups.Children.Add(wrap);
        }
        NoResults.Visibility = Groups.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Cards share the available width (min 250 px each).</summary>
    private double CardWidth()
    {
        var available = Groups.ActualWidth > 0 ? Groups.ActualWidth : 820;
        var columns = Math.Max(1, (int)((available + 10) / 260));
        return Math.Floor(available / columns); // each card keeps a 10 px right margin inside its slot
    }

    private void OnGroupsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged) return;
        var width = CardWidth();
        foreach (var wrap in Groups.Children.OfType<WrapPanel>()) wrap.ItemWidth = width;
    }

    private bool PassesFilter(AchievementItem i) =>
        FilterUnlocked.IsChecked == true ? i.Unlocked
        : FilterLocked.IsChecked == true ? !i.Unlocked
        : FilterNear.IsChecked != true || (!i.Unlocked && i.ShowProgress && i.Fraction >= 0.5);

    /// <summary>Cards fade and rise one after another.</summary>
    private static void Stagger(FrameworkElement element, int index)
    {
        var delay = TimeSpan.FromMilliseconds(Math.Min(index, 40) * 14);
        element.Opacity = 0;
        var shift = new TranslateTransform(0, 14);
        element.RenderTransform = shift;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = delay });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(340)) { BeginTime = delay, EasingFunction = EaseOut });
    }

    private void OnFilter(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) BuildGroups(animate: true);
    }

    private void OnSearch(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) BuildGroups(animate: false);
    }

    private void OnNotifyToggle(object sender, RoutedEventArgs e) => _app.Config.Settings.AchievementNotifications = NotifyToggle.IsChecked == true;
    private void OnSoundToggle(object sender, RoutedEventArgs e) => _app.Config.Settings.AchievementSound = SoundToggle.IsChecked == true;

    private async void OnReset(object sender, RoutedEventArgs e)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("ach.reset"),
            Content = Loc.T("ach.resetConfirm"),
            PrimaryButtonText = Loc.T("ach.resetButton"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        _app.Achievements.Reset();
        _snapshot = _app.Achievements.Snapshot();
        BuildSummary(animate: true);
        BuildGroups(animate: true);
    }

    // ── statistics ───────────────────────────────────────────────────────

    private void BuildStats(bool animate)
    {
        var d = _snapshot.Data;
        var culture = Loc.Instance.Culture;

        // Headline numbers
        Tiles.Children.Clear();
        var days = Math.Max(0, (long)(DateTime.Now - d.FirstUse).TotalDays);
        (string Label, long Value, string Symbol)[] tiles =
        [
            (Loc.T("stat.commands.run"), d.Get(Stat.CommandsRun), "Flash24"),
            (Loc.T("stat.actions.run"), d.Get(Stat.ActionsRun), "Rocket24"),
            (Loc.T("stat.menus.opened"), d.Get(Stat.MenusOpened), "Navigation24"),
            (Loc.T("stats.days"), days, "CalendarStar24"),
        ];
        foreach (var (label, value, symbol) in tiles)
        {
            var number = new TextBlock
            {
                FontSize = 30,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                Foreground = (Brush)FindResource("TextFillColorPrimaryBrush"),
                Margin = new Thickness(0, 6, 0, 0),
            };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new SymbolIcon { Symbol = Enum.Parse<SymbolRegular>(symbol), FontSize = 16, Foreground = (Brush)FindResource("Kd.AccentText"), Margin = new Thickness(0, 0, 8, 0) });
            head.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("Caption"), VerticalAlignment = VerticalAlignment.Center });
            var panel = new StackPanel();
            panel.Children.Add(head);
            panel.Children.Add(number);
            Tiles.Children.Add(new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 12, 12), Child = panel });
            CountUp(number, value, animate);
        }

        // Most used commands
        TopCommands.Children.Clear();
        var top = d.CommandRuns.OrderByDescending(kv => kv.Value).Take(10).ToList();
        var max = top.Count == 0 ? 1 : top[0].Value;
        var total = Math.Max(1, d.CommandRuns.Values.Sum());
        var i = 0;
        foreach (var (id, count) in top)
        {
            var cmd = _app.Config.FindCommand(id);
            var name = cmd?.Name ?? Loc.T("qm.deletedCommand");
            var icon = new Image { Width = 20, Height = 20, Source = cmd is null ? null : IconProvider.ForCommand(cmd), Margin = new Thickness(0, 0, 10, 0) };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            TopCommands.Children.Add(BarRow(icon, name, count, (double)count / max, (double)count / total, animate, i++));
        }
        TopEmpty.Visibility = top.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Origins
        Sources.Children.Clear();
        var sources = TriggerSourceNames.All.Select(k => (Key: k, Value: d.RunsBySource.GetValueOrDefault(k))).Where(x => x.Value > 0)
                                            .OrderByDescending(x => x.Value).ToList();
        var maxSource = sources.Count == 0 ? 1 : sources[0].Value;
        var totalSources = Math.Max(1, sources.Sum(x => x.Value));
        i = 0;
        foreach (var (key, value) in sources)
            Sources.Children.Add(BarRow(null, Loc.T($"source.{key}"), value, (double)value / maxSource, (double)value / totalSources, animate, i++));
        SourcesEmpty.Visibility = sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Every counter, as a table
        CountersLeft.Children.Clear();
        CountersRight.Children.Clear();
        var groupIndex = 0;
        foreach (var (group, keys) in Stat.Display)
        {
            var column = new StackPanel { Margin = new Thickness(0, 0, 24, 14) };
            column.Children.Add(new TextBlock { Text = Loc.T($"statgroup.{group}"), Style = (Style)FindResource("FieldLabel"), FontWeight = FontWeights.SemiBold });
            foreach (var key in keys)
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = Loc.T($"stat.{key}"), Foreground = (Brush)FindResource("TextFillColorSecondaryBrush") });
                var value = new TextBlock { Text = d.Get(key).ToString("N0", culture), FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextFillColorPrimaryBrush") };
                Grid.SetColumn(value, 1);
                row.Children.Add(value);
                column.Children.Add(row);
            }
            (groupIndex++ % 2 == 0 ? CountersLeft : CountersRight).Children.Add(column);
        }
    }

    /// <summary>Label · horizontal bar (rounded at its end) · value. Hover shows the exact share.</summary>
    private FrameworkElement BarRow(FrameworkElement? icon, string label, long value, double fraction, double share, bool animate, int index)
    {
        var culture = Loc.Instance.Culture;
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5), Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ToolTip = $"{label}: {value.ToString("N0", culture)} ({share.ToString("P1", culture)})";

        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null) name.Children.Add(icon);
        name.Children.Add(new TextBlock
        {
            Text = label,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = icon is null ? 180 : 150,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextFillColorPrimaryBrush"),
        });
        grid.Children.Add(name);

        var track = new Grid { Height = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(fraction, 0.005), GridUnitType.Star) });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - fraction, 0.0001), GridUnitType.Star) });
        var scale = new ScaleTransform(1, 1);
        var bar = new Border
        {
            CornerRadius = new CornerRadius(0, 4, 4, 0),
            Background = (Brush)FindResource("Kd.Bar"),
            RenderTransformOrigin = new Point(0, 0.5),
            RenderTransform = scale,
        };
        track.Children.Add(bar);
        Grid.SetColumn(track, 1);
        grid.Children.Add(track);

        var number = new TextBlock
        {
            Text = value.ToString("N0", culture),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
        };
        Grid.SetColumn(number, 2);
        grid.Children.Add(number);

        grid.MouseEnter += (_, _) => bar.Opacity = 0.8;
        grid.MouseLeave += (_, _) => bar.Opacity = 1;
        if (animate)
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(650)) { BeginTime = TimeSpan.FromMilliseconds(index * 45), EasingFunction = EaseOut });
        return grid;
    }

    /// <summary>Counts from 0 up to the value (ease-out), or sets it directly.</summary>
    private static void CountUp(TextBlock text, long value, bool animate)
    {
        var culture = Loc.Instance.Culture;
        if (!animate || value <= 0)
        {
            text.Text = value.ToString("N0", culture);
            return;
        }
        var start = DateTime.Now;
        const double ms = 900;
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var t = Math.Min(1, (DateTime.Now - start).TotalMilliseconds / ms);
            var eased = 1 - Math.Pow(1 - t, 3);
            text.Text = ((long)Math.Round(value * eased)).ToString("N0", culture);
            if (t >= 1) timer.Stop();
        };
        text.Text = "0";
        timer.Start();
    }
}

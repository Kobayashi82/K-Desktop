using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KDesktop.Core.Achievements;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using KDesktop.Services;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Pages;

public partial class AboutPage : UserControl
{
    private static readonly TimeSpan TipTime = TimeSpan.FromSeconds(9);
    private static readonly HashSet<string> Seen = [];
    private readonly Random _random = new();
    private IReadOnlyList<Tips.Tip> _tips = Tips.All;
    private int _index;
    private bool _paused;
    private bool _hovering;

    /// <summary>A link of the page (GitHub): opens in the browser as the normal user, not elevated like K-Desktop.</summary>
    private void OnOpenLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        ProcessLauncher.Launch(new LaunchSpec(e.Uri.AbsoluteUri), asAdmin: false);
        e.Handled = true;
    }

    public AboutPage()
    {
        InitializeComponent();
        var version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = Loc.T("about.version", version?.ToString(3) ?? "5.0",
            Loc.T(ProcessLauncher.IsElevated ? "about.elevated" : "about.notElevated"));
        BuildFilters();
        _index = _random.Next(_tips.Count);
        Loaded += (_, _) =>
        {
            ShowTip(0);
        };
        Unloaded += (_, _) => Progress.BeginAnimation(ScaleTransform.ScaleXProperty, null);
    }

    // ── filters ──────────────────────────────────────────────────────────

    private void BuildFilters()
    {
        foreach (var category in Tips.Categories.Prepend("all"))
        {
            var pill = new RadioButton
            {
                Style = (Style)FindResource("FilterPill"),
                GroupName = "tips",
                Content = Loc.T($"tips.cat.{category}"),
                Tag = category,
                IsChecked = category == "all",
            };
            pill.Checked += OnFilter;
            Filters.Children.Add(pill);
        }
    }

    private void OnFilter(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var category = (string)((RadioButton)sender).Tag;
        _tips = category == "all" ? Tips.All : Tips.All.Where(t => t.Category == category).ToList();
        _index = _random.Next(_tips.Count);
        ShowTip(1);
    }

    // ── navigation ───────────────────────────────────────────────────────

    private void OnNext(object sender, RoutedEventArgs e) => Move(+1);
    private void OnPrevious(object sender, RoutedEventArgs e) => Move(-1);

    private void OnRandom(object sender, RoutedEventArgs e)
    {
        if (_tips.Count < 2) return;
        var next = _index;
        while (next == _index) next = _random.Next(_tips.Count);
        _index = next;
        ShowTip(1);
    }

    private void Move(int delta)
    {
        if (_tips.Count == 0) return;
        _index = (_index + delta + _tips.Count) % _tips.Count;
        ShowTip(delta);
    }

    private void OnPlayPause(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        PlayButton.Icon = new SymbolIcon { Symbol = _paused ? SymbolRegular.Play24 : SymbolRegular.Pause24 };
        PlayButton.ToolTip = Loc.T(_paused ? "tips.play" : "tips.pause");
        if (_paused) Hold();
        else Resume();
    }

    // Hovering the tip holds the timer, so it can be read calmly.
    private void OnTipEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hovering = true;
        Hold();
    }

    private void OnTipLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _hovering = false;
        Resume();
    }

    // ── display ──────────────────────────────────────────────────────────

    /// <summary>Shows the current tip, sliding in from the side of <paramref name="direction"/> (0 = fade only).</summary>
    private void ShowTip(int direction)
    {
        if (_tips.Count == 0) return;
        var tip = _tips[_index];
        Counter.Text = $"{_index + 1} / {_tips.Count}";

        Fill(tip);
        TipCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        TipShift.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(34 * Math.Sign(direction), 0, TimeSpan.FromMilliseconds(360)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        if (Seen.Add(tip.Id)) App.Current.Achievements.Increment(Stat.TipsSeen);
        Restart();
    }

    private void Fill(Tips.Tip tip)
    {
        TipIcon.Source = KdIcons.Render(Tips.Icon(tip.Category));
        TipCategory.Text = Loc.T($"tips.cat.{tip.Category}").ToUpper(Loc.Instance.Culture);
        TipText.Text = tip.Text;
        TipKeys.Children.Clear();
        var first = true;
        foreach (var alternative in tip.Keys)
        {
            if (!first)
                TipKeys.Children.Add(new TextBlock
                {
                    Text = Loc.T("tips.or"),
                    Margin = new Thickness(4, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)FindResource("TextFillColorTertiaryBrush"),
                });
            first = false;
            var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            foreach (var key in alternative.Split(" + ", StringSplitOptions.TrimEntries))
                group.Children.Add(Cap(key));
            TipKeys.Children.Add(group);
        }
    }

    private static FrameworkElement Cap(string text)
    {
        var cap = new Border
        {
            Margin = new Thickness(0, 0, 5, 0),
            Padding = new Thickness(10, 4, 10, 4),
            CornerRadius = new CornerRadius(5),
            MinWidth = 30,
            BorderThickness = new Thickness(1, 1, 1, 3),
            Child = new TextBlock { Text = text, FontSize = 14, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
        };
        cap.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
        cap.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        ((TextBlock)cap.Child).SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        return cap;
    }

    // ── automatic advance (the bar under the tip is the timer) ───────────

    private void Restart()
    {
        Progress.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        Progress.ScaleX = 0;
        if (!_paused && !_hovering) Resume();
    }

    private void Hold()
    {
        var value = Progress.ScaleX;
        Progress.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        Progress.ScaleX = value;
    }

    private void Resume()
    {
        if (_paused || _hovering || !IsLoaded) return;
        var from = Progress.ScaleX;
        var animation = new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(TipTime.TotalMilliseconds * (1 - from)));
        animation.Completed += (_, _) =>
        {
            if (!_paused && !_hovering && Progress.ScaleX >= 0.999) Move(+1);
        };
        Progress.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
    }
}

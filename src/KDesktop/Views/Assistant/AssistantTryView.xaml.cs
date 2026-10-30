using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using Image = System.Windows.Controls.Image;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Assistant;

/// <summary>
/// The «Probar» tab of an installed assistant's card (stages 13.9 and 16.1): its stage — another copy of it on a desktop of
/// its own, with pretend windows, drawn off the screen and shown here live (the mouse reaches it: it can be petted) — as big
/// as the page lets it, and under it what to show, picked with two lists: the type (its own animations by group, then the
/// host's entrances, exits, states, reactions, gestures and what it does to a window) and the thing itself. Picking one does
/// it; «Hacerlo» does it again. One at a time (another one, or «Parar», stops it). The stage opens with the tab and stays a
/// while after it is left (<see cref="App.LeaveAssistantStage"/>), so coming back is instant. Made in code.
/// </summary>
public partial class AssistantTryView : UserControl
{
    /// <summary>The host's groups, in this order after its own ones.</summary>
    private static readonly string[] HostGroups = ["entrances", "exits", "states", "reactions", "gestures", "windows"];

    /// <summary>Size of the stage's picture (pixels); it is shown up to half as big again.</summary>
    private const double StageWidth = 720, StageHeight = 405, MostZoom = 1.5;

    /// <summary>The stage is never smaller than this (smaller, it is hard to see what it does).</summary>
    private const double MinPicture = 260;

    /// <summary>What was last picked on each assistant's stage (type and thing), for when its tab is shown again.</summary>
    private static readonly Dictionary<string, (string Kind, string Item)> Picked = new(StringComparer.Ordinal);

    private readonly App _app = App.Current;
    private readonly DispatcherTimer _noAnswer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _coverDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _playSoon = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private string? _id;
    private IReadOnlyList<ShowcaseEntry>? _items;
    private ShowcaseEntry? _running;
    private Image? _picture;
    private TextBlock? _cover;
    private Border? _chip;
    private TextBlock? _status;
    private ComboBox? _kinds;
    private ComboBox? _actions;
    private Button? _play;
    private Button? _stop;
    private TextBlock? _hint;
    private TextBlock[] _labels = [];
    private bool _filling;
    private bool _pressed;
    private Point _lastPoint = new(-1, -1);
    private ScrollViewer? _page;

    public AssistantTryView()
    {
        InitializeComponent();
        _noAnswer.Tick += (_, _) =>
        {
            _noAnswer.Stop();
            if (_id is { } id && _items is null) Message(Loc.T("assistantTry.noAnswer", App.AssistantName(id)), retry: true);
        };
        _coverDelay.Tick += (_, _) =>
        {
            _coverDelay.Stop();
            if (_cover is not null && _picture?.Source is not System.Windows.Media.Imaging.BitmapSource) _cover.Visibility = Visibility.Visible;
        };
        _playSoon.Tick += (_, _) =>
        {
            _playSoon.Stop();
            PlayPicked();
        };
        Loaded += (_, _) =>
        {
            _app.AssistantShowcaseListed += OnListed;
            _app.AssistantShowcaseDone += OnDone;
            _app.AssistantStageChanged += OnStageChanged;
            _app.AssistantStagePainted += OnPainted;
            _page = FindPage();
            if (_page is not null)
            {
                _page.SizeChanged += OnPageResized;
                _page.ScrollChanged += OnPageScrolled;
            }
            Fit();
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantShowcaseListed -= OnListed;
            _app.AssistantShowcaseDone -= OnDone;
            _app.AssistantStageChanged -= OnStageChanged;
            _app.AssistantStagePainted -= OnPainted;
            if (_page is not null)
            {
                _page.SizeChanged -= OnPageResized;
                _page.ScrollChanged -= OnPageScrolled;
            }
            _page = null;
            _noAnswer.Stop();
            Close();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Open();
            else Close();
            Fit();
        };
    }

    private void OnPageResized(object sender, SizeChangedEventArgs e) => Fit();

    private void OnPageScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource == _page && (e.ViewportWidthChange != 0 || e.ViewportHeightChange != 0)) Fit();
    }

    /// <summary>The scroll of the page it is in (Options).</summary>
    private ScrollViewer? FindPage()
    {
        for (var parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ScrollViewer scroll) return scroll;
        }
        return null;
    }

    /// <summary>
    /// As wide as the page, and the stage as tall as what is left of the page below its top once the lists under it have
    /// their room, so the page does not scroll. In a very small window, the page scrolls.
    /// </summary>
    private void Fit()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!IsVisible || _page?.Content is not FrameworkElement content || !content.IsAncestorOf(this))
            {
                Height = double.NaN;
                Width = double.NaN;
                return;
            }
            var at = TransformToAncestor(content).Transform(new Point(0, 0));
            // The sections of Opciones have their width set (up to 980); otherwise, the most they may take.
            var most = !double.IsNaN(content.Width) ? content.Width : double.IsInfinity(content.MaxWidth) ? double.MaxValue : content.MaxWidth;
            Width = Math.Max(0, Math.Min(most, _page.ViewportWidth) - at.X);
            if (_picture is null) return;
            Narrow(Width);
            // What goes under the stage (the lists and the hint), which the stage leaves room for.
            var below = Root.Children.OfType<FrameworkElement>().Where(c => Grid.GetRow(c) > 0).Sum(c => c.ActualHeight + c.Margin.Top + c.Margin.Bottom);
            _picture.MaxHeight = Math.Clamp(_page.ViewportHeight - at.Y - 28 - below - 2, MinPicture, StageHeight * MostZoom);
        });
    }

    /// <summary>
    /// What the row under the stage leaves out to fit in one line: the labels of the lists below 700 px, and the text of
    /// «Hacerlo» below 540 (its name stays as a tooltip).
    /// </summary>
    private void Narrow(double width)
    {
        foreach (var label in _labels) label.Visibility = width < 700 ? Visibility.Collapsed : Visibility.Visible;
        if (_play is not null) _play.Content = width < 540 ? null : Loc.T("assistantTry.play");
    }

    /// <summary>The assistant whose showcase it shows (its stage opens each time the tab is shown).</summary>
    public void Show(string id)
    {
        if (_id != id)
        {
            Close();
            _items = null;
            _running = null;
            _picture = null;
        }
        _id = id;
        if (IsVisible) Open();
    }

    private void Open()
    {
        if (_id is not { } id) return;
        if (!_app.OpenAssistantStage(id))
        {
            Message(Loc.T("assistantTry.notInstalled", App.AssistantName(id)), retry: false);
            return;
        }
        if (_picture is null) Layout();
        OnPainted();
        if (_items is not null) return;
        if (_app.AssistantStageReady) _app.AskAssistantShowcase(id);
        _noAnswer.Stop();
        _noAnswer.Start();
    }

    /// <summary>The tab is left: its stage stays a while, in case it comes back.</summary>
    private void Close()
    {
        _playSoon.Stop();
        if (_id is { } id) _app.LeaveAssistantStage(id);
    }

    private void OnStageChanged(string id, bool ready)
    {
        if (id != _id) return;
        if (ready)
        {
            if (IsVisible && !_app.AskAssistantShowcase(id)) Message(Loc.T("assistantTry.closed"), retry: true);
            return;
        }
        _noAnswer.Stop();
        _items = null;
        _running = null;
        // Closed while the tab was elsewhere (it waited too long): it opens again, quietly, when the tab is shown.
        if (!IsVisible)
        {
            _picture = null;
            return;
        }
        Message(Loc.T("assistantTry.closed"), retry: true);
    }

    private void OnPainted()
    {
        if (_picture is null || _app.AssistantStageId != _id || _app.AssistantStagePicture is not { } picture) return;
        if (_picture.Source != picture) _picture.Source = picture;
        _coverDelay.Stop();
        if (_cover is not null) _cover.Visibility = Visibility.Collapsed;
    }

    private void OnListed(ShowcaseListMessage list)
    {
        if (list.Assistant != _id || !IsVisible) return;
        _noAnswer.Stop();
        _items = list.Items;
        if (_picture is null) Layout();
        FillKinds();
    }

    private void OnDone(ShowcaseDoneMessage done)
    {
        if (done.Assistant != _id || _running is not { } running || running.Id != done.Item || running.Own != done.Own) return;
        _running = null;
        Say(Loc.T("assistantTry.result." + done.Result, App.ShowcaseName(running)));
        if (_stop is not null) _stop.IsEnabled = false;
    }

    /// <summary>Only a line (not installed, closed, no answer), and a button to open the stage again.</summary>
    private void Message(string text, bool retry)
    {
        Root.Children.Clear();
        Root.RowDefinitions.Clear();
        _picture = null;
        _cover = null;
        _chip = null;
        _status = null;
        _kinds = null;
        _actions = null;
        _play = null;
        _stop = null;
        _hint = null;
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = Loc.T("assistantTry.title") });
        stack.Children.Add(Caption(text));
        if (retry)
        {
            var again = new Button { Content = Loc.T("assistantTry.retry"), Icon = new SymbolIcon { Symbol = SymbolRegular.ArrowClockwise24 }, Margin = new Thickness(0, 10, 0, 0) };
            again.Click += (_, _) =>
            {
                _app.CloseAssistantStage();
                Open();
            };
            stack.Children.Add(again);
        }
        Root.Children.Add(new Border { Style = (Style)FindResource("Card"), Child = stack, VerticalAlignment = VerticalAlignment.Top });
    }

    /// <summary>The stage (its picture, with what it is doing over it) and, under it, the lists to pick what it does.</summary>
    private void Layout()
    {
        Root.Children.Clear();
        Root.RowDefinitions.Clear();
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Until the first picture comes, an empty one of its size keeps the frame's shape.
        var empty = new DrawingImage(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, StageWidth, StageHeight))));
        _picture = new Image
        {
            Stretch = Stretch.Uniform,
            MaxWidth = StageWidth * MostZoom,
            MaxHeight = StageHeight,
            Source = empty,
            Cursor = Cursors.Arrow,
        };
        RenderOptions.SetBitmapScalingMode(_picture, BitmapScalingMode.HighQuality);
        AutomationProperties.SetName(_picture, Loc.T("assistantTry.stage"));
        _picture.MouseMove += (_, e) => Point("cursor", e);
        _picture.MouseLeave += (_, _) =>
        {
            if (_pressed) return;
            _lastPoint = new Point(-1, -1);
            _app.PointAtStage(StageMessage.Leave, 0, 0);
        };
        _picture.MouseLeftButtonDown += (_, e) =>
        {
            _pressed = _picture.CaptureMouse();
            Point(StageMessage.Down, e);
        };
        _picture.MouseLeftButtonUp += (_, e) =>
        {
            Point(StageMessage.Up, e);
            _pressed = false;
            _picture.ReleaseMouseCapture();
        };

        var frame = new Grid();
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            BorderThickness = new Thickness(1),
            Child = frame,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };
        border.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        border.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
        frame.Children.Add(_picture);
        _cover = Caption(Loc.T("assistantTry.preparing"));
        _cover.HorizontalAlignment = HorizontalAlignment.Center;
        _cover.VerticalAlignment = VerticalAlignment.Center;
        _cover.Visibility = Visibility.Collapsed;
        frame.Children.Add(_cover);
        _status = new TextBlock { Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
        _chip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x10, 0x10, 0x14)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 4),
            Margin = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Child = _status,
        };
        frame.Children.Add(_chip);
        Root.Children.Add(border);
        _coverDelay.Stop();
        _coverDelay.Start();

        // Type · what it does · do it · stop · put the windows back, always in one row: the lists share what is left, and in
        // a narrow window the labels and then the text of «Hacerlo» give way (Narrow).
        var controls = new Grid { Margin = new Thickness(0, 12, 0, 8), MaxWidth = StageWidth * MostZoom };
        foreach (var width in new[] { GridLength.Auto, new GridLength(2, GridUnitType.Star), GridLength.Auto, new GridLength(3, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto })
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        controls.ColumnDefinitions[1].MinWidth = 100;
        controls.ColumnDefinitions[3].MinWidth = 130;
        controls.ColumnDefinitions[1].MaxWidth = 220;
        controls.ColumnDefinitions[3].MaxWidth = 300;
        _kinds = new ComboBox { IsEnabled = false, Margin = new Thickness(0, 0, 6, 0) };
        AutomationProperties.SetName(_kinds, Loc.T("assistantTry.kind"));
        _kinds.SelectionChanged += (_, _) => OnKindPicked(play: true);
        _actions = new ComboBox { IsEnabled = false, Margin = new Thickness(0, 0, 4, 0) };
        AutomationProperties.SetName(_actions, Loc.T("assistantTry.action"));
        _actions.SelectionChanged += (_, _) =>
        {
            if (_filling) return;
            Remember();
            // Moving through the list with the arrows does only the one it rests on.
            _playSoon.Stop();
            _playSoon.Start();
        };
        _play = new Button { Content = Loc.T("assistantTry.play"), Icon = new SymbolIcon { Symbol = SymbolRegular.Play24 }, Appearance = ControlAppearance.Primary, IsEnabled = false, Margin = new Thickness(4, 0, 0, 0) };
        AutomationProperties.SetName(_play, Loc.T("assistantTry.play"));
        _play.ToolTip = Loc.T("assistantTry.play");
        _play.Click += (_, _) => PlayPicked();
        _stop = IconButton(SymbolRegular.Stop24, Loc.T("assistantTry.stop"));
        _stop.IsEnabled = false;
        _stop.Click += (_, _) =>
        {
            if (_id is { } who) _app.StopAssistantShowcase(who);
        };
        var reset = IconButton(SymbolRegular.WindowArrowUp24, Loc.T("assistantTry.reset"));
        reset.Click += (_, _) => _app.ResetAssistantStage();
        _labels = [Label(Loc.T("assistantTry.kind"), 0), Label(Loc.T("assistantTry.action"), 12)];
        Place(_labels[0], 0);
        Place(_kinds, 1);
        Place(_labels[1], 2);
        Place(_actions, 3);
        Place(_play, 4);
        Place(_stop, 5);
        Place(reset, 6);
        Grid.SetRow(controls, 1);
        Root.Children.Add(controls);

        _hint = Caption(Loc.T("assistantTry.hint", _id is { } id ? App.AssistantName(id) : ""));
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _hint.TextAlignment = TextAlignment.Center;
        _hint.Margin = new Thickness(0, 2, 0, 0);
        Grid.SetRow(_hint, 2);
        Root.Children.Add(_hint);
        Fit();

        void Place(UIElement element, int column)
        {
            Grid.SetColumn(element, column);
            controls.Children.Add(element);
        }

        static Button IconButton(SymbolRegular symbol, string name)
        {
            var button = new Button { Icon = new SymbolIcon { Symbol = symbol }, ToolTip = name, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(9, 6, 9, 6) };
            AutomationProperties.SetName(button, name);
            return button;
        }

        static TextBlock Label(string text, double left)
        {
            var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(left, 0, 8, 0) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            return label;
        }
    }

    /// <summary>One entry of the list of types: a group of its own animations or of the host's.</summary>
    private sealed record Kind(string Key, string Label, List<ShowcaseEntry> Entries)
    {
        public override string ToString() => Label;
    }

    /// <summary>The types it has (its own groups as they come, then the host's), with a heading for each half when there are both.</summary>
    private void FillKinds()
    {
        if (_items is not { } items || _kinds is null) return;
        var own = items.Where(i => i.Own).GroupBy(i => i.Group)
            .Select(g => new Kind("own:" + g.Key, App.ShowcaseGroupName(g.Key), g.ToList())).ToList();
        var host = HostGroups.Select(g => new Kind("host:" + g, App.ShowcaseGroupName(g), items.Where(i => !i.Own && i.Group == g).ToList()))
            .Where(k => k.Entries.Count > 0).ToList();
        _filling = true;
        _kinds.Items.Clear();
        var both = own.Count > 0 && host.Count > 0;
        Add(Loc.T("assistantTry.own"), own);
        Add(Loc.T("assistantTry.host"), host);
        _kinds.IsEnabled = _kinds.Items.Count > 0;
        var picked = _id is { } id && Picked.TryGetValue(id, out var p) ? p.Kind : null;
        var choose = _kinds.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is Kind k && k.Key == picked)
            ?? _kinds.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is Kind);
        _kinds.SelectedItem = choose;
        _filling = false;
        OnKindPicked();

        void Add(string heading, List<Kind> kinds)
        {
            if (kinds.Count == 0) return;
            if (both)
            {
                var head = new ComboBoxItem { Content = heading, IsEnabled = false, FontWeight = FontWeights.SemiBold, FontSize = 12 };
                _kinds.Items.Add(head);
            }
            foreach (var kind in kinds)
                _kinds.Items.Add(new ComboBoxItem { Content = kind.Label, Tag = kind, Padding = new Thickness(both ? 22 : 10, 6, 10, 6) });
        }
    }

    /// <summary>
    /// A type was picked: its things in the second list (the one picked before, if it is there). Picked by the user
    /// (<paramref name="play"/>), it does the one that list shows, as picking it there does.
    /// </summary>
    private void OnKindPicked(bool play = false)
    {
        if (_filling || _actions is null || _kinds?.SelectedItem is not ComboBoxItem { Tag: Kind kind }) return;
        _filling = true;
        _actions.Items.Clear();
        foreach (var entry in kind.Entries)
        {
            var name = App.ShowcaseName(entry);
            var item = new ComboBoxItem { Content = name, Tag = entry };
            AutomationProperties.SetName(item, name);
            _actions.Items.Add(item);
        }
        var picked = _id is { } id && Picked.TryGetValue(id, out var p) && p.Kind == kind.Key ? p.Item : null;
        _actions.SelectedItem = _actions.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is ShowcaseEntry e && Key(e) == picked)
            ?? _actions.Items.OfType<ComboBoxItem>().FirstOrDefault();
        _actions.IsEnabled = _actions.Items.Count > 0;
        if (_play is not null) _play.IsEnabled = _actions.Items.Count > 0;
        _filling = false;
        Remember();
        if (play && _actions.Items.Count > 0)
        {
            _playSoon.Stop();
            _playSoon.Start();
        }
    }

    private void Remember()
    {
        if (_id is { } id && _kinds?.SelectedItem is ComboBoxItem { Tag: Kind kind } && _actions?.SelectedItem is ComboBoxItem { Tag: ShowcaseEntry entry })
            Picked[id] = (kind.Key, Key(entry));
    }

    private void PlayPicked()
    {
        if (_actions?.SelectedItem is ComboBoxItem { Tag: ShowcaseEntry entry }) Play(entry);
    }

    /// <summary>The pointer over the picture, in pixels of the stage.</summary>
    private void Point(string op, MouseEventArgs e)
    {
        if (_picture?.Source is not System.Windows.Media.Imaging.BitmapSource source || _picture.ActualWidth < 1) return;
        var at = e.GetPosition(_picture);
        var x = Math.Clamp((int)(at.X * source.PixelWidth / _picture.ActualWidth), 0, source.PixelWidth - 1);
        var y = Math.Clamp((int)(at.Y * source.PixelHeight / _picture.ActualHeight), 0, source.PixelHeight - 1);
        if (op == "cursor" && _lastPoint == new Point(x, y)) return;
        _lastPoint = new Point(x, y);
        _app.PointAtStage(op, x, y);
    }

    private void Play(ShowcaseEntry entry)
    {
        if (_id is not { } id || !_app.PlayAssistantShowcase(id, entry))
        {
            Message(Loc.T("assistantTry.closed"), retry: true);
            return;
        }
        _running = entry;
        Say(Loc.T("assistantTry.playing", App.ShowcaseName(entry)));
        if (_stop is not null) _stop.IsEnabled = true;
    }

    /// <summary>What it is doing, or how it went, over the top left corner of the stage.</summary>
    private void Say(string text)
    {
        if (_status is null || _chip is null) return;
        _status.Text = text;
        _chip.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string Key(ShowcaseEntry entry) => (entry.Own ? "own:" : "host:") + entry.Id;

    private TextBlock Caption(string text) => new() { Style = (Style)FindResource("Caption"), Text = text, TextWrapping = TextWrapping.Wrap };
}

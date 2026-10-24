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
/// its own, with pretend windows, drawn off the screen and shown here live (the mouse reaches it: it can be petted) — and a
/// button for each thing it can show: its own animations first, then the host's entrances, exits, states, reactions,
/// gestures and what it does to a window. One at a time (another one, or «Parar», stops it). The stage is open while the
/// tab is shown. Made in code. The stage stays in sight: the view is as tall as what is left of the page below its top, and the
/// buttons scroll on their own under the stage (<see cref="Fit"/>).
/// </summary>
public partial class AssistantTryView : UserControl
{
    /// <summary>The host's groups, in this order after its own ones.</summary>
    private static readonly string[] HostGroups = ["entrances", "exits", "states", "reactions", "gestures", "windows"];

    /// <summary>Height of the stage (pixels of its picture, as big as it is shown).</summary>
    private const double StageHeight = 405;

    /// <summary>The stage is never smaller than this (smaller, it is hard to see what it does).</summary>
    private const double MinPicture = 300;

    /// <summary>Room the stage leaves to the buttons below it if it can (about three rows), and the least they get.</summary>
    private const double ButtonsRoom = 150;

    private const double MinButtons = 120;

    private readonly App _app = App.Current;
    private readonly DispatcherTimer _noAnswer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
    private string? _id;
    private IReadOnlyList<ShowcaseEntry>? _items;
    private ShowcaseEntry? _running;
    private TextBlock? _status;
    private Button? _stop;
    private Image? _picture;
    private TextBlock? _cover;
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
        // Only the page's own (the one of the buttons bubbles up too).
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
    /// As wide as the page and as tall as what is left of it below its top, so the page does not scroll and the stage stays in sight; the buttons
    /// scroll on their own. The stage shrinks to leave them room (<see cref="ButtonsRoom"/>); in a very small window, the page
    /// scrolls.
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
            var top = at.Y;

            // As wide as the page lets it: the sections are as wide as their content (and its texts would not wrap).
            var most = double.IsInfinity(content.MaxWidth) ? double.MaxValue : content.MaxWidth;
            Width = Math.Max(0, Math.Min(most, _page.ViewportWidth) - at.X);
            var height = _page.ViewportHeight - top - 28;
            if (_picture is not null)
            {
                // What the stage card takes besides its picture, and the picture as big as it can be leaving room for the buttons.
                var chrome = Body.ActualHeight - _picture.ActualHeight;
                _picture.MaxHeight = Math.Clamp(height - chrome - ButtonsRoom, MinPicture, StageHeight);
                Body.UpdateLayout();
            }
            Height = Math.Max(height, Body.ActualHeight + MinButtons);
        });
    }

    /// <summary>The assistant whose showcase it shows (its stage opens each time the tab is shown).</summary>
    public void Show(string id)
    {
        if (_id != id) Close();
        _id = id;
        if (IsVisible) Open();
    }

    private void Open()
    {
        if (_id is not { } id) return;
        if (_app.AssistantStageId == id && _items is not null) return;
        _items = null;
        _running = null;
        if (!_app.OpenAssistantStage(id))
        {
            Message(Loc.T("assistantTry.notInstalled", App.AssistantName(id)), retry: false);
            return;
        }
        Stage(Loc.T("assistantTry.preparing"));
        _noAnswer.Stop();
        _noAnswer.Start();
    }

    private void Close()
    {
        if (_id is { } id && _app.AssistantStageId == id) _app.CloseAssistantStage();
        _items = null;
    }

    private void OnStageChanged(string id, bool ready)
    {
        if (id != _id || !IsVisible) return;
        if (ready)
        {
            if (!_app.AskAssistantShowcase(id)) Message(Loc.T("assistantTry.closed"), retry: true);
            return;
        }
        _noAnswer.Stop();
        _items = null;
        _running = null;
        Message(Loc.T("assistantTry.closed"), retry: true);
    }

    private void OnPainted()
    {
        if (_picture is null || _app.AssistantStageId != _id) return;
        if (_picture.Source != _app.AssistantStagePicture)
        {
            _picture.Source = _app.AssistantStagePicture;
            Fit();
        }
        if (_cover is not null) _cover.Visibility = Visibility.Collapsed;
    }

    private void OnListed(ShowcaseListMessage list)
    {
        if (list.Assistant != _id || !IsVisible) return;
        _noAnswer.Stop();
        _items = list.Items;
        Build();
    }

    private void OnDone(ShowcaseDoneMessage done)
    {
        if (done.Assistant != _id || _running is not { } running || running.Id != done.Item || running.Own != done.Own) return;
        _running = null;
        Mark(null);
        if (_status is not null) _status.Text = Loc.T("assistantTry.result." + done.Result, App.ShowcaseName(running));
        if (_stop is not null) _stop.IsEnabled = false;
    }

    /// <summary>A card with only a line (not installed, closed, no answer), and a button to open the stage again.</summary>
    private void Message(string text, bool retry)
    {
        Body.Children.Clear();
        List.Children.Clear();
        _buttons.Clear();
        _status = null;
        _stop = null;
        _picture = null;
        _cover = null;
        var (card, stack) = Card(Loc.T("assistantTry.title"));
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
        Body.Children.Add(card);
    }

    /// <summary>The stage's card (its picture, what it is doing, «Parar»), with a line over the picture until it comes.</summary>
    private void Stage(string? waiting)
    {
        Body.Children.Clear();
        List.Children.Clear();
        _buttons.Clear();
        // Title, what it is doing and the buttons in one line: the room is for the picture.
        var top = new StackPanel();
        var head = new Border { Style = (Style)FindResource("Card"), Child = top };
        var line = new DockPanel();
        top.Children.Add(line);
        var intro = Caption(Loc.T("assistantTry.intro", _id is { } id ? App.AssistantName(id) : ""));
        intro.Margin = new Thickness(0, 2, 0, 10);
        top.Children.Add(intro);

        _picture = new Image
        {
            Stretch = Stretch.Uniform,
            MaxWidth = 720,
            Source = _app.AssistantStageId == _id ? _app.AssistantStagePicture : null,
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
        var frame = new Grid { MinHeight = 120, HorizontalAlignment = HorizontalAlignment.Left };
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = _picture,
            BorderThickness = new Thickness(1),
        };
        border.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        border.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
        frame.Children.Add(border);
        _cover = Caption(waiting ?? "");
        _cover.HorizontalAlignment = HorizontalAlignment.Center;
        _cover.VerticalAlignment = VerticalAlignment.Center;
        _cover.Visibility = waiting is null || _picture.Source is not null ? Visibility.Collapsed : Visibility.Visible;
        frame.Children.Add(_cover);
        if (_picture.Source is null) border.MinWidth = 480;
        top.Children.Add(frame);

        _stop = new Button
        {
            Content = Loc.T("assistantTry.stop"),
            Icon = new SymbolIcon { Symbol = SymbolRegular.Stop24 },
            IsEnabled = false,
            Margin = new Thickness(8, 0, 0, 0),
        };
        _stop.Click += (_, _) =>
        {
            if (_id is { } who) _app.StopAssistantShowcase(who);
        };
        var reset = new Button
        {
            Content = Loc.T("assistantTry.reset"),
            Icon = new SymbolIcon { Symbol = SymbolRegular.WindowArrowUp24 },
            Margin = new Thickness(12, 0, 0, 0),
        };
        reset.Click += (_, _) => _app.ResetAssistantStage();
        DockPanel.SetDock(_stop, Dock.Right);
        DockPanel.SetDock(reset, Dock.Right);
        line.Children.Add(_stop);
        line.Children.Add(reset);
        var title = new TextBlock { Style = (Style)FindResource("CardTitle"), Text = Loc.T("assistantTry.title"), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(title, Dock.Left);
        line.Children.Add(title);
        _status = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        line.Children.Add(_status);
        Body.Children.Add(head);
        Fit();
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

    private void Build()
    {
        if (_items is not { } items) return;
        Stage(null);
        // Its own ones first, in the order of their groups as they come; then the host's. A row per group.
        var own = items.Where(i => i.Own).GroupBy(i => i.Group).Select(g => (g.Key, g.ToList())).ToList();
        var host = HostGroups.Select(g => (g, items.Where(i => !i.Own && i.Group == g).ToList())).ToList();
        Groups(Loc.T("assistantTry.own"), own);
        Groups(Loc.T("assistantTry.host"), host);
        ListScroll.ScrollToTop();
    }

    private void Groups(string title, IReadOnlyList<(string Group, List<ShowcaseEntry> Entries)> groups)
    {
        if (groups.All(g => g.Entries.Count == 0)) return;
        var (card, stack) = Card(title);
        foreach (var (group, entries) in groups)
        {
            if (entries.Count == 0) continue;
            var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = Caption(App.ShowcaseGroupName(group));
            label.Margin = new Thickness(0, 7, 12, 0);
            row.Children.Add(label);
            var wrap = new WrapPanel();
            foreach (var entry in entries) wrap.Children.Add(ButtonFor(entry));
            Grid.SetColumn(wrap, 1);
            row.Children.Add(wrap);
            stack.Children.Add(row);
        }
        List.Children.Add(card);
    }

    private Button ButtonFor(ShowcaseEntry entry)
    {
        var name = App.ShowcaseName(entry);
        var button = new Button
        {
            Content = name,
            Icon = new SymbolIcon { Symbol = SymbolRegular.Play24 },
            Margin = new Thickness(0, 0, 8, 8),
        };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => Play(entry);
        _buttons[Key(entry)] = button;
        return button;
    }

    private void Play(ShowcaseEntry entry)
    {
        if (_id is not { } id || !_app.PlayAssistantShowcase(id, entry))
        {
            Message(Loc.T("assistantTry.closed"), retry: true);
            return;
        }
        _running = entry;
        Mark(entry);
        if (_status is not null) _status.Text = Loc.T("assistantTry.playing", App.ShowcaseName(entry));
        if (_stop is not null) _stop.IsEnabled = true;
    }

    /// <summary>The one playing stands out.</summary>
    private void Mark(ShowcaseEntry? playing)
    {
        foreach (var (key, button) in _buttons)
            button.Appearance = playing is not null && key == Key(playing) ? ControlAppearance.Primary : ControlAppearance.Secondary;
    }

    private static string Key(ShowcaseEntry entry) => (entry.Own ? "own:" : "host:") + entry.Id;

    private (Border Card, StackPanel Content) Card(string title)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Style = (Style)FindResource("CardTitle"), Text = title });
        return (new Border { Style = (Style)FindResource("Card"), Child = stack }, stack);
    }

    private TextBlock Caption(string text) => new() { Style = (Style)FindResource("Caption"), Text = text, TextWrapping = TextWrapping.Wrap };
}

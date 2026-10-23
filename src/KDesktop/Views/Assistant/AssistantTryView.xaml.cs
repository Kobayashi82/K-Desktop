using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Assistant;

/// <summary>
/// The «Probar» tab of an installed assistant's card (stage 13.9): a button for each thing it can show — its own
/// animations first, then the host's entrances, exits, states, reactions and gestures — and it does it now on the desktop.
/// One at a time (another one, or «Parar», stops it). Only while it is on the desktop. Made in code.
/// </summary>
public partial class AssistantTryView : UserControl
{
    /// <summary>The host's groups, in this order after its own ones.</summary>
    private static readonly string[] HostGroups = ["entrances", "exits", "states", "reactions", "gestures"];

    private readonly App _app = App.Current;
    private readonly DispatcherTimer _noAnswer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
    private string? _id;
    private bool _loaded;
    private IReadOnlyList<ShowcaseEntry>? _items;
    private ShowcaseEntry? _running;
    private TextBlock? _status;
    private Button? _stop;

    public AssistantTryView()
    {
        InitializeComponent();
        _noAnswer.Tick += (_, _) =>
        {
            _noAnswer.Stop();
            if (_id is { } id && _items is null) Message(Loc.T("assistantTry.noAnswer", App.AssistantName(id)));
        };
        Loaded += (_, _) =>
        {
            _app.AssistantShowcaseListed += OnListed;
            _app.AssistantShowcaseDone += OnDone;
            _app.AssistantChanged += OnAssistantChanged;
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantShowcaseListed -= OnListed;
            _app.AssistantShowcaseDone -= OnDone;
            _app.AssistantChanged -= OnAssistantChanged;
            _noAnswer.Stop();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Refresh();
        };
    }

    /// <summary>The assistant whose showcase it shows (asked again each time the tab is shown).</summary>
    public void Show(string id)
    {
        _id = id;
        _items = null;
        _running = null;
        if (IsVisible) Refresh();
    }

    /// <summary>Put on the desktop or hidden: what it can show is asked again.</summary>
    private void OnAssistantChanged()
    {
        if (_id is not { } id || !IsVisible) return;
        var loaded = _app.IsAssistantLoaded(id);
        if (loaded == _loaded) return;
        Refresh();
    }

    private void Refresh()
    {
        if (_id is not { } id) return;
        _loaded = _app.IsAssistantLoaded(id);
        _items = null;
        _running = null;
        if (!_loaded || !_app.AskAssistantShowcase(id))
        {
            Message(Loc.T("assistantTry.notOnDesktop", App.AssistantName(id)));
            return;
        }
        Message(Loc.T("assistantTry.loading"));
        _noAnswer.Stop();
        _noAnswer.Start();
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

    /// <summary>A card with only a line (not on the desktop, asking, no answer).</summary>
    private void Message(string text)
    {
        Body.Children.Clear();
        _buttons.Clear();
        _status = null;
        _stop = null;
        var (card, stack) = Card(Loc.T("assistantTry.title"));
        stack.Children.Add(Caption(text));
        Body.Children.Add(card);
    }

    private void Build()
    {
        if (_id is not { } id || _items is not { } items) return;
        Body.Children.Clear();
        _buttons.Clear();

        var (head, top) = Card(Loc.T("assistantTry.title"));
        var intro = Caption(Loc.T("assistantTry.intro", App.AssistantName(id)));
        intro.Margin = new Thickness(0, 4, 0, 10);
        top.Children.Add(intro);
        var line = new DockPanel();
        _stop = new Button
        {
            Content = Loc.T("assistantTry.stop"),
            Icon = new SymbolIcon { Symbol = SymbolRegular.Stop24 },
            IsEnabled = false,
            Margin = new Thickness(12, 0, 0, 0),
        };
        _stop.Click += (_, _) =>
        {
            if (_id is { } who) _app.StopAssistantShowcase(who);
        };
        DockPanel.SetDock(_stop, Dock.Right);
        line.Children.Add(_stop);
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(_status);
        top.Children.Add(line);
        Body.Children.Add(head);

        // Its own ones first, in the order of their groups as they come; then the host's. A row per group.
        var own = items.Where(i => i.Own).GroupBy(i => i.Group).Select(g => (g.Key, g.ToList())).ToList();
        var host = HostGroups.Select(g => (g, items.Where(i => !i.Own && i.Group == g).ToList())).ToList();
        Groups(Loc.T("assistantTry.own"), own);
        Groups(Loc.T("assistantTry.host"), host);
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
        Body.Children.Add(card);
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
            Refresh();
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

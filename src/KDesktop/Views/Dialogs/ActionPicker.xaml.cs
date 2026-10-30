using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Dialogs;

/// <summary>Something that can be chosen (an action, a kind of event): its icon, name and what it does.</summary>
public sealed class ActionChoice(string name, string description, string symbol, object value)
{
    public string Name { get; } = name;
    public string Description { get; } = description;
    public SymbolRegular Symbol { get; } = Enum.TryParse<SymbolRegular>(symbol, out var s) ? s : SymbolRegular.Flash24;
    public object Value { get; } = value;
}

/// <summary>A category (or «Todas»), with the choices it shows now (after the search).</summary>
public sealed class ActionCategory(string? key, string name, SymbolRegular symbol, IReadOnlyList<ActionChoice> all)
{
    public string? Key { get; } = key;
    public string Name { get; } = name;
    public SymbolRegular Symbol { get; } = symbol;
    public IReadOnlyList<ActionChoice> All { get; } = all;
    public IReadOnlyList<ActionChoice> Shown { get; set; } = all;
}

/// <summary>
/// «Añadir acción»: every action with what it does, by category, with a search. A click adds it. The same window chooses
/// when an event runs (Eventos → «Cuándo»).
/// </summary>
public partial class ActionPicker : FluentWindow
{
    private readonly List<ActionCategory> _categories;
    private readonly bool _events;
    private static string? _lastCategory, _lastEventCategory;

    public object? Chosen { get; private set; }

    private ActionPicker(List<ActionCategory> categories, bool events)
    {
        InitializeComponent();
        _categories = categories;
        _events = events;
        if (events)
        {
            Bar.Title = Loc.T("event.when");
            SearchBox.PlaceholderText = Loc.T("eventPicker.search");
            NothingFound.Text = Loc.T("eventPicker.none");
        }
        var all = new ActionCategory(null, Loc.T("actionPicker.all"), SymbolRegular.Grid24, []);
        Categories.ItemsSource = new[] { all }.Concat(_categories).ToList();
        var last = events ? _lastEventCategory : _lastCategory;
        Categories.SelectedItem = _categories.FirstOrDefault(c => c.Key == last) ?? (object)all;
        Loaded += (_, _) => SearchBox.Focus();
    }

    /// <summary>Lets the user choose an action; null when closed without choosing.</summary>
    public static ActionInfo? Pick(Window? owner)
    {
        // The assistant module's actions only while it is on (off, K-Desktop is the one of always).
        var assistant = App.Current.Config.Settings.AssistantEnabled;
        var categories = ActionCatalog.All.Where(a => assistant || !a.NeedsAssistant).GroupBy(a => a.CategoryKey)
            .Select(g => new ActionCategory(g.Key, g.First().Category, CategorySymbol(g.Key),
                g.Select(a => new ActionChoice(a.Name, a.Description, a.Symbol, a)).ToList()))
            .ToList();
        var dlg = new ActionPicker(categories, events: false) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.Chosen as ActionInfo : null;
    }

    /// <summary>Lets the user choose when an event runs; null when closed without choosing.</summary>
    public static EventKindInfo? PickEventKind(Window? owner)
    {
        var categories = EventGroups
            .Select(g => new ActionCategory(g.Key, Loc.T("eventPicker.cat." + g.Key), g.Symbol,
                g.Kinds.Select(EventCatalog.Get).Select(k => new ActionChoice(k.Name, k.Description, k.Symbol, k)).ToList()))
            .ToList();
        var dlg = new ActionPicker(categories, events: true) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.Chosen as EventKindInfo : null;
    }

    /// <summary>The kinds of event by what they watch (every kind in one of them).</summary>
    private static readonly (string Key, SymbolRegular Symbol, EventKind[] Kinds)[] EventGroups =
    [
        ("kdesktop", SymbolRegular.Flash24, [EventKind.AppStart, EventKind.AppExit, EventKind.RemoteCall]),
        ("time", SymbolRegular.Clock24, [EventKind.Time, EventKind.Interval, EventKind.Calendar, EventKind.Sun, EventKind.Idle]),
        ("programs", SymbolRegular.WindowApps24, [EventKind.ProcessStart, EventKind.Foreground]),
        ("system", SymbolRegular.Settings24, [EventKind.Session, EventKind.PowerSource, EventKind.Battery, EventKind.Load, EventKind.Network]),
        ("devices", SymbolRegular.UsbStick24, [EventKind.Gadget, EventKind.AudioDevice, EventKind.Display, EventKind.Device]),
        ("files", SymbolRegular.Document24, [EventKind.File]),
    ];

    private static SymbolRegular CategorySymbol(string key) => key switch
    {
        "apps" => SymbolRegular.Open24,
        "windows" => SymbolRegular.WindowApps24,
        "input" => SymbolRegular.Keyboard24,
        "sound" => SymbolRegular.Speaker224,
        "screen" => SymbolRegular.Desktop24,
        "network" => SymbolRegular.Globe24,
        "calendar" => SymbolRegular.CalendarLtr24,
        "system" => SymbolRegular.Settings24,
        "assistant" => SymbolRegular.Bot24,
        _ => SymbolRegular.Flash24,
    };

    private void OnCategory(object sender, SelectionChangedEventArgs e)
    {
        if (Categories.SelectedItem is ActionCategory c)
        {
            if (_events) _lastEventCategory = c.Key;
            else _lastCategory = c.Key;
        }
        Refresh();
    }

    private void OnSearch(object sender, TextChangedEventArgs e) => Refresh();

    /// <summary>The categories to show (the chosen one, or all) with the choices that match the search.</summary>
    private void Refresh()
    {
        if (Categories.SelectedItem is not ActionCategory chosen) return;
        var tokens = FuzzyMatch.Tokens(SearchBox.Text);
        // While searching, every category: what is looked for may not be in the chosen one.
        var groups = tokens.Length > 0 || chosen.Key is null ? _categories : [chosen];
        foreach (var g in groups)
            g.Shown = tokens.Length == 0 ? g.All
                : g.All.Where(a => FuzzyMatch.Score(tokens, new SearchKey(a.Name)) > 0 || Contains(a.Description, SearchBox.Text)).ToList();
        var visible = groups.Where(g => g.Shown.Count > 0).ToList();
        Groups.ItemsSource = null;
        Groups.ItemsSource = visible;
        NothingFound.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Scroll.ScrollToTop();
    }

    private static bool Contains(string text, string query) =>
        query.Trim().Length > 2 && text.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase);

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                // The first one shown.
                if (Groups.ItemsSource is IEnumerable<ActionCategory> groups && groups.SelectMany(g => g.Shown).FirstOrDefault() is { } first)
                    Choose(first);
                e.Handled = true;
                break;
            case Key.Escape:
                DialogResult = false;
                break;
        }
    }

    private void OnPick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ActionChoice choice }) Choose(choice);
    }

    private void Choose(ActionChoice choice)
    {
        Chosen = choice.Value;
        DialogResult = true;
    }
}

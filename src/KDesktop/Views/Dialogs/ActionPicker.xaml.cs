using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Dialogs;

/// <summary>An action that can be added: its icon, name and what it does.</summary>
public sealed class ActionChoice(ActionInfo info)
{
    public ActionInfo Info { get; } = info;
    public string Name => Info.Name;
    public string Description => Info.Description;
    public SymbolRegular Symbol { get; } = Enum.TryParse<SymbolRegular>(info.Symbol, out var s) ? s : SymbolRegular.Flash24;
}

/// <summary>A category of actions (or «Todas»), with the actions it shows now (after the search).</summary>
public sealed class ActionCategory(string? key, string name, SymbolRegular symbol, IReadOnlyList<ActionChoice> all)
{
    public string? Key { get; } = key;
    public string Name { get; } = name;
    public SymbolRegular Symbol { get; } = symbol;
    public IReadOnlyList<ActionChoice> All { get; } = all;
    public IReadOnlyList<ActionChoice> Shown { get; set; } = all;
}

/// <summary>
/// «Añadir acción»: every action with what it does, by category, with a search. A click adds it.
/// </summary>
public partial class ActionPicker : FluentWindow
{
    private readonly List<ActionCategory> _categories;
    private static string? _lastCategory;

    public ActionInfo? Chosen { get; private set; }

    private ActionPicker()
    {
        InitializeComponent();
        _categories = ActionCatalog.All.GroupBy(a => a.CategoryKey)
            .Select(g => new ActionCategory(g.Key, g.First().Category, CategorySymbol(g.Key), g.Select(a => new ActionChoice(a)).ToList()))
            .ToList();
        var all = new ActionCategory(null, Loc.T("actionPicker.all"), SymbolRegular.Grid24, []);
        Categories.ItemsSource = new[] { all }.Concat(_categories).ToList();
        Categories.SelectedItem = _categories.FirstOrDefault(c => c.Key == _lastCategory) ?? (object)all;
        Loaded += (_, _) => SearchBox.Focus();
    }

    /// <summary>Lets the user choose an action; null when closed without choosing.</summary>
    public static ActionInfo? Pick(Window? owner)
    {
        var dlg = new ActionPicker { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.Chosen : null;
    }

    private static SymbolRegular CategorySymbol(string key) => key switch
    {
        "apps" => SymbolRegular.Open24,
        "windows" => SymbolRegular.WindowApps24,
        "input" => SymbolRegular.Keyboard24,
        "sound" => SymbolRegular.Speaker224,
        "screen" => SymbolRegular.Desktop24,
        "network" => SymbolRegular.Globe24,
        "system" => SymbolRegular.Settings24,
        _ => SymbolRegular.Flash24,
    };

    private void OnCategory(object sender, SelectionChangedEventArgs e)
    {
        if (Categories.SelectedItem is ActionCategory c) _lastCategory = c.Key;
        Refresh();
    }

    private void OnSearch(object sender, TextChangedEventArgs e) => Refresh();

    /// <summary>The categories to show (the chosen one, or all) with the actions that match the search.</summary>
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
                    Choose(first.Info);
                e.Handled = true;
                break;
            case Key.Escape:
                DialogResult = false;
                break;
        }
    }

    private void OnPick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ActionChoice choice }) Choose(choice.Info);
    }

    private void Choose(ActionInfo info)
    {
        Chosen = info;
        DialogResult = true;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Dialogs;

/// <summary>Searchable list to pick one or more existing commands (or one event).</summary>
public partial class CommandPicker : FluentWindow
{
    private readonly List<object> _all;

    public IReadOnlyList<object> Selected { get; private set; } = [];

    private CommandPicker(IEnumerable<object> items, bool single)
    {
        InitializeComponent();
        _all = items.OrderBy(NameOf, StringComparer.CurrentCultureIgnoreCase).ToList();
        List.SelectionMode = single ? SelectionMode.Single : SelectionMode.Extended;
        if (single) AcceptButton.Content = Loc.T("common.accept");
        ShowHiddenToggle.IsChecked = App.Current.Config.Settings.ShowHiddenCommands;
        Refill();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private static string NameOf(object item) => item switch
    {
        Command c => c.Name,
        EventRule r => r.Name,
        _ => string.Empty,
    };

    /// <summary>One command, for a box that holds one, with <paramref name="current"/> marked (null: cancelled).</summary>
    public static Command? PickOne(Window? owner, IEnumerable<Command> commands, string? current = null)
    {
        var dlg = new CommandPicker(commands, single: true) { Owner = owner };
        dlg.Bar.Title = Loc.T("editor.pickCommand");
        dlg.Mark(current);
        return dlg.ShowDialog() == true ? dlg.Selected.OfType<Command>().FirstOrDefault() : null;
    }

    /// <summary>One event, with <paramref name="current"/> marked (null: cancelled).</summary>
    public static EventRule? PickEvent(Window? owner, IEnumerable<EventRule> events, string? current = null)
    {
        var dlg = new CommandPicker(events, single: true) { Owner = owner };
        dlg.Bar.Title = Loc.T("editor.pickEvent");
        dlg.ShowHiddenToggle.Visibility = Visibility.Collapsed;
        dlg.SearchBox.PlaceholderText = Loc.T("event.search");
        dlg.Mark(current);
        return dlg.ShowDialog() == true ? dlg.Selected.OfType<EventRule>().FirstOrDefault() : null;
    }

    /// <summary>The one already chosen starts selected (when it is in the list).</summary>
    private void Mark(string? id)
    {
        var item = List.Items.OfType<object>().FirstOrDefault(i => i is Command c && c.Id == id || i is EventRule r && r.Id == id);
        if (item is null) return;
        List.SelectedItem = item;
        List.ScrollIntoView(item);
    }

    private bool _wantsNew;

    /// <summary>The commands picked; with <paramref name="onNew"/>, a «Nuevo comando» button that closes it and runs that instead.</summary>
    public static IReadOnlyList<Command> Pick(Window? owner, IEnumerable<Command> commands, Action? onNew = null)
    {
        var dlg = new CommandPicker(commands, single: false) { Owner = owner };
        if (onNew is not null) dlg.NewButton.Visibility = Visibility.Visible;
        if (dlg.ShowDialog() == true) return dlg.Selected.OfType<Command>().ToList();
        if (dlg._wantsNew) onNew?.Invoke();
        return [];
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        _wantsNew = true;
        DialogResult = false;
    }

    private void OnSearch(object sender, TextChangedEventArgs e) => Refill();

    /// <summary>The hidden commands too (the same switch as in Comandos).</summary>
    private void OnShowHidden(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        App.Current.Config.Settings.ShowHiddenCommands = ShowHiddenToggle.IsChecked == true;
        Refill();
    }

    private void Refill()
    {
        var shown = ShowHiddenToggle.IsChecked == true ? _all : _all.Where(c => c is not Command { Hidden: true }).ToList();
        // Like the launcher: initials, loose letters, no accents; the best matches first.
        var tokens = KDesktop.Core.Engine.FuzzyMatch.Tokens(SearchBox.Text);
        List.ItemsSource = tokens.Length == 0 ? shown
            : shown.Select(c => (Item: c, Score: KDesktop.Core.Engine.FuzzyMatch.Score(tokens, new KDesktop.Core.Engine.SearchKey(NameOf(c)))))
                  .Where(x => x.Score > 0).OrderByDescending(x => x.Score).Select(x => x.Item).ToList();
        if (List.Items.Count > 0) List.SelectedIndex = 0;
    }

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when List.Items.Count > 0:
                List.SelectedIndex = Math.Min(List.SelectedIndex + 1, List.Items.Count - 1);
                List.ScrollIntoView(List.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when List.Items.Count > 0:
                List.SelectedIndex = Math.Max(List.SelectedIndex - 1, 0);
                List.ScrollIntoView(List.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                OnAccept(sender, e);
                e.Handled = true;
                break;
            case Key.Escape:
                OnCancel(sender, e);
                break;
        }
    }

    private void OnListKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnAccept(sender, e);
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Selected = List.SelectedItems.OfType<object>().ToList();
        if (Selected.Count == 0) return;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

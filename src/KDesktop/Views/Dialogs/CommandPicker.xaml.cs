using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KDesktop.Core.Model;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Dialogs;

/// <summary>Searchable list to pick one or more existing commands.</summary>
public partial class CommandPicker : FluentWindow
{
    private readonly List<Command> _all;

    public IReadOnlyList<Command> Selected { get; private set; } = [];

    public CommandPicker(IEnumerable<Command> commands)
    {
        InitializeComponent();
        _all = commands.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        List.SelectionMode = SelectionMode.Extended;
        ShowHiddenToggle.IsChecked = App.Current.Config.Settings.ShowHiddenCommands;
        Refill();
        Loaded += (_, _) => SearchBox.Focus();
    }

    public static IReadOnlyList<Command> Pick(Window? owner, IEnumerable<Command> commands)
    {
        var dlg = new CommandPicker(commands) { Owner = owner };
        return dlg.ShowDialog() == true ? dlg.Selected : [];
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
        var shown = ShowHiddenToggle.IsChecked == true ? _all : _all.Where(c => !c.Hidden).ToList();
        // Like the launcher: initials, loose letters, no accents; the best matches first.
        var tokens = KDesktop.Core.Engine.FuzzyMatch.Tokens(SearchBox.Text);
        List.ItemsSource = tokens.Length == 0 ? shown
            : shown.Select(c => (Command: c, Score: KDesktop.Core.Engine.FuzzyMatch.Score(tokens, new KDesktop.Core.Engine.SearchKey(c.Name))))
                  .Where(x => x.Score > 0).OrderByDescending(x => x.Score).Select(x => x.Command).ToList();
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
        Selected = List.SelectedItems.OfType<Command>().ToList();
        if (Selected.Count == 0) return;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

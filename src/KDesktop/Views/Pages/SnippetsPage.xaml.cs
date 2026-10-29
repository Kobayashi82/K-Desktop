using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Storage;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Pages;

/// <summary>Textos: the expander's abbreviations and their texts, the prefix, and a box to try them.</summary>
public sealed partial class SnippetsPage : UserControl, IDisposable
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private readonly App _app = App.Current;
    private readonly ListCollectionView _view;
    private TextSnippet? _selected;

    private AppConfig Config => _app.Config;
    private AppSettings Settings => Config.Settings;

    public SnippetsPage()
    {
        InitializeComponent();
        DataContext = Settings;
        TextExpander.IsExpanded = !Settings.SnippetTextFolded;
        TryExpander.IsExpanded = !Settings.SnippetTryFolded;
        OptionsExpander.IsExpanded = Settings.SnippetOptionsOpen;
        NotesExpander.IsExpanded = !Settings.SnippetNotesFolded;
        _view = new ListCollectionView(Config.Snippets) { Filter = Filter, IsLiveSorting = true, IsLiveFiltering = true };
        _view.SortDescriptions.Add(new SortDescription(nameof(TextSnippet.Abbreviation), ListSortDirection.Ascending));
        _view.LiveSortingProperties.Add(nameof(TextSnippet.Abbreviation));
        _view.LiveFilteringProperties.Add(nameof(TextSnippet.Abbreviation));
        SnippetList.ItemsSource = _view;
        Config.Snippets.CollectionChanged += OnSnippetsChanged;
        Settings.PropertyChanged += OnSettingChanged;
        UpdateCount();
        UpdatePrefix();
        Loaded += (_, _) =>
        {
            if (SnippetList.SelectedItem is null && !_view.IsEmpty) SnippetList.SelectedIndex = 0;
        };
        // Inside K-Desktop the texts are written only in the box to try them, never while editing them.
        TryBox.GotKeyboardFocus += (_, _) => _app.Router.SnippetsInKDesktop = true;
        TryBox.LostKeyboardFocus += (_, _) => _app.Router.SnippetsInKDesktop = false;
    }

    public void Select(string id)
    {
        if (Config.Snippets.FirstOrDefault(s => s.Id == id) is not { } snippet) return;
        if (!_view.Contains(snippet)) SearchBox.Text = string.Empty;
        SnippetList.SelectedItem = snippet;
        SnippetList.ScrollIntoView(snippet);
    }

    /// <summary>«Texto», «Pruébalo», «Opciones» and «Notas» stay as they were left, from one text to the next.</summary>
    private void OnSectionFolded(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        Settings.SnippetTextFolded = TextExpander.IsExpanded != true;
        Settings.SnippetTryFolded = TryExpander.IsExpanded != true;
        Settings.SnippetOptionsOpen = OptionsExpander.IsExpanded == true;
        Settings.SnippetNotesFolded = NotesExpander.IsExpanded != true;
    }

    // ── programs ─────────────────────────────────────────────────────────

    private void OnProcessDropDown(object sender, EventArgs e) => ProcessCombo.Fill(ProcessBox, _selected?.Processes);

    private void OnProcessPicked(object sender, EventArgs e) => ProcessCombo.AddPicked(ProcessBox, _selected?.Processes);

    private void OnProcessKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        OnAddProcess(sender, e);
        e.Handled = true; // stays in the box to add another
    }

    private void OnAddProcess(object sender, RoutedEventArgs e) => ProcessCombo.AddTyped(ProcessBox, _selected?.Processes);

    private void OnPickProcess(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } snippet) return;
        WindowPicker.Pick(name =>
        {
            if (!snippet.Processes.Contains(name)) snippet.Processes.Add(name);
        });
    }

    private void OnRemoveProcess(object sender, RoutedEventArgs e)
    {
        if (_selected is not null && sender is FrameworkElement { Tag: string p }) _selected.Processes.Remove(p);
    }

    public void Dispose()
    {
        Track(null);
        Config.Snippets.CollectionChanged -= OnSnippetsChanged;
        Settings.PropertyChanged -= OnSettingChanged;
        _view.DetachFromSourceCollection();
    }

    private void OnSnippetsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        UpdateCount();
        UpdateHow();
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.SnippetPrefix) or nameof(AppSettings.SnippetsEnabled))
        {
            UpdatePrefix();
            UpdateHow();
        }
    }

    private void UpdateCount()
    {
        CountText.Text = Config.Snippets.Count.ToString(Loc.Instance.Culture);
        EmptyText.Text = Loc.T(Config.Snippets.Count == 0 ? "snippets.emptyText" : "snippets.pickText");
    }

    private string Prefix => Settings.SnippetPrefix.Trim();

    private void UpdatePrefix()
    {
        PrefixShown.Text = Prefix;
        PrefixShown.Visibility = Prefix.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _view.Refresh(); // the rows show it in front
    }

    /// <summary>The prefix is never left with spaces (they would end the abbreviation).</summary>
    private void OnPrefixLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var clean = new string(Settings.SnippetPrefix.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (clean != Settings.SnippetPrefix) Settings.SnippetPrefix = clean;
    }

    // ── list ─────────────────────────────────────────────────────────────

    private bool Filter(object o)
    {
        var q = SearchBox?.Text?.Trim();
        return string.IsNullOrEmpty(q) || o is TextSnippet s &&
            (s.Abbreviation.Contains(q, StringComparison.CurrentCultureIgnoreCase) || s.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase));
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _view.Refresh();

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_selected is { } previous) EnsureAbbreviation(previous);
        Track(SnippetList.SelectedItem as TextSnippet);
        Editor.DataContext = _selected;
        TryBox.Text = string.Empty;
        if (_selected is null)
        {
            Editor.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        UpdateHow();
        Editor.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(180)));
        EditorShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
    }

    private void Track(TextSnippet? snippet)
    {
        if (_selected is not null) _selected.PropertyChanged -= OnSnippetChanged;
        _selected = snippet;
        if (snippet is not null) snippet.PropertyChanged += OnSnippetChanged;
    }

    private void OnSnippetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TextSnippet.Abbreviation) or nameof(TextSnippet.Enabled) or nameof(TextSnippet.Text) or nameof(TextSnippet.AppsList)) UpdateHow();
    }

    private void OnListKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && _selected is not null)
        {
            OnDelete(sender, e);
            e.Handled = true;
        }
    }

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (ListContextMenu.SelectRow(SnippetList, e) is not { DataContext: TextSnippet snippet } row)
        {
            ListContextMenu.OpenImport(SnippetList, e);
            return;
        }
        ListContextMenu.Open(row, [
            ListContextMenu.Entry(Loc.T("common.duplicate"), SymbolRegular.Copy24, () => OnDuplicate(row, new RoutedEventArgs())),
            new Separator(),
            .. ListContextMenu.Transfer(row, TransferKind.Snippet, snippet.Id, snippet.Abbreviation),
            ListContextMenu.Delete(() => OnDelete(row, new RoutedEventArgs()))]);
    }

    // ── abbreviation ─────────────────────────────────────────────────────

    private string? _abbreviationBefore;

    private void OnAbbreviationFocus(object sender, KeyboardFocusChangedEventArgs e) => _abbreviationBefore = _selected?.Abbreviation;

    private void OnAbbreviationLostFocus(object sender, KeyboardFocusChangedEventArgs e) => EnsureAbbreviation(AbbreviationBox.DataContext as TextSnippet);

    private void OnAbbreviationKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            EnsureAbbreviation(_selected);
            BodyBox.Focus();
        }
        if (e.Key == Key.Escape && _selected is { } snippet && !string.IsNullOrWhiteSpace(_abbreviationBefore)) snippet.Abbreviation = _abbreviationBefore;
    }

    /// <summary>Without spaces nor the prefix typed in front; an emptied one goes back to what it was.</summary>
    private void EnsureAbbreviation(TextSnippet? snippet)
    {
        if (snippet is null) return;
        var clean = TextSnippet.Clean(snippet.Abbreviation, Prefix);
        if (clean.Length == 0) clean = !string.IsNullOrWhiteSpace(_abbreviationBefore) ? _abbreviationBefore : Free(Loc.T("snippets.newAbbreviation"));
        if (clean != snippet.Abbreviation) snippet.Abbreviation = clean;
    }

    /// <summary>How the abbreviation goes off, or why it does not.</summary>
    private void UpdateHow()
    {
        if (_selected is not { } snippet) return;
        var abbreviation = TextSnippet.Clean(snippet.Abbreviation, Prefix);
        var typed = Prefix + abbreviation;
        // The others that can go off where this one does (a text for everywhere meets every other).
        var others = Config.Snippets.Where(s => !ReferenceEquals(s, snippet) && s.Enabled && s.Text.Length > 0 &&
                                                (s.Processes.Count == 0 || snippet.Processes.Count == 0 || s.SharesPrograms(snippet))).ToList();
        bool Same(TextSnippet s) => TextSnippet.Clean(s.Abbreviation, Prefix).Equals(abbreviation, StringComparison.OrdinalIgnoreCase);
        var mine = Config.Snippets.IndexOf(snippet);
        string how;
        var warn = false;
        if (!Settings.SnippetsEnabled) (how, warn) = (Loc.T("snippets.howAllOff"), true);
        else if (!snippet.Enabled) how = Loc.T("snippets.howOff");
        else if (abbreviation.Length == 0) how = string.Empty;
        else if (snippet.Text.Length == 0) (how, warn) = (Loc.T("snippets.howNoText"), true);
        // Same abbreviation and the same programs: the first in the list wins (one limited to programs wins over one for everywhere).
        else if (others.Any(s => Same(s) && s.SharesPrograms(snippet) && Config.Snippets.IndexOf(s) < mine))
            (how, warn) = (Loc.T("snippets.howTaken", typed), true);
        else if (others.Select(s => TextSnippet.Clean(s.Abbreviation, Prefix))
                     .FirstOrDefault(o => o.Length > abbreviation.Length && o.StartsWith(abbreviation, StringComparison.OrdinalIgnoreCase)) is { } longer)
            how = Loc.T("snippets.howWaits", typed, Prefix + longer);
        else how = snippet.HasApps ? Loc.T("snippets.howNowIn", typed, string.Join(", ", snippet.Processes)) : Loc.T("snippets.howNow", typed);
        HowText.Text = how;
        HowText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, warn ? "Kd.Danger" : "TextFillColorSecondaryBrush");
        TryHint.Text = abbreviation.Length > 0 ? Loc.T("snippets.tryHint", typed) : string.Empty;
    }

    // ── new, duplicate, delete ───────────────────────────────────────────

    private void OnNew(object sender, RoutedEventArgs e)
    {
        var snippet = new TextSnippet { Abbreviation = Free(Loc.T("snippets.newAbbreviation")) };
        SearchBox.Text = string.Empty;
        Config.Snippets.Add(snippet);
        SnippetList.SelectedItem = snippet;
        SnippetList.ScrollIntoView(snippet);
        Dispatcher.BeginInvoke(() =>
        {
            AbbreviationBox.Focus();
            AbbreviationBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>An abbreviation no other text has: «texto», «texto2»…</summary>
    private string Free(string baseName)
    {
        var name = TextSnippet.Clean(baseName, Prefix);
        for (var i = 2; Config.Snippets.Any(s => s.Abbreviation.Equals(name, StringComparison.OrdinalIgnoreCase)); i++) name = baseName + i.ToString(Loc.Instance.Culture);
        return name;
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (_selected is { } item) ItemTransferUi.Export(this, TransferKind.Snippet, item.Id, item.Abbreviation);
    }

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var copy = _selected.Clone();
        copy.Abbreviation = Free(_selected.Abbreviation);
        Config.Snippets.Add(copy);
        SnippetList.SelectedItem = copy;
        SnippetList.ScrollIntoView(copy);
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } snippet) return;
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("snippets.delete"),
            Content = Loc.T("snippets.deleteText", Prefix + snippet.Abbreviation),
            PrimaryButtonText = Loc.T("common.delete"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        var index = SnippetList.SelectedIndex;
        Config.Snippets.Remove(snippet);
        if (!_view.IsEmpty) SnippetList.SelectedIndex = Math.Clamp(index, 0, _view.Count - 1);
    }
}

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using KDesktop.Core.Achievements;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Pages;

/// <summary>
/// Disposiciones (E1, 5.0.375): where some windows go, saved as they are now and put back with «Disposición de ventanas».
/// A list and an editor, as Textos and Macros.
/// </summary>
public sealed partial class ArrangementsPage : UserControl, IDisposable
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private readonly App _app = App.Current;
    private readonly ListCollectionView _view;
    private WindowArrangement? _selected;

    private AppConfig Config => _app.Config;
    private AppSettings Settings => Config.Settings;

    public ArrangementsPage()
    {
        InitializeComponent();
        WindowsExpander.IsExpanded = !Settings.ArrangementWindowsFolded;
        OptionsExpander.IsExpanded = Settings.ArrangementOptionsOpen;
        NotesExpander.IsExpanded = !Settings.ArrangementNotesFolded;
        _view = new ListCollectionView(Config.Arrangements) { Filter = Filter, IsLiveSorting = true, IsLiveFiltering = true };
        _view.SortDescriptions.Add(new SortDescription(nameof(WindowArrangement.Name), ListSortDirection.Ascending));
        _view.LiveSortingProperties.Add(nameof(WindowArrangement.Name));
        _view.LiveFilteringProperties.Add(nameof(WindowArrangement.Name));
        ArrangementList.ItemsSource = _view;
        Config.Arrangements.CollectionChanged += OnArrangementsChanged;
        UpdateCount();
        Loaded += (_, _) =>
        {
            if (ArrangementList.SelectedItem is null && !_view.IsEmpty) ArrangementList.SelectedIndex = 0;
        };
    }

    public void Dispose()
    {
        Track(null);
        Config.Arrangements.CollectionChanged -= OnArrangementsChanged;
        _view.DetachFromSourceCollection();
    }

    /// <summary>«Ventanas», «Opciones» and «Notas» stay as they were left, from one to the next.</summary>
    private void OnSectionFolded(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        Settings.ArrangementWindowsFolded = WindowsExpander.IsExpanded != true;
        Settings.ArrangementOptionsOpen = OptionsExpander.IsExpanded == true;
        Settings.ArrangementNotesFolded = NotesExpander.IsExpanded != true;
    }

    private void OnArrangementsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        CountText.Text = Config.Arrangements.Count.ToString(Loc.Instance.Culture);
        ListEmpty.Visibility = Config.Arrangements.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Select(string id)
    {
        if (Config.FindArrangement(id) is not { } arrangement) return;
        if (!_view.Contains(arrangement)) SearchBox.Text = string.Empty;
        ArrangementList.SelectedItem = arrangement;
        ArrangementList.ScrollIntoView(arrangement);
    }

    // ── list ─────────────────────────────────────────────────────────────

    private bool Filter(object o)
    {
        var q = SearchBox?.Text?.Trim();
        return string.IsNullOrEmpty(q) || o is WindowArrangement a &&
            (a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || a.Windows.Any(w => w.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)));
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _view.Refresh();

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_selected is { } previous) EnsureName(previous);
        Track(ArrangementList.SelectedItem as WindowArrangement);
        Editor.DataContext = _selected;
        NameError.Visibility = Visibility.Collapsed;
        if (_selected is null)
        {
            Editor.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        UpdateWindows();
        Editor.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(180)));
        EditorShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
    }

    private void Track(WindowArrangement? arrangement)
    {
        if (_selected is not null) _selected.Windows.CollectionChanged -= OnWindowsChanged;
        _selected = arrangement;
        if (arrangement is not null) arrangement.Windows.CollectionChanged += OnWindowsChanged;
    }

    private void OnWindowsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateWindows();

    private void UpdateWindows() => NoWindows.Visibility = _selected is { Windows.Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;

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
        if (ListContextMenu.SelectRow(ArrangementList, e) is not { DataContext: WindowArrangement } row) return;
        ListContextMenu.Open(row,
            ListContextMenu.Entry(Loc.T("macro.test"), SymbolRegular.Play24, () => OnTest(row, new RoutedEventArgs())),
            ListContextMenu.Entry(Loc.T("common.duplicate"), SymbolRegular.Copy24, () => OnDuplicate(row, new RoutedEventArgs())),
            ListContextMenu.Entry(Loc.T("commands.rename"), SymbolRegular.Rename24, () =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            }),
            new Separator(),
            ListContextMenu.Entry(Loc.T("common.delete"), SymbolRegular.Delete24, () => OnDelete(row, new RoutedEventArgs())));
    }

    // ── name ─────────────────────────────────────────────────────────────

    private string? _nameBeforeEdit;

    /// <summary>The one whose name is being typed (only its name is checked against the others).</summary>
    private WindowArrangement? _nameOwner;

    private void OnNameFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _nameBeforeEdit = _selected?.Name;
        _nameOwner = _selected;
    }

    private void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e) => EnsureName(NameBox.DataContext as WindowArrangement);

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) EnsureName(_selected);
        if (e.Key == Key.Escape && _selected is { } arrangement && !string.IsNullOrWhiteSpace(_nameBeforeEdit)) arrangement.Name = _nameBeforeEdit;
    }

    private void EnsureName(WindowArrangement? arrangement)
    {
        if (arrangement is null) return;
        if (string.IsNullOrWhiteSpace(arrangement.Name))
        {
            arrangement.Name = !string.IsNullOrWhiteSpace(_nameBeforeEdit) ? _nameBeforeEdit : UniqueName(Loc.T("arrangement.newName"));
            if (ReferenceEquals(arrangement, _selected)) NameError.Visibility = Visibility.Visible;
        }
        else
        {
            if (arrangement.Name != arrangement.Name.Trim()) arrangement.Name = arrangement.Name.Trim();
            if (ReferenceEquals(arrangement, _nameOwner) && !string.IsNullOrWhiteSpace(arrangement.Name))
                arrangement.Name = UniqueNames.Settle(Config.Arrangements, arrangement, x => x.Name, arrangement.Name, _nameBeforeEdit, UniqueName);
            if (ReferenceEquals(arrangement, _selected)) NameError.Visibility = Visibility.Collapsed;
        }
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (var i = 2; Config.Arrangements.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++) name = $"{baseName} {i}";
        return name;
    }

    // ── windows ──────────────────────────────────────────────────────────

    /// <summary>«Guardar cómo están ahora»: the windows open now take the place of the ones it had (asked first if it had some).</summary>
    private async void OnCapture(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } arrangement) return;
        if (arrangement.Windows.Count > 0)
        {
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Title = Loc.T("arrangement.capture"),
                Content = Loc.T("arrangement.captureReplace", arrangement.Windows.Count),
                PrimaryButtonText = Loc.T("arrangement.captureYes"),
                CloseButtonText = Loc.T("common.cancel"),
            };
            if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        }
        Capture(arrangement);
    }

    private void Capture(WindowArrangement arrangement)
    {
        var windows = WindowArrangements.Capture();
        arrangement.Windows.Clear();
        foreach (var window in windows) arrangement.Windows.Add(window);
        _app.Ui.ShowOsd(Loc.T("osd.arrangementSaved", arrangement.Name, arrangement.Summary), Core.Engine.OsdKind.Success, look: Core.Engine.OsdLook.Of("WindowApps24"));
    }

    private void OnRemoveWindow(object sender, RoutedEventArgs e)
    {
        if (_selected is not null && sender is FrameworkElement { Tag: ArrangedWindow window }) _selected.Windows.Remove(window);
    }

    /// <summary>«Probar»: puts it now, as the action does.</summary>
    private async void OnTest(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } arrangement) return;
        var test = new Command { Name = arrangement.Name };
        test.Actions.Add(new ArrangementAction { ArrangementId = arrangement.Id, ArrangementName = arrangement.Name });
        await _app.Engine.TestActionAsync(test.Actions[0], test);
    }

    // ── new, duplicate, delete, command ──────────────────────────────────

    /// <summary>A new one already has the windows as they are now: that is what one usually wants to keep.</summary>
    private void OnNew(object sender, RoutedEventArgs e)
    {
        var arrangement = new WindowArrangement { Name = UniqueName(Loc.T("arrangement.newName")) };
        SearchBox.Text = string.Empty;
        Config.Arrangements.Add(arrangement);
        ArrangementList.SelectedItem = arrangement;
        ArrangementList.ScrollIntoView(arrangement);
        Capture(arrangement);
        Dispatcher.BeginInvoke(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var copy = _selected.Duplicate();
        copy.Name = UniqueName(_selected.Name);
        Config.Arrangements.Add(copy);
        ArrangementList.SelectedItem = copy;
        ArrangementList.ScrollIntoView(copy);
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } arrangement) return;
        var used = Config.Commands.SelectMany(c => c.Actions).OfType<ArrangementAction>().Count(a => a.ArrangementId == arrangement.Id);
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("arrangement.delete"),
            Content = Loc.T("arrangement.deleteText", arrangement.Name) + (used > 0 ? "\n\n" + Loc.T("arrangement.deleteUsed", used) : ""),
            PrimaryButtonText = Loc.T("common.delete"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        var index = ArrangementList.SelectedIndex;
        Config.Arrangements.Remove(arrangement);
        if (!_view.IsEmpty) ArrangementList.SelectedIndex = Math.Clamp(index, 0, _view.Count - 1);
    }

    private void OnCreateCommand(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } arrangement) return;
        var cmd = new Command { Name = UniqueNames.Free(Config.Commands.Select(c => c.Name), arrangement.Name) };
        cmd.Actions.Add(new ArrangementAction { ArrangementId = arrangement.Id, ArrangementName = arrangement.Name });
        Config.Commands.Add(cmd);
        _app.Achievements.Increment(Stat.CommandsCreated);
        _app.Achievements.Increment(Stat.ActionsCreated);
        (Window.GetWindow(this) as MainWindow)?.OpenCommand(cmd.Id);
    }
}

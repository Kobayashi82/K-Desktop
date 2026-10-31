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

/// <summary>
/// Paneles (5.0.452): small windows on the desktop with figures of the PC (network, processor, memory, graphics card). A
/// list and an editor, as Disposiciones and Textos; every change shows on the panel at once.
/// </summary>
public sealed partial class PanelsPage : UserControl, IDisposable
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly int[] Intervals = [500, 1000, 2000, 5000, 10000];
    private readonly App _app = App.Current;
    private readonly ListCollectionView _view;
    private InfoPanel? _selected;

    private AppConfig Config => _app.Config;
    private AppSettings Settings => Config.Settings;

    public PanelsPage()
    {
        InitializeComponent();
        ItemsExpander.IsExpanded = !Settings.PanelItemsFolded;
        LookExpander.IsExpanded = !Settings.PanelLookFolded;
        OptionsExpander.IsExpanded = Settings.PanelOptionsOpen;
        NotesExpander.IsExpanded = !Settings.PanelNotesFolded;
        IntervalBox.ItemsSource = Intervals.Select(ms => new NamedChoice<int>(ms,
            Loc.T("settings.seconds", (ms / 1000.0).ToString("0.#", Loc.Instance.Culture)))).ToList();
        _view = new ListCollectionView(Config.Panels) { Filter = Filter, IsLiveSorting = true, IsLiveFiltering = true };
        _view.SortDescriptions.Add(new SortDescription(nameof(InfoPanel.Name), ListSortDirection.Ascending));
        _view.LiveSortingProperties.Add(nameof(InfoPanel.Name));
        _view.LiveFilteringProperties.Add(nameof(InfoPanel.Name));
        PanelList.ItemsSource = _view;
        Config.Panels.CollectionChanged += OnPanelsChanged;
        UpdateCount();
        Loaded += (_, _) =>
        {
            if (PanelList.SelectedItem is null && !_view.IsEmpty) PanelList.SelectedIndex = 0;
        };
    }

    public void Dispose()
    {
        Track(null);
        Config.Panels.CollectionChanged -= OnPanelsChanged;
        _view.DetachFromSourceCollection();
    }

    /// <summary>«Datos», «Aspecto», «Opciones» and «Notas» stay as they were left, from one panel to the next.</summary>
    private void OnSectionFolded(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        Settings.PanelItemsFolded = ItemsExpander.IsExpanded != true;
        Settings.PanelLookFolded = LookExpander.IsExpanded != true;
        Settings.PanelOptionsOpen = OptionsExpander.IsExpanded == true;
        Settings.PanelNotesFolded = NotesExpander.IsExpanded != true;
    }

    private void OnPanelsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        CountText.Text = Config.Panels.Count.ToString(Loc.Instance.Culture);
        ListEmpty.Visibility = Config.Panels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Select(string id)
    {
        if (Config.Panels.FirstOrDefault(p => p.Id == id) is not { } panel) return;
        if (!_view.Contains(panel)) SearchBox.Text = string.Empty;
        PanelList.SelectedItem = panel;
        PanelList.ScrollIntoView(panel);
    }

    // ── list ─────────────────────────────────────────────────────────────

    private bool Filter(object o)
    {
        var q = SearchBox?.Text?.Trim();
        return string.IsNullOrEmpty(q) || o is InfoPanel p &&
            (p.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || p.Summary.Contains(q, StringComparison.CurrentCultureIgnoreCase));
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _view.Refresh();

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_selected is { } previous) EnsureName(previous);
        Track(PanelList.SelectedItem as InfoPanel);
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
        UpdateItems();
        Editor.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(180)));
        EditorShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
    }

    private void Track(InfoPanel? panel)
    {
        if (_selected is not null) _selected.Items.CollectionChanged -= OnItemsChanged;
        _selected = panel;
        if (panel is not null) panel.Items.CollectionChanged += OnItemsChanged;
    }

    private void OnItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateItems();

    /// <summary>Without figures, a line saying what to do instead of an empty box.</summary>
    private void UpdateItems()
    {
        var any = _selected?.Items.Count > 0;
        NoItems.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        ItemsBox.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
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
        if (ListContextMenu.SelectRow(PanelList, e) is not { DataContext: InfoPanel panel } row)
        {
            ListContextMenu.OpenImport(PanelList, e);
            return;
        }
        ListContextMenu.Open(row, [
            ListContextMenu.Entry(Loc.T("common.duplicate"), SymbolRegular.Copy24, () => OnDuplicate(row, new RoutedEventArgs())),
            ListContextMenu.Entry(Loc.T("commands.rename"), SymbolRegular.Rename24, () =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            }),
            new Separator(),
            .. ListContextMenu.Transfer(row, TransferKind.Panel, panel.Id, panel.Name),
            ListContextMenu.Delete(() => OnDelete(row, new RoutedEventArgs()))]);
    }

    // ── name ─────────────────────────────────────────────────────────────

    private string? _nameBeforeEdit;

    /// <summary>The one whose name is being typed (only its name is checked against the others).</summary>
    private InfoPanel? _nameOwner;

    private void OnNameFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _nameBeforeEdit = _selected?.Name;
        _nameOwner = _selected;
    }

    private void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e) => EnsureName(NameBox.DataContext as InfoPanel);

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) EnsureName(_selected);
        if (e.Key == Key.Escape && _selected is { } panel && !string.IsNullOrWhiteSpace(_nameBeforeEdit)) panel.Name = _nameBeforeEdit;
    }

    private void EnsureName(InfoPanel? panel)
    {
        if (panel is null) return;
        if (string.IsNullOrWhiteSpace(panel.Name))
        {
            panel.Name = !string.IsNullOrWhiteSpace(_nameBeforeEdit) ? _nameBeforeEdit : UniqueName(Loc.T("panels.newName"));
            if (ReferenceEquals(panel, _selected)) NameError.Visibility = Visibility.Visible;
        }
        else
        {
            if (panel.Name != panel.Name.Trim()) panel.Name = panel.Name.Trim();
            if (ReferenceEquals(panel, _nameOwner) && !string.IsNullOrWhiteSpace(panel.Name))
                panel.Name = UniqueNames.Settle(Config.Panels, panel, x => x.Name, panel.Name, _nameBeforeEdit, UniqueName);
            if (ReferenceEquals(panel, _selected)) NameError.Visibility = Visibility.Collapsed;
        }
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (var i = 2; Config.Panels.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++) name = $"{baseName} {i}";
        return name;
    }

    // ── figures ──────────────────────────────────────────────────────────

    /// <summary>«Añadir dato»: a menu with every figure (the ones it already shows, too: a second one can have another look).</summary>
    private void OnAddItem(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } panel || sender is not UIElement button) return;
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var kind in Enum.GetValues<PanelItemKind>())
            menu.Items.Add(ListContextMenu.Entry(Loc.T("enum.panelItem." + kind), PanelItemSymbol.Of(kind), () => panel.Items.Add(new InfoPanelItem { Kind = kind })));
        menu.IsOpen = true;
    }

    private void OnRemoveItem(object sender, RoutedEventArgs e)
    {
        if (_selected is not null && sender is FrameworkElement { Tag: InfoPanelItem item }) _selected.Items.Remove(item);
    }

    /// <summary>Back to the top right of the main monitor (one lost off the screen, for instance).</summary>
    private void OnToCorner(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } panel) return;
        panel.Monitor = null;
        panel.X = null;
        panel.Y = null;
        if (!panel.Enabled) panel.Enabled = true;
    }

    // ── new, duplicate, delete ───────────────────────────────────────────

    /// <summary>A new one already shows the usual figures (network, processor, memory, graphics card) and goes on the desktop.</summary>
    private void OnNew(object sender, RoutedEventArgs e)
    {
        var panel = InfoPanel.CreateDefault(UniqueName(Loc.T("panels.newName")));
        SearchBox.Text = string.Empty;
        Config.Panels.Add(panel);
        PanelList.SelectedItem = panel;
        PanelList.ScrollIntoView(panel);
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
        Config.Panels.Add(copy);
        PanelList.SelectedItem = copy;
        PanelList.ScrollIntoView(copy);
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } panel) return;
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("panels.delete"),
            Content = Loc.T("panels.deleteText", panel.Name),
            PrimaryButtonText = Loc.T("common.delete"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowCenteredAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        var index = PanelList.SelectedIndex;
        Config.Panels.Remove(panel);
        if (!_view.IsEmpty) PanelList.SelectedIndex = Math.Clamp(index, 0, _view.Count - 1);
    }
}

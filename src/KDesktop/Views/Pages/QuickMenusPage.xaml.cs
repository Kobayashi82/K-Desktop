using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KDesktop.Core;
using KDesktop.Core.Achievements;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Views.Dialogs;
using KDesktop.Views.QuickMenuEditor;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Pages;

/// <summary>
/// Visual QuickMenu editor: the menu is drawn like the real one with the open submenus side by side.
/// Drag &amp; drop (items, commands from the palette, files/folders/links from outside), context menu,
/// inline rename, icons, cut/copy/paste and undo/redo.
/// </summary>
public partial class QuickMenusPage : UserControl, IDisposable
{
    private const string ItemFormat = "KDesktop.QmItem";
    private const string CommandFormat = "KDesktop.Command";
    private const string KindFormat = "KDesktop.NewKind";
    private const int MaxUndo = 60;

    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly App _app = App.Current;
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private readonly List<string> _path = [];   // ids of the items whose children are shown
    private List<QmColumn> _columns = [];
    private QuickMenu? _menu;
    private string? _selectedId;
    private bool _rebuildQueued;
    private bool _restoring;
    private int _lastColumnCount;
    private Point _dragStart;
    private object? _dragSource;
    private (QuickMenuItem Item, bool Cut)? _clipboard;

    private AppConfig Config => _app.Config;

    private readonly ListCollectionView _view;

    public QuickMenusPage()
    {
        InitializeComponent();
        _view = new ListCollectionView(Config.QuickMenus) { Filter = Filter, IsLiveFiltering = true };
        _view.LiveFilteringProperties.Add(nameof(QuickMenu.Name));
        MenuList.ItemsSource = _view;
        Config.Commands.CollectionChanged += OnCommandsChanged;
        Config.QuickMenus.CollectionChanged += OnMenusChanged;
        RefreshPalette();
        UpdateCount();
        if (!_view.IsEmpty) MenuList.SelectedIndex = 0;
        else ShowEditor();
        Loaded += (_, _) => Canvas.Focus();
    }

    public void Dispose()
    {
        if (_menu is not null) _menu.Modified -= OnMenuModified;
        Config.Commands.CollectionChanged -= OnCommandsChanged;
        Config.QuickMenus.CollectionChanged -= OnMenusChanged;
        _view.DetachFromSourceCollection();
    }

    private void OnMenusChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateCount();

    private void UpdateCount() => CountText.Text = Config.QuickMenus.Count.ToString();

    private bool Filter(object o)
    {
        var q = SearchBox?.Text?.Trim();
        return string.IsNullOrEmpty(q) || o is QuickMenu m && m.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _view.Refresh();

    private void OnMenuListKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && _menu is not null)
        {
            OnDeleteMenu(sender, e);
            e.Handled = true;
        }
    }

    private void OnMenuRightClick(object sender, MouseButtonEventArgs e)
    {
        if (ListContextMenu.SelectRow(MenuList, e) is not { DataContext: QuickMenu } row) return;
        ListContextMenu.Open(row,
            ListContextMenu.Entry(Loc.T("qm.test"), SymbolRegular.Play24, () => OnTest(row, new RoutedEventArgs())),
            ListContextMenu.Entry(Loc.T("qm.renameMenu"), SymbolRegular.Rename24, FocusName),
            ListContextMenu.Entry(Loc.T("qm.duplicateMenu"), SymbolRegular.Copy24, () => OnDuplicateMenu(row, new RoutedEventArgs())),
            new Separator(),
            ListContextMenu.Entry(Loc.T("qm.deleteMenu"), SymbolRegular.Delete24, () => OnDeleteMenu(row, new RoutedEventArgs())));
    }

    private void FocusName() => Dispatcher.BeginInvoke(() =>
    {
        NameBox.Focus();
        NameBox.SelectAll();
    }, System.Windows.Threading.DispatcherPriority.Input);

    /// <summary>The editor of the chosen menu, or the empty state when there is none.</summary>
    private void ShowEditor()
    {
        Editor.DataContext = _menu;
        NameError.Visibility = Visibility.Collapsed;
        if (_menu is null)
        {
            Editor.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        Editor.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(180)));
        EditorShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
    }

    // ── name ─────────────────────────────────────────────────────────────

    private string? _nameBeforeEdit;

    private void OnNameFocus(object sender, KeyboardFocusChangedEventArgs e) => _nameBeforeEdit = _menu?.Name;

    private void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e) => EnsureName(NameBox.DataContext as QuickMenu);

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) EnsureName(_menu);
        if (e.Key == Key.Escape && _menu is { } menu && !string.IsNullOrWhiteSpace(_nameBeforeEdit)) menu.Name = _nameBeforeEdit;
    }

    /// <summary>A menu never stays without a name: an emptied name goes back to the one it had.</summary>
    private void EnsureName(QuickMenu? menu)
    {
        if (menu is null) return;
        var empty = string.IsNullOrWhiteSpace(menu.Name);
        if (empty) menu.Name = !string.IsNullOrWhiteSpace(_nameBeforeEdit) ? _nameBeforeEdit : UniqueName(Loc.T("qm.newName"));
        else if (menu.Name != menu.Name.Trim()) menu.Name = menu.Name.Trim();
        if (ReferenceEquals(menu, _menu)) NameError.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (var i = 2; Config.QuickMenus.Any(m => m.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)); i++) name = $"{baseName} {i}";
        return name;
    }

    /// <summary>
    /// Shows a menu. With <paramref name="commandId"/> (from «QuickMenus: …» of a command) the submenus that lead to it
    /// open, it is selected and its rows flash.
    /// </summary>
    public void Select(string menuId, string? commandId = null)
    {
        if (Config.FindMenu(menuId) is not { } menu) return;
        if (!_view.Contains(menu)) SearchBox.Text = string.Empty;
        MenuList.SelectedItem = menu;
        MenuList.ScrollIntoView(menu);
        if (commandId is null || menu.Descendants().FirstOrDefault(i => i.CommandId == commandId) is not { } item) return;
        _found = commandId;
        SelectItem(item);
        _found = null;
        // Bring the row into view once the columns are laid out.
        Dispatcher.BeginInvoke(() =>
        {
            var row = FindVisualChildren<FrameworkElement>(ColumnsControl).FirstOrDefault(fe => fe is Grid { Name: "Row" } && fe.DataContext is QmRow { IsFound: true });
            row?.BringIntoView();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Command whose rows flash in the next rebuild (only that one).</summary>
    private string? _found;

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var c in FindVisualChildren<T>(child)) yield return c;
        }
    }

    // ── menu selection & management ──────────────────────────────────────

    private void OnMenuSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_menu is not null)
        {
            EnsureName(_menu);
            _menu.Modified -= OnMenuModified;
        }
        _menu = MenuList.SelectedItem as QuickMenu;
        _undo.Clear();
        _redo.Clear();
        _path.Clear();
        _selectedId = null;
        _lastColumnCount = 0;
        if (_menu is not null) _menu.Modified += OnMenuModified;
        ShowEditor();
        UpdateUndoButtons();
        Rebuild();
    }

    private void OnSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var size = (int)Math.Round(SizeSlider.Value);
        SizeText.Text = Loc.T($"qm.size{size}");
        if (_menu is not null && size != _menu.Size) _menu.Size = size;
    }

    private void OnNewMenu(object sender, RoutedEventArgs e)
    {
        var menu = new QuickMenu { Name = UniqueName(Loc.T("qm.newName")) };
        SearchBox.Text = string.Empty;
        Config.QuickMenus.Add(menu);
        _app.Achievements.Increment(Stat.MenusCreated);
        MenuList.SelectedItem = menu;
        MenuList.ScrollIntoView(menu);
        FocusName();
    }

    private void OnDuplicateMenu(object sender, RoutedEventArgs e)
    {
        if (_menu is null) return;
        var copy = Json.Clone(_menu);
        copy.Id = Ids.New();
        copy.Name = UniqueName(_menu.Name);
        foreach (var item in copy.Descendants()) item.Id = Ids.New();
        Config.QuickMenus.Add(copy);
        _app.Achievements.Increment(Stat.MenusCreated);
        MenuList.SelectedItem = copy;
    }

    private async void OnDeleteMenu(object sender, RoutedEventArgs e)
    {
        if (_menu is not { } menu) return;
        var users = Config.Commands.Count(c => c.Actions.OfType<OpenQuickMenuAction>().Any(a => a.MenuId == menu.Id));
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("qm.deleteMenu"),
            Content = Loc.T("qm.deleteMenuText", menu.Name) + (users > 0 ? "\n\n" + Loc.T("qm.deleteMenuUsed", users) : ""),
            PrimaryButtonText = Loc.T("common.delete"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        var index = MenuList.SelectedIndex;
        Config.QuickMenus.Remove(menu);
        _app.Achievements.Increment(Stat.MenusDeleted);
        MenuList.SelectedIndex = _view.IsEmpty ? -1 : Math.Clamp(index, 0, _view.Count - 1);
    }

    private void OnTest(object sender, RoutedEventArgs e)
    {
        if (_menu is not null) _app.QuickMenus.Show(_menu.Id, ForegroundWindow.Capture());
    }

    // ── palette ───────────────────────────────────────────────────────────

    private void OnCommandsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshPalette();

    private void OnPaletteSearch(object sender, TextChangedEventArgs e) => RefreshPalette();

    private void OnPaletteShowHidden(object sender, RoutedEventArgs e) => RefreshPalette();

    private void RefreshPalette()
    {
        var q = PaletteSearch?.Text?.Trim() ?? string.Empty;
        var hidden = ShowHiddenToggle?.IsChecked == true;
        Palette.ItemsSource = Config.Commands
            .Where(c => hidden || !c.Hidden)
            .Where(c => q.Length == 0 || c.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void OnPaletteMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragSource = (e.OriginalSource as FrameworkElement)?.DataContext as Command;
    }

    private void OnPaletteMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSource is not Command cmd || !MovedEnough(e)) return;
        _dragSource = null;
        DragDrop.DoDragDrop(Palette, new DataObject(CommandFormat, cmd.Id), DragDropEffects.Copy);
    }

    private void OnPaletteDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not Command cmd) return;
        var (list, index) = DefaultInsertionPoint();
        Mutate(() => InsertNew(list, index, QuickMenuOps.NewCommandItem(cmd)));
    }

    private void OnChipMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragSource = (sender as FrameworkElement)?.Tag as string;
    }

    private void OnChipMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSource is not string kind || !MovedEnough(e)) return;
        _dragSource = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(KindFormat, kind), DragDropEffects.Copy);
    }

    private void OnQuickCommand(object sender, RoutedEventArgs e)
    {
        if (QuickCommandDialog.Create(Window.GetWindow(this)) is not { } cmd) return;
        Config.Commands.Add(cmd);
        _app.Achievements.Increment(Stat.CommandsCreated);
        var (list, index) = DefaultInsertionPoint();
        Mutate(() => InsertNew(list, index, QuickMenuOps.NewCommandItem(cmd)));
    }

    // ── building ──────────────────────────────────────────────────────────

    private void OnMenuModified(object? sender, EventArgs e)
    {
        if (_restoring || _rebuildQueued) return;
        _rebuildQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _rebuildQueued = false;
            Rebuild();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>The rows grow with the menu's size like the real menu does (QuickMenuWindow.ApplySize).</summary>
    private void ApplySize(int size)
    {
        size = Math.Clamp(size, 0, 5);
        Resources["Qm.FontSize"] = 12.0 + size;
        Resources["Qm.IconSize"] = 16.0 + size * 2;
        Resources["Qm.RowHeight"] = 28.0 + size * 2.5;
        if ((int)Math.Round(SizeSlider.Value) != size) SizeSlider.Value = size;
        SizeText.Text = Loc.T($"qm.size{size}");
    }

    private void Rebuild()
    {
        ApplySize(_menu?.Size ?? 1);
        var columns = new List<QmColumn>();
        if (_menu is { } menu)
        {
            var root = new QmColumn(0, null, menu.Items, menu.Name);
            Fill(root);
            columns.Add(root);

            var list = menu.Items;
            for (var i = 0; i < _path.Count; i++)
            {
                var owner = list.FirstOrDefault(x => x.Id == _path[i]);
                if (owner is null || owner.IsSeparator)
                {
                    _path.RemoveRange(i, _path.Count - i);
                    break;
                }
                var col = new QmColumn(i + 1, owner, owner.Children, columns[^1].Rows.First(r => r.Item == owner).Label);
                Fill(col);
                columns.Add(col);
                list = owner.Children;
            }
            if (_selectedId is not null && !menu.Descendants().Any(d => d.Id == _selectedId)) _selectedId = null;
        }

        _columns = columns;
        ColumnsControl.ItemsSource = columns;
        if (columns.Count > _lastColumnCount)
            Dispatcher.BeginInvoke(() => Canvas.ScrollToRightEnd(), System.Windows.Threading.DispatcherPriority.Loaded);
        _lastColumnCount = columns.Count;
    }

    private void Fill(QmColumn column)
    {
        foreach (var item in column.List)
        {
            column.Rows.Add(new QmRow(item, Config, column)
            {
                IsSelected = item.Id == _selectedId,
                IsOnPath = _path.Contains(item.Id),
                IsFound = _found is not null && item.CommandId == _found,
            });
        }
    }

    /// <summary>Slide-in for columns that just appeared.</summary>
    private void OnColumnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: QmColumn col } fe || col.Level == 0) return;
        if (col.Level < _lastColumnCount - 1) return;
        var t = new TranslateTransform();
        fe.RenderTransform = t;
        fe.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-14, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = EaseOut });
    }

    // ── selection ─────────────────────────────────────────────────────────

    private QmRow? SelectedRow => _columns.SelectMany(c => c.Rows).FirstOrDefault(r => r.Item.Id == _selectedId);

    private void SelectRow(QmRow row, bool openChildren = true)
    {
        _selectedId = row.Item.Id;
        var level = row.Column.Level;
        var newPath = _path.Take(level).ToList();
        if (openChildren && !row.IsSeparator && (row.HasChildren || row.IsSubmenu || row.IsTitle)) newPath.Add(row.Item.Id);

        if (newPath.SequenceEqual(_path))
        {
            foreach (var r in _columns.SelectMany(c => c.Rows)) r.IsSelected = r.Item.Id == _selectedId;
            return;
        }
        _path.Clear();
        _path.AddRange(newPath);
        Rebuild();
    }

    private void SelectItem(QuickMenuItem item, bool openChildren = false)
    {
        if (_menu is null) return;
        // Open the path that leads to the item.
        var chain = new List<QuickMenuItem>();
        var current = item;
        while (QuickMenuOps.FindParent(_menu, current) is { Owner: { } owner })
        {
            chain.Insert(0, owner);
            current = owner;
        }
        _path.Clear();
        _path.AddRange(chain.Select(c => c.Id));
        if (openChildren) _path.Add(item.Id);
        _selectedId = item.Id;
        Rebuild();
    }

    private void OnCanvasClick(object sender, MouseButtonEventArgs e)
    {
        Canvas.Focus();
        if (e.OriginalSource is ScrollViewer or Border { DataContext: not QmRow })
        {
            _selectedId = null;
            foreach (var r in _columns.SelectMany(c => c.Rows)) r.IsSelected = false;
        }
    }

    // ── row mouse ─────────────────────────────────────────────────────────

    private static QmRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as QmRow;

    private void OnRowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (RowOf(sender) is not { } row || row.IsRenaming) return;
        _dragStart = e.GetPosition(this);
        _dragSource = row;
        Canvas.Focus();
        if (e.ClickCount == 2 && !row.IsSeparator)
        {
            BeginRename(row);
            e.Handled = true;
        }
    }

    private void OnRowMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSource is not QmRow row || row.IsRenaming || !MovedEnough(e)) return;
        _dragSource = null;
        SelectRow(row, openChildren: false);
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(ItemFormat, row.Item.Id), DragDropEffects.Move);
        ClearDropHints();
    }

    private void OnRowMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragSource is QmRow row && RowOf(sender) == row) SelectRow(row);
        _dragSource = null;
    }

    private bool MovedEnough(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        return Math.Abs(p.X - _dragStart.X) > SystemParameters.MinimumHorizontalDragDistance ||
               Math.Abs(p.Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance;
    }

    // ── drag & drop ───────────────────────────────────────────────────────

    private bool CanAccept(IDataObject data) =>
        data.GetDataPresent(ItemFormat) || data.GetDataPresent(CommandFormat) || data.GetDataPresent(KindFormat) ||
        data.GetDataPresent(DataFormats.FileDrop) || ExternalUrl(data) is not null;

    private static string? ExternalUrl(IDataObject data)
    {
        foreach (var format in new[] { "UniformResourceLocatorW", "UniformResourceLocator", DataFormats.UnicodeText, DataFormats.Text })
        {
            if (!data.GetDataPresent(format)) continue;
            var value = data.GetData(format) switch
            {
                string s => s,
                System.IO.MemoryStream ms => format.EndsWith('W')
                    ? System.Text.Encoding.Unicode.GetString(ms.ToArray())
                    : System.Text.Encoding.ASCII.GetString(ms.ToArray()),
                _ => null,
            };
            value = value?.Split('\0', '\r', '\n')[0].Trim();
            if (!string.IsNullOrEmpty(value) && (value.Contains("://", StringComparison.Ordinal) || value.StartsWith("www.", StringComparison.OrdinalIgnoreCase)))
                return value;
        }
        return null;
    }

    private QuickMenuItem? DraggedItem(IDataObject data) =>
        data.GetData(ItemFormat) is string id ? _menu?.Descendants().FirstOrDefault(i => i.Id == id) : null;

    private DropHint ComputeHint(QmRow row, FrameworkElement element, DragEventArgs e)
    {
        var y = e.GetPosition(element).Y / Math.Max(1, element.ActualHeight);
        var dragged = DraggedItem(e.Data);
        var canInto = !row.IsSeparator && (dragged is null || !(ReferenceEquals(dragged, row.Item) || QuickMenuOps.IsAncestorOf(dragged, row.Item)));
        if (row.IsSeparator || !canInto) return y < 0.5 ? DropHint.Before : DropHint.After;
        return y < 0.28 ? DropHint.Before : y > 0.72 ? DropHint.After : DropHint.Into;
    }

    private void OnRowDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (RowOf(sender) is not { } row || !CanAccept(e.Data))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        var hint = ComputeHint(row, (FrameworkElement)sender, e);
        var dragged = DraggedItem(e.Data);
        if (dragged is not null && ReferenceEquals(dragged, row.Item)) hint = DropHint.None;
        if (row.DropHint != hint)
        {
            ClearDropHints();
            row.DropHint = hint;
        }
        e.Effects = hint == DropHint.None ? DragDropEffects.None : dragged is not null ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    private void OnRowDragLeave(object sender, DragEventArgs e)
    {
        if (RowOf(sender) is { } row) row.DropHint = DropHint.None;
    }

    private void OnRowDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (RowOf(sender) is not { } row) return;
        var hint = row.DropHint;
        ClearDropHints();
        if (hint == DropHint.None) return;
        var index = row.Column.List.IndexOf(row.Item);
        var (list, at) = hint switch
        {
            DropHint.Before => (row.Column.List, index),
            DropHint.After => (row.Column.List, index + 1),
            _ => (row.Item.Children, row.Item.Children.Count),
        };
        HandleDrop(e.Data, list, at, intoOwner: hint == DropHint.Into ? row.Item : null);
    }

    private void OnColumnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { DataContext: QmColumn col } || !CanAccept(e.Data))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        var dragged = DraggedItem(e.Data);
        e.Effects = dragged is not null && _menu is not null && QuickMenuOps.WouldCreateCycle(_menu, dragged, col.List)
            ? DragDropEffects.None
            : dragged is not null ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    private void OnColumnDragLeave(object sender, DragEventArgs e) { }

    private void OnColumnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearDropHints();
        if (sender is FrameworkElement { DataContext: QmColumn col }) HandleDrop(e.Data, col.List, col.List.Count, null);
    }

    private void OnCanvasDragOver(object sender, DragEventArgs e)
    {
        // Dropping on the empty canvas appends to the deepest open level.
        e.Effects = CanAccept(e.Data) ? (DraggedItem(e.Data) is not null ? DragDropEffects.Move : DragDropEffects.Copy) : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCanvasDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearDropHints();
        if (_columns.Count == 0) return;
        var col = _columns[^1];
        HandleDrop(e.Data, col.List, col.List.Count, null);
    }

    private void ClearDropHints()
    {
        foreach (var r in _columns.SelectMany(c => c.Rows))
            if (r.DropHint != DropHint.None) r.DropHint = DropHint.None;
    }

    private void HandleDrop(IDataObject data, ObservableCollection<QuickMenuItem> list, int index, QuickMenuItem? intoOwner)
    {
        if (_menu is not { } menu) return;
        QuickMenuItem? select = null;

        if (DraggedItem(data) is { } item)
        {
            if (QuickMenuOps.WouldCreateCycle(menu, item, list)) return;
            Mutate(() => QuickMenuOps.Move(menu, item, list, index));
            select = item;
        }
        else if (data.GetData(CommandFormat) is string commandId && Config.FindCommand(commandId) is { } cmd)
        {
            select = QuickMenuOps.NewCommandItem(cmd);
            Mutate(() => list.Insert(Math.Clamp(index, 0, list.Count), select));
        }
        else if (data.GetData(KindFormat) is string kindName && Enum.TryParse<QuickMenuItemKind>(kindName, out var kind))
        {
            select = NewItemOfKind(kind);
            Mutate(() => list.Insert(Math.Clamp(index, 0, list.Count), select));
        }
        else if (data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            Mutate(() =>
            {
                var at = Math.Clamp(index, 0, list.Count);
                foreach (var file in files)
                {
                    if (CommandFactory.FromTarget(file) is not { } newCmd) continue;
                    Config.Commands.Add(newCmd);
                    _app.Achievements.Increment(Stat.CommandsCreated);
                    select = QuickMenuOps.NewCommandItem(newCmd);
                    list.Insert(at++, select);
                }
            });
            _app.Ui.ShowOsd(Loc.T("qm.commandsCreated", files.Length), OsdKind.Success);
        }
        else if (ExternalUrl(data) is { } url && CommandFactory.FromTarget(url) is { } urlCmd)
        {
            Config.Commands.Add(urlCmd);
            _app.Achievements.Increment(Stat.CommandsCreated);
            select = QuickMenuOps.NewCommandItem(urlCmd);
            Mutate(() => list.Insert(Math.Clamp(index, 0, list.Count), select));
            _app.Ui.ShowOsd(Loc.T("qm.commandsCreated", 1), OsdKind.Success);
        }

        if (select is null) return;
        SelectItem(select);
        if (intoOwner is not null)
        {
            // Show the submenu that just received the item.
            SelectItem(select);
        }
        if (select.Kind is QuickMenuItemKind.Submenu or QuickMenuItemKind.Title && select.Label is not null &&
            SelectedRow is { } newRow)
            BeginRename(newRow);
    }

    private static QuickMenuItem NewItemOfKind(QuickMenuItemKind kind) => kind switch
    {
        QuickMenuItemKind.Separator => QuickMenuOps.NewSeparator(),
        QuickMenuItemKind.Title => new QuickMenuItem { Kind = QuickMenuItemKind.Title, Label = Loc.T("qm.newTitle") },
        _ => new QuickMenuItem { Kind = QuickMenuItemKind.Submenu, Label = Loc.T("qm.newSubmenu") },
    };

    // ── mutations, undo/redo ──────────────────────────────────────────────

    /// <summary>Runs a change to the menu, recording an undo snapshot first.</summary>
    private void Mutate(Action change)
    {
        if (_menu is null) return;
        _undo.Push(Json.Serialize(_menu.Items));
        if (_undo.Count > MaxUndo) TrimUndo();
        _redo.Clear();
        change();
        UpdateUndoButtons();
    }

    private void TrimUndo()
    {
        var keep = _undo.Take(MaxUndo).Reverse().ToList();
        _undo.Clear();
        foreach (var s in keep) _undo.Push(s);
    }

    private void OnUndo(object sender, RoutedEventArgs e) => Undo();
    private void OnRedo(object sender, RoutedEventArgs e) => Redo();

    private void Undo()
    {
        if (_menu is null || _undo.Count == 0) return;
        _redo.Push(Json.Serialize(_menu.Items));
        Restore(_undo.Pop());
    }

    private void Redo()
    {
        if (_menu is null || _redo.Count == 0) return;
        _undo.Push(Json.Serialize(_menu.Items));
        Restore(_redo.Pop());
    }

    private void Restore(string json)
    {
        if (_menu is null) return;
        var items = Json.Deserialize<List<QuickMenuItem>>(json) ?? [];
        _restoring = true;
        try
        {
            _app.Store.Batch(() =>
            {
                _menu.Items.Clear();
                foreach (var i in items) _menu.Items.Add(i);
            });
        }
        finally
        {
            _restoring = false;
        }
        UpdateUndoButtons();
        Rebuild();
    }

    private void UpdateUndoButtons()
    {
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
    }

    private (ObservableCollection<QuickMenuItem> List, int Index) DefaultInsertionPoint()
    {
        if (SelectedRow is { } row) return (row.Column.List, row.Column.List.IndexOf(row.Item) + 1);
        var col = _columns.Count > 0 ? _columns[^1] : null;
        return col is null ? (_menu!.Items, _menu.Items.Count) : (col.List, col.List.Count);
    }

    private void InsertNew(ObservableCollection<QuickMenuItem> list, int index, QuickMenuItem item)
    {
        list.Insert(Math.Clamp(index, 0, list.Count), item);
        Dispatcher.BeginInvoke(() => SelectItem(item), System.Windows.Threading.DispatcherPriority.Background);
    }

    // ── inline rename ─────────────────────────────────────────────────────

    private void BeginRename(QmRow row)
    {
        if (row.IsSeparator) return;
        foreach (var r in _columns.SelectMany(c => c.Rows)) r.IsRenaming = false;
        row.EditText = row.Label;
        row.IsRenaming = true;
    }

    private void OnRenameVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box)
            Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnRenameKey(object sender, KeyEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        if (e.Key == Key.Enter) { CommitRename(row); e.Handled = true; }
        else if (e.Key == Key.Escape) { row.IsRenaming = false; e.Handled = true; Canvas.Focus(); }
    }

    private void OnRenameLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (RowOf(sender) is { IsRenaming: true } row) CommitRename(row);
    }

    private void CommitRename(QmRow row)
    {
        row.IsRenaming = false;
        var text = row.EditText.Trim();
        string? label = text.Length == 0 || row.Command is not null && text == row.Command.Name ? null : text;
        if (row.Item.Kind != QuickMenuItemKind.Command && label is null) label = row.Label;
        if (label != row.Item.Label) Mutate(() => row.Item.Label = label);
        Canvas.Focus();
    }

    // ── context menus ─────────────────────────────────────────────────────

    private void OnRowRightClick(object sender, MouseButtonEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        e.Handled = true;
        SelectRow(row, openChildren: false);
        var menu = new ContextMenu();
        var index = row.Column.List.IndexOf(row.Item);

        menu.Items.Add(AddMenu(Loc.T("qm.addAfter"), row.Column.List, index + 1));
        if (!row.IsSeparator) menu.Items.Add(AddMenu(Loc.T("qm.addInside"), row.Item.Children, row.Item.Children.Count, row.Item));
        menu.Items.Add(new Separator());

        if (!row.IsSeparator)
        {
            menu.Items.Add(Item(Loc.T("qm.rename"), "Rename24", () => BeginRename(row), "F2"));
            if (row.HasCustomLabel) menu.Items.Add(Item(Loc.T("qm.resetName"), "ArrowUndo24", () => Mutate(() => row.Item.Label = null)));
            menu.Items.Add(Item(Loc.T("qm.changeIcon"), "Image24", () =>
            {
                if (Dialogs.IconPicker.Pick(Window.GetWindow(this), row.Item.Icon, "qm-" + row.Item.Id) is not { } icon) return;
                Mutate(() => row.Item.Icon = icon.Length == 0 ? null : icon);
                if (icon.Length > 0) _app.Achievements.Increment(Stat.IconsChanged);
            }));
            if (row.Item.Icon is not null) menu.Items.Add(Item(Loc.T("qm.resetIcon"), "SubtractCircle24", () => Mutate(() => row.Item.Icon = null)));
        }
        if (row.Command is { } cmd)
        {
            menu.Items.Add(Item(Loc.T("qm.editCommand"), "Edit24", () => _app.ShowMainWindow(w => w.OpenCommand(cmd.Id))));
            menu.Items.Add(Item(Loc.T("qm.runCommand"), "Play24", () => _ = _app.Engine.RunAsync(cmd, TriggerSource.Manual)));
        }
        if (row.IsMissing || row.Command is not null)
            menu.Items.Add(Item(Loc.T("qm.changeCommand"), "Flash24", () =>
            {
                if (CommandPicker.Pick(Window.GetWindow(this), Config.Commands).FirstOrDefault() is { } picked)
                    Mutate(() => row.Item.CommandId = picked.Id);
            }));
        if (row.IsSubmenu || row.IsTitle)
        {
            var convert = new MenuItem { Header = Loc.T("qm.convertTo") };
            convert.Items.Add(Item(Loc.T("qm.kind.Submenu"), "FolderAdd24", () => Mutate(() => row.Item.Kind = QuickMenuItemKind.Submenu), enabled: !row.IsSubmenu));
            convert.Items.Add(Item(Loc.T("qm.kind.Title"), "TextHeader124", () => Mutate(() => row.Item.Kind = QuickMenuItemKind.Title), enabled: !row.IsTitle));
            menu.Items.Add(convert);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("common.duplicate"), "Copy24", () => Duplicate(row), "Ctrl+D"));
        menu.Items.Add(Item(Loc.T("qm.cut"), "Cut24", () => _clipboard = (row.Item, true), "Ctrl+X"));
        menu.Items.Add(Item(Loc.T("qm.copy"), "Copy24", () => _clipboard = (row.Item, false), "Ctrl+C"));
        menu.Items.Add(Item(Loc.T("qm.paste"), "ClipboardPaste24", () => Paste(row.Column.List, index + 1), "Ctrl+V", _clipboard is not null));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("common.delete"), "Delete24", () => Delete(row), "Supr"));

        // Selecting may rebuild the rows, so anchor the menu to the page at the mouse position.
        menu.PlacementTarget = this;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void OnColumnRightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || sender is not FrameworkElement { DataContext: QmColumn col } fe) return;
        e.Handled = true;
        var cm = new ContextMenu();
        FillAddItems(cm, col.List, col.List.Count, col.Owner);
        cm.Items.Add(new Separator());
        cm.Items.Add(Item(Loc.T("qm.paste"), "ClipboardPaste24", () => Paste(col.List, col.List.Count), "Ctrl+V", _clipboard is not null));
        cm.PlacementTarget = fe;
        cm.IsOpen = true;
    }

    private void OnColumnAdd(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: QmColumn col } fe) return;
        var cm = new ContextMenu { PlacementTarget = fe, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        FillAddItems(cm, col.List, col.List.Count, col.Owner);
        cm.IsOpen = true;
    }

    /// <summary>"Add" submenu: existing command, quick command, submenu, title, separator.</summary>
    private MenuItem AddMenu(string header, ObservableCollection<QuickMenuItem> list, int index, QuickMenuItem? owner = null)
    {
        var add = new MenuItem { Header = header, Icon = new SymbolIcon { Symbol = SymbolRegular.Add24 } };
        FillAddItems(add, list, index, owner);
        return add;
    }

    private void FillAddItems(ItemsControl add, ObservableCollection<QuickMenuItem> list, int index, QuickMenuItem? owner)
    {
        void Done(QuickMenuItem item)
        {
            if (owner is not null && !_path.Contains(owner.Id)) SelectItem(owner, openChildren: true);
            SelectItem(item);
            if (item.Kind is QuickMenuItemKind.Submenu or QuickMenuItemKind.Title && SelectedRow is { } r) BeginRename(r);
        }

        add.Items.Add(Item(Loc.T("qm.existingCommand"), "Flash24", () =>
        {
            var picked = CommandPicker.Pick(Window.GetWindow(this), Config.Commands);
            if (picked.Count == 0) return;
            QuickMenuItem? last = null;
            Mutate(() =>
            {
                var at = Math.Clamp(index, 0, list.Count);
                foreach (var c in picked)
                {
                    last = QuickMenuOps.NewCommandItem(c);
                    list.Insert(at++, last);
                }
            });
            if (last is not null) Done(last);
        }));
        add.Items.Add(Item(Loc.T("qm.quickCommand") + "…", "DocumentAdd24", () =>
        {
            if (QuickCommandDialog.Create(Window.GetWindow(this)) is not { } cmd) return;
            Config.Commands.Add(cmd);
            _app.Achievements.Increment(Stat.CommandsCreated);
            var item = QuickMenuOps.NewCommandItem(cmd);
            Mutate(() => list.Insert(Math.Clamp(index, 0, list.Count), item));
            Done(item);
        }));
        add.Items.Add(new Separator());
        foreach (var kind in new[] { QuickMenuItemKind.Submenu, QuickMenuItemKind.Title, QuickMenuItemKind.Separator })
        {
            var symbol = kind switch { QuickMenuItemKind.Submenu => "FolderAdd24", QuickMenuItemKind.Title => "TextHeader124", _ => "SplitHorizontal24" };
            add.Items.Add(Item(Loc.T($"qm.kind.{kind}"), symbol, () =>
            {
                var item = NewItemOfKind(kind);
                Mutate(() => list.Insert(Math.Clamp(index, 0, list.Count), item));
                Done(item);
            }));
        }
    }

    private static MenuItem Item(string header, string symbol, Action action, string? gesture = null, bool enabled = true)
    {
        var mi = new MenuItem
        {
            Header = header,
            Icon = new SymbolIcon { Symbol = Enum.Parse<SymbolRegular>(symbol) },
            InputGestureText = gesture ?? string.Empty,
            IsEnabled = enabled,
        };
        mi.Click += (_, _) => action();
        return mi;
    }

    // ── item operations ───────────────────────────────────────────────────

    private void Delete(QmRow row)
    {
        var list = row.Column.List;
        var index = list.IndexOf(row.Item);
        Mutate(() => list.Remove(row.Item));
        if (list.Count > 0) SelectItem(list[Math.Clamp(index, 0, list.Count - 1)]);
        else if (row.Column.Owner is { } owner) SelectItem(owner, openChildren: true);
    }

    private void Duplicate(QmRow row)
    {
        var copy = row.Item.DeepClone();
        var list = row.Column.List;
        Mutate(() => list.Insert(list.IndexOf(row.Item) + 1, copy));
        SelectItem(copy);
    }

    private void Paste(ObservableCollection<QuickMenuItem> list, int index)
    {
        if (_clipboard is not { } clip || _menu is null) return;
        if (clip.Cut)
        {
            if (QuickMenuOps.WouldCreateCycle(_menu, clip.Item, list)) return;
            Mutate(() => QuickMenuOps.Move(_menu, clip.Item, list, index));
            SelectItem(clip.Item);
            _clipboard = null;
        }
        else
        {
            var copy = clip.Item.DeepClone();
            Mutate(() => list.Insert(Math.Clamp(index, 0, list.Count), copy));
            SelectItem(copy);
        }
    }

    // ── keyboard ──────────────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox || Keyboard.FocusedElement is TextBox or ComboBox) return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var alt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var row = SelectedRow;

        switch (key)
        {
            case Key.Z when ctrl: Undo(); break;
            case Key.Y when ctrl: Redo(); break;
            case Key.C when ctrl && row is not null: _clipboard = (row.Item, false); break;
            case Key.X when ctrl && row is not null: _clipboard = (row.Item, true); break;
            case Key.V when ctrl:
                var (list, index) = DefaultInsertionPoint();
                Paste(list, index);
                break;
            case Key.D when ctrl && row is not null: Duplicate(row); break;
            case Key.Delete when row is not null: Delete(row); break;
            case Key.F2 or Key.Enter when row is not null: BeginRename(row); break;
            case Key.Up when alt && row is not null: MoveWithin(row, -1); break;
            case Key.Down when alt && row is not null: MoveWithin(row, +1); break;
            case Key.Up: Step(row, -1); break;
            case Key.Down: Step(row, +1); break;
            case Key.Right when row is not null:
                if (row.Item.Children.Count > 0) SelectItem(row.Item.Children[0]);
                else SelectRow(row);
                break;
            case Key.Left when row?.Column.Owner is { } owner: SelectItem(owner, openChildren: true); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Step(QmRow? row, int delta)
    {
        if (row is null)
        {
            if (_columns.FirstOrDefault()?.Rows.FirstOrDefault() is { } first) SelectRow(first);
            return;
        }
        var rows = row.Column.Rows;
        var i = rows.IndexOf(row) + delta;
        if (i >= 0 && i < rows.Count) SelectRow(rows[i]);
    }

    private void MoveWithin(QmRow row, int delta)
    {
        var list = row.Column.List;
        var i = list.IndexOf(row.Item);
        var j = i + delta;
        if (j < 0 || j >= list.Count) return;
        Mutate(() => list.Move(i, j));
    }
}

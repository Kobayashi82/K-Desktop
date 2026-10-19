using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using KDesktop.Core;
using KDesktop.Core.Achievements;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Services;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace KDesktop.Views.Pages;

public partial class CommandsPage : UserControl, IDisposable
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private readonly App _app = App.Current;
    private readonly ListCollectionView _view;
    private Command? _selected;

    private AppConfig Config => _app.Config;

    public CommandsPage()
    {
        InitializeComponent();
        _view = new ListCollectionView(Config.Commands) { Filter = Filter, IsLiveSorting = true, IsLiveFiltering = true };
        _view.SortDescriptions.Add(new SortDescription(nameof(Command.Name), ListSortDirection.Ascending));
        _view.LiveSortingProperties.Add(nameof(Command.Name));
        _view.LiveFilteringProperties.Add(nameof(Command.Hidden));
        CommandList.ItemsSource = _view;
        ShowHiddenToggle.IsChecked = Config.Settings.ShowHiddenCommands;
        _menuFilter ??= string.IsNullOrEmpty(Config.Settings.CommandsMenuFilter) ? FilterNone : Config.Settings.CommandsMenuFilter;
        Config.Commands.CollectionChanged += OnCommandsChanged;
        IconProvider.StaticPropertyChanged += OnIconsChanged;
        UpdateCount();
        Loaded += (_, _) =>
        {
            // The QuickMenus may have changed on their page meanwhile.
            FillMenuFilter();
            if (CommandList.SelectedItem is null && !_view.IsEmpty) CommandList.SelectedIndex = 0;
        };
        Unloaded += (_, _) =>
        {
            HotkeyCapture.CancelActive();
            EnsureName(_selected);
        };
    }

    public void Dispose()
    {
        HotkeyCapture.CancelActive();
        EnsureName(_selected);
        Config.Commands.CollectionChanged -= OnCommandsChanged;
        IconProvider.StaticPropertyChanged -= OnIconsChanged;
        if (_selected is not null) _selected.PropertyChanged -= OnSelectedChanged;
        _view.DetachFromSourceCollection();
    }

    // ── list ─────────────────────────────────────────────────────────────

    // ── QuickMenu filter ─────────────────────────────────────────────────

    private const string FilterNone = "none", FilterAll = "all";

    private sealed record MenuFilterOption(string Key, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>
    /// Chosen filter: "none" (commands in no QuickMenu, the default), "all", or a QuickMenu id. Starts with the one
    /// chosen last time (Settings.CommandsMenuFilter).
    /// </summary>
    private static string? _menuFilter;

    /// <summary>Ids of the commands the filter is about (those of every menu, or of the chosen one); null for "all".</summary>
    private HashSet<string>? _menuCommands;

    private bool _fillingFilter;

    private void FillMenuFilter()
    {
        _fillingFilter = true;
        var options = new List<MenuFilterOption>
        {
            new(FilterNone, Loc.T("commands.filterNone")),
            new(FilterAll, Loc.T("commands.filterAll")),
        };
        options.AddRange(Config.QuickMenus.Select(m => new MenuFilterOption(m.Id, Loc.T("commands.filterMenu", m.Name))));
        if (options.All(o => o.Key != _menuFilter)) _menuFilter = FilterNone;
        MenuFilter.ItemsSource = options;
        MenuFilter.SelectedItem = options.First(o => o.Key == _menuFilter);
        _fillingFilter = false;
        ApplyMenuFilter();
    }

    private void OnMenuFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingFilter || MenuFilter.SelectedItem is not MenuFilterOption option) return;
        _menuFilter = option.Key;
        Config.Settings.CommandsMenuFilter = option.Key;
        ApplyMenuFilter();
    }

    private void ApplyMenuFilter()
    {
        IEnumerable<QuickMenu> menus = _menuFilter switch
        {
            FilterAll => [],
            FilterNone => Config.QuickMenus,
            _ => Config.QuickMenus.Where(m => m.Id == _menuFilter),
        };
        _menuCommands = _menuFilter == FilterAll ? null
            : menus.SelectMany(m => m.Descendants()).Select(i => i.CommandId).OfType<string>().ToHashSet();
        _view.Refresh();
    }

    /// <summary>Clears the search, and the QuickMenu filter when it hides <paramref name="cmd"/>, so it can be selected.</summary>
    private void ClearFiltersFor(Command cmd)
    {
        if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = string.Empty;
        if (PassesMenuFilter(cmd)) return;
        _menuFilter = FilterAll;
        FillMenuFilter();
    }

    private bool PassesMenuFilter(Command c) => _menuCommands is not { } ids || (_menuFilter == FilterNone ? !ids.Contains(c.Id) : ids.Contains(c.Id));

    private bool Filter(object obj)
    {
        if (obj is not Command c) return false;
        if (c.Hidden && ShowHiddenToggle.IsChecked != true) return false;
        if (!PassesMenuFilter(c) && !ReferenceEquals(c, _selected)) return false;
        var q = SearchBox?.Text?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        return c.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
               || c.Hotkeys.Any(h => h.Contains(q, StringComparison.OrdinalIgnoreCase))
               || c.Actions.Any(a => a.Summary.Contains(q, StringComparison.CurrentCultureIgnoreCase));
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _view.Refresh();

    private void OnShowHidden(object sender, RoutedEventArgs e)
    {
        Config.Settings.ShowHiddenCommands = ShowHiddenToggle.IsChecked == true;
        _view.Refresh();
    }

    private void OnCommandsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateCount();

    private void UpdateCount() => CountText.Text = Config.Commands.Count.ToString();

    public void Select(string id)
    {
        if (Config.FindCommand(id) is not { } cmd) return;
        if (cmd.Hidden && ShowHiddenToggle.IsChecked != true)
        {
            ShowHiddenToggle.IsChecked = true;
            _view.Refresh();
        }
        ClearFiltersFor(cmd);
        CommandList.SelectedItem = cmd;
        CommandList.ScrollIntoView(cmd);
    }

    private void OnCommandSelected(object sender, SelectionChangedEventArgs e)
    {
        HotkeyCapture.CancelActive();
        if (_selected is not null) _selected.PropertyChanged -= OnSelectedChanged;
        var previous = _selected;
        if (previous is not null && string.IsNullOrWhiteSpace(previous.Name)) EnsureName(previous);
        _selected = CommandList.SelectedItem as Command;
        NameError.Visibility = Visibility.Collapsed;
        // The selected command was just hidden: it left the list, so the next one (or the previous, at the end) takes its place.
        if (_selected is null && previous is { Hidden: true } && !_view.IsEmpty)
        {
            var index = Math.Min(_hiddenIndex, _view.Count - 1);
            Dispatcher.BeginInvoke(() => CommandList.SelectedIndex = index, System.Windows.Threading.DispatcherPriority.Background);
        }
        // The filter keeps the selected command listed while the QuickMenu filter would drop it: once another one
        // is chosen, it goes away.
        if (previous is not null && !ReferenceEquals(previous, _selected) && !Filter(previous))
            Dispatcher.BeginInvoke(() => _view.Refresh(), System.Windows.Threading.DispatcherPriority.Background);
        Editor.DataContext = _selected;
        HotkeyWarning.Visibility = Visibility.Collapsed;

        if (_selected is null)
        {
            EditorScroll.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            return;
        }
        _selected.PropertyChanged += OnSelectedChanged;
        foreach (var action in _selected.Actions)
        {
            action.PropertyChanged -= OnActionReferenceChanged;
            action.PropertyChanged += OnActionReferenceChanged;
        }
        EmptyState.Visibility = Visibility.Collapsed;
        EditorScroll.Visibility = Visibility.Visible;
        EditorScroll.ScrollToTop();
        RefreshHeader();

        // Small entrance animation for the editor.
        Editor.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(180)));
        EditorShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
    }

    /// <summary>Position of the selected command when it was hidden, to select its neighbour once it leaves the list.</summary>
    private int _hiddenIndex;

    private void OnSelectedChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Runs before the live filter takes it out of the list.
        if (e.PropertyName is nameof(Command.Hidden) && _selected is { Hidden: true }) _hiddenIndex = Math.Max(0, CommandList.SelectedIndex);
        if (e.PropertyName is nameof(Command.Icon) or nameof(Command.PrimaryAction)) RefreshHeader();
        if (e.PropertyName is nameof(Command.Name))
            NameError.Visibility = string.IsNullOrWhiteSpace(_selected?.Name) ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── name (never empty) ───────────────────────────────────────────────

    /// <summary>Name the command had when the name box got the focus: an emptied name goes back to it.</summary>
    private string? _nameBeforeEdit;

    private void OnNameFocus(object sender, KeyboardFocusChangedEventArgs e) => _nameBeforeEdit = _selected?.Name;

    private void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e) => EnsureName(NameBox.DataContext as Command);

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) EnsureName(_selected);
        if (e.Key == Key.Escape && _selected is { } cmd && !string.IsNullOrWhiteSpace(_nameBeforeEdit)) cmd.Name = _nameBeforeEdit;
    }

    private void EnsureName(Command? cmd)
    {
        if (cmd is null) return;
        if (string.IsNullOrWhiteSpace(cmd.Name))
            cmd.Name = !string.IsNullOrWhiteSpace(_nameBeforeEdit) ? _nameBeforeEdit : UniqueName(Loc.T("commands.newName"));
        else if (cmd.Name != cmd.Name.Trim()) cmd.Name = cmd.Name.Trim();
        if (ReferenceEquals(cmd, _selected)) NameError.Visibility = Visibility.Collapsed;
    }

    private void OnIconsChanged(object? sender, PropertyChangedEventArgs e) => RefreshHeader();

    private void RefreshHeader()
    {
        if (_selected is null) return;
        HeaderIcon.Source = IconProvider.ForCommand(_selected);
        NoActions.Visibility = _selected.Actions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshUsedBy();
    }

    /// <summary>«Eventos: «Al abrir Steam» · …» and «QuickMenus: «Juegos» · …», each one a link to it; a row only when there are some.</summary>
    private void RefreshUsedBy()
    {
        UsedByPanel.Children.Clear();
        UsedByPanel.Visibility = Visibility.Collapsed;
        if (_selected is not { } cmd) return;
        AddUsedByRow("commands.usedByEvents", Config.EventsUsing(cmd.Id).Select(rule =>
        {
            var id = rule.Id;
            var label = rule.Commands.Contains(cmd.Id) ? $"«{rule.Name}»" : Loc.T("commands.usedByEnd", rule.Name);
            return (label, (Action)(() => (Window.GetWindow(this) as MainWindow)?.OpenEvent(id)));
        }));
        AddUsedByRow("commands.usedByMenus", Config.QuickMenus.Where(m => m.Descendants().Any(i => i.CommandId == cmd.Id)).Select(menu =>
        {
            var id = menu.Id;
            var commandId = cmd.Id;
            return ($"«{menu.Name}»", (Action)(() => (Window.GetWindow(this) as MainWindow)?.OpenQuickMenu(id, commandId)));
        }));
        UsedByPanel.Visibility = UsedByPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddUsedByRow(string titleKey, IEnumerable<(string Label, Action Open)> links)
    {
        var text = new System.Windows.Controls.TextBlock { TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("Caption"), Margin = new Thickness(0, 0, 0, 4) };
        text.Inlines.Add(new System.Windows.Documents.Run(Loc.T(titleKey) + " "));
        var first = true;
        foreach (var (label, open) in links)
        {
            if (!first) text.Inlines.Add(new System.Windows.Documents.Run("  ·  "));
            first = false;
            var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(label)) { Foreground = (System.Windows.Media.Brush)FindResource("Kd.AccentText") };
            link.Click += (_, _) => open();
            text.Inlines.Add(link);
        }
        if (!first) UsedByPanel.Children.Add(text);
    }

    // ── command buttons ──────────────────────────────────────────────────

    private void OnNewCommand(object sender, RoutedEventArgs e)
    {
        var cmd = new Command { Name = UniqueName(Loc.T("commands.newName")) };
        _app.Achievements.Increment(Stat.CommandsCreated);
        Config.Commands.Add(cmd);
        ClearFiltersFor(cmd);
        CommandList.SelectedItem = cmd;
        CommandList.ScrollIntoView(cmd);
        Dispatcher.BeginInvoke(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (var i = 2; Config.Commands.Any(c => c.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)); i++) name = $"{baseName} {i}";
        return name;
    }

    private void OnDuplicateCommand(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var copy = _selected.Duplicate();
        copy.Name = UniqueName(_selected.Name);
        copy.Hotkeys.Clear(); // two commands with the same hotkey is rarely what you want
        _app.Achievements.Increment(Stat.CommandsCreated);
        Config.Commands.Add(copy);
        CommandList.SelectedItem = copy;
        CommandList.ScrollIntoView(copy);
    }

    private async void OnDeleteCommand(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } cmd) return;
        var inMenus = Config.QuickMenus.SelectMany(m => m.Descendants()).Count(i => i.CommandId == cmd.Id);
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("commands.deleteTitle"),
            Content = Loc.T("commands.deleteText", cmd.Name) + (inMenus > 0 ? "\n\n" + Loc.T("commands.deleteInMenus", inMenus) : ""),
            PrimaryButtonText = Loc.T("common.delete"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;

        var index = CommandList.SelectedIndex;
        _app.Store.Batch(() =>
        {
            foreach (var menu in Config.QuickMenus) RemoveReferences(menu.Items, cmd.Id);
            Config.Commands.Remove(cmd);
            _app.Achievements.Increment(Stat.CommandsDeleted);
        });
        if (!_view.IsEmpty) CommandList.SelectedIndex = Math.Clamp(index, 0, _view.Count - 1);
    }

    private static void RemoveReferences(IList<QuickMenuItem> items, string commandId)
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            RemoveReferences(item.Children, commandId);
            if (item.CommandId != commandId) continue;
            if (item.Children.Count > 0)
            {
                // Keep its children: the item becomes a plain submenu.
                item.Kind = QuickMenuItemKind.Submenu;
                item.CommandId = null;
                item.Label ??= "…";
            }
            else items.RemoveAt(i);
        }
    }

    private void OnRunCommand(object sender, RoutedEventArgs e)
    {
        if (_selected is { } cmd) _ = _app.Engine.RunAsync(cmd, TriggerSource.Manual);
    }

    // ── list context menu ────────────────────────────────────────────────

    private void OnCommandRightClick(object sender, MouseButtonEventArgs e)
    {
        if (ListContextMenu.SelectRow(CommandList, e) is not { DataContext: Command cmd } row) return;
        ListContextMenu.Open(row,
            ListContextMenu.Entry(Loc.T("commands.run"), SymbolRegular.Play24, () => OnRunCommand(this, e)),
            ListContextMenu.Entry(Loc.T("common.duplicate"), SymbolRegular.Copy24, () => OnDuplicateCommand(this, e)),
            ListContextMenu.Entry(Loc.T("commands.rename"), SymbolRegular.Rename24, () =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            }),
            new Separator(),
            ListContextMenu.Entry(cmd.Enabled ? Loc.T("commands.disable") : Loc.T("commands.enable"),
                cmd.Enabled ? SymbolRegular.Prohibited24 : SymbolRegular.CheckmarkCircle24, () => cmd.Enabled = !cmd.Enabled),
            ListContextMenu.Entry(cmd.Hidden ? Loc.T("commands.unhide") : Loc.T("commands.hide"),
                cmd.Hidden ? SymbolRegular.Eye24 : SymbolRegular.EyeOff24, () => cmd.Hidden = !cmd.Hidden),
            new Separator(),
            ListContextMenu.Entry(Loc.T("common.delete"), SymbolRegular.Delete24, () => OnDeleteCommand(this, e)));
    }

    private void OnCommandListKey(object sender, KeyEventArgs e)
    {
        if (_selected is null || e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase) return;
        switch (e.Key)
        {
            case Key.Delete: OnDeleteCommand(this, e); break;
            case Key.F2:
                NameBox.Focus();
                NameBox.SelectAll();
                break;
            default: return;
        }
        e.Handled = true;
    }

    // ── icon ─────────────────────────────────────────────────────────────

    private void OnChooseIcon(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        if (Dialogs.IconPicker.Pick(Window.GetWindow(this), _selected.Icon, "cmd-" + _selected.Id) is not { } icon) return;
        _selected.Icon = icon.Length == 0 ? null : icon;
        if (icon.Length > 0) _app.Achievements.Increment(Stat.IconsChanged);
    }

    private void OnResetIcon(object sender, RoutedEventArgs e)
    {
        if (_selected is not null) _selected.Icon = null;
    }

    // ── hotkeys ──────────────────────────────────────────────────────────

    private void OnAddHotkey(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } cmd) return;
        AddHotkeyButton.IsEnabled = false;
        CaptureChip.Visibility = Visibility.Visible;
        CaptureText.Text = Loc.T("commands.pressHotkey");
        HotkeyCapture.Start(
            preview => CaptureText.Text = preview.Length > 0 ? preview + "…" : Loc.T("commands.pressHotkey"),
            hotkey =>
            {
                CaptureChip.Visibility = Visibility.Collapsed;
                AddHotkeyButton.IsEnabled = true;
                if (hotkey is null) return;
                var normalized = Hotkey.Normalize(hotkey);
                if (!cmd.Hotkeys.Contains(normalized, Hotkey.Comparer)) cmd.Hotkeys.Add(normalized);
                ShowConflicts(cmd, normalized);
            },
            Window.GetWindow(this));
    }

    private void ShowConflicts(Command cmd, string hotkey)
    {
        // Same hotkey: both run when both are for every program or for the same one; a command limited to some
        // programs wins over one for every program there (HotkeyIndex.Resolve); different programs don't meet.
        static string[] Procs(Command c) => c.Processes.Select(ProcessNames.Normalize).Where(p => p.Length > 0).Distinct().ToArray();
        var mine = Procs(cmd);
        var others = new List<string>();
        var lines = new List<string>();
        foreach (var other in Config.Commands.Where(c => !ReferenceEquals(c, cmd) && c.Hotkeys.Contains(hotkey, Hotkey.Comparer)))
        {
            var theirs = Procs(other);
            if (mine.Length == 0 && theirs.Length == 0 || mine.Intersect(theirs).Any()) others.Add(other.Name);
            else if (mine.Length > 0 && theirs.Length == 0) lines.Add(Loc.T("commands.hotkeyWins", string.Join(", ", mine), other.Name));
            else if (mine.Length == 0 && theirs.Length > 0) lines.Add(Loc.T("commands.hotkeyLoses", string.Join(", ", theirs), other.Name));
        }
        if (Hotkey.Comparer.Equals(hotkey, Hotkey.Normalize(Config.Settings.ShowWindowHotkey)) ||
            Hotkey.Comparer.Equals(hotkey, Hotkey.Normalize(Config.Settings.ExitHotkey)) ||
            Hotkey.Comparer.Equals(hotkey, Hotkey.Normalize(Config.Settings.VoiceHotkey)) ||
            Hotkey.Comparer.Equals(hotkey, Hotkey.Normalize(Config.Settings.DisabledInHotkey)) ||
            Hotkey.Comparer.Equals(hotkey, Hotkey.Normalize(Config.Settings.PauseHotkey)) ||
            Hotkey.Comparer.Equals(hotkey, Hotkey.Normalize(Config.Settings.LauncherHotkey)))
            others.Add("K-Desktop");
        if (others.Count > 0) lines.Insert(0, Loc.T("commands.hotkeyConflict", hotkey, string.Join(", ", others)));
        HotkeyWarning.Text = string.Join(Environment.NewLine, lines);
        HotkeyWarning.Visibility = lines.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRemoveHotkey(object sender, ExecutedRoutedEventArgs e)
    {
        if (_selected is not null && e.Parameter is string hk) _selected.Hotkeys.Remove(hk);
        HotkeyWarning.Visibility = Visibility.Collapsed;
    }

    // ── actions ──────────────────────────────────────────────────────────

    private void OnAddAction(object sender, RoutedEventArgs e)
    {
        if (Dialogs.ActionPicker.Pick(Window.GetWindow(this)) is { } info) AddAction(info.Create());
    }

    private void AddAction(CommandAction action)
    {
        if (_selected is null) return;
        if (action is OpenQuickMenuAction q && Config.QuickMenus.FirstOrDefault() is { } first)
        {
            q.MenuId = first.Id;
            q.MenuName = first.Name;
        }
        if (action is PlayMacroAction pm && Config.Macros.FirstOrDefault() is { } macro)
        {
            pm.MacroId = macro.Id;
            pm.MacroName = macro.Name;
        }
        if (action is KBoardAction kb && Config.KBoards.FirstOrDefault() is { } profile)
        {
            kb.ProfileId = profile.Id;
            kb.ProfileName = profile.Name;
        }
        if (action is PowerPlanAction plan && PowerPlans.All() is { Count: > 0 } plans)
        {
            // The one that isn't active: switching to it is what one usually wants.
            var other = plans.FirstOrDefault(p => !p.IsActive) ?? plans[0];
            plan.PlanId = other.Key;
            plan.PlanName = other.Name;
        }
        if (action is DisplayAction display && DisplayTools.Monitors() is { Count: > 0 } monitors)
        {
            var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
            display.Monitor = primary.DeviceName;
            display.MonitorName = primary.Name;
            if (DisplayTools.CurrentMode(primary.DeviceName) is { } mode) display.ModeKey = mode.Key;
        }
        action.PropertyChanged += OnActionReferenceChanged;
        _selected.Actions.Add(action);
        _app.Achievements.Increment(Stat.ActionsCreated);
        ActionList.SelectedItem = action;
        RefreshHeader();
        Dispatcher.BeginInvoke(() =>
        {
            if (ActionList.ItemContainerGenerator.ContainerFromItem(action) is FrameworkElement fe) fe.BringIntoView();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Keeps the display names of menu/command references in sync with the selection.</summary>
    private void OnActionReferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (sender)
        {
            case OpenQuickMenuAction q when e.PropertyName == nameof(OpenQuickMenuAction.MenuId):
                q.MenuName = Config.FindMenu(q.MenuId)?.Name;
                break;
            case CommandControlAction c when e.PropertyName == nameof(CommandControlAction.CommandId):
                c.CommandName = Config.FindCommand(c.CommandId)?.Name;
                break;
            case CommandControlAction c when e.PropertyName == nameof(CommandControlAction.EventId):
                c.EventName = Config.FindEvent(c.EventId)?.Name;
                break;
            case PlayMacroAction m when e.PropertyName == nameof(PlayMacroAction.MacroId):
                m.MacroName = Config.FindMacro(m.MacroId)?.Name;
                break;
            case KBoardAction k when e.PropertyName == nameof(KBoardAction.ProfileId):
                k.ProfileName = Config.FindKBoard(k.ProfileId)?.Name;
                break;
            case PowerPlanAction p when e.PropertyName == nameof(PowerPlanAction.PlanId):
                if (PowerPlans.All().FirstOrDefault(plan => plan.Key == p.PlanId) is { } chosen) p.PlanName = chosen.Name;
                break;
            case DisplayAction d when e.PropertyName == nameof(DisplayAction.Monitor):
                d.MonitorName = DisplayTools.Monitors().FirstOrDefault(m => m.DeviceName == d.Monitor)?.Name;
                // A mode this monitor doesn't have is not kept: its current one is proposed.
                if (!DisplayTools.Modes(d.Monitor).Any(m => m.Key == d.ModeKey) && DisplayTools.CurrentMode(d.Monitor) is { } current) d.ModeKey = current.Key;
                break;
        }
    }

    private void OnTestAction(object sender, ExecutedRoutedEventArgs e)
    {
        if (_selected is not null && e.Parameter is CommandAction a) _ = _app.Engine.TestActionAsync(a, _selected);
    }

    private void OnRemoveAction(object sender, ExecutedRoutedEventArgs e)
    {
        if (_selected is null || e.Parameter is not CommandAction a) return;
        _selected.Actions.Remove(a);
        _app.Achievements.Increment(Stat.ActionsDeleted);
        RefreshHeader();
    }

    private void OnDuplicateAction(object sender, ExecutedRoutedEventArgs e)
    {
        if (_selected is null || e.Parameter is not CommandAction a) return;
        var copy = a.Clone();
        _selected.Actions.Insert(_selected.Actions.IndexOf(a) + 1, copy);
        _app.Achievements.Increment(Stat.ActionsCreated);
        ActionList.SelectedItem = copy;
    }

    private void OnMoveActionUp(object sender, ExecutedRoutedEventArgs e) => MoveAction(e.Parameter as CommandAction, -1);
    private void OnMoveActionDown(object sender, ExecutedRoutedEventArgs e) => MoveAction(e.Parameter as CommandAction, +1);

    private void MoveAction(CommandAction? action, int delta)
    {
        if (_selected is null || action is null) return;
        var i = _selected.Actions.IndexOf(action);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= _selected.Actions.Count) return;
        _selected.Actions.Move(i, j);
        ActionList.SelectedItem = action;
        CommandManager.InvalidateRequerySuggested(); // the arrows of the first and last rows
    }

    /// <summary>The arrows only where the action can move (they hide otherwise).</summary>
    private void OnCanMoveActionUp(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = e.Parameter is CommandAction a && _selected?.Actions.IndexOf(a) > 0;

    private void OnCanMoveActionDown(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = e.Parameter is CommandAction a && _selected is { } cmd && cmd.Actions.IndexOf(a) is var i && i >= 0 && i < cmd.Actions.Count - 1;

    private void OnActionReordered(object? sender, ReorderedEventArgs e)
    {
        _app.Achievements.Increment(Stat.ActionsReordered);
        _app.Achievements.Unlock("reorder");
    }

    /// <summary>Action that was already open when its header was pressed: releasing there closes it.</summary>
    private object? _pressedOpenAction;
    private Point _actionPressPoint;

    private void OnActionHeaderDown(object sender, MouseButtonEventArgs e)
    {
        _pressedOpenAction = null;
        if (sender is not FrameworkElement header || IsInsideControl(e.OriginalSource as DependencyObject, header)) return;
        if (ActionList.SelectedItem is { } open && ReferenceEquals(open, header.DataContext)) _pressedOpenAction = open;
        _actionPressPoint = e.GetPosition(this);
    }

    private void OnActionHeaderUp(object sender, MouseButtonEventArgs e)
    {
        var open = _pressedOpenAction;
        _pressedOpenAction = null;
        if (open is null || sender is not FrameworkElement header || !ReferenceEquals(open, header.DataContext)) return;
        // A drag (reordering) handles its own release before this; a small wobble is still a click.
        if ((e.GetPosition(this) - _actionPressPoint).Length > 6) return;
        ActionList.SelectedItem = null;
    }

    /// <summary>The click was on a button or check box of the header (they do their own thing).</summary>
    private static bool IsInsideControl(DependencyObject? source, DependencyObject header)
    {
        for (var d = source; d is not null && !ReferenceEquals(d, header); d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
        return false;
    }

    private void OnActionListWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;
        EditorScroll.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = sender });
    }

    // ── editor commands ──────────────────────────────────────────────────

    private void OnBrowseFile(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not RunAppAction a) return;
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("editor.chooseFile"),
            Filter = Loc.T("editor.filterPrograms") + "|*.exe;*.lnk;*.bat;*.cmd;*.vbs;*.ps1;*.msc;*.url|" + Loc.T("editor.filterAll") + "|*.*",
            DereferenceLinks = false,
        };
        if (!string.IsNullOrWhiteSpace(a.Path) && Path.GetDirectoryName(a.Path) is { } dir && Directory.Exists(dir)) dlg.InitialDirectory = dir;
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        a.Path = dlg.FileName;
        if (_selected is { } cmd && (string.IsNullOrWhiteSpace(cmd.Name) || cmd.Name.StartsWith(Loc.T("commands.newName"), StringComparison.CurrentCulture)))
            cmd.Name = UniqueName(Path.GetFileNameWithoutExtension(dlg.FileName));
    }

    private void OnBrowseFolder(object sender, ExecutedRoutedEventArgs e)
    {
        switch (e.Parameter)
        {
            case OpenFolderAction a when PickFolder(a.Path) is { } path: a.Path = path; break;
            case ScreenshotAction s when PickFolder(s.Folder) is { } path: s.Folder = path; break;
            case AudioRecordAction r when PickFolder(r.Folder) is { } path: r.Folder = path; break;
        }
    }

    private void OnBrowseWorkingDirectory(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is RunAppAction a && PickFolder(a.WorkingDirectory) is { } path) a.WorkingDirectory = path;
    }

    private string? PickFolder(string? initial)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("editor.chooseFolder") };
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial)) dlg.InitialDirectory = initial;
        return dlg.ShowDialog(Window.GetWindow(this)) == true ? dlg.FolderName : null;
    }

    private void OnCaptureKeys(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not SendInputAction a) return;
        var previous = a.Value;
        a.Value = Loc.T("commands.pressHotkey");
        HotkeyCapture.Start(
            preview => a.Value = preview + "…",
            hotkey => a.Value = hotkey is null ? previous : Hotkey.Normalize(hotkey),
            Window.GetWindow(this));
    }

    private async void OnCaptureMouse(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not SendInputAction a) return;
        using (_app.Ui.ShowPersistentOsd(Loc.T("editor.placeMouse"), DateTime.Now.AddSeconds(3)))
            await Task.Delay(3000);
        var (x, y) = InputSimulator.CursorPosition();
        a.Value = $"{x},{y}";
        _app.Ui.ShowOsd($"{x}, {y}", OsdKind.Success);
    }

    private void OnEditMacro(object sender, ExecutedRoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.OpenMacro(e.Parameter as string);

    private void OnEditKBoard(object sender, ExecutedRoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.OpenKBoard(e.Parameter as string);

    private void OnAddAudioDevice(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not AudioOutputAction a) return;
        AudioDeviceCache.Invalidate();
        var menu = new ContextMenu { PlacementTarget = e.OriginalSource as UIElement, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var devices = AudioDeviceCache.Devices.Where(d => a.Devices.All(x => x.Id != d.Id)).ToList();
        foreach (var device in devices)
        {
            var item = new MenuItem
            {
                Header = device.Name + (device.IsDefault ? "  ·  " + Loc.T("editor.audioDefault") : device.IsActive ? "" : "  ·  " + Loc.T("editor.audioDisconnected")),
                ToolTip = device.Adapter,
                Icon = new SymbolIcon { Symbol = AudioDeviceSymbolConverter.Symbol(device.FormFactor) },
            };
            item.Click += (_, _) => a.Devices.Add(new AudioDeviceRef { Id = device.Id, Name = device.Name });
            menu.Items.Add(item);
        }
        if (devices.Count == 0) menu.Items.Add(new MenuItem { Header = Loc.T("editor.audioNoMore"), IsEnabled = false });
        menu.IsOpen = true;
    }

    private void OnRemoveAudioDevice(object sender, ExecutedRoutedEventArgs e)
    {
        if (_selected is null || e.Parameter is not AudioDeviceRef device) return;
        foreach (var a in _selected.Actions.OfType<AudioOutputAction>()) a.Devices.Remove(device);
    }

    private void OnAddHttpHeader(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is HttpRequestAction a) a.Headers.Add(new HttpHeader());
    }

    private void OnRemoveHttpHeader(object sender, ExecutedRoutedEventArgs e)
    {
        if (_selected is null || e.Parameter is not HttpHeader header) return;
        foreach (var a in _selected.Actions.OfType<HttpRequestAction>()) a.Headers.Remove(header);
    }

    /// <summary>Sends the request now and shows the code and the answer under the button.</summary>
    /// <summary>«Averiguar MAC»: asks the network for the MAC of the machine in «Equipo» (it must be on).</summary>
    private async void OnFindMac(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not WakeOnLanAction a) return;
        if (string.IsNullOrWhiteSpace(a.Host))
        {
            _app.Ui.ShowOsd(Loc.T("osd.wolNeedHost"), OsdKind.Warning);
            return;
        }
        try
        {
            var mac = await WakeOnLan.FindMacAsync(a.Host);
            if (mac is null) _app.Ui.ShowOsd(Loc.T("osd.wolMacNotFound", a.Host.Trim()), OsdKind.Warning);
            else
            {
                a.Mac = WakeOnLan.FormatMac(mac);
                _app.Ui.ShowOsd(Loc.T("osd.wolMacFound", a.Mac), OsdKind.Success);
            }
        }
        catch (Exception ex)
        {
            _app.Ui.ShowOsd(Loc.T("osd.wolMacNotFound", a.Host.Trim()) + " · " + ex.Message, OsdKind.Warning);
        }
    }

    private async void OnTestHttpRequest(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not HttpRequestAction a || a.Testing) return;
        a.Testing = true;
        a.TestResult = null;
        try
        {
            var result = await HttpRequests.SendAsync(a, CancellationToken.None);
            a.TestResult = result.Error is not null
                ? Loc.T("editor.httpTestError", result.Error)
                : $"{result.Status} {result.Reason}  ·  {result.Milliseconds} ms" + (result.Body.Length > 0 ? Environment.NewLine + Environment.NewLine + Indented(result.Body) : "");
        }
        catch (Exception ex)
        {
            a.TestResult = Loc.T("editor.httpTestError", ex.Message);
        }
        finally { a.Testing = false; }

        // A JSON answer, easier to read.
        static string Indented(string body)
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                return System.Text.Json.JsonSerializer.Serialize(doc.RootElement, Json.Options);
            }
            catch (System.Text.Json.JsonException) { return body; }
        }
    }

    // ── processes ────────────────────────────────────────────────────────

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
        if (_selected is not { } cmd) return;
        WindowPicker.Pick(name =>
        {
            if (!cmd.Processes.Contains(name)) cmd.Processes.Add(name);
        });
    }

    private void OnRemoveProcess(object sender, ExecutedRoutedEventArgs e)
    {
        if (_selected is not null && e.Parameter is string p) _selected.Processes.Remove(p);
    }

    // ── voice phrases ────────────────────────────────────────────────────

    private void OnVoicePhraseKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        OnAddVoicePhrase(sender, e);
        e.Handled = true;
    }

    /// <summary>The typed phrase becomes a chip (once; the same phrase twice, ignoring case and spaces, is kept once).</summary>
    private void OnAddVoicePhrase(object sender, RoutedEventArgs e)
    {
        var phrase = string.Join(' ', VoicePhraseBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (_selected is null || phrase.Length == 0) return;
        if (!_selected.VoicePhrases.Any(p => string.Equals(p, phrase, StringComparison.CurrentCultureIgnoreCase)))
            _selected.VoicePhrases.Add(phrase);
        VoicePhraseBox.Text = "";
        VoicePhraseBox.Focus();
    }

    private void OnRemoveVoicePhrase(object sender, ExecutedRoutedEventArgs e)
    {
        if (_selected is not null && e.Parameter is string p) _selected.VoicePhrases.Remove(p);
    }
}


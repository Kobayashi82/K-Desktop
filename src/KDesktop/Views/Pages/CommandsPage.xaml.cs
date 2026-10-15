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
        Config.Commands.CollectionChanged += OnCommandsChanged;
        IconProvider.StaticPropertyChanged += OnIconsChanged;
        UpdateCount();
        Loaded += (_, _) =>
        {
            if (CommandList.SelectedItem is null && !_view.IsEmpty) CommandList.SelectedIndex = 0;
        };
        Unloaded += (_, _) => HotkeyCapture.CancelActive();
    }

    public void Dispose()
    {
        HotkeyCapture.CancelActive();
        Config.Commands.CollectionChanged -= OnCommandsChanged;
        IconProvider.StaticPropertyChanged -= OnIconsChanged;
        if (_selected is not null) _selected.PropertyChanged -= OnSelectedChanged;
        _view.DetachFromSourceCollection();
    }

    // ── list ─────────────────────────────────────────────────────────────

    private bool Filter(object obj)
    {
        if (obj is not Command c) return false;
        if (c.Hidden && ShowHiddenToggle.IsChecked != true && !ReferenceEquals(c, _selected)) return false;
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
        if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = string.Empty;
        CommandList.SelectedItem = cmd;
        CommandList.ScrollIntoView(cmd);
    }

    private void OnCommandSelected(object sender, SelectionChangedEventArgs e)
    {
        HotkeyCapture.CancelActive();
        if (_selected is not null) _selected.PropertyChanged -= OnSelectedChanged;
        _selected = CommandList.SelectedItem as Command;
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

    private void OnSelectedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Command.Icon) or nameof(Command.PrimaryAction)) RefreshHeader();
    }

    private void OnIconsChanged(object? sender, PropertyChangedEventArgs e) => RefreshHeader();

    private void RefreshHeader()
    {
        if (_selected is null) return;
        HeaderIcon.Source = IconProvider.ForCommand(_selected);
        NoActions.Visibility = _selected.Actions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── command buttons ──────────────────────────────────────────────────

    private void OnNewCommand(object sender, RoutedEventArgs e)
    {
        var cmd = new Command { Name = UniqueName(Loc.T("commands.newName")) };
        _app.Achievements.Increment(Stat.CommandsCreated);
        Config.Commands.Add(cmd);
        if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = string.Empty;
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
        AddHotkeyButton.Visibility = Visibility.Collapsed;
        CaptureChip.Visibility = Visibility.Visible;
        CaptureText.Text = Loc.T("commands.pressHotkey");
        HotkeyCapture.Start(
            preview => CaptureText.Text = preview.Length > 0 ? preview + "…" : Loc.T("commands.pressHotkey"),
            hotkey =>
            {
                CaptureChip.Visibility = Visibility.Collapsed;
                AddHotkeyButton.Visibility = Visibility.Visible;
                if (hotkey is null) return;
                var normalized = Hotkey.Normalize(hotkey);
                if (!cmd.Hotkeys.Contains(normalized, Hotkey.Comparer)) cmd.Hotkeys.Add(normalized);
                ShowConflicts(cmd, normalized);
            },
            Window.GetWindow(this));
    }

    private void ShowConflicts(Command cmd, string hotkey)
    {
        var others = Config.Commands.Where(c => !ReferenceEquals(c, cmd) && c.Hotkeys.Contains(hotkey, Hotkey.Comparer)).Select(c => c.Name).ToList();
        if (Hotkey.Comparer.Equals(hotkey, Hotkey.Normalize(Config.Settings.ShowWindowHotkey)) ||
            Hotkey.Comparer.Equals(hotkey, Hotkey.Normalize(Config.Settings.ExitHotkey)))
            others.Add("K-Desktop");
        HotkeyWarning.Text = others.Count > 0 ? Loc.T("commands.hotkeyConflict", hotkey, string.Join(", ", others)) : string.Empty;
        HotkeyWarning.Visibility = others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRemoveHotkey(object sender, ExecutedRoutedEventArgs e)
    {
        if (_selected is not null && e.Parameter is string hk) _selected.Hotkeys.Remove(hk);
        HotkeyWarning.Visibility = Visibility.Collapsed;
    }

    // ── actions ──────────────────────────────────────────────────────────

    private void OnAddAction(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddActionButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var group in ActionCatalog.All.GroupBy(a => a.CategoryKey))
        {
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = group.First().Category, IsEnabled = false, FontWeight = FontWeights.SemiBold });
            foreach (var info in group)
            {
                var item = new MenuItem
                {
                    Header = info.Name,
                    ToolTip = info.Description,
                    Icon = new SymbolIcon { Symbol = Enum.Parse<SymbolRegular>(info.Symbol) },
                };
                item.Click += (_, _) => AddAction(info.Create());
                menu.Items.Add(item);
            }
        }
        menu.IsOpen = true;
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
            case PlayMacroAction m when e.PropertyName == nameof(PlayMacroAction.MacroId):
                m.MacroName = Config.FindMacro(m.MacroId)?.Name;
                break;
            case KBoardAction k when e.PropertyName == nameof(KBoardAction.ProfileId):
                k.ProfileName = Config.FindKBoard(k.ProfileId)?.Name;
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
    }

    private void OnActionReordered(object? sender, ReorderedEventArgs e)
    {
        _app.Achievements.Increment(Stat.ActionsReordered);
        _app.Achievements.Unlock("reorder");
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
        if (e.Parameter is OpenFolderAction a && PickFolder(a.Path) is { } path) a.Path = path;
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

    // ── processes ────────────────────────────────────────────────────────

    private void OnProcessDropDown(object sender, EventArgs e) => ProcessBox.ItemsSource = Lookups.RunningProcesses();

    private void OnProcessKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnAddProcess(sender, e);
    }

    private void OnAddProcess(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var name = ProcessNames.Normalize(ProcessBox.Text);
        if (name.Length > 0 && !_selected.Processes.Contains(name)) _selected.Processes.Add(name);
        ProcessBox.Text = string.Empty;
    }

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
}


using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using KDesktop.Core.Achievements;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Services;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Pages;

/// <summary>
/// Eventos: «when X happens, run these commands». List on the left (in the order «Al iniciar» runs them, drag to
/// reorder), editor on the right.
/// </summary>
public partial class EventsPage : UserControl, IDisposable
{
    public static readonly RoutedUICommand RemoveProcess = new("RemoveProcess", nameof(RemoveProcess), typeof(EventsPage));
    public static readonly RoutedUICommand RemoveCommand = new("RemoveCommand", nameof(RemoveCommand), typeof(EventsPage));
    public static readonly RoutedUICommand OpenCommand = new("OpenCommand", nameof(OpenCommand), typeof(EventsPage));

    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly App _app = App.Current;
    private EventRule? _selected;
    private bool _settingKind;

    private AppConfig Config => _app.Config;

    public EventsPage()
    {
        InitializeComponent();
        EventList.ItemsSource = Config.Events;
        EventList.Items.Filter = Filter;
        Config.Events.CollectionChanged += OnEventsChanged;
        IconProvider.StaticPropertyChanged += OnIconsChanged;
        UpdateCount();
        Loaded += (_, _) =>
        {
            if (EventList.SelectedItem is null && EventList.Items.Count > 0) EventList.SelectedIndex = 0;
            RefreshSuspended();
        };
        Unloaded += (_, _) => EnsureName(_selected);
    }

    public void Dispose()
    {
        EnsureName(_selected);
        Config.Events.CollectionChanged -= OnEventsChanged;
        IconProvider.StaticPropertyChanged -= OnIconsChanged;
        if (_selected is not null) Detach(_selected);
        EventList.Items.Filter = null;
    }

    // ── list ─────────────────────────────────────────────────────────────

    private bool Filter(object obj)
    {
        if (obj is not EventRule e) return false;
        var q = SearchBox?.Text?.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        return e.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || e.Summary.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        EventList.Items.Refresh();
        // Reordering a filtered list would move the wrong items: only without a search.
        DragReorder.SetIsEnabled(EventList, string.IsNullOrWhiteSpace(SearchBox.Text));
    }

    private void OnEventsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateCount();

    private void UpdateCount() => CountText.Text = Config.Events.Count.ToString();

    public void Select(string id)
    {
        if (Config.FindEvent(id) is not { } rule) return;
        if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = string.Empty;
        EventList.SelectedItem = rule;
        EventList.ScrollIntoView(rule);
    }

    private void OnEventSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_selected is not null) Detach(_selected);
        var previous = _selected;
        if (previous is not null && string.IsNullOrWhiteSpace(previous.Name)) EnsureName(previous);
        _selected = EventList.SelectedItem as EventRule;
        Editor.DataContext = _selected;
        if (_selected is null)
        {
            EditorScroll.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            return;
        }
        _selected.PropertyChanged += OnSelectedChanged;
        _selected.Commands.CollectionChanged += OnCommandListChanged;
        _selected.Conditions.PropertyChanged += OnConditionsChanged;
        EmptyState.Visibility = Visibility.Collapsed;
        EditorScroll.Visibility = Visibility.Visible;
        EditorScroll.ScrollToTop();
        ConditionsExpander.IsExpanded = !_selected.Conditions.IsEmpty;
        RefreshAll();

        Editor.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(180)));
        EditorShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
    }

    private void Detach(EventRule rule)
    {
        rule.PropertyChanged -= OnSelectedChanged;
        rule.Commands.CollectionChanged -= OnCommandListChanged;
        rule.Conditions.PropertyChanged -= OnConditionsChanged;
    }

    private void OnSelectedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EventRule.Trigger) or nameof(EventRule.Icon) or nameof(EventRule.HasEnd) or nameof(EventRule.Enabled)) RefreshAll();
        if (e.PropertyName is nameof(EventRule.Conditions) && _selected is not null)
            _selected.Conditions.PropertyChanged += OnConditionsChanged;
    }

    private void OnCommandListChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => RefreshAll();

    private void OnConditionsChanged(object? sender, PropertyChangedEventArgs e) => RefreshAll();

    private void OnIconsChanged(object? sender, PropertyChangedEventArgs e) => RefreshAll();

    private void RefreshAll()
    {
        if (_selected is not { } rule) return;
        HeaderIcon.Source = IconProvider.ForEvent(rule);
        var info = EventCatalog.Get(rule.Trigger.Kind);
        _settingKind = true;
        KindBox.SelectedItem = info;
        _settingKind = false;
        KindDescription.Text = info.Description;
        NoCommands.Visibility = rule.Commands.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ExitNote.Visibility = rule.Trigger is AppExitTrigger ? Visibility.Visible : Visibility.Collapsed;
        EndHint.Text = rule.HasEnd ? Loc.T($"event.endHint.{rule.Trigger.Kind}") : string.Empty;
        ConditionsBadge.Visibility = rule.Conditions.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        RefreshSuspended();
    }

    private void RefreshSuspended() =>
        SuspendedHint.Visibility = _selected is { } rule && _app.Events.IsSuspended(rule) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The trigger type changed: a new trigger, keeping the programs; «no repetir» follows the type's default.</summary>
    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingKind || _selected is not { } rule || KindBox.SelectedItem is not EventKindInfo info || info.Kind == rule.Trigger.Kind) return;
        var old = rule.Trigger;
        var trigger = EventCatalog.Convert(old, info.Kind);
        if (rule.CooldownSeconds == old.DefaultCooldown) rule.CooldownSeconds = trigger.DefaultCooldown;
        rule.Trigger = trigger;
        if (!trigger.HasEnd && rule.EndCommands.Count > 0) Core.Log.Info($"Evento «{rule.Name}»: su tipo ya no tiene «Al terminar»; esos comandos se conservan por si vuelve a tenerlo");
    }

    // ── name (never empty) ───────────────────────────────────────────────

    private string? _nameBeforeEdit;

    private void OnNameFocus(object sender, KeyboardFocusChangedEventArgs e) => _nameBeforeEdit = _selected?.Name;

    private void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e) => EnsureName(NameBox.DataContext as EventRule);

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) EnsureName(_selected);
        if (e.Key == Key.Escape && _selected is { } rule && !string.IsNullOrWhiteSpace(_nameBeforeEdit)) rule.Name = _nameBeforeEdit;
    }

    private void EnsureName(EventRule? rule)
    {
        if (rule is null) return;
        if (string.IsNullOrWhiteSpace(rule.Name))
            rule.Name = !string.IsNullOrWhiteSpace(_nameBeforeEdit) ? _nameBeforeEdit : UniqueName(Loc.T("event.newName"));
        else if (rule.Name != rule.Name.Trim()) rule.Name = rule.Name.Trim();
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (var i = 2; Config.Events.Any(e => e.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)); i++) name = $"{baseName} {i}";
        return name;
    }

    // ── buttons ──────────────────────────────────────────────────────────

    private void OnNewEvent(object sender, RoutedEventArgs e)
    {
        var trigger = new ProcessStartTrigger();
        var rule = new EventRule { Name = UniqueName(Loc.T("event.newName")), Trigger = trigger, CooldownSeconds = trigger.DefaultCooldown };
        Config.Events.Add(rule);
        _app.Achievements.Increment(Stat.EventsCreated);
        if (!string.IsNullOrEmpty(SearchBox.Text)) SearchBox.Text = string.Empty;
        EventList.SelectedItem = rule;
        EventList.ScrollIntoView(rule);
        Dispatcher.BeginInvoke(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnDuplicateEvent(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var copy = _selected.Duplicate();
        copy.Name = UniqueName(_selected.Name);
        Config.Events.Insert(Config.Events.IndexOf(_selected) + 1, copy);
        _app.Achievements.Increment(Stat.EventsCreated);
        EventList.SelectedItem = copy;
        EventList.ScrollIntoView(copy);
    }

    private async void OnDeleteEvent(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } rule) return;
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("event.deleteTitle"),
            Content = Loc.T("event.deleteText", rule.Name),
            PrimaryButtonText = Loc.T("common.delete"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        var index = EventList.SelectedIndex;
        Config.Events.Remove(rule);
        if (EventList.Items.Count > 0) EventList.SelectedIndex = Math.Clamp(index, 0, EventList.Items.Count - 1);
    }

    private void OnTestEvent(object sender, RoutedEventArgs e)
    {
        if (_selected is { } rule) _ = _app.Events.TestAsync(rule);
    }

    // ── list context menu ────────────────────────────────────────────────

    private void OnEventRightClick(object sender, MouseButtonEventArgs e)
    {
        if (ListContextMenu.SelectRow(EventList, e) is not { DataContext: EventRule rule } row) return;
        ListContextMenu.Open(row,
            ListContextMenu.Entry(Loc.T("event.test"), SymbolRegular.Play24, () => OnTestEvent(this, e)),
            ListContextMenu.Entry(Loc.T("common.duplicate"), SymbolRegular.Copy24, () => OnDuplicateEvent(this, e)),
            ListContextMenu.Entry(Loc.T("commands.rename"), SymbolRegular.Rename24, () =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            }),
            new Separator(),
            ListContextMenu.Entry(rule.Enabled ? Loc.T("commands.disable") : Loc.T("commands.enable"),
                rule.Enabled ? SymbolRegular.Prohibited24 : SymbolRegular.CheckmarkCircle24, () => rule.Enabled = !rule.Enabled),
            new Separator(),
            ListContextMenu.Entry(Loc.T("common.delete"), SymbolRegular.Delete24, () => OnDeleteEvent(this, e)));
    }

    private void OnEventListKey(object sender, KeyEventArgs e)
    {
        if (_selected is null || e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase) return;
        switch (e.Key)
        {
            case Key.Delete: OnDeleteEvent(this, e); break;
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
        if (Dialogs.IconPicker.Pick(Window.GetWindow(this), _selected.Icon, "evt-" + _selected.Id) is not { } icon) return;
        _selected.Icon = icon.Length == 0 ? null : icon;
        if (icon.Length > 0) _app.Achievements.Increment(Stat.IconsChanged);
    }

    private void OnResetIcon(object sender, RoutedEventArgs e)
    {
        if (_selected is not null) _selected.Icon = null;
    }

    // ── commands ─────────────────────────────────────────────────────────

    private void OnAddCommand(object sender, RoutedEventArgs e) => AddCommands(_selected?.Commands);

    private void OnAddEndCommand(object sender, RoutedEventArgs e) => AddCommands(_selected?.EndCommands);

    private void AddCommands(ICollection<string>? list)
    {
        if (list is null) return;
        foreach (var command in Dialogs.CommandPicker.Pick(Window.GetWindow(this), Config.Commands.Where(c => !list.Contains(c.Id))))
            if (!list.Contains(command.Id)) list.Add(command.Id);
    }

    private void OnRemoveCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (_selected is null || e.Parameter is not string id) return;
        // The same row template serves both lists: remove it from the one it is in.
        if (e.OriginalSource is DependencyObject source && FindParent<ListBox>(source) is { } list && ReferenceEquals(list, EndCommandsList))
            _selected.EndCommands.Remove(id);
        else
            _selected.Commands.Remove(id);
    }

    private void OnOpenCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is string id && Config.FindCommand(id) is not null) (Window.GetWindow(this) as MainWindow)?.OpenCommand(id);
    }

    /// <summary>The inner lists don't scroll: the wheel goes to the editor.</summary>
    private void OnInnerListWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;
        EditorScroll.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = sender });
    }

    // ── processes («al abrir», «en primer plano») ────────────────────────

    private ICollection<string>? Processes => _selected?.Trigger switch
    {
        ProcessStartTrigger p => p.Processes,
        ForegroundTrigger f => f.Processes,
        _ => null,
    };

    private void OnProcessDropDown(object sender, EventArgs e)
    {
        if (sender is ComboBox box) ProcessCombo.Fill(box, Processes);
    }

    private void OnProcessPicked(object sender, EventArgs e)
    {
        if (sender is ComboBox box) ProcessCombo.AddPicked(box, Processes);
    }

    private void OnProcessKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not ComboBox box) return;
        ProcessCombo.AddTyped(box, Processes);
        e.Handled = true;
    }

    private void OnAddProcess(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement button && FindParent<StackPanel>(button) is { } row && row.Children.OfType<ComboBox>().FirstOrDefault() is { } box)
            ProcessCombo.AddTyped(box, Processes);
    }

    private void OnPickProcess(object sender, RoutedEventArgs e)
    {
        if (Processes is not { } list) return;
        WindowPicker.Pick(name =>
        {
            if (!list.Contains(name)) list.Add(name);
        });
    }

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        if (_selected?.Trigger is not FileTrigger t) return;
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("editor.chooseFolder") };
        if (Directory.Exists(t.Folder)) dlg.InitialDirectory = t.Folder;
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) t.Folder = dlg.FolderName;
    }

    private void OnRemoveProcess(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is string name) Processes?.Remove(name);
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        for (var p = System.Windows.Media.VisualTreeHelper.GetParent(d); p is not null; p = System.Windows.Media.VisualTreeHelper.GetParent(p))
            if (p is T found) return found;
        return null;
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using KDesktop.Core.Achievements;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Storage;
using KDesktop.Services;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Pages;

/// <summary>
/// Eventos: «when X happens, run these commands». List on the left (in the order «Al iniciar» runs them, drag to
/// reorder), editor on the right.
/// </summary>
public sealed partial class EventsPage : UserControl, IDisposable
{
    public static readonly RoutedUICommand RemoveProcess = new("RemoveProcess", nameof(RemoveProcess), typeof(EventsPage));
    public static readonly RoutedUICommand RemoveCommand = new("RemoveCommand", nameof(RemoveCommand), typeof(EventsPage));
    public static readonly RoutedUICommand RemoveCondition = new("RemoveCondition", nameof(RemoveCondition), typeof(EventsPage));
    public static readonly RoutedUICommand TestCondition = new("TestCondition", nameof(TestCondition), typeof(EventsPage));
    public static readonly RoutedUICommand OpenCommand = new("OpenCommand", nameof(OpenCommand), typeof(EventsPage));
    public static readonly RoutedUICommand RunCommand = new("RunCommand", nameof(RunCommand), typeof(EventsPage));
    public static readonly RoutedUICommand MoveCommandUp = new("MoveCommandUp", nameof(MoveCommandUp), typeof(EventsPage));
    public static readonly RoutedUICommand MoveCommandDown = new("MoveCommandDown", nameof(MoveCommandDown), typeof(EventsPage));

    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly App _app = App.Current;
    private EventRule? _selected;

    private AppConfig Config => _app.Config;

    public EventsPage()
    {
        InitializeComponent();
        WhenExpander.IsExpanded = !Config.Settings.EventsWhenFolded;
        OptionsExpander.IsExpanded = Config.Settings.EventsOptionsOpen;
        RunExpander.IsExpanded = !Config.Settings.EventsRunFolded;
        AskCard.IsExpanded = !Config.Settings.EventsAskFolded;
        EndCard.IsExpanded = !Config.Settings.EventsEndFolded;
        NotesExpander.IsExpanded = !Config.Settings.EventsNotesFolded;
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

    private void UpdateCount() => CountText.Text = Config.Events.Count.ToString(Loc.Instance.Culture);

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
        if (e.PropertyName is nameof(EventRule.Trigger) or nameof(EventRule.Icon) or nameof(EventRule.HasEnd) or nameof(EventRule.Enabled) or nameof(EventRule.Ask)) RefreshAll();
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
        KindIcon.Symbol = Enum.TryParse<SymbolRegular>(info.Symbol, out var symbol) ? symbol : SymbolRegular.Flash24;
        KindName.Text = info.Name;
        System.Windows.Automation.AutomationProperties.SetName(KindButton, info.Name);
        KindDescription.Text = info.Description;
        WhenSummary.Text = info.Name;
        NoCommands.Visibility = rule.Commands.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ExitNote.Visibility = rule.Trigger is AppExitTrigger ? Visibility.Visible : Visibility.Collapsed;
        // On exit there is no time to ask anyone.
        AskCard.Visibility = rule.Trigger is AppExitTrigger && rule.Ask is null ? Visibility.Collapsed : Visibility.Visible;
        var asks = rule.Ask is not null;
        AddAskButton.Visibility = asks ? Visibility.Collapsed : Visibility.Visible;
        RemoveAskButton.Visibility = asks ? Visibility.Visible : Visibility.Collapsed;
        NoAsk.Visibility = asks ? Visibility.Collapsed : Visibility.Visible;
        AskEditor.Visibility = asks ? Visibility.Visible : Visibility.Collapsed;
        // Programmed by an assistant: who, and what it may do then without asking.
        ScheduledInfo.Visibility = rule.Ask is { IsScheduled: true } ? Visibility.Visible : Visibility.Collapsed;
        if (rule.Ask is { IsScheduled: true } scheduled)
        {
            var approved = (scheduled.Tools ?? []).Where(t => t != "run_command").Select(ToolTitle)
                .Concat((scheduled.Commands ?? []).Select(id => Config.FindCommand(id)?.Name).OfType<string>().Select(n => $"«{n}»"))
                .ToList();
            ScheduledText.Text = Loc.T("event.scheduledBy", App.AssistantName(rule.CreatedBy ?? scheduled.Assistant))
                + " " + (approved.Count > 0 ? Loc.T("event.scheduledApproved", string.Join(", ", approved)) : Loc.T("event.scheduledNothing"));
        }
        EndHint.Text = rule.HasEnd ? Loc.T($"event.endHint.{rule.Trigger.Kind}") : string.Empty;
        ConditionsBadge.Visibility = rule.Conditions.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        NoConditions.Visibility = rule.Conditions.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AnyRow.Visibility = rule.Conditions.Items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        RefreshSuspended();
    }

    private void RefreshSuspended() =>
        SuspendedHint.Visibility = _selected is { } rule && _app.Events.IsSuspended(rule) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The trigger type changed: a new trigger, keeping the programs; «no repetir» follows the type's default.</summary>
    private void OnChooseKind(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } rule || Dialogs.ActionPicker.PickEventKind(Window.GetWindow(this)) is not { } info || info.Kind == rule.Trigger.Kind) return;
        var old = rule.Trigger;
        var trigger = EventCatalog.Convert(old, info.Kind);
        if (rule.CooldownSeconds == old.DefaultCooldown) rule.CooldownSeconds = trigger.DefaultCooldown;
        rule.Trigger = trigger;
        if (!trigger.HasEnd && rule.EndCommands.Count > 0) Core.Log.Info($"Evento «{rule.Name}»: su tipo ya no tiene «Al terminar»; esos comandos se conservan por si vuelve a tenerlo");
    }

    // ── name (never empty) ───────────────────────────────────────────────

    private string? _nameBeforeEdit;

    /// <summary>The one whose name is being typed (only its name is checked against the others).</summary>
    private EventRule? _nameOwner;

    private void OnNameFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _nameBeforeEdit = _selected?.Name;
        _nameOwner = _selected;
    }

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
        if (ReferenceEquals(rule, _nameOwner) && !string.IsNullOrWhiteSpace(rule.Name))
            rule.Name = UniqueNames.Settle(Config.Events, rule, x => x.Name, rule.Name, _nameBeforeEdit, UniqueName);
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (var i = 2; Config.Events.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++) name = $"{baseName} {i}";
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
        if (ListContextMenu.SelectRow(EventList, e) is not { DataContext: EventRule rule } row)
        {
            ListContextMenu.OpenImport(EventList, e);
            return;
        }
        ListContextMenu.Open(row, [
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
            .. ListContextMenu.Transfer(row, TransferKind.Event, rule.Id, rule.Name),
            ListContextMenu.Delete(() => OnDeleteEvent(this, e))]);
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

    private void OnAddCommand(object sender, RoutedEventArgs e)
    {
        RunExpander.IsExpanded = true;
        AddCommands(_selected?.Commands);
    }

    private void OnAddEndCommand(object sender, RoutedEventArgs e)
    {
        EndCard.IsExpanded = true;
        AddCommands(_selected?.EndCommands);
    }

    /// <summary>A new command that runs this event's commands (to give it a hotkey, a voice phrase…), opened in Comandos.</summary>
    private void OnCreateCommand(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } rule) return;
        var cmd = new Command { Name = UniqueNames.Free(Config.Commands.Select(c => c.Name), rule.Name) };
        cmd.Actions.Add(new CommandControlAction { Target = CommandControlTarget.Event, Mode = CommandControlMode.Run, EventId = rule.Id, EventName = rule.Name });
        Config.Commands.Add(cmd);
        _app.Achievements.Increment(Stat.CommandsCreated);
        _app.Achievements.Increment(Stat.ActionsCreated);
        (Window.GetWindow(this) as MainWindow)?.OpenCommand(cmd.Id);
    }

    /// <summary>«Cuando», «Ejecutar», «Pedir a un asistente», «Al terminar», «Opciones» and «Notas» stay as they were left, from one event to the next.</summary>
    private void OnSectionFolded(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        Config.Settings.EventsWhenFolded = WhenExpander.IsExpanded != true;
        Config.Settings.EventsOptionsOpen = OptionsExpander.IsExpanded == true;
        Config.Settings.EventsRunFolded = RunExpander.IsExpanded != true;
        Config.Settings.EventsAskFolded = AskCard.IsExpanded != true;
        Config.Settings.EventsEndFolded = EndCard.IsExpanded != true;
        Config.Settings.EventsNotesFolded = NotesExpander.IsExpanded != true;
    }

    /// <summary>
    /// A command just for this event, named after it and hidden (it doesn't fill the list), added to it and opened to put
    /// its actions in. It is a normal command: it can be used for anything else later.
    /// </summary>
    private void NewCommand(ICollection<string>? list)
    {
        if (_selected is not { } rule || list is null) return;
        var baseName = string.IsNullOrWhiteSpace(rule.Name) ? Loc.T("commands.newName") : rule.Name.Trim();
        var name = baseName;
        for (var i = 2; Config.Commands.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++) name = $"{baseName} {i}";
        var cmd = new Command { Name = name, Hidden = true };
        Config.Commands.Add(cmd);
        list.Add(cmd.Id);
        _app.Achievements.Increment(Stat.CommandsCreated);
        (Window.GetWindow(this) as MainWindow)?.OpenCommand(cmd.Id);
    }

    private void AddCommands(ICollection<string>? list)
    {
        if (list is null) return;
        // «Nuevo comando» is in the window: a new one for this event instead of picking one
        foreach (var command in Dialogs.CommandPicker.Pick(Window.GetWindow(this), Config.Commands.Where(c => !list.Contains(c.Id)), () => NewCommand(list)))
            if (!list.Contains(command.Id)) list.Add(command.Id);
    }

    /// <summary>The same row template serves both lists: the one a row is in, and its unticked ones.</summary>
    private (ObservableCollection<string> Ids, ObservableCollection<string> Disabled)? ListOf(object source)
    {
        if (_selected is not { } rule) return null;
        return source is DependencyObject d && FindParent<ListBox>(d) is { } list && ReferenceEquals(list, EndCommandsList)
            ? (rule.EndCommands, rule.DisabledEndCommands)
            : (rule.Commands, rule.DisabledCommands);
    }

    private void OnRemoveCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not string id || ListOf(e.OriginalSource) is not var (ids, disabled)) return;
        ids.Remove(id);
        disabled.Remove(id);
    }

    /// <summary>
    /// Unticked, it stays in the list but is skipped when the event fires. Only when it differs from what is kept: the box
    /// also changes when its binding is worked out again (with the value kept, so nothing to do then).
    /// </summary>
    private void OnCommandTicked(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: string id } box || ListOf(box) is not var (_, disabled)) return;
        var ticked = box.IsChecked == true;
        if (ticked == !disabled.Contains(id)) return;
        if (ticked) disabled.Remove(id);
        else disabled.Add(id);
    }

    private void OnRunCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is string id && Config.FindCommand(id) is { } command) _ = _app.Engine.RunAsync(command, TriggerSource.Manual);
    }

    private void OnMoveCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not string id || ListOf(e.OriginalSource) is not var (ids, _)) return;
        var index = ids.IndexOf(id);
        var to = index + (e.Command == MoveCommandUp ? -1 : 1);
        if (index >= 0 && to >= 0 && to < ids.Count) ids.Move(index, to);
    }

    private void OnCanMoveCommand(object sender, CanExecuteRoutedEventArgs e)
    {
        if (e.Parameter is not string id || ListOf(e.OriginalSource) is not var (ids, _)) return;
        var index = ids.IndexOf(id);
        e.CanExecute = e.Command == MoveCommandUp ? index > 0 : index >= 0 && index < ids.Count - 1;
    }

    private void OnOpenCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is string id && Config.FindCommand(id) is not null) (Window.GetWindow(this) as MainWindow)?.OpenCommand(id);
    }

    // ── «Pedir a un asistente» ───────────────────────────────────────────

    private void OnAddAsk(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } rule) return;
        rule.Ask = new EventAsk();
        AskCard.IsExpanded = true;
        Dispatcher.BeginInvoke(() => AskEditor.Children.OfType<Wpf.Ui.Controls.TextBox>().FirstOrDefault()?.Focus(),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>A tool's name for people (its title in Options → Tools, when it has one).</summary>
    private static string ToolTitle(string tool)
    {
        var key = "tool." + tool + ".title";
        var title = Loc.T(key);
        return title == key ? tool : title;
    }

    private void OnRemoveAsk(object sender, RoutedEventArgs e)
    {
        if (_selected is { } rule) rule.Ask = null;
    }

    /// <summary>The inner lists don't scroll: the wheel goes to the editor.</summary>
    private void OnInnerListWheel(object sender, MouseWheelEventArgs e) => InputPolish.ForwardWheel(sender, e, EditorScroll);

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

    /// <summary>«Ver hoy»: today's sunrise and sunset where it looks (and that the place is found).</summary>
    private async void OnSunToday(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SunTrigger sun) return;
        var place = await Core.Platform.HomeLocation.FindAsync(sun.Place);
        if (place is null)
        {
            _app.Ui.ShowOsd(Loc.T("event.sunNotFound"), OsdKind.Warning, TimeSpan.FromSeconds(5), OsdLook.Of("Location24"));
            return;
        }
        var (rise, set) = Core.Engine.Events.SunTimes.For(DateOnly.FromDateTime(DateTime.Now), place.Latitude, place.Longitude);
        string At(DateTime? utc) => utc is { } u ? u.ToLocalTime().ToString("t", Loc.Instance.Culture) : "—";
        _app.Ui.ShowOsd(Loc.T("event.sunTodayIs", place.Name, At(rise), At(set)), OsdKind.Info, TimeSpan.FromSeconds(6), OsdLook.Of("WeatherSunnyLow24"));
    }

    // ── «Solo si»: the same checks as «Si…» (5.0.340) ──────────────────────

    private void OnAddCondition(object sender, RoutedEventArgs e) =>
        _selected?.Conditions.Items.Add(new ConditionAction { Condition = ConditionKind.Schedule });

    private void OnRemoveCondition(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is ConditionAction check) _selected?.Conditions.Items.Remove(check);
    }

    /// <summary>«Probar»: whether it is met now, on the OSD (like ▶ on «Si…»).</summary>
    private async void OnTestCondition(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not ConditionAction check) return;
        var met = await Conditions.IsMetAsync(check, Core.Input.ForegroundWindow.Capture(), CancellationToken.None);
        _app.Ui.ShowOsd(Loc.T(met ? "osd.ifMet" : "osd.ifNotMet", check.Describe()), met ? OsdKind.Success : OsdKind.Warning, look: OsdLook.Of("Filter24"));
    }

    private void OnBrowseFile(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is ConditionAction check) ConditionPicks.File(check, Window.GetWindow(this));
    }

    private void OnBrowseFolder(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is ConditionAction check) ConditionPicks.Folder(check, Window.GetWindow(this));
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        for (var p = System.Windows.Media.VisualTreeHelper.GetParent(d); p is not null; p = System.Windows.Media.VisualTreeHelper.GetParent(p))
            if (p is T found) return found;
        return null;
    }
}

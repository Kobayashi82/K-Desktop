using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using KDesktop.Core.Achievements;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Osd;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace KDesktop.Views.Pages;

/// <summary>Macros: list, recording, step editor, test and "create command".</summary>
public sealed partial class MacrosPage : UserControl, IDisposable
{
    public static readonly RoutedCommand MoveStepUp = new();
    public static readonly RoutedCommand MoveStepDown = new();
    public static readonly RoutedCommand DuplicateStep = new();
    public static readonly RoutedCommand DeleteStep = new();

    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private readonly App _app = App.Current;
    private readonly ListCollectionView _view;
    private Macro? _selected;
    private bool _busy;

    private AppConfig Config => _app.Config;

    public MacrosPage()
    {
        InitializeComponent();
        _view = new ListCollectionView(Config.Macros) { Filter = Filter, IsLiveSorting = true, IsLiveFiltering = true };
        _view.SortDescriptions.Add(new SortDescription(nameof(Macro.Name), ListSortDirection.Ascending));
        _view.LiveSortingProperties.Add(nameof(Macro.Name));
        _view.LiveFilteringProperties.Add(nameof(Macro.Name));
        MacroList.ItemsSource = _view;
        RecordMovesToggle.IsChecked = Config.Settings.MacroRecordMoves;
        Config.Macros.CollectionChanged += OnMacrosChanged;
        CommandBindings.Add(new CommandBinding(EditorCommands.CaptureStepKey, OnCaptureStepKey));
        CommandBindings.Add(new CommandBinding(EditorCommands.CaptureStepPosition, OnCaptureStepPosition));
        UpdateCount();
        Loaded += (_, _) =>
        {
            if (MacroList.SelectedItem is null && !_view.IsEmpty) MacroList.SelectedIndex = 0;
        };
        Unloaded += (_, _) => HotkeyCapture.CancelActive();
    }

    public void Dispose()
    {
        HotkeyCapture.CancelActive();
        Track(null);
        Config.Macros.CollectionChanged -= OnMacrosChanged;
        _view.DetachFromSourceCollection();
    }

    private void OnMacrosChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        CountText.Text = Config.Macros.Count.ToString(Loc.Instance.Culture);
        ListEmpty.Visibility = Config.Macros.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Select(string id)
    {
        if (Config.FindMacro(id) is not { } macro) return;
        if (!_view.Contains(macro)) SearchBox.Text = string.Empty;
        MacroList.SelectedItem = macro;
        MacroList.ScrollIntoView(macro);
    }

    // ── list ─────────────────────────────────────────────────────────────

    private bool Filter(object o)
    {
        var q = SearchBox?.Text?.Trim();
        return string.IsNullOrEmpty(q) || o is Macro m && m.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _view.Refresh();

    private void OnMacroSelected(object sender, SelectionChangedEventArgs e)
    {
        HotkeyCapture.CancelActive();
        if (_selected is { } previous) EnsureName(previous);
        Track(MacroList.SelectedItem as Macro);
        Editor.DataContext = _selected;
        StepList.Tag = null;
        NameError.Visibility = Visibility.Collapsed;
        if (_selected is null)
        {
            Editor.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        UpdateStepsState();
        Editor.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(180)));
        EditorShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
    }

    private void OnMacroListKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && _selected is not null)
        {
            OnDelete(sender, e);
            e.Handled = true;
        }
    }

    private void OnMacroRightClick(object sender, MouseButtonEventArgs e)
    {
        if (ListContextMenu.SelectRow(MacroList, e) is not { DataContext: Macro } row) return;
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

    private void UpdateStepsState()
    {
        var empty = _selected is null || _selected.Steps.Count == 0;
        NoSteps.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        StepList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        DeleteStepsButton.IsEnabled = StepList.SelectedItems.Count > 0;
        UpdateUndoButtons();
    }

    // ── name ─────────────────────────────────────────────────────────────

    private string? _nameBeforeEdit;

    /// <summary>The one whose name is being typed (only its name is checked against the others).</summary>
    private Macro? _nameOwner;

    private void OnNameFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _nameBeforeEdit = _selected?.Name;
        _nameOwner = _selected;
    }

    private void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e) => EnsureName(NameBox.DataContext as Macro);

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) EnsureName(_selected);
        if (e.Key == Key.Escape && _selected is { } macro && !string.IsNullOrWhiteSpace(_nameBeforeEdit)) macro.Name = _nameBeforeEdit;
    }

    /// <summary>A macro never stays without a name: an emptied name goes back to the one it had.</summary>
    private void EnsureName(Macro? macro)
    {
        if (macro is null) return;
        if (string.IsNullOrWhiteSpace(macro.Name))
        {
            macro.Name = !string.IsNullOrWhiteSpace(_nameBeforeEdit) ? _nameBeforeEdit : UniqueName(Loc.T("macro.newName"));
            if (ReferenceEquals(macro, _selected)) NameError.Visibility = Visibility.Visible;
        }
        else
        {
            if (macro.Name != macro.Name.Trim()) macro.Name = macro.Name.Trim();
            if (ReferenceEquals(macro, _nameOwner) && !string.IsNullOrWhiteSpace(macro.Name))
                macro.Name = UniqueNames.Settle(Config.Macros, macro, x => x.Name, macro.Name, _nameBeforeEdit, UniqueName);
            if (ReferenceEquals(macro, _selected)) NameError.Visibility = Visibility.Collapsed;
        }
    }

    // ── macros ───────────────────────────────────────────────────────────

    private void OnNewMacro(object sender, RoutedEventArgs e)
    {
        var macro = new Macro { Name = UniqueName(Loc.T("macro.newName")) };
        SearchBox.Text = string.Empty;
        Config.Macros.Add(macro);
        MacroList.SelectedItem = macro;
        MacroList.ScrollIntoView(macro);
        Dispatcher.BeginInvoke(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (var i = 2; Config.Macros.Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++) name = $"{baseName} {i}";
        return name;
    }

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var copy = _selected.Duplicate();
        copy.Name = UniqueName(_selected.Name);
        Config.Macros.Add(copy);
        MacroList.SelectedItem = copy;
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } macro) return;
        var used = Config.Commands.SelectMany(c => c.Actions).OfType<PlayMacroAction>().Count(a => a.MacroId == macro.Id);
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("macro.delete"),
            Content = Loc.T("macro.deleteText", macro.Name) + (used > 0 ? "\n\n" + Loc.T("macro.deleteUsed", used) : ""),
            PrimaryButtonText = Loc.T("common.delete"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        var index = MacroList.SelectedIndex;
        Config.Macros.Remove(macro);
        _histories.Remove(macro);
        if (!_view.IsEmpty) MacroList.SelectedIndex = Math.Clamp(index, 0, _view.Count - 1);
    }

    private void OnCreateCommand(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } macro) return;
        var cmd = new Command { Name = UniqueNames.Free(Config.Commands.Select(c => c.Name), macro.Name) };
        cmd.Actions.Add(new PlayMacroAction { MacroId = macro.Id, MacroName = macro.Name });
        Config.Commands.Add(cmd);
        _app.Achievements.Increment(Stat.CommandsCreated);
        _app.Achievements.Increment(Stat.ActionsCreated);
        (Window.GetWindow(this) as MainWindow)?.OpenCommand(cmd.Id);
    }

    private void OnRecordMoves(object sender, RoutedEventArgs e) => Config.Settings.MacroRecordMoves = RecordMovesToggle.IsChecked == true;

    // ── recording ────────────────────────────────────────────────────────

    private async void OnRecord(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } macro || _busy) return;
        if (!_app.Router.IsRunning)
        {
            _app.Ui.ShowNotice(Loc.T("macro.noHooks"), OsdKind.Warning);
            return;
        }

        var append = false;
        if (macro.Steps.Count > 0)
        {
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Title = Loc.T("macro.record"),
                Content = Loc.T("macro.recordExisting", macro.Steps.Count),
                PrimaryButtonText = Loc.T("macro.replace"),
                SecondaryButtonText = Loc.T("macro.append"),
                CloseButtonText = Loc.T("common.cancel"),
            };
            var result = await box.ShowDialogAsync();
            if (result == Wpf.Ui.Controls.MessageBoxResult.None) return;
            append = result == Wpf.Ui.Controls.MessageBoxResult.Secondary;
        }

        _busy = true;
        var window = Window.GetWindow(this);
        var bar = new RecorderBar();
        try
        {
            if (window is not null) window.WindowState = WindowState.Minimized;
            if (!await bar.CountdownAsync(3))
            {
                bar.CloseAnimated();
                return;
            }

            using var recorder = new MacroRecorder(Config.Settings.MacroRecordMoves, macro.GameMode);
            var done = new TaskCompletionSource();
            recorder.StopRequested += () => done.TrySetResult();
            bar.StopClicked += () => done.TrySetResult();
            bar.PauseToggled += paused =>
            {
                if (paused) recorder.Paused = true;
                else recorder.Resume();
            };
            _app.Router.SetRecorder(recorder);
            bar.StartRecording(() => recorder.Count);
            await done.Task;
            _app.Router.SetRecorder(null);
            bar.CloseAnimated();

            var steps = recorder.Finish();
            if (steps.Count == 0)
            {
                _app.Ui.ShowOsd(Loc.T("macro.nothingRecorded"), OsdKind.Warning);
                return;
            }
            if (append) steps[0].Delay = 250;
            macro.ReplaceSteps(append ? [.. macro.Steps, .. steps] : steps);
            _app.Achievements.Increment(Stat.MacrosRecorded);
            _app.Achievements.SetMax(Stat.MacroLongest, macro.Steps.Count);
            _app.Ui.ShowOsd(Loc.T("macro.recorded", steps.Count, Macro.FormatDuration(TimeSpan.FromMilliseconds(steps.Sum(s => (long)s.Delay)))), OsdKind.Success);
        }
        finally
        {
            _app.Router.SetRecorder(null);
            _busy = false;
            Restore(window);
            UpdateStepsState();
        }
    }

    private static void Restore(Window? window)
    {
        if (window is null) return;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private async void OnTest(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } macro || _busy || macro.Steps.Count == 0) return;
        _busy = true;
        var window = Window.GetWindow(this);
        try
        {
            if (window is not null) window.WindowState = WindowState.Minimized;
            using (_app.Ui.ShowPersistentOsd(Loc.T("macro.testStarting"), DateTime.Now.AddSeconds(3)))
                await Task.Delay(3000);
            await _app.Engine.PlayMacroAsync(macro, 1, 1, 0, showOsd: true, CancellationToken.None);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _busy = false;
            Restore(window);
        }
    }

    // ── steps ────────────────────────────────────────────────────────────

    private void OnStepSelected(object sender, SelectionChangedEventArgs e)
    {
        HotkeyCapture.CancelActive();
        DeleteStepsButton.IsEnabled = StepList.SelectedItems.Count > 0;
        CommandManager.InvalidateRequerySuggested();
        // The step opens under its row when it is the only one selected (the rows' editors look at the list's Tag).
        StepList.Tag = StepList.SelectedItems.Count == 1 ? StepList.SelectedItem : null;
    }

    private void OnStepKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TextBox) return;
        // Delete inside the open editor of a step (a combo…) is not «delete the steps».
        if (e.Key == Key.Delete && e.OriginalSource is ListBoxItem or ListBox)
        {
            OnDeleteSteps(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control || e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Redo();
            e.Handled = true;
        }
    }

    private List<MacroStep> SelectedSteps() => StepList.SelectedItems.OfType<MacroStep>().ToList();

    /// <summary>
    /// What a row button works on: the whole selection when the row is part of a selection of several steps,
    /// otherwise just that row.
    /// </summary>
    private List<MacroStep> Targets(object? parameter)
    {
        if (parameter is not MacroStep step) return SelectedSteps();
        var selected = SelectedSteps();
        return selected.Count > 1 && selected.Contains(step) ? selected : [step];
    }

    /// <summary>Indexes of <paramref name="steps"/> in the macro, in order.</summary>
    private List<int> IndexesOf(IEnumerable<MacroStep> steps) =>
        _selected is { } macro ? steps.Select(s => macro.Steps.IndexOf(s)).Where(i => i >= 0).Order().ToList() : [];

    private void OnCanMoveStepUp(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = IndexesOf(Targets(e.Parameter)) is [var first, ..] && first > 0;

    private void OnCanMoveStepDown(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = _selected is { } macro && IndexesOf(Targets(e.Parameter)) is [.., var last] && last < macro.Steps.Count - 1;

    private void OnMoveStepUp(object sender, ExecutedRoutedEventArgs e) => MoveSteps(Targets(e.Parameter), -1);

    private void OnMoveStepDown(object sender, ExecutedRoutedEventArgs e) => MoveSteps(Targets(e.Parameter), +1);

    /// <summary>Moves the steps one place up or down, as a block that keeps its order (and its selection).</summary>
    private void MoveSteps(List<MacroStep> steps, int delta)
    {
        if (_selected is not { } macro || steps.Count == 0) return;
        var indexes = IndexesOf(steps);
        if (indexes.Count == 0 || indexes[0] + delta < 0 || indexes[^1] + delta >= macro.Steps.Count) return;
        var keep = SelectedSteps();
        foreach (var i in delta < 0 ? indexes : Enumerable.Reverse(indexes)) macro.Steps.Move(i, i + delta);
        // What was selected stays so; steps moved with their arrows are not selected (nor opened) for it.
        if (keep.Count > 0) Reselect(keep);
        StepList.ScrollIntoView(steps[0]);
        CommandManager.InvalidateRequerySuggested();
    }

    private void Reselect(IReadOnlyCollection<MacroStep> steps)
    {
        StepList.SelectedItems.Clear();
        foreach (var s in steps) StepList.SelectedItems.Add(s);
        if (steps.FirstOrDefault() is { } first) StepList.ScrollIntoView(first);
    }

    private void OnDuplicateStep(object sender, ExecutedRoutedEventArgs e)
    {
        if (_selected is not { } macro) return;
        var indexes = IndexesOf(Targets(e.Parameter));
        if (indexes.Count == 0) return;
        // The copies go right after the last one, in the same order.
        var copies = indexes.Select(i => macro.Steps[i].Clone()).ToList();
        var at = indexes[^1] + 1;
        for (var k = 0; k < copies.Count; k++) macro.Steps.Insert(at + k, copies[k]);
        UpdateStepsState();
        Reselect(copies);
    }

    private void OnDeleteStep(object sender, ExecutedRoutedEventArgs e) => DeleteSteps(Targets(e.Parameter));

    private void OnDeleteSteps(object sender, RoutedEventArgs e) => DeleteSteps(SelectedSteps());

    private void DeleteSteps(List<MacroStep> steps)
    {
        if (_selected is not { } macro || steps.Count == 0) return;
        var index = IndexesOf(steps).FirstOrDefault();
        var drop = new HashSet<MacroStep>(steps);
        macro.ReplaceSteps(macro.Steps.Where(s => !drop.Contains(s)).ToList());
        UpdateStepsState();
        if (macro.Steps.Count > 0) StepList.SelectedIndex = Math.Clamp(index, 0, macro.Steps.Count - 1);
    }

    private void OnInsert(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = InsertButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var kind in Enum.GetValues<MacroStepKind>())
        {
            var item = new MenuItem
            {
                Header = Loc.T($"enum.stepKind.{kind}"),
                Icon = new SymbolIcon { Symbol = StepIconConverter.Symbol(kind) },
            };
            item.Click += (_, _) => InsertStep(kind);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void InsertStep(MacroStepKind kind)
    {
        if (_selected is not { } macro) return;
        var (x, y) = InputSimulator.CursorPosition();
        var step = new MacroStep
        {
            Kind = kind,
            Delay = kind == MacroStepKind.Pause ? 1000 : 100,
            Vk = kind is MacroStepKind.KeyDown or MacroStepKind.KeyUp or MacroStepKind.KeyPress ? KeyNames.VK_RETURN : 0,
            X = kind == MacroStepKind.MouseMoveBy ? 100 : x,
            Y = kind == MacroStepKind.MouseMoveBy ? 0 : y,
            Wheel = kind == MacroStepKind.Wheel ? 120 : 0,
            Text = kind == MacroStepKind.Text ? "" : null,
        };
        var at = StepList.SelectedItems.Count > 0 ? SelectedSteps().Max(s => macro.Steps.IndexOf(s)) + 1 : macro.Steps.Count;
        macro.Steps.Insert(at, step);
        UpdateStepsState();
        StepList.SelectedItem = step;
        StepList.ScrollIntoView(step);
    }

    private void OnSimplify(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SimplifyButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        void Add(string key, SymbolRegular symbol, Func<List<MacroStep>, List<MacroStep>> transform)
        {
            var item = new MenuItem { Header = Loc.T(key), Icon = new SymbolIcon { Symbol = symbol } };
            item.Click += (_, _) => Transform(transform);
            menu.Items.Add(item);
        }
        Add("macro.mergePresses", SymbolRegular.Merge24, s => MacroTools.MergePresses(s));
        Add("macro.removeMoves", SymbolRegular.CursorHoverOff24, s => MacroTools.RemoveMoves(s));
        menu.Items.Add(new Separator());
        Add("macro.faster", SymbolRegular.FastForward24, s => { MacroTools.ScaleDelays(s, 0.5); return s; });
        Add("macro.slower", SymbolRegular.Rewind24, s => { MacroTools.ScaleDelays(s, 2); return s; });
        Add("macro.capDelays", SymbolRegular.Timer24, s => { MacroTools.CapDelays(s, 1000); return s; });
        Add("macro.fixedDelays", SymbolRegular.TopSpeed24, s => { foreach (var x in s.Skip(1)) x.Delay = 50; return s; });
        menu.IsOpen = true;
    }

    /// <summary>Applies a transformation to a copy of the steps (one change in the undo history).</summary>
    private void Transform(Func<List<MacroStep>, List<MacroStep>> transform)
    {
        if (_selected is not { } macro || macro.Steps.Count == 0) return;
        var before = macro.Steps.Count;
        var result = transform(macro.Steps.Select(s => s.Clone()).ToList());
        macro.ReplaceSteps(result);
        UpdateStepsState();
        _app.Ui.ShowNotice(Loc.T("macro.simplified", before, result.Count, Macro.FormatDuration(macro.Duration)), OsdKind.Success);
    }

    // ── undo / redo ──────────────────────────────────────────────────────

    /// <summary>
    /// The steps of a macro as they were after each change. Any change counts (recording, deleting, moving, a delay,
    /// the kind of a step…); quick edits of the same step (typing a delay) make a single change.
    /// </summary>
    private sealed class History
    {
        public readonly Stack<List<MacroStep>> Undo = new();
        public readonly Stack<List<MacroStep>> Redo = new();
        public List<MacroStep> Current = [];
        public int LastStep = -1;
        public long LastTime;
    }

    private const int MaxUndo = 60;
    private readonly Dictionary<Macro, History> _histories = [];
    private bool _snapshotQueued;

    private static List<MacroStep> Snapshot(Macro macro) => macro.Steps.Select(s => s.Clone()).ToList();

    private void Track(Macro? macro)
    {
        if (_selected is not null) _selected.Modified -= OnMacroModified;
        _selected = macro;
        if (macro is null) return;
        macro.Modified += OnMacroModified;
        // Changes made while it was not shown are where its history starts from.
        if (!_histories.TryGetValue(macro, out var history)) _histories[macro] = history = new History();
        history.Current = Snapshot(macro);
    }

    private void OnMacroModified(object? sender, EventArgs e)
    {
        // A change usually comes as several notifications (the kind of a step, a whole recording): looked at once.
        if (_snapshotQueued) return;
        _snapshotQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _snapshotQueued = false;
            TakeSnapshot();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void TakeSnapshot()
    {
        if (_selected is not { } macro || !_histories.TryGetValue(macro, out var h)) return;
        var now = Snapshot(macro);
        var changed = Difference(h.Current, now);
        if (changed is null) return; // the name, the game mode… not the steps
        var time = Environment.TickCount64;
        var sameEdit = changed >= 0 && changed == h.LastStep && time - h.LastTime < 1500;
        if (!sameEdit)
        {
            h.Undo.Push(h.Current);
            if (h.Undo.Count > MaxUndo) Trim(h.Undo);
        }
        h.Redo.Clear();
        h.Current = now;
        h.LastStep = changed.Value;
        h.LastTime = time;
        UpdateUndoButtons();
        UpdateStepsState();
    }

    private static void Trim(Stack<List<MacroStep>> stack)
    {
        var keep = stack.Take(MaxUndo).Reverse().ToList();
        stack.Clear();
        foreach (var s in keep) stack.Push(s);
    }

    /// <summary>Null: the same steps. The index of the only step that changed, or -1 when more than that changed.</summary>
    private static int? Difference(List<MacroStep> a, List<MacroStep> b)
    {
        if (a.Count != b.Count) return -1;
        int? found = null;
        for (var i = 0; i < a.Count; i++)
        {
            if (Same(a[i], b[i])) continue;
            if (found is not null) return -1;
            found = i;
        }
        return found;
    }

    private static bool Same(MacroStep a, MacroStep b) =>
        a.Kind == b.Kind && a.Delay == b.Delay && a.Vk == b.Vk && a.Extended == b.Extended && a.Button == b.Button &&
        a.X == b.X && a.Y == b.Y && a.Wheel == b.Wheel && a.Horizontal == b.Horizontal && a.Text == b.Text && a.Raw == b.Raw;

    private void UpdateUndoButtons()
    {
        var h = _selected is { } macro ? _histories.GetValueOrDefault(macro) : null;
        UndoButton.IsEnabled = h is { Undo.Count: > 0 };
        RedoButton.IsEnabled = h is { Redo.Count: > 0 };
    }

    private void OnUndo(object sender, RoutedEventArgs e) => Undo();

    private void OnRedo(object sender, RoutedEventArgs e) => Redo();

    private void Undo()
    {
        if (_selected is not { } macro || !_histories.TryGetValue(macro, out var h) || h.Undo.Count == 0) return;
        TakeSnapshot(); // a pending change is undone first
        if (h.Undo.Count == 0) return;
        h.Redo.Push(h.Current);
        Apply(macro, h, h.Undo.Pop());
    }

    private void Redo()
    {
        if (_selected is not { } macro || !_histories.TryGetValue(macro, out var h) || h.Redo.Count == 0) return;
        h.Undo.Push(h.Current);
        Apply(macro, h, h.Redo.Pop());
    }

    private void Apply(Macro macro, History h, List<MacroStep> steps)
    {
        h.Current = steps;
        h.LastStep = -1;
        macro.ReplaceSteps(steps.Select(s => s.Clone()));
        UpdateStepsState();
    }

    // ── capture ──────────────────────────────────────────────────────────

    private void OnCaptureStepKey(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not MacroStep step) return;
        HotkeyCapture.Start(_ => { }, hotkey =>
        {
            if (hotkey is null) return;
            var (_, main) = Hotkey.Parse(hotkey);
            var key = main.Split(Hotkey.Separator).LastOrDefault();
            if (!string.IsNullOrEmpty(key)) step.KeyName = key;
        }, Window.GetWindow(this));
        _app.Ui.ShowOsd(Loc.T("commands.pressHotkey"));
    }

    private async void OnCaptureStepPosition(object sender, ExecutedRoutedEventArgs e)
    {
        if (e.Parameter is not MacroStep step) return;
        using (_app.Ui.ShowPersistentOsd(Loc.T("editor.placeMouse"), DateTime.Now.AddSeconds(3)))
            await Task.Delay(3000);
        var (x, y) = InputSimulator.CursorPosition();
        step.X = x;
        step.Y = y;
        _app.Ui.ShowOsd($"{x}, {y}", OsdKind.Success);
    }
}

/// <summary>Step kind → Fluent symbol.</summary>
public sealed class StepIconConverter : IValueConverter
{
    public static SymbolRegular Symbol(MacroStepKind kind) => kind switch
    {
        MacroStepKind.KeyDown => SymbolRegular.ArrowDown24,
        MacroStepKind.KeyUp => SymbolRegular.ArrowUp24,
        MacroStepKind.KeyPress => SymbolRegular.Keyboard24,
        MacroStepKind.Text => SymbolRegular.TextT24,
        MacroStepKind.MouseMove => SymbolRegular.Cursor24,
        MacroStepKind.MouseMoveBy => SymbolRegular.ArrowMove24,
        MacroStepKind.Wheel => SymbolRegular.ArrowSort24,
        MacroStepKind.Pause => SymbolRegular.Timer24,
        _ => SymbolRegular.CursorClick24,
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is MacroStepKind k ? Symbol(k) : SymbolRegular.Record24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

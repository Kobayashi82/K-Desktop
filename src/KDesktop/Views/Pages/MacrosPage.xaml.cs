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
public partial class MacrosPage : UserControl, IDisposable
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private readonly App _app = App.Current;
    private readonly ListCollectionView _view;
    private Macro? _selected;
    private (Macro Macro, List<MacroStep> Steps)? _undo;
    private bool _busy;

    private AppConfig Config => _app.Config;

    public MacrosPage()
    {
        InitializeComponent();
        _view = new ListCollectionView(Config.Macros) { IsLiveSorting = true };
        _view.SortDescriptions.Add(new SortDescription(nameof(Macro.Name), ListSortDirection.Ascending));
        _view.LiveSortingProperties.Add(nameof(Macro.Name));
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
        Config.Macros.CollectionChanged -= OnMacrosChanged;
        _view.DetachFromSourceCollection();
    }

    private void OnMacrosChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        CountText.Text = Config.Macros.Count.ToString();
        ListEmpty.Visibility = Config.Macros.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MacroList.IsEnabled = Config.Macros.Count > 0;
    }

    public void Select(string id)
    {
        if (Config.FindMacro(id) is not { } macro) return;
        MacroList.SelectedItem = macro;
    }

    private void OnMacroSelected(object sender, SelectionChangedEventArgs e)
    {
        HotkeyCapture.CancelActive();
        _selected = MacroList.SelectedItem as Macro;
        Editor.DataContext = _selected;
        StepPanel.Visibility = Visibility.Collapsed;
        DuplicateButton.IsEnabled = DeleteButton.IsEnabled = CommandButton.IsEnabled = _selected is not null;
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

    private void UpdateStepsState()
    {
        var empty = _selected is null || _selected.Steps.Count == 0;
        NoSteps.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        StepList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        UndoButton.IsEnabled = _undo is { } u && ReferenceEquals(u.Macro, _selected);
    }

    // ── macros ───────────────────────────────────────────────────────────

    private void OnNewMacro(object sender, RoutedEventArgs e)
    {
        var macro = new Macro { Name = UniqueName(Loc.T("macro.newName")) };
        Config.Macros.Add(macro);
        MacroList.SelectedItem = macro;
        Dispatcher.BeginInvoke(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (var i = 2; Config.Macros.Any(m => m.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)); i++) name = $"{baseName} {i}";
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
        Config.Macros.Remove(macro);
        if (!_view.IsEmpty) MacroList.SelectedIndex = 0;
    }

    private void OnCreateCommand(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } macro) return;
        var cmd = new Command { Name = macro.Name };
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
            _app.Ui.ShowOsd(Loc.T("macro.noHooks"), OsdKind.Warning);
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

            var recorder = new MacroRecorder(Config.Settings.MacroRecordMoves);
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
            SaveUndo(macro);
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
        if (StepList.SelectedItems.Count == 1 && StepList.SelectedItem is MacroStep step)
        {
            StepEditor.ContentTemplate = (DataTemplate)FindResource("StepEditorTemplate");
            StepEditor.Content = step;
            if (StepPanel.Visibility != Visibility.Visible)
            {
                StepPanel.Visibility = Visibility.Visible;
                StepPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            }
        }
        else
        {
            StepEditor.Content = null;
            StepPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void OnStepKey(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TextBox) return;
        if (e.Key == Key.Delete)
        {
            OnDeleteSteps(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OnUndo(sender, e);
            e.Handled = true;
        }
    }

    private List<MacroStep> SelectedSteps() => StepList.SelectedItems.OfType<MacroStep>().ToList();

    private void SaveUndo(Macro macro)
    {
        _undo = (macro, macro.Steps.Select(s => s.Clone()).ToList());
        UndoButton.IsEnabled = ReferenceEquals(macro, _selected);
    }

    private void OnUndo(object sender, RoutedEventArgs e)
    {
        if (_undo is not { } u || !ReferenceEquals(u.Macro, _selected)) return;
        _undo = null;
        u.Macro.ReplaceSteps(u.Steps);
        UpdateStepsState();
    }

    private void OnDeleteSteps(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } macro) return;
        var selected = SelectedSteps();
        if (selected.Count == 0) return;
        SaveUndo(macro);
        var index = macro.Steps.IndexOf(selected[0]);
        var keep = new HashSet<MacroStep>(selected);
        macro.ReplaceSteps(macro.Steps.Where(s => !keep.Contains(s)).ToList());
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
            Delay = 100,
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

    /// <summary>Applies a transformation to a copy of the steps (so it can be undone).</summary>
    private void Transform(Func<List<MacroStep>, List<MacroStep>> transform)
    {
        if (_selected is not { } macro || macro.Steps.Count == 0) return;
        SaveUndo(macro);
        var before = macro.Steps.Count;
        var result = transform(macro.Steps.Select(s => s.Clone()).ToList());
        macro.ReplaceSteps(result);
        UpdateStepsState();
        _app.Ui.ShowOsd(Loc.T("macro.simplified", before, result.Count, Macro.FormatDuration(macro.Duration)), OsdKind.Success);
    }

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
        _ => SymbolRegular.CursorClick24,
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is MacroStepKind k ? Symbol(k) : SymbolRegular.Record24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PlusOneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is int i ? (i + 1).ToString(culture) : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

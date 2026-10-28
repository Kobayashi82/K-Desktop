using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;
using KDesktop.Core.Achievements;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;

namespace KDesktop.Views.Pages;

/// <summary>
/// K-Board: profiles of key / mouse remappings. A visual keyboard and mouse to pick the source, the modifier "layer"
/// (Ctrl + J is not the same key as J), and what the key does instead: other keys, text, nothing or a command.
/// </summary>
public sealed partial class KBoardPage : UserControl, IDisposable
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private readonly App _app = App.Current;
    private readonly ListCollectionView _view;
    private KBoardProfile? _selected;
    private string? _source;
    private KeyRemap? _remap;
    private bool _updating;

    private AppConfig Config => _app.Config;

    public KBoardPage()
    {
        InitializeComponent();
        RemapsExpander.IsExpanded = !Config.Settings.KBoardRemapsFolded;
        NotesExpander.IsExpanded = !Config.Settings.KBoardNotesFolded;
        OptionsExpander.IsExpanded = Config.Settings.KBoardOptionsOpen;
        KeyboardExpander.IsExpanded = !Config.Settings.KBoardKeyboardFolded;
        _view = new ListCollectionView(Config.KBoards) { Filter = Filter, IsLiveSorting = true, IsLiveFiltering = true };
        _view.SortDescriptions.Add(new SortDescription(nameof(KBoardProfile.Name), ListSortDirection.Ascending));
        _view.LiveSortingProperties.Add(nameof(KBoardProfile.Name));
        _view.LiveFilteringProperties.Add(nameof(KBoardProfile.Name));
        ProfileList.ItemsSource = _view;
        TargetKeyBox.ItemsSource = TargetKeys;
        Config.KBoards.CollectionChanged += OnProfilesChanged;
        Surface.KeyClicked += SelectSource;
        Surface.LayerToggled += OnLayerToggled;
        UpdateCount();
        Loaded += (_, _) =>
        {
            if (ProfileList.SelectedItem is null && !_view.IsEmpty) ProfileList.SelectedIndex = 0;
        };
        Unloaded += (_, _) => HotkeyCapture.CancelActive();
    }

    /// <summary>«Teclado y ratón», «Reasignaciones», «Opciones» and «Notas» stay as they were left, from one profile to the next.</summary>
    private void OnSectionFolded(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        Config.Settings.KBoardRemapsFolded = RemapsExpander.IsExpanded != true;
        Config.Settings.KBoardNotesFolded = NotesExpander.IsExpanded != true;
        Config.Settings.KBoardOptionsOpen = OptionsExpander.IsExpanded == true;
        Config.Settings.KBoardKeyboardFolded = KeyboardExpander.IsExpanded != true;
    }

    public void Dispose()
    {
        HotkeyCapture.CancelActive();
        Config.KBoards.CollectionChanged -= OnProfilesChanged;
        Watch(null);
        _view.DetachFromSourceCollection();
    }

    /// <summary>Every key, button and wheel direction that can be sent.</summary>
    private static readonly IReadOnlyList<string> TargetKeys = BuildTargetKeys();

    private static List<string> BuildTargetKeys()
    {
        var keys = new List<string>();
        for (var c = 'A'; c <= 'Z'; c++) keys.Add(c.ToString());
        for (var d = 0; d <= 9; d++) keys.Add(d.ToString(CultureInfo.InvariantCulture));
        for (var f = 1; f <= 24; f++) keys.Add("F" + f);
        keys.AddRange([
            "Enter", "Escape", "Space", "Tab", "Backspace", "Delete", "Insert", "Home", "End", "PageUp", "PageDown",
            "Up", "Down", "Left", "Right", "CapsLock", "NumLock", "Scroll", "PrintScreen", "Pause", "AppsKey",
            "LCtrl", "RCtrl", "LAlt", "RAlt", "LShift", "RShift", "LWin", "RWin",
            "Mouse 1", "Mouse 2", "Mouse 3", "Mouse 4", "Mouse 5", "Wheel Up", "Wheel Down", "HWheel Up", "HWheel Down",
            "VolumeUp", "VolumeDown", "VolumeMute", "MediaPlayPause", "MediaNextTrack", "MediaPreviousTrack", "MediaStop",
            "BrowserBack", "BrowserForward", "BrowserRefresh", "BrowserHome", "BrowserSearch", "LaunchMail", "LaunchApplication1", "LaunchApplication2",
        ]);
        for (var n = 0; n <= 9; n++) keys.Add("NumPad" + n);
        keys.AddRange(["NumPadAdd", "NumPadSub", "NumPadMult", "NumPadDiv", "NumPadDot", "NumPadEnter", "\\", "'", "¡", "`", "+", "Ñ", "´", "ç", "<", ",", ".", "-"]);
        return keys;
    }

    // ── profiles ─────────────────────────────────────────────────────────

    private void OnProfilesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => UpdateCount();

    private void UpdateCount()
    {
        CountText.Text = Config.KBoards.Count.ToString(Loc.Instance.Culture);
        ListEmpty.Visibility = Config.KBoards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Select(string id)
    {
        if (Config.FindKBoard(id) is not { } profile) return;
        if (!_view.Contains(profile)) SearchBox.Text = string.Empty;
        ProfileList.SelectedItem = profile;
        ProfileList.ScrollIntoView(profile);
    }

    private bool Filter(object o)
    {
        var q = SearchBox?.Text?.Trim();
        return string.IsNullOrEmpty(q) || o is KBoardProfile p && p.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => _view.Refresh();

    private void OnProfileListKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && _selected is not null)
        {
            OnDelete(sender, e);
            e.Handled = true;
        }
    }

    private void OnProfileRightClick(object sender, MouseButtonEventArgs e)
    {
        if (ListContextMenu.SelectRow(ProfileList, e) is not { DataContext: KBoardProfile profile } row) return;
        ListContextMenu.Open(row,
            ListContextMenu.Entry(Loc.T("common.duplicate"), SymbolRegular.Copy24, () => OnDuplicate(row, new RoutedEventArgs())),
            ListContextMenu.Entry(Loc.T("commands.rename"), SymbolRegular.Rename24, () =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            }),
            new Separator(),
            ListContextMenu.Entry(profile.Active ? Loc.T("commands.disable") : Loc.T("commands.enable"),
                profile.Active ? SymbolRegular.Prohibited24 : SymbolRegular.CheckmarkCircle24, () => profile.Active = !profile.Active),
            new Separator(),
            ListContextMenu.Entry(Loc.T("common.delete"), SymbolRegular.Delete24, () => OnDelete(row, new RoutedEventArgs())));
    }

    // ── name ─────────────────────────────────────────────────────────────

    private string? _nameBeforeEdit;

    /// <summary>The one whose name is being typed (only its name is checked against the others).</summary>
    private KBoardProfile? _nameOwner;

    private void OnNameFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _nameBeforeEdit = _selected?.Name;
        _nameOwner = _selected;
    }

    private void OnNameLostFocus(object sender, KeyboardFocusChangedEventArgs e) => EnsureName(NameBox.DataContext as KBoardProfile);

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) EnsureName(_selected);
        if (e.Key == Key.Escape && _selected is { } profile && !string.IsNullOrWhiteSpace(_nameBeforeEdit)) profile.Name = _nameBeforeEdit;
    }

    /// <summary>A profile never stays without a name: an emptied name goes back to the one it had.</summary>
    private void EnsureName(KBoardProfile? profile)
    {
        if (profile is null) return;
        var empty = string.IsNullOrWhiteSpace(profile.Name);
        if (empty) profile.Name = !string.IsNullOrWhiteSpace(_nameBeforeEdit) ? _nameBeforeEdit : UniqueName(Loc.T("kboard.newName"));
        else if (profile.Name != profile.Name.Trim()) profile.Name = profile.Name.Trim();
        if (ReferenceEquals(profile, _nameOwner) && !string.IsNullOrWhiteSpace(profile.Name))
            profile.Name = UniqueNames.Settle(Config.KBoards, profile, x => x.Name, profile.Name, _nameBeforeEdit, UniqueName);
        if (ReferenceEquals(profile, _selected)) NameError.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        HotkeyCapture.CancelActive();
        if (_selected is { } previous) EnsureName(previous);
        Watch(ProfileList.SelectedItem as KBoardProfile);
        Editor.DataContext = _selected;
        Surface.Profile = _selected;
        SelectSource(null);
        NameError.Visibility = Visibility.Collapsed;
        if (_selected is null)
        {
            EditorScroll.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            return;
        }
        EmptyState.Visibility = Visibility.Collapsed;
        EditorScroll.Visibility = Visibility.Visible;
        EditorScroll.ScrollToTop();
        UpdateRemapsState();
        Editor.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(180)));
        EditorShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
    }

    /// <summary>Follows the changes of the selected profile to keep the keyboard picture up to date.</summary>
    private void Watch(KBoardProfile? profile)
    {
        if (_selected is not null) _selected.Modified -= OnProfileModified;
        _selected = profile;
        if (_selected is not null) _selected.Modified += OnProfileModified;
    }

    private void OnProfileModified(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        Surface.Refresh();
        UpdateRemapsState();
    });

    private void UpdateRemapsState()
    {
        NoRemaps.Visibility = _selected is { Remaps.Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;
        ClearAllButton.Visibility = _selected is { Remaps.Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNewProfile(object sender, RoutedEventArgs e)
    {
        var profile = new KBoardProfile { Name = UniqueName(Loc.T("kboard.newName")) };
        SearchBox.Text = string.Empty;
        Config.KBoards.Add(profile);
        _app.Achievements.Increment(Stat.KBoardsCreated);
        ProfileList.SelectedItem = profile;
        Dispatcher.BeginInvoke(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        for (var i = 2; Config.KBoards.Any(k => k.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); i++) name = $"{baseName} {i}";
        return name;
    }

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var copy = _selected.Duplicate();
        copy.Name = UniqueName(_selected.Name);
        copy.Active = false; // two copies of the same remaps working at once is rarely what you want
        Config.KBoards.Add(copy);
        _app.Achievements.Increment(Stat.KBoardsCreated);
        ProfileList.SelectedItem = copy;
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } profile) return;
        var used = Config.Commands.SelectMany(c => c.Actions).OfType<KBoardAction>().Count(a => a.ProfileId == profile.Id);
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("kboard.delete"),
            Content = Loc.T("kboard.deleteText", profile.Name) + (used > 0 ? "\n\n" + Loc.T("kboard.deleteUsed", used) : ""),
            PrimaryButtonText = Loc.T("common.delete"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        var index = ProfileList.SelectedIndex;
        Config.KBoards.Remove(profile);
        if (!_view.IsEmpty) ProfileList.SelectedIndex = Math.Clamp(index, 0, _view.Count - 1);
    }

    /// <summary>Creates a command that switches this profile on and off.</summary>
    private void OnCreateCommand(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } profile) return;
        var cmd = new Command { Name = UniqueNames.Free(Config.Commands.Select(c => c.Name), "K-Board · " + profile.Name) };
        cmd.Actions.Add(new KBoardAction { ProfileId = profile.Id, ProfileName = profile.Name });
        Config.Commands.Add(cmd);
        _app.Achievements.Increment(Stat.CommandsCreated);
        _app.Achievements.Increment(Stat.ActionsCreated);
        (Window.GetWindow(this) as MainWindow)?.OpenCommand(cmd.Id);
    }

    // ── applications ─────────────────────────────────────────────────────

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
        if (_selected is not { } profile) return;
        WindowPicker.Pick(name =>
        {
            if (!profile.Processes.Contains(name)) profile.Processes.Add(name);
        });
    }

    private void OnRemoveProcess(object sender, RoutedEventArgs e)
    {
        if (_selected is not null && sender is FrameworkElement { Tag: string p }) _selected.Processes.Remove(p);
    }

    // ── source key and layer ─────────────────────────────────────────────

    /// <summary>The modifiers of the keys shown (the corners of Ctrl, Alt, Shift and Win on the keyboard).</summary>
    private Modifiers Layer => Surface.Layer;

    private void SetLayer(Modifiers mods)
    {
        Surface.Layer = mods;
    }

    private void OnLayerToggled(Modifiers mod)
    {
        // Off when all of it is on (AltGr is Ctrl + Alt), else all of it on
        Surface.Layer = (Layer & mod) == mod ? Layer & ~mod : Layer | mod;
        if (_source is not null) SelectSource(_source);
    }

    private void OnCaptureSource(object sender, RoutedEventArgs e)
    {
        CaptureSourceButton.Content = Loc.T("commands.pressHotkey");
        HotkeyCapture.Start(_ => { }, hotkey =>
        {
            CaptureSourceButton.Content = Loc.T("kboard.captureSource");
            if (hotkey is null) return;
            var (mods, main) = Hotkey.Parse(hotkey);
            var key = main.Split(Hotkey.Separator).LastOrDefault();
            if (string.IsNullOrEmpty(key)) return;
            SetLayer(mods);
            SelectSource(key);
        }, Window.GetWindow(this));
    }

    private void OnCloseKey(object sender, RoutedEventArgs e) => SelectSource(null);

    /// <summary>
    /// Not wide enough, the navigation block, the numeric pad and the mouse go under the keyboard: in one row its keys
    /// would be drawn under ~40 px wide. A margin around the switch so that a scroll bar coming and going doesn't flip it.
    /// </summary>
    private void OnKeyboardAreaSize(object sender, SizeChangedEventArgs e)
    {
        var width = KeyboardArea.ActualWidth;
        var compact = Surface.Compact ? width < 1150 : width < 1100;
        if (compact != Surface.Compact)
        {
            Surface.Compact = compact;
            Surface.MaxHeight = compact ? 600 : 340;
        }
    }

    /// <summary>Selects the key being edited (null = none) and shows its remap.</summary>
    private void SelectSource(string? name)
    {
        HotkeyCapture.CancelActive();
        _source = name;
        Surface.Selected = name;
        if (name is null || _selected is null)
        {
            _remap = null;
            KeyPanel.Visibility = Visibility.Collapsed;
            RemapList.SelectedItem = null;
            return;
        }
        var from = Hotkey.Format(Layer, name);
        _remap = _selected.Find(from);
        SourceCaps.Content = from;
        ShowRemap();
        _updating = true;
        RemapList.SelectedItem = _remap;
        _updating = false;
        if (KeyPanel.Visibility != Visibility.Visible)
        {
            KeyPanel.Visibility = Visibility.Visible;
            KeyPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            Dispatcher.BeginInvoke(() => KeyPanel.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void ShowRemap()
    {
        _updating = true;
        KeyPanel.DataContext = _remap;
        var kind = _remap?.Kind;
        (kind switch
        {
            RemapKind.Keys => KindKeys,
            RemapKind.Text => KindText,
            RemapKind.Disable => KindDisable,
            RemapKind.Command => KindCommand,
            _ => KindNormal,
        }).IsChecked = true;
        KeysPanel.Visibility = kind == RemapKind.Keys ? Visibility.Visible : Visibility.Collapsed;
        TextPanel.Visibility = kind == RemapKind.Text ? Visibility.Visible : Visibility.Collapsed;
        CommandPanel.Visibility = kind == RemapKind.Command ? Visibility.Visible : Visibility.Collapsed;
        DisableHint.Visibility = kind == RemapKind.Disable ? Visibility.Visible : Visibility.Collapsed;
        NormalHint.Visibility = kind is null ? Visibility.Visible : Visibility.Collapsed;
        RemapEnabled.Visibility = kind is null ? Visibility.Collapsed : Visibility.Visible;

        if (kind == RemapKind.Keys)
        {
            var (mods, main) = Hotkey.Parse(_remap!.To);
            TargetCtrl.IsChecked = mods.HasFlag(Modifiers.Ctrl);
            TargetAlt.IsChecked = mods.HasFlag(Modifiers.Alt);
            TargetShift.IsChecked = mods.HasFlag(Modifiers.Shift);
            TargetWin.IsChecked = mods.HasFlag(Modifiers.Win);
            TargetKeyBox.Text = main;
            TargetCaps.Content = _remap.To;
        }
        UpdateWarning();
        _updating = false;
    }

    private void UpdateWarning()
    {
        string? warning = null;
        if (_source is "Mouse 1" or "Mouse 2" && _remap is not null && _selected is { Processes.Count: 0 })
            warning = Loc.T("kboard.warnClick");
        else if (_remap is { Kind: RemapKind.Keys } r && Hotkey.Comparer.Equals(Hotkey.Normalize(r.To), Hotkey.Normalize(r.From)))
            warning = Loc.T("kboard.warnSame");
        Warning.Visibility = warning is null ? Visibility.Collapsed : Visibility.Visible;
        WarningText.Text = warning ?? string.Empty;
    }

    // ── what the key does ────────────────────────────────────────────────

    private void OnKind(object sender, RoutedEventArgs e)
    {
        if (_updating || _selected is null || _source is null) return;
        RemapKind? kind = sender == KindKeys ? RemapKind.Keys
            : sender == KindText ? RemapKind.Text
            : sender == KindDisable ? RemapKind.Disable
            : sender == KindCommand ? RemapKind.Command
            : null;

        if (kind is null)
        {
            if (_remap is not null) _selected.Remaps.Remove(_remap);
            _remap = null;
        }
        else if (_remap is null)
        {
            _remap = new KeyRemap { From = Hotkey.Format(Layer, _source), Kind = kind.Value };
            if (kind == RemapKind.Command && Config.Commands.FirstOrDefault() is { } first)
            {
                _remap.CommandId = first.Id;
                _remap.CommandName = first.Name;
            }
            _selected.Remaps.Add(_remap);
        }
        else
        {
            if (_remap.Kind != kind && kind is RemapKind.Keys or RemapKind.Text) _remap.To = string.Empty;
            _remap.Kind = kind.Value;
        }
        ShowRemap();
        _updating = true;
        RemapList.SelectedItem = _remap;
        _updating = false;
        if (kind == RemapKind.Keys && string.IsNullOrEmpty(_remap?.To)) Dispatcher.BeginInvoke(() => TargetKeyBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        if (kind == RemapKind.Text) Dispatcher.BeginInvoke(() => TextBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void ComposeTarget()
    {
        if (_updating || _remap is not { Kind: RemapKind.Keys }) return;
        var key = KeyNames.Canonical(TargetKeyBox.Text ?? string.Empty);
        var mods = (TargetCtrl.IsChecked == true ? Modifiers.Ctrl : 0) | (TargetAlt.IsChecked == true ? Modifiers.Alt : 0) |
                   (TargetShift.IsChecked == true ? Modifiers.Shift : 0) | (TargetWin.IsChecked == true ? Modifiers.Win : 0);
        _remap.To = key.Length == 0 ? Hotkey.Format(mods, string.Empty).TrimEnd(' ', '+') : Hotkey.Format(mods, key);
        TargetCaps.Content = _remap.To;
        UpdateWarning();
    }

    private void OnTargetKeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || TargetKeyBox.SelectedItem is not string key) return;
        TargetKeyBox.Text = key;
        ComposeTarget();
    }

    private void OnTargetKeyLostFocus(object sender, RoutedEventArgs e) => ComposeTarget();

    private void OnTargetMod(object sender, RoutedEventArgs e) => ComposeTarget();

    private void OnCaptureTarget(object sender, RoutedEventArgs e)
    {
        if (_remap is not { Kind: RemapKind.Keys } remap) return;
        CaptureTargetButton.Content = Loc.T("commands.pressHotkey");
        HotkeyCapture.Start(_ => { }, hotkey =>
        {
            CaptureTargetButton.Content = Loc.T("editor.captureKeys");
            if (hotkey is null) return;
            var (mods, main) = Hotkey.Parse(hotkey);
            remap.To = Hotkey.Format(mods, main.Split(Hotkey.Separator).Last());
            if (ReferenceEquals(remap, _remap)) ShowRemap();
        }, Window.GetWindow(this));
    }

    private void OnCommandChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_remap is not null && CommandBox.SelectedItem is Command c) _remap.CommandName = c.Name;
    }

    // ── list of remaps ───────────────────────────────────────────────────

    /// <summary>A remap picked with the mouse, waiting for the button to be released.</summary>
    private KeyRemap? _pendingRemap;

    private void OnRemapSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || RemapList.SelectedItem is not KeyRemap remap) return;
        // Showing it changes the panel above the list, which moves the list: with the button still pressed, the
        // list would then select whatever ended up under the mouse. So with the mouse it is shown on release.
        if (Mouse.LeftButton == MouseButtonState.Pressed && RemapList.IsMouseOver)
        {
            _pendingRemap = remap;
            return;
        }
        ShowRemapOf(remap);
    }

    private void OnRemapMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_pendingRemap is not { } remap) return;
        _pendingRemap = null;
        Dispatcher.BeginInvoke(() => ShowRemapOf(remap), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void ShowRemapOf(KeyRemap remap)
    {
        if (_selected is null || !_selected.Remaps.Contains(remap)) return;
        var (mods, main) = Hotkey.Parse(remap.From);
        SetLayer(mods);
        SelectSource(main);
    }

    private void OnDeleteRemap(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: KeyRemap remap }) DeleteRemap(remap);
    }

    private void DeleteRemap(KeyRemap remap)
    {
        if (_selected is null) return;
        _selected.Remaps.Remove(remap);
        if (ReferenceEquals(remap, _remap)) SelectSource(_source);
    }

    private void OnRemapRightClick(object sender, MouseButtonEventArgs e)
    {
        if (ListContextMenu.SelectRow(RemapList, e) is not { DataContext: KeyRemap remap } row) return;
        ListContextMenu.Open(row,
            ListContextMenu.Entry(remap.Enabled ? Loc.T("commands.disable") : Loc.T("commands.enable"),
                remap.Enabled ? SymbolRegular.Prohibited24 : SymbolRegular.CheckmarkCircle24, () => remap.Enabled = !remap.Enabled),
            new Separator(),
            ListContextMenu.Entry(Loc.T("common.delete"), SymbolRegular.Delete24, () => DeleteRemap(remap)));
    }

    private async void OnClearAll(object sender, RoutedEventArgs e)
    {
        if (_selected is not { Remaps.Count: > 0 } profile) return;
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("kboard.clearAll"),
            Content = Loc.T("kboard.clearAllText", profile.Remaps.Count, profile.Name),
            PrimaryButtonText = Loc.T("common.delete"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        profile.Remaps.Clear();
        SelectSource(_source);
    }

    private void OnListWheel(object sender, MouseWheelEventArgs e) => InputPolish.ForwardWheel(sender, e, EditorScroll);
}

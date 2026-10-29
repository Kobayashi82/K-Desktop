using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using KDesktop.Core.Achievements;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Pages;

/// <summary>
/// Disposiciones (E1, 5.0.375; zones since E1b): how each monitor is cut into zones and which programs go in each one, put
/// with «Disposición de ventanas». A list and an editor, as Textos and Macros; in «Zonas», a pill per monitor and its drawing.
/// </summary>
public sealed partial class ArrangementsPage : UserControl, IDisposable
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private readonly App _app = App.Current;
    private readonly ListCollectionView _view;
    private WindowArrangement? _selected;

    /// <summary>A monitor of the pills: connected now or seen before; its size gives the drawing its proportions.</summary>
    private sealed record MonitorChoice(string Name, string Device, bool Connected, int Width, int Height, int WorkWidth, int WorkHeight);

    private List<MonitorChoice> _monitors = [];
    private MonitorChoice? _monitor;
    private ArrangedZone? _zone;

    private AppConfig Config => _app.Config;
    private AppSettings Settings => Config.Settings;

    public ArrangementsPage()
    {
        InitializeComponent();
        ZonesExpander.IsExpanded = !Settings.ArrangementZonesFolded;
        OptionsExpander.IsExpanded = Settings.ArrangementOptionsOpen;
        NotesExpander.IsExpanded = !Settings.ArrangementNotesFolded;
        _view = new ListCollectionView(Config.Arrangements) { Filter = Filter, IsLiveSorting = true, IsLiveFiltering = true };
        _view.SortDescriptions.Add(new SortDescription(nameof(WindowArrangement.Name), ListSortDirection.Ascending));
        _view.LiveSortingProperties.Add(nameof(WindowArrangement.Name));
        _view.LiveFilteringProperties.Add(nameof(WindowArrangement.Name));
        ArrangementList.ItemsSource = _view;
        Config.Arrangements.CollectionChanged += OnArrangementsChanged;
        Surface.ZoneSelected += OnZoneSelected;
        BuildTemplates();
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

    /// <summary>«Zonas», «Opciones» and «Notas» stay as they were left, from one to the next.</summary>
    private void OnSectionFolded(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        Settings.ArrangementZonesFolded = ZonesExpander.IsExpanded != true;
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
            (a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
             a.Zones.Any(z => z.Programs.Any(p => p.Display.Contains(q, StringComparison.CurrentCultureIgnoreCase))));
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
        _zone = null;
        // The monitor shown stays from one disposición to the next; else the first one with zones in it, or the main one.
        BuildMonitors(keep: _monitor);
        Editor.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(180)));
        EditorShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
    }

    private void Track(WindowArrangement? arrangement)
    {
        if (_selected is not null) _selected.Zones.CollectionChanged -= OnZonesChanged;
        _selected = arrangement;
        if (arrangement is not null) arrangement.Zones.CollectionChanged += OnZonesChanged;
    }

    /// <summary>Zones added or removed (a cut, a join, a template…): the dots of the pills follow.</summary>
    private void OnZonesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => PaintPills();

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

    // ── monitors ─────────────────────────────────────────────────────────

    /// <summary>
    /// The pills: the monitors connected now (left to right), then the ones seen before (remembered in the settings) and the
    /// ones only its zones name; a faded one is not connected.
    /// </summary>
    private void BuildMonitors(MonitorChoice? keep)
    {
        var connected = WindowArrangements.Monitors();
        WindowArrangements.Remember(Settings.KnownMonitors, connected);
        var list = connected.Select(m => new MonitorChoice(m.Name, m.Device, true, m.Area.Width, m.Area.Height, m.WorkArea.Width, m.WorkArea.Height)).ToList();
        foreach (var k in Settings.KnownMonitors)
            if (!list.Any(c => Same(c, k.Name, k.Device)))
                list.Add(new MonitorChoice(k.Name, k.Device, false, k.Width, k.Height, k.WorkWidth, k.WorkHeight));
        foreach (var z in _selected?.Zones ?? [])
            if (ChoiceOf(z, list) is null)
                list.Add(new MonitorChoice(z.MonitorName, z.MonitorDevice, false, 1920, 1080, 1920, 1040));
        _monitors = list;

        MonitorPills.Children.Clear();
        foreach (var choice in list)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(7, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, Tag = choice };
            dot.SetResourceReference(Shape.FillProperty, "Kd.Accent");
            var pill = new RadioButton
            {
                GroupName = "ArrangementMonitor", Tag = choice,
                Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = Label(choice) }, dot } },
                Opacity = choice.Connected ? 1 : 0.55,
                ToolTip = ZoneSurface.Resolution(choice.Width, choice.Height) + (choice.Connected ? "" : "  ·  " + Loc.T("arrangement.notConnected")),
            };
            pill.SetResourceReference(StyleProperty, "Pill");
            System.Windows.Automation.AutomationProperties.SetName(pill, Label(choice));
            pill.Checked += (_, _) => ShowMonitor(choice);
            MonitorPills.Children.Add(pill);
        }
        PaintPills();
        var show = (keep is null ? null : list.FirstOrDefault(c => Same(c, keep.Name, keep.Device)))
                   ?? list.FirstOrDefault(c => _selected?.Zones.Any(z => ReferenceEquals(ChoiceOf(z, list), c)) == true)
                   ?? list.FirstOrDefault(c => c.Connected && connected.Any(m => m.Primary && Same(c, m.Name, m.Device)))
                   ?? list.FirstOrDefault();
        if (show is null) return;
        var chosen = MonitorPills.Children.OfType<RadioButton>().First(p => ReferenceEquals(p.Tag, show));
        if (chosen.IsChecked == true) ShowMonitor(show);
        else chosen.IsChecked = true;
    }

    /// <summary>Its name; two connected the same carry their Windows number («LG 27GL850 · 2»).</summary>
    private string Label(MonitorChoice choice) =>
        _monitors.Count(c => c.Name.Equals(choice.Name, StringComparison.OrdinalIgnoreCase)) > 1
            ? $"{choice.Name}  ·  {choice.Device.Replace(@"\\.\DISPLAY", "", StringComparison.OrdinalIgnoreCase)}"
            : choice.Name;

    private static bool Same(MonitorChoice c, string name, string device) =>
        c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && c.Device.Equals(device, StringComparison.OrdinalIgnoreCase);

    /// <summary>The pill a zone is on: its name and GDI name; else the only one with its name (Windows may number it otherwise now).</summary>
    private static MonitorChoice? ChoiceOf(ArrangedZone zone, IReadOnlyList<MonitorChoice> list)
    {
        var named = list.Where(c => c.Name.Equals(zone.MonitorName, StringComparison.OrdinalIgnoreCase)).ToList();
        return named.FirstOrDefault(c => c.Device.Equals(zone.MonitorDevice, StringComparison.OrdinalIgnoreCase)) ?? (named.Count == 1 ? named[0] : null);
    }

    private bool OnShownMonitor(ArrangedZone zone) => _monitor is not null && ReferenceEquals(ChoiceOf(zone, _monitors), _monitor);

    /// <summary>The dot of a pill: that monitor has zones in this disposición.</summary>
    private void PaintPills()
    {
        foreach (var pill in MonitorPills.Children.OfType<RadioButton>())
            if (pill is { Tag: MonitorChoice choice, Content: StackPanel { Children: [_, Ellipse dot] } })
                dot.Visibility = _selected?.Zones.Any(z => ReferenceEquals(ChoiceOf(z, _monitors), choice)) == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowMonitor(MonitorChoice choice)
    {
        if (_selected is not { } arrangement) return;
        _monitor = choice;
        if (_zone is not null && !OnShownMonitor(_zone)) _zone = null;
        Surface.Show(arrangement.Zones, OnShownMonitor, choice.Name, choice.Device, choice.WorkWidth, choice.WorkHeight, _zone);
        ShowZone(_zone);
    }

    // ── zones ────────────────────────────────────────────────────────────

    private void BuildTemplates()
    {
        foreach (var template in Enum.GetValues<ZoneTemplate>())
            TemplatePills.Children.Add(TemplatePill(Loc.T("arrangement.template." + template), () => ApplyTemplate(template)));
        TemplatePills.Children.Add(TemplatePill(Loc.T("arrangement.clearZones"), ClearZones));
    }

    /// <summary>
    /// A pill that does something: it is not left chosen, nor with the focus border (it would look chosen). Smaller than the
    /// monitors' ones, so all of them fit in a row at K-Desktop's smallest width.
    /// </summary>
    private static RadioButton TemplatePill(string text, Action apply)
    {
        var pill = new RadioButton { Content = text, GroupName = "ArrangementTemplate", Focusable = false, FontSize = 11, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 3, 4) };
        pill.SetResourceReference(StyleProperty, "Pill");
        pill.Checked += (_, _) =>
        {
            pill.IsChecked = false;
            apply();
        };
        return pill;
    }

    private List<ArrangedZone> ShownZones() => [.. _selected?.Zones.Where(OnShownMonitor) ?? []];

    private void ApplyTemplate(ZoneTemplate template)
    {
        if (_selected is not { } arrangement || _monitor is not { } monitor) return;
        var fresh = ZoneCuts.Apply(template, arrangement.Zones, ShownZones(), monitor.Name, monitor.Device);
        _zone = fresh.FirstOrDefault();
        Surface.Refresh(_zone);
        ShowZone(_zone);
    }

    /// <summary>«Quitar zonas»: this monitor is left without zones (it is not touched when the disposición is put).</summary>
    private void ClearZones()
    {
        if (_selected is not { } arrangement) return;
        foreach (var zone in ShownZones()) arrangement.Zones.Remove(zone);
        _zone = null;
        Surface.Refresh(null);
        ShowZone(null);
    }

    private void OnZoneSelected(ArrangedZone? zone)
    {
        _zone = zone;
        ShowZone(zone);
    }

    /// <summary>Below the drawing, the programs of the zone marked.</summary>
    private void ShowZone(ArrangedZone? zone)
    {
        NoZoneSelected.Visibility = zone is null ? Visibility.Visible : Visibility.Collapsed;
        ZonePanel.Visibility = zone is null ? Visibility.Collapsed : Visibility.Visible;
        if (zone is null)
        {
            ZonePrograms.ItemsSource = null;
            return;
        }
        var number = ShownZones().IndexOf(zone) + 1;
        ZoneTitle.Text = Loc.T("arrangement.zonePrograms", number);
        ZonePrograms.ItemsSource = zone.Programs;
    }

    private void OnRemoveZone(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _zone is null) return;
        _selected.Zones.Remove(_zone);
        _zone = null;
        Surface.Refresh(null);
        ShowZone(null);
    }

    // ── programs of a zone (as «Solo en estas aplicaciones» of K-Board) ───

    private List<string> ZoneProcesses() => [.. _zone?.Programs.Select(p => p.Process) ?? []];

    private void OnProcessDropDown(object sender, EventArgs e) => ProcessCombo.Fill(ProcessBox, ZoneProcesses());

    private void OnProcessPicked(object sender, EventArgs e)
    {
        if (ProcessBox.SelectedItem is not string picked) return;
        AddProgram(picked);
        ProcessBox.SelectedItem = null;
        ProcessBox.Text = string.Empty;
    }

    private void OnProcessKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        OnAddProcess(sender, e);
        e.Handled = true; // stays in the box to add another
    }

    private void OnAddProcess(object sender, RoutedEventArgs e)
    {
        AddProgram(ProcessBox.Text);
        ProcessBox.SelectedItem = null;
        ProcessBox.Text = string.Empty;
    }

    private void OnPickProcess(object sender, RoutedEventArgs e) => WindowPicker.Pick(AddProgram);

    /// <summary>A program to the zone marked: its name and file from one of its windows, if it is open (to open it when it is not).</summary>
    private void AddProgram(string text)
    {
        if (_zone is not { } zone) return;
        var process = ProcessNames.Normalize(text);
        if (process.Length == 0 || zone.Programs.Any(p => p.Process == process)) return;
        var window = AppFinder.OpenWindows().FirstOrDefault(w => w.Process.Equals(process, StringComparison.OrdinalIgnoreCase));
        var path = window is null ? string.Empty : WindowTools.ProcessPath((int)window.Pid) ?? string.Empty;
        if (process == "applicationframehost") path = string.Empty;
        zone.Programs.Add(new ZoneProgram { Process = process, Name = window?.DisplayName ?? string.Empty, Path = path });
        Surface.Refresh(zone);
    }

    private void OnRemoveProgram(object sender, RoutedEventArgs e)
    {
        if (_zone is null || sender is not FrameworkElement { Tag: ZoneProgram program }) return;
        _zone.Programs.Remove(program);
        Surface.Refresh(_zone);
    }

    // ── save as they are now, try it ─────────────────────────────────────

    /// <summary>«Guardar cómo están ahora»: the windows open now take the place of its zones, on every monitor (asked first if it had some).</summary>
    private async void OnCapture(object sender, RoutedEventArgs e)
    {
        if (_selected is not { } arrangement) return;
        if (arrangement.Zones.Count > 0)
        {
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Title = Loc.T("arrangement.capture"),
                Content = Loc.T("arrangement.captureReplace"),
                PrimaryButtonText = Loc.T("arrangement.captureYes"),
                CloseButtonText = Loc.T("common.cancel"),
            };
            if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        }
        Capture(arrangement);
    }

    private void Capture(WindowArrangement arrangement)
    {
        var zones = WindowArrangements.Capture();
        arrangement.Zones.Clear();
        foreach (var zone in zones) arrangement.Zones.Add(zone);
        _zone = null;
        if (ReferenceEquals(arrangement, _selected)) BuildMonitors(keep: _monitor);
        _app.Ui.ShowOsd(Loc.T("osd.arrangementSaved", arrangement.Name, arrangement.Summary), Core.Engine.OsdKind.Success, look: Core.Engine.OsdLook.Of("WindowApps24"));
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
        foreach (var active in Settings.ActiveArrangements.Where(a => a.ArrangementId == arrangement.Id).ToList()) Settings.ActiveArrangements.Remove(active);
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

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Ellipse = System.Windows.Shapes.Ellipse;
using Rectangle = System.Windows.Shapes.Rectangle;
using KDesktop.Core;
using KDesktop.Core.Calendar;
using KDesktop.Core.Engine;
using KDesktop.Core.Localization;
using Microsoft.Win32;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using UiButton = Wpf.Ui.Controls.Button;

namespace KDesktop.Views.Pages;

/// <summary>
/// «Agenda» (calendar, phase 2): the citas by days from today on, with their calendar's colour; new, change, delete
/// (a series asks «solo esta / esta y las siguientes / todas»), hide calendars, import and export .ics (also by
/// dragging a .ics onto it).
/// </summary>
public partial class AgendaPage : UserControl
{
    private static CalendarService Calendar => App.Current.Calendar;

    /// <summary>Days shown before and after today (the buttons add more).</summary>
    private int _back, _ahead = 42;

    private bool _subscribed;

    public AgendaPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!_subscribed) Calendar.Changed += OnCalendarChanged;
            _subscribed = true;
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            Calendar.Changed -= OnCalendarChanged;
            _subscribed = false;
        };
        Loc.Instance.Changed += () => Dispatcher.BeginInvoke(Refresh);
    }

    private void OnCalendarChanged() => Dispatcher.BeginInvoke(Refresh);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private void Refresh()
    {
        if (!IsLoaded) return;
        BuildChips();
        BuildProblems();
        var today = Today;
        var from = new DateTimeOffset(today.AddDays(-_back).ToDateTime(TimeOnly.MinValue));
        var to = new DateTimeOffset(today.AddDays(_ahead + 1).ToDateTime(TimeOnly.MinValue));
        var search = SearchBox.Text.Trim();
        var list = search.Length > 0 ? Calendar.Search(search, from, to) : Calendar.Occurrences(from, to, visibleOnly: true);
        var calendars = Calendar.Calendars.ToDictionary(c => c.SourceId + "/" + c.Id);
        if (search.Length > 0) list = [.. list.Where(o => !calendars.TryGetValue(o.Series.SourceId + "/" + o.Series.CalendarId, out var c) || c.Visible)];

        Rows.Children.Clear();
        // A cita of several days shows on each of them.
        var byDay = new SortedDictionary<DateOnly, List<CalendarOccurrence>>();
        foreach (var o in list)
        {
            var first = o.AllDay ? DateOnly.FromDateTime(o.Event.Start) : DateOnly.FromDateTime(o.Start.LocalDateTime);
            var last = o.AllDay ? DateOnly.FromDateTime(o.Event.End).AddDays(-1) : DateOnly.FromDateTime((o.End > o.Start ? o.End.AddTicks(-1) : o.Start).LocalDateTime);
            if (last < first) last = first;
            for (var d = first; d <= last && d.DayNumber - first.DayNumber < 366; d = d.AddDays(1))
            {
                if (d < today.AddDays(-_back) || d > today.AddDays(_ahead)) continue;
                if (!byDay.TryGetValue(d, out var day)) byDay[d] = day = [];
                day.Add(o);
            }
        }
        foreach (var (day, items) in byDay)
        {
            Rows.Children.Add(DayHeader(day, today));
            foreach (var o in items.OrderBy(o => !o.AllDay).ThenBy(o => o.Start))
                Rows.Children.Add(Row(o, day, calendars.GetValueOrDefault(o.Series.SourceId + "/" + o.Series.CalendarId)));
        }
        var count = list.Count;
        CountText.Text = count.ToString(Loc.Instance.Culture);
        Empty.Visibility = byDay.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = search.Length > 0 ? Loc.T("agenda.noResults") : Loc.T("agenda.empty");
        ExportButton.IsEnabled = Calendar.Calendars.Count > 0;
        SyncButton.Visibility = Calendar.Sources.Any(s => s.Kind != CalendarSourceKind.Local) ? Visibility.Visible : Visibility.Collapsed;
    }

    private static FrameworkElement DayHeader(DateOnly day, DateOnly today)
    {
        var text = new TextBlock
        {
            Text = CalendarText.Day(day, today),
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            Margin = new Thickness(2, 14, 0, 6),
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, day == today ? "Kd.AccentText" : "TextFillColorPrimaryBrush");
        return text;
    }

    private FrameworkElement Row(CalendarOccurrence o, DateOnly day, CalendarInfo? calendar)
    {
        var grid = new Grid { Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bar = new Rectangle { Width = 4, RadiusX = 2, RadiusY = 2, Margin = new Thickness(0, 0, 12, 0), Fill = Brush(calendar?.Color) };
        grid.Children.Add(bar);

        var hours = new TextBlock { Text = CalendarText.Hours(o, day), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        hours.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        Grid.SetColumn(hours, 1);
        grid.Children.Add(hours);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal };
        titleLine.Children.Add(new TextBlock { Text = o.Event.Title.Length > 0 ? o.Event.Title : Loc.T("agenda.untitled"), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (o.Series.IsRecurring)
            titleLine.Children.Add(new SymbolIcon { Symbol = SymbolRegular.ArrowRepeatAll24, FontSize = 13, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = CalendarText.Repeat(o.Series.Recurrence) });
        if (o.Event.Reminders.Count > 0)
            titleLine.Children.Add(new SymbolIcon { Symbol = SymbolRegular.Alert24, FontSize = 13, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = string.Join(", ", o.Event.Reminders.Select(CalendarText.Minutes)) });
        texts.Children.Add(titleLine);
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(o.Event.Location)) details.Add(o.Event.Location!);
        if (calendar is not null) details.Add(calendar.Name);
        var detail = new TextBlock { Text = string.Join("  ·  ", details), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        texts.Children.Add(detail);
        Grid.SetColumn(texts, 2);
        grid.Children.Add(texts);

        if (o.Event.MeetingUrl is { } meeting)
        {
            var join = new UiButton { Icon = new SymbolIcon { Symbol = SymbolRegular.Video24 }, Content = Loc.T("agenda.join"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            join.Click += (_, e) =>
            {
                e.Handled = true;
                App.OpenMeeting(meeting);
            };
            Grid.SetColumn(join, 3);
            grid.Children.Add(join);
        }

        var border = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 12, 8), Margin = new Thickness(0, 0, 0, 4), Child = grid, Cursor = Cursors.Hand };
        border.SetResourceReference(Border.BackgroundProperty, "Kd.Subtle");
        border.MouseEnter += (_, _) => border.SetResourceReference(Border.BackgroundProperty, "Kd.SubtleHover");
        border.MouseLeave += (_, _) => border.SetResourceReference(Border.BackgroundProperty, "Kd.Subtle");
        border.MouseLeftButtonUp += (_, _) => _ = EditAsync(o);
        border.ContextMenu = Menu(o, calendar);
        return border;
    }

    private ContextMenu Menu(CalendarOccurrence o, CalendarInfo? calendar)
    {
        var menu = new ContextMenu();
        var canWrite = calendar is { ReadOnly: false } && Calendar.Source(o.Series.SourceId)?.Capabilities.HasFlag(CalendarCapabilities.Write) == true;
        menu.Items.Add(Item(canWrite ? "agenda.edit" : "agenda.see", SymbolRegular.Edit24, () => _ = EditAsync(o)));
        if (o.Event.MeetingUrl is { } url) menu.Items.Add(Item("agenda.join", SymbolRegular.Video24, () => App.OpenMeeting(url)));
        menu.Items.Add(Item("agenda.copy", SymbolRegular.Copy24, () => Clipboard.SetText(Describe(o))));
        if (canWrite)
        {
            menu.Items.Add(new Separator());
            var delete = Item("common.delete", SymbolRegular.Delete24, () => _ = DeleteAsync(o));
            delete.SetResourceReference(ForegroundProperty, "Kd.Danger");
            menu.Items.Add(delete);
        }
        return menu;
    }

    private static MenuItem Item(string key, SymbolRegular icon, Action click)
    {
        var item = new MenuItem { Header = Loc.T(key), Icon = new SymbolIcon { Symbol = icon } };
        item.Click += (_, _) => click();
        return item;
    }

    /// <summary>«Dentista — jueves, 5 de noviembre, 17:00 – 18:00 (Consulta 3)».</summary>
    public static string Describe(CalendarOccurrence o)
    {
        var day = o.AllDay ? DateOnly.FromDateTime(o.Event.Start) : DateOnly.FromDateTime(o.Start.LocalDateTime);
        var place = string.IsNullOrWhiteSpace(o.Event.Location) ? string.Empty : $" ({o.Event.Location})";
        return $"{o.Event.Title} — {CalendarText.Day(day, Today)}, {CalendarText.Hours(o, day)}{place}";
    }

    public static Brush Brush(string? color)
    {
        try
        {
            if (ColorConverter.ConvertFromString(color ?? CalendarColors.Default) is Color c)
            {
                var brush = new SolidColorBrush(c);
                brush.Freeze();
                return brush;
            }
        }
        catch (FormatException) { }
        return Brushes.SteelBlue;
    }

    private void BuildChips()
    {
        CalendarChips.Children.Clear();
        var all = Calendar.Calendars;
        if (all.Count < 2)
        {
            CalendarChips.Visibility = Visibility.Collapsed;
            return;
        }
        CalendarChips.Visibility = Visibility.Visible;
        foreach (var c in all)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = Brush(c.Color), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            content.Children.Add(new TextBlock { Text = c.Name, VerticalAlignment = VerticalAlignment.Center });
            var chip = new ToggleButton { Content = content, IsChecked = c.Visible, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 4, 10, 4), ToolTip = Loc.T("agenda.toggleCalendar") };
            var info = c;
            chip.Click += (_, _) => Calendar.SetLook(info with { Visible = chip.IsChecked == true });
            CalendarChips.Children.Add(chip);
        }
    }

    private void BuildProblems()
    {
        Problems.Children.Clear();
        foreach (var s in Calendar.Sources.Where(s => s.Problem is not null))
        {
            var text = new TextBlock { Text = $"{s.Name}: {s.Problem}", TextWrapping = TextWrapping.Wrap };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            var box = new Border { Child = text, Margin = new Thickness(0, 0, 0, 8) };
            box.SetResourceReference(StyleProperty, "InfoBox");
            Problems.Children.Add(box);
        }
    }

    // ── actions ──────────────────────────────────────────────────────────

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnEarlier(object sender, RoutedEventArgs e)
    {
        _back += 30;
        Refresh();
    }

    private void OnLater(object sender, RoutedEventArgs e)
    {
        _ahead += 60;
        Refresh();
    }

    private async void OnSync(object sender, RoutedEventArgs e)
    {
        SyncButton.IsEnabled = false;
        try { await Calendar.SyncAllAsync(); }
        finally { SyncButton.IsEnabled = true; }
        Refresh();
    }

    private void OnOptions(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.Navigate("settings/calendars");

    private async void OnNew(object sender, RoutedEventArgs e)
    {
        var start = DateTime.Now.Date.AddHours(DateTime.Now.Hour + 1);
        var draft = new CalendarEvent { Start = start, End = start.AddHours(1), TimeZone = null };
        if (Dialogs.CalendarEventDialog.Edit(Window.GetWindow(this), draft, null) is not { } result) return;
        await RunAsync(() => Calendar.AddAsync(result.Event));
    }

    private async Task EditAsync(CalendarOccurrence o)
    {
        if (Dialogs.CalendarEventDialog.Edit(Window.GetWindow(this), o.Appearance, o) is not { } result) return;
        if (result.Delete)
        {
            await DeleteAsync(o);
            return;
        }
        var scope = EditScope.All;
        if (o.Series.IsRecurring && o.RecurrenceId is not null)
        {
            var ruleChanged = result.Event.Recurrence != o.Series.Recurrence;
            if (Dialogs.RecurringScopeDialog.Ask(Window.GetWindow(this), ruleChanged ? [EditScope.ThisAndFollowing, EditScope.All] : null, delete: false) is not { } chosen) return;
            scope = chosen;
        }
        await RunAsync(() => Calendar.ChangeAsync(o, result.Event, scope));
    }

    private async Task DeleteAsync(CalendarOccurrence o)
    {
        EditScope scope;
        if (o.Series.IsRecurring && o.RecurrenceId is not null)
        {
            if (Dialogs.RecurringScopeDialog.Ask(Window.GetWindow(this), null, delete: true) is not { } chosen) return;
            scope = chosen;
        }
        else
        {
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Title = Loc.T("agenda.deleteTitle"),
                Content = Loc.T("agenda.deleteText", o.Event.Title),
                PrimaryButtonText = Loc.T("common.delete"),
                CloseButtonText = Loc.T("common.cancel"),
            };
            if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
            scope = EditScope.All;
        }
        var change = await RunAsync(() => Calendar.DeleteAsync(o, scope));
        if (change is not null) App.Current.Ui.ShowOsd(Loc.T("agenda.deleted", o.Event.Title), OsdKind.Info, TimeSpan.FromSeconds(3), OsdLook.Of("CalendarCancel24"));
    }

    /// <summary>Runs a change and says what went wrong (read only, conflict, no network) instead of failing.</summary>
    private static async Task<CalendarChange?> RunAsync(Func<Task<CalendarChange>> change)
    {
        try
        {
            return await change();
        }
        catch (Exception ex) when (ex is CalendarException or CalendarConflictException or System.Net.Http.HttpRequestException or IOException or TaskCanceledException)
        {
            Log.Warn($"Agenda: {ex.Message}");
            var box = new Wpf.Ui.Controls.MessageBox { Title = Loc.T("agenda.errorTitle"), Content = ex is CalendarConflictException ? Loc.T("agenda.conflict") : ex.Message, CloseButtonText = Loc.T("common.close") };
            await box.ShowDialogAsync();
            return null;
        }
    }

    // ── .ics ─────────────────────────────────────────────────────────────

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = Loc.T("agenda.icsFilter") + "|*.ics;*.ical;*.ifb;*.icalendar" };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) Import(dlg.FileName);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = IcsFiles(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        foreach (var file in IcsFiles(e)) Import(file);
    }

    private static List<string> IcsFiles(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] files
            ? [.. files.Where(f => f.EndsWith(".ics", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ical", StringComparison.OrdinalIgnoreCase))]
            : [];

    /// <summary>Imports a .ics into an own calendar (asks which when there are several).</summary>
    public void Import(string file)
    {
        string text;
        try { text = File.ReadAllText(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Current.Ui.ShowOsd(Loc.T("agenda.importError", ex.Message), OsdKind.Warning);
            return;
        }
        var own = Calendar.Local.Calendars;
        var target = own.Count == 1 ? own[0] : Dialogs.CalendarPickDialog.Pick(Window.GetWindow(this), own, Loc.T("agenda.importWhere", Path.GetFileName(file)));
        if (target is null) return;
        var count = Calendar.ImportIcs(text, target.Id);
        App.Current.Ui.ShowOsd(Loc.T("agenda.imported", count, target.Name), count > 0 ? OsdKind.Success : OsdKind.Warning, null, OsdLook.Of("CalendarAdd24"));
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var all = Calendar.Calendars;
        var calendar = all.Count == 1 ? all[0] : Dialogs.CalendarPickDialog.Pick(Window.GetWindow(this), all, Loc.T("agenda.exportWhich"));
        if (calendar is null) return;
        var dlg = new SaveFileDialog { Filter = Loc.T("agenda.icsFilter") + "|*.ics", FileName = string.Concat(calendar.Name.Split(Path.GetInvalidFileNameChars())) + ".ics" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, Calendar.ExportIcs(calendar.SourceId, calendar.Id));
            App.Current.Ui.ShowOsd(Loc.T("agenda.exported", calendar.Name), OsdKind.Success, null, OsdLook.Of("ArrowExport24"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Current.Ui.ShowOsd(Loc.T("agenda.importError", ex.Message), OsdKind.Warning);
        }
    }
}

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
/// «Agenda» (calendar, phase 2): the citas as a list by days from today on or as a month, with their calendar's colour;
/// new, change, delete (a series asks «solo esta / esta y las siguientes / todas»), hide calendars, import and export
/// .ics (also by dragging a .ics onto it).
/// </summary>
public partial class AgendaPage : UserControl
{
    private static CalendarService Calendar => App.Current.Calendar;

    /// <summary>Days shown before and after today in the list (the buttons add more).</summary>
    private int _back, _ahead = 42;

    /// <summary>The month shown (its first day).</summary>
    private DateOnly _month = new(Today.Year, Today.Month, 1);

    /// <summary>How many citas fit in a day of the month, from the height of its cells.</summary>
    private int _fit = 3;

    /// <summary>The day the list scrolls to once it is built (today when the page opens).</summary>
    private DateOnly? _scrollTo = Today;

    private bool _subscribed, _ready;

    public AgendaPage()
    {
        InitializeComponent();
        if (App.Current.Config.Settings.AgendaMonthView) MonthTab.IsChecked = true;
        _ready = true;
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

    private bool MonthMode => MonthTab.IsChecked == true;

    private void Refresh()
    {
        if (!IsLoaded) return;
        BuildChips();
        BuildProblems();
        Scroll.Visibility = MonthMode ? Visibility.Collapsed : Visibility.Visible;
        MonthView.Visibility = MonthNav.Visibility = MonthMode ? Visibility.Visible : Visibility.Collapsed;
        if (MonthMode) BuildMonth(0);
        else BuildList();
        ExportButton.IsEnabled = Calendar.Calendars.Count > 0;
        SyncButton.Visibility = Calendar.Sources.Any(s => s.Kind != CalendarSourceKind.Local) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The citas between two days (both included) of the calendars shown, each on every day it covers.</summary>
    private (SortedDictionary<DateOnly, List<CalendarOccurrence>> ByDay, int Count) Load(DateOnly first, DateOnly last)
    {
        var from = new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue));
        var to = new DateTimeOffset(last.AddDays(1).ToDateTime(TimeOnly.MinValue));
        var search = SearchBox.Text.Trim();
        var list = search.Length > 0 ? Calendar.Search(search, from, to) : Calendar.Occurrences(from, to, visibleOnly: true);
        if (search.Length > 0)
        {
            var calendars = Calendar.Calendars.ToDictionary(c => c.SourceId + "/" + c.Id);
            list = [.. list.Where(o => !calendars.TryGetValue(o.Series.SourceId + "/" + o.Series.CalendarId, out var c) || c.Visible)];
        }
        var byDay = new SortedDictionary<DateOnly, List<CalendarOccurrence>>();
        foreach (var o in list)
        {
            var start = o.AllDay ? DateOnly.FromDateTime(o.Event.Start) : DateOnly.FromDateTime(o.Start.LocalDateTime);
            var end = o.AllDay ? DateOnly.FromDateTime(o.Event.End).AddDays(-1) : DateOnly.FromDateTime((o.End > o.Start ? o.End.AddTicks(-1) : o.Start).LocalDateTime);
            if (end < start) end = start;
            for (var d = start; d <= end && d.DayNumber - start.DayNumber < 366; d = d.AddDays(1))
            {
                if (d < first || d > last) continue;
                if (!byDay.TryGetValue(d, out var day)) byDay[d] = day = [];
                day.Add(o);
            }
        }
        foreach (var day in byDay.Values) day.Sort((a, b) => a.AllDay != b.AllDay ? (a.AllDay ? -1 : 1) : a.Start.CompareTo(b.Start));
        return (byDay, list.Count);
    }

    private static Dictionary<string, CalendarInfo> CalendarsById() => Calendar.Calendars.ToDictionary(c => c.SourceId + "/" + c.Id);

    private static CalendarInfo? CalendarOf(Dictionary<string, CalendarInfo> calendars, CalendarOccurrence o) =>
        calendars.GetValueOrDefault(o.Series.SourceId + "/" + o.Series.CalendarId);

    /// <summary>A cita that is over (an all-day one, once its day is gone).</summary>
    private static bool IsPast(CalendarOccurrence o, DateOnly day) => o.AllDay ? day < Today : o.End <= DateTimeOffset.Now;

    private static bool IsNow(CalendarOccurrence o) => !o.AllDay && o.Start <= DateTimeOffset.Now && DateTimeOffset.Now < o.End;

    // ── list ─────────────────────────────────────────────────────────────

    private void BuildList()
    {
        var today = Today;
        var (byDay, count) = Load(today.AddDays(-_back), today.AddDays(_ahead));
        var searching = SearchBox.Text.Trim().Length > 0;
        // Today always has its place, to see at a glance that nothing is left.
        if (!searching && !byDay.ContainsKey(today)) byDay[today] = [];
        var calendars = CalendarsById();

        Rows.Children.Clear();
        FrameworkElement? target = null;
        var month = -1;
        foreach (var (day, items) in byDay)
        {
            if (day.Year * 12 + day.Month != month)
            {
                month = day.Year * 12 + day.Month;
                Rows.Children.Add(MonthHeader(day, Rows.Children.Count == 0));
            }
            var group = DayGroup(day, today, items, calendars);
            if (target is null && _scrollTo is { } to && day >= to) target = group;
            Rows.Children.Add(group);
        }
        CountText.Text = count.ToString(Loc.Instance.Culture);
        Empty.Visibility = byDay.Values.All(d => d.Count == 0) && searching ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = Loc.T("agenda.noResults");

        if (_scrollTo is not null)
        {
            _scrollTo = null;
            Dispatcher.BeginInvoke(() =>
            {
                if (target is null || Scroll.Content is not UIElement content || !target.IsVisible) return;
                Scroll.ScrollToVerticalOffset(Math.Max(0, target.TranslatePoint(new Point(0, 0), content).Y - 8));
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private static TextBlock MonthHeader(DateOnly day, bool first)
    {
        var text = new TextBlock { Text = MonthName(day), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, first ? 4 : 22, 0, 4) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        return text;
    }

    /// <summary>«Noviembre de 2026».</summary>
    private static string MonthName(DateOnly day)
    {
        var culture = Loc.Instance.Culture;
        var text = day.ToString(culture.DateTimeFormat.YearMonthPattern, culture);
        return text.Length > 0 ? char.ToUpper(text[0], culture) + text[1..] : text;
    }

    /// <summary>A day: its number and weekday on the left, «Hoy»/«jueves, 5 de noviembre» and its citas on the right.</summary>
    private FrameworkElement DayGroup(DateOnly day, DateOnly today, List<CalendarOccurrence> items, Dictionary<string, CalendarInfo> calendars)
    {
        var culture = Loc.Instance.Culture;
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badge = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 0) };
        var number = new TextBlock { Text = day.Day.ToString(culture), FontSize = 26, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        var weekday = new TextBlock { Text = culture.DateTimeFormat.GetAbbreviatedDayName(day.DayOfWeek), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
        number.SetResourceReference(TextBlock.ForegroundProperty, day == today ? "Kd.AccentText" : day < today ? "TextFillColorTertiaryBrush" : "TextFillColorPrimaryBrush");
        weekday.SetResourceReference(TextBlock.ForegroundProperty, day == today ? "Kd.AccentText" : "TextFillColorSecondaryBrush");
        badge.Children.Add(number);
        badge.Children.Add(weekday);
        grid.Children.Add(badge);

        var list = new StackPanel();
        var label = new TextBlock { Text = CalendarText.Day(day, today), FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(2, 6, 0, 6) };
        label.SetResourceReference(TextBlock.ForegroundProperty, day == today ? "Kd.AccentText" : "TextFillColorSecondaryBrush");
        list.Children.Add(label);
        foreach (var o in items) list.Children.Add(Row(o, day, CalendarOf(calendars, o)));
        if (items.Count == 0)
        {
            var nothing = new TextBlock { Text = Loc.T("agenda.nothingToday"), Margin = new Thickness(2, 0, 0, 4), FontStyle = FontStyles.Italic };
            nothing.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
            list.Children.Add(nothing);
        }
        Grid.SetColumn(list, 1);
        grid.Children.Add(list);
        return grid;
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

        var now = IsNow(o);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal };
        titleLine.Children.Add(new TextBlock { Text = Title(o), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (now)
        {
            var badgeText = new TextBlock { Text = Loc.T("agenda.now"), FontSize = 11, FontWeight = FontWeights.SemiBold };
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, "Kd.AccentText");
            var badge = new Border { Child = badgeText, CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 0, 7, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            badge.SetResourceReference(Border.BackgroundProperty, "Kd.AccentSoft");
            titleLine.Children.Add(badge);
        }
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
            if (now) join.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            join.Click += (_, e) =>
            {
                e.Handled = true;
                App.OpenMeeting(meeting);
            };
            Grid.SetColumn(join, 3);
            grid.Children.Add(join);
        }

        var border = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 12, 8), Margin = new Thickness(0, 0, 0, 4), Child = grid, Cursor = Cursors.Hand, BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "Kd.Subtle");
        border.SetResourceReference(Border.BorderBrushProperty, now ? "Kd.Accent" : "Kd.Subtle");
        if (IsPast(o, day)) border.Opacity = 0.55;
        border.MouseEnter += (_, _) => border.SetResourceReference(Border.BackgroundProperty, "Kd.SubtleHover");
        border.MouseLeave += (_, _) => border.SetResourceReference(Border.BackgroundProperty, "Kd.Subtle");
        border.MouseLeftButtonUp += (_, _) => _ = EditAsync(o);
        border.ContextMenu = Menu(o, calendar);
        return border;
    }

    private static string Title(CalendarOccurrence o) => o.Event.Title.Length > 0 ? o.Event.Title : Loc.T("agenda.untitled");

    // ── month ────────────────────────────────────────────────────────────

    /// <summary>Builds the month; <paramref name="direction"/> slides it in from the right (1) or the left (-1).</summary>
    private void BuildMonth(int direction)
    {
        var culture = Loc.Instance.Culture;
        var today = Today;
        var firstDay = culture.DateTimeFormat.FirstDayOfWeek;
        var lead = ((int)_month.DayOfWeek - (int)firstDay + 7) % 7;
        var gridStart = _month.AddDays(-lead);
        var weeks = (int)Math.Ceiling((lead + DateTime.DaysInMonth(_month.Year, _month.Month)) / 7.0);
        var gridEnd = gridStart.AddDays(weeks * 7 - 1);
        var (byDay, _) = Load(gridStart, gridEnd);
        var calendars = CalendarsById();

        MonthTitle.Text = MonthName(_month);
        WeekdayNames.Children.Clear();
        for (var i = 0; i < 7; i++)
        {
            var name = new TextBlock { Text = culture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)(((int)firstDay + i) % 7)), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0) };
            name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            WeekdayNames.Children.Add(name);
        }

        MonthGrid.Children.Clear();
        MonthGrid.Rows = weeks;
        _fit = Fit(weeks);
        for (var d = gridStart; d <= gridEnd; d = d.AddDays(1))
            MonthGrid.Children.Add(DayCell(d, today, byDay.GetValueOrDefault(d) ?? [], calendars));

        // The citas of the month itself (one of several days counts once).
        var inMonth = byDay.Where(p => p.Key.Month == _month.Month && p.Key.Year == _month.Year).SelectMany(p => p.Value).Distinct().Count();
        CountText.Text = inMonth.ToString(culture);
        Empty.Visibility = Visibility.Collapsed;

        if (direction != 0)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            MonthShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(28 * direction, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            MonthGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(220)));
        }
    }

    private FrameworkElement DayCell(DateOnly day, DateOnly today, List<CalendarOccurrence> items, Dictionary<string, CalendarInfo> calendars)
    {
        var inMonth = day.Month == _month.Month;
        var panel = new StackPanel();

        var number = new TextBlock { Text = day.Day.ToString(Loc.Instance.Culture), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var circle = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 2), Child = number };
        if (day == today)
        {
            circle.SetResourceReference(Border.BackgroundProperty, "Kd.Accent");
            number.SetResourceReference(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        }
        else number.SetResourceReference(TextBlock.ForegroundProperty, inMonth ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush");
        panel.Children.Add(circle);

        var shown = items.Count > _fit ? Math.Max(0, _fit - 1) : items.Count;
        foreach (var o in items.Take(shown)) panel.Children.Add(MonthItem(o, day, CalendarOf(calendars, o)));
        if (items.Count > shown)
        {
            var more = new TextBlock { Text = Loc.T("agenda.more", items.Count - shown), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 1, 0, 0), Cursor = Cursors.Hand };
            more.SetResourceReference(TextBlock.ForegroundProperty, "Kd.AccentText");
            more.MouseLeftButtonDown += (_, e) => e.Handled = true;
            more.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                ShowDayInList(day);
            };
            panel.Children.Add(more);
        }

        var cell = new Border
        {
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(2),
            Padding = new Thickness(5, 4, 5, 4),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = panel,
            ToolTip = Loc.T("agenda.cellHint"),
        };
        ToolTipService.SetInitialShowDelay(cell, 1500);
        // The days of the months around: only their outline, and their citas faded.
        if (inMonth) cell.SetResourceReference(Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
        else
        {
            cell.Background = Brushes.Transparent;
            panel.Opacity = 0.6;
        }
        cell.SetResourceReference(Border.BorderBrushProperty, day == today ? "Kd.Accent" : "CardStrokeColorDefaultBrush");
        cell.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2) return;
            e.Handled = true;
            _ = NewAsync(day);
        };
        return cell;
    }

    /// <summary>A cita in a day of the month: a band in its colour if it is all-day, a dot and its hour if not.</summary>
    private FrameworkElement MonthItem(CalendarOccurrence o, DateOnly day, CalendarInfo? calendar)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        if (!o.AllDay)
        {
            line.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = Brush(calendar?.Color), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            if (DateOnly.FromDateTime(o.Start.LocalDateTime) == day)
            {
                var hour = new TextBlock { Text = o.Start.ToLocalTime().ToString("t", Loc.Instance.Culture), FontSize = 11.5, Margin = new Thickness(0, 0, 5, 0) };
                hour.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                line.Children.Add(hour);
            }
        }
        var title = new TextBlock { Text = Title(o), FontSize = 11.5, FontWeight = o.AllDay ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        line.Children.Add(title);

        var item = new Border { Child = line, CornerRadius = new CornerRadius(4), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 1, 0, 0), Cursor = Cursors.Hand, ToolTip = Describe(o) };
        var rest = o.AllDay ? Tint(calendar?.Color, 0x40) : Brushes.Transparent;
        item.Background = rest;
        if (IsNow(o)) item.SetResourceReference(Border.BackgroundProperty, "Kd.AccentSoft");
        if (IsPast(o, day)) item.Opacity = 0.55;
        item.MouseEnter += (_, _) => item.SetResourceReference(Border.BackgroundProperty, "Kd.SubtleHover");
        item.MouseLeave += (_, _) =>
        {
            if (IsNow(o)) item.SetResourceReference(Border.BackgroundProperty, "Kd.AccentSoft");
            else item.Background = rest;
        };
        item.MouseLeftButtonDown += (_, e) => e.Handled = true;
        item.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _ = EditAsync(o);
        };
        item.ContextMenu = Menu(o, calendar);
        return item;
    }

    private void OnMonthSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MonthGrid.Rows > 0 && Fit(MonthGrid.Rows) != _fit && MonthMode) BuildMonth(0);
    }

    /// <summary>Lines of citas that fit in a day with that many weeks: about 17 px each, under the day's number (36 px with the margins).</summary>
    private int Fit(int weeks) => MonthGrid.ActualHeight > 0 ? Math.Max(1, (int)((MonthGrid.ActualHeight / weeks - 36) / 17)) : 3;

    private void OnMonthWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        GoMonth(e.Delta < 0 ? 1 : -1);
    }

    private void OnPrevMonth(object sender, RoutedEventArgs e) => GoMonth(-1);

    private void OnNextMonth(object sender, RoutedEventArgs e) => GoMonth(1);

    private void GoMonth(int months)
    {
        _month = _month.AddMonths(months);
        BuildMonth(Math.Sign(months));
    }

    /// <summary>From «+3 más» in the month: that day in the list.</summary>
    private void ShowDayInList(DateOnly day)
    {
        var today = Today;
        if (day < today.AddDays(-_back)) _back = today.DayNumber - day.DayNumber;
        if (day > today.AddDays(_ahead)) _ahead = day.DayNumber - today.DayNumber;
        _scrollTo = day;
        ListTab.IsChecked = true;
    }

    // ── calendars ────────────────────────────────────────────────────────

    public static Brush Brush(string? color) => Tint(color, 0xFF);

    /// <summary>The calendar's colour with that opacity (0x40 for a tint behind text).</summary>
    private static Brush Tint(string? color, byte alpha)
    {
        try
        {
            if (ColorConverter.ConvertFromString(color ?? CalendarColors.Default) is Color c)
            {
                var brush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
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
        var visible = all.Count(c => c.Visible);
        foreach (var c in all)
        {
            var dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center, StrokeThickness = 1.5, Stroke = Brush(c.Color) };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(dot);
            content.Children.Add(new TextBlock { Text = c.Name, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            // The last one shown stays: hiding it would take the Agenda away from the list.
            var last = c.Visible && visible == 1;
            var chip = new ToggleButton
            {
                Style = (Style)Resources["CalendarChip"],
                Content = content,
                IsChecked = c.Visible,
                ToolTip = Loc.T(last ? "agenda.lastVisible" : "agenda.toggleCalendar"),
                IsEnabled = !last,
            };
            ToolTipService.SetShowOnDisabled(chip, true);
            AutomationProperties.SetName(chip, c.Name);
            void Look()
            {
                var on = chip.IsChecked == true;
                dot.Fill = on ? Brush(c.Color) : Brushes.Transparent;
                if (on)
                {
                    chip.Background = Tint(c.Color, 0x2E);
                    chip.BorderBrush = Tint(c.Color, 0xB0);
                }
                else
                {
                    chip.SetResourceReference(BackgroundProperty, "Kd.Subtle");
                    chip.SetResourceReference(BorderBrushProperty, "CardStrokeColorDefaultBrush");
                }
            }
            Look();
            var info = c;
            // Checked/Unchecked, not Click: a screen reader toggles it without clicking.
            void Toggled(object? sender, RoutedEventArgs e)
            {
                Look();
                Calendar.SetLook(info with { Visible = chip.IsChecked == true });
            }
            chip.Checked += Toggled;
            chip.Unchecked += Toggled;
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

    // ── actions ──────────────────────────────────────────────────────────

    private void OnViewChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        App.Current.Config.Settings.AgendaMonthView = MonthMode;
        if (!MonthMode && _scrollTo is null) _scrollTo = Today;
        Refresh();
    }

    private void OnToday(object sender, RoutedEventArgs e)
    {
        var today = Today;
        if (MonthMode)
        {
            var month = new DateOnly(today.Year, today.Month, 1);
            var direction = month.CompareTo(_month);
            _month = month;
            BuildMonth(direction);
            return;
        }
        _scrollTo = today;
        BuildList();
    }

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

    private void OnNew(object sender, RoutedEventArgs e) => _ = NewAsync(null);

    /// <summary>A new cita: at the next hour, or at 9:00 of the day double-clicked in the month (the next hour if it is today).</summary>
    private async Task NewAsync(DateOnly? day)
    {
        var now = DateTime.Now;
        var nextHour = now.Date.AddHours(now.Hour + 1);
        var start = day is { } d && d != Today ? d.ToDateTime(new TimeOnly(9, 0)) : nextHour;
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
            if (await box.ShowCenteredAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
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
            await box.ShowCenteredAsync();
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
            App.Current.Ui.ShowOsd(Loc.T("agenda.exported", calendar.Name), OsdKind.Success, null, OsdLook.Of("ArrowDownload24"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Current.Ui.ShowOsd(Loc.T("agenda.importError", ex.Message), OsdKind.Warning);
        }
    }
}

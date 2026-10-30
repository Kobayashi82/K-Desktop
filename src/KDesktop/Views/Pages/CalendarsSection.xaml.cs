using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Calendar;
using KDesktop.Core.Localization;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using UiTextBox = Wpf.Ui.Controls.TextBox;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options › Calendario (calendar, phases 2, 5, 6; one card since 5.0.430): the default calendar, every calendar —the
/// own ones, those of each account, the .ics subscriptions— with its name, colour and switch, and how the reminders show.
/// </summary>
public partial class CalendarsSection : UserControl
{
    private static CalendarService Calendar => App.Current.Calendar;

    private bool _subscribed;
    private bool _building;

    public CalendarsSection()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!_subscribed)
            {
                Calendar.Changed += OnChanged;
                App.Current.Accounts.Changed += OnChanged;
            }
            _subscribed = true;
            Build();
        };
        Unloaded += (_, _) =>
        {
            Calendar.Changed -= OnChanged;
            App.Current.Accounts.Changed -= OnChanged;
            _subscribed = false;
        };
    }

    private void OnChanged() => Dispatcher.BeginInvoke(() =>
    {
        // Not while the user types a name in a box of this page.
        if (IsKeyboardFocusWithin && Keyboard() is TextBox) return;
        Build();
    });

    private static IInputElement? Keyboard() => System.Windows.Input.Keyboard.FocusedElement;

    private void Build()
    {
        if (_building || !IsLoaded) return;
        _building = true;
        try
        {
            Root.Children.Clear();
            BuildCalendars();
            BuildReminders();
        }
        finally
        {
            _building = false;
        }
    }

    // ── calendars ────────────────────────────────────────────────────────

    /// <summary>
    /// Every calendar in one card (5.0.430), by where it comes from: the own ones, those of each Google account and the
    /// .ics subscriptions; and below, a new own calendar, a new subscription (a window) or connecting an account.
    /// </summary>
    private void BuildCalendars()
    {
        var card = CalendarUi.Card(Root, Loc.T("calendars.title"), Loc.T("calendars.intro"));
        var writable = Calendar.Calendars.Where(c => !c.ReadOnly && Calendar.Source(c.SourceId)?.Capabilities.HasFlag(CalendarCapabilities.Write) == true).ToList();
        var defaultBox = new ComboBox { DisplayMemberPath = "Name", ItemsSource = writable };
        var current = Calendar.DefaultCalendar;
        defaultBox.SelectedItem = writable.FirstOrDefault(c => c.SourceId == current.SourceId && c.Id == current.Id);
        defaultBox.SelectionChanged += (_, _) =>
        {
            if (_building || defaultBox.SelectedItem is not CalendarInfo chosen) return;
            Calendar.Settings.DefaultCalendar = chosen.SourceId + "/" + chosen.Id;
            Calendar.SaveSettings();
        };
        card.Children.Add(CalendarUi.Row(Loc.T("calendars.default"), Loc.T("calendars.defaultHint"), defaultBox));
        card.Children.Add(CalendarUi.Rule());

        // The own ones
        var local = Calendar.Local;
        card.Children.Add(CalendarUi.Label(local.Name));
        foreach (var c in Calendar.Calendars.Where(c => c.SourceId == local.Id)) card.Children.Add(CalendarRow(c, local));

        // Those of each account
        foreach (var account in App.Current.Accounts.Accounts.Where(a => a.Uses.HasFlag(Core.Accounts.AccountUse.CalendarRead)))
        {
            card.Children.Add(new Border { Height = 10 });
            card.Children.Add(CalendarUi.Label("Google · " + account.Display));
            if (account.Disconnected) card.Children.Add(CalendarUi.Status(Loc.T("accounts.disconnected"), "Kd.Warning"));
            var used = Calendar.Settings.Accounts.Any(a => a.AccountId == account.Id);
            if (!used || Calendar.Source(CalendarService.AccountSourceId(account.Id)) is not RemoteCalendar source)
            {
                var use = new ToggleSwitch { IsChecked = false };
                use.Click += (_, _) => Calendar.UseAccount(account.Id, []);
                card.Children.Add(CalendarUi.Row(Loc.T("calendars.useAccount"), null, use, 0));
                continue;
            }
            if (source.Problem is { } problem) card.Children.Add(CalendarUi.Status(problem, "Kd.Warning"));
            var all = source.AllCalendars;
            if (all.Count == 0) card.Children.Add(CalendarUi.Caption(Loc.T("calendars.loadingAccount"), new Thickness(0, 0, 0, 4)));
            var chosen = source.Calendars.Select(c => c.Id).ToHashSet();
            void Choose(string id, bool on)
            {
                var now = source.Calendars.Select(x => x.Id).ToHashSet();
                if (on) now.Add(id);
                else now.Remove(id);
                if (now.Count == 0) Calendar.ForgetAccount(account.Id);
                else Calendar.UseAccount(account.Id, now);
            }
            foreach (var c in all)
            {
                var id = c.Id;
                card.Children.Add(chosen.Contains(id)
                    ? CalendarRow(Calendar.Calendars.FirstOrDefault(x => x.SourceId == source.Id && x.Id == id) ?? c, source, on => Choose(id, on))
                    : UnusedRow(c, () => Choose(id, true)));
            }
        }

        // The .ics subscriptions
        var subscriptions = Calendar.Sources.OfType<IcsSubscription>().ToList();
        if (subscriptions.Count > 0)
        {
            card.Children.Add(new Border { Height = 10 });
            card.Children.Add(CalendarUi.Label(Loc.T("calendars.subscriptions")));
            foreach (var source in subscriptions) card.Children.Add(SubscriptionRow(source));
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(CalendarUi.Button(Loc.T("calendars.newCalendar"), SymbolRegular.CalendarAdd24, NewOwn, primary: true));
        buttons.Children.Add(CalendarUi.Button(Loc.T("calendars.subscribeIcs"), SymbolRegular.Link24, () => Dialogs.SubscriptionDialog.Show(Window.GetWindow(this))));
        buttons.Children.Add(CalendarUi.Button(Loc.T("accounts.connectTitle"), SymbolRegular.PersonAdd24, () => (Window.GetWindow(this) as MainWindow)?.Navigate("settings/accounts/google")));
        card.Children.Add(buttons);
    }

    private static readonly int[] DefaultReminders = [-1, 0, 5, 10, 15, 30, 60, 1440];

    /// <summary>
    /// A calendar shown: its colour, its name (the assistant knows it by it), its default reminder and its switch: shown
    /// in the agenda, or —one of an account, <paramref name="used"/>— read from the account at all.
    /// </summary>
    private FrameworkElement CalendarRow(CalendarInfo c, ICalendarSource source, Action<bool>? used = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(CalendarUi.ColorDot(c.Color, color => Calendar.SetLook(c with { Color = color })));

        var name = new UiTextBox { Text = c.Name, MinWidth = 160, ClearButtonEnabled = false };
        System.Windows.Automation.AutomationProperties.SetName(name, Loc.T("calendars.name"));
        name.LostFocus += (_, _) =>
        {
            var text = name.Text.Trim();
            if (text.Length > 0 && text != c.Name) Calendar.SetLook(c with { Name = text });
        };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var reminders = DefaultReminders.Select(m => new NamedChoice<int>(m, m < 0 ? Loc.T("calendars.noDefaultReminder") : m == 0 ? Loc.T("agenda.reminder.atStart") : Loc.T("agenda.reminder.before", CalendarText.Minutes(m)))).ToList();
        if (c.DefaultReminder is { } r && reminders.All(x => x.Value != r)) reminders.Add(new(r, Loc.T("agenda.reminder.before", CalendarText.Minutes(r))));
        var reminderBox = new ComboBox { ItemsSource = reminders, DisplayMemberPath = "Name", SelectedValuePath = "Value", SelectedValue = c.DefaultReminder ?? -1, Width = 200, Margin = new Thickness(8, 0, 0, 0), ToolTip = Loc.T("calendars.defaultReminder") };
        reminderBox.IsEnabled = !c.ReadOnly;
        reminderBox.SelectionChanged += (_, _) =>
        {
            if (_building || reminderBox.SelectedValue is not int value) return;
            Calendar.SetLook(c with { DefaultReminder = value < 0 ? null : value });
        };
        Grid.SetColumn(reminderBox, 2);
        grid.Children.Add(reminderBox);

        var shown = new ToggleSwitch { IsChecked = used is not null || c.Visible, Margin = new Thickness(12, 0, 0, 0), ToolTip = Loc.T("calendars.visible"), VerticalAlignment = VerticalAlignment.Center };
        shown.Click += (_, _) =>
        {
            if (used is not null) used(shown.IsChecked == true);
            else Calendar.SetLook(c with { Visible = shown.IsChecked == true });
        };
        Grid.SetColumn(shown, 4);
        grid.Children.Add(shown);

        if (source.Kind == CalendarSourceKind.Local && Calendar.Local.Calendars.Count > 1)
        {
            var delete = CalendarUi.IconButton(SymbolRegular.Delete24, Loc.T("calendars.delete"), () => _ = DeleteOwnAsync(c), danger: true);
            Grid.SetColumn(delete, 3);
            grid.Children.Add(delete);
        }
        return grid;
    }

    /// <summary>A calendar of an account that is not read: its name, dimmed, and the switch to read it.</summary>
    private static FrameworkElement UnusedRow(CalendarInfo c, Action use)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4), Opacity = 0.55 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var dot = new System.Windows.Shapes.Ellipse { Width = 16, Height = 16, Fill = AgendaPage.Brush(c.Color), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(dot);
        var name = new TextBlock { Text = c.Name + (c.ReadOnly ? " " + Loc.T("calendars.readOnlyMark") : string.Empty), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 0, 0, 0) };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var shown = new ToggleSwitch { IsChecked = false, Margin = new Thickness(12, 0, 0, 0), ToolTip = Loc.T("calendars.visible"), VerticalAlignment = VerticalAlignment.Center };
        shown.Click += (_, _) => use();
        Grid.SetColumn(shown, 2);
        grid.Children.Add(shown);
        return grid;
    }

    /// <summary>A .ics subscription: colour, name, how it went last time, shown or not, and read now, change (a window) or remove.</summary>
    private FrameworkElement SubscriptionRow(IcsSubscription source)
    {
        var settings = source.Settings;
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(CalendarUi.ColorDot(settings.Color, color =>
        {
            settings.Color = color;
            Calendar.UpdateSubscription(settings);
        }));
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        texts.Children.Add(new TextBlock { Text = settings.Name, TextTrimming = TextTrimming.CharacterEllipsis });
        var status = source.Problem ?? (source.LastSync is { } last
            ? Loc.T("calendars.every", CalendarText.Minutes(settings.RefreshMinutes)) + " · " + Loc.T("calendars.lastSync", last.ToLocalTime().ToString("g", Loc.Instance.Culture), source.Events.Count)
            : Loc.T("calendars.neverSynced"));
        texts.Children.Add(CalendarUi.Status(status, source.Problem is null ? "TextFillColorSecondaryBrush" : "Kd.Warning"));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);

        var look = Calendar.Calendars.FirstOrDefault(c => c.SourceId == source.Id);
        if (look is not null)
        {
            var shown = new ToggleSwitch { IsChecked = look.Visible, Margin = new Thickness(12, 0, 0, 0), ToolTip = Loc.T("calendars.visible"), VerticalAlignment = VerticalAlignment.Center };
            shown.Click += (_, _) => Calendar.SetLook(look with { Visible = shown.IsChecked == true });
            Grid.SetColumn(shown, 3);
            grid.Children.Add(shown);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(CalendarUi.IconButton(SymbolRegular.ArrowSync24, Loc.T("agenda.sync"), () => _ = source.SyncAsync()));
        actions.Children.Add(CalendarUi.IconButton(SymbolRegular.Edit24, Loc.T("calendars.editSubscription"), () => Dialogs.SubscriptionDialog.Show(Window.GetWindow(this), settings)));
        actions.Children.Add(CalendarUi.IconButton(SymbolRegular.Delete24, Loc.T("calendars.unsubscribe"), () => Calendar.RemoveSubscription(settings.Id), danger: true));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        return grid;
    }

    /// <summary>A new own calendar: its name in a small window.</summary>
    private static void NewOwn()
    {
        var box = new UiTextBox { PlaceholderText = Loc.T("calendars.newName"), MinWidth = 380, ClearButtonEnabled = false };
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("calendars.newCalendar"),
            Content = box,
            PrimaryButtonText = Loc.T("calendars.add"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        _ = dialog.ShowCenteredAsync().ContinueWith(t =>
        {
            if (t.Result == Wpf.Ui.Controls.MessageBoxResult.Primary && box.Text.Trim().Length > 0) Calendar.Local.AddCalendar(box.Text.Trim());
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static async Task DeleteOwnAsync(CalendarInfo c)
    {
        var count = Calendar.Local.Events.Count(e => e.CalendarId == c.Id);
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("calendars.deleteTitle"),
            Content = Loc.T("calendars.deleteText", c.Name, count),
            PrimaryButtonText = Loc.T("common.delete"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowCenteredAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        Calendar.Local.RemoveCalendar(c.Id);
    }

    // ── reminders ────────────────────────────────────────────────────────

    private void BuildReminders()
    {
        var card = CalendarUi.Card(Root, Loc.T("calendars.reminders"), Loc.T("calendars.remindersIntro"));
        var styles = Enum.GetValues<ReminderStyle>().Select(s => new NamedChoice<ReminderStyle>(s, Loc.T("enum.reminderStyle." + s))).ToList();
        var style = new ComboBox { ItemsSource = styles, DisplayMemberPath = "Name", SelectedValuePath = "Value", SelectedValue = Calendar.Settings.ReminderStyle };
        style.SelectionChanged += (_, _) =>
        {
            if (_building || style.SelectedValue is not ReminderStyle value) return;
            Calendar.Settings.ReminderStyle = value;
            Calendar.SaveSettings();
        };
        card.Children.Add(CalendarUi.Row(Loc.T("calendars.reminderStyle"), Loc.T("calendars.reminderStyleHint"), style));
        var accounts = new ToggleSwitch { IsChecked = Calendar.Settings.AccountReminders };
        accounts.Click += (_, _) =>
        {
            Calendar.Settings.AccountReminders = accounts.IsChecked == true;
            Calendar.SaveSettings();
        };
        card.Children.Add(CalendarUi.Row(Loc.T("calendars.accountReminders"), Loc.T("calendars.accountRemindersHint"), accounts, 0));
        var test = CalendarUi.Button(Loc.T("calendars.testReminder"), SymbolRegular.Alert24, () =>
        {
            var now = DateTime.Now;
            var e = new CalendarEvent { Title = Loc.T("calendars.testTitle"), Start = now.AddMinutes(10), End = now.AddMinutes(40), Location = Loc.T("calendars.testPlace") };
            var o = Recurrence.Expand(e, DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1)).First();
            App.Current.ShowCalendarReminderPreview(new CalendarReminder(o, 10, null));
        });
        test.Margin = new Thickness(0, 6, 0, 0);
        card.Children.Add(test);
    }
}

using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Calendar;
using KDesktop.Core.Localization;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using UiTextBox = Wpf.Ui.Controls.TextBox;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options › Calendarios (calendar, phases 2, 5, 6): the default calendar, every calendar with its name, colour, shown
/// or not and its default reminder (the own ones can be added and removed), the .ics subscriptions, which calendars of
/// each account are shown, and how the reminders show.
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
            BuildSubscriptions();
            BuildAccounts();
            BuildReminders();
        }
        finally
        {
            _building = false;
        }
    }

    // ── calendars ────────────────────────────────────────────────────────

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

        foreach (var source in Calendar.Sources)
        {
            var calendars = Calendar.Calendars.Where(c => c.SourceId == source.Id).ToList();
            if (source.Kind == CalendarSourceKind.Subscription) continue; // in their own card
            card.Children.Add(CalendarUi.Label(source.Name));
            if (source.Problem is { } problem) card.Children.Add(CalendarUi.Status(problem, "Kd.Warning"));
            foreach (var c in calendars) card.Children.Add(CalendarRow(c, source));
            if (source.Kind == CalendarSourceKind.Local) card.Children.Add(AddOwnRow());
            card.Children.Add(new Border { Height = 10 });
        }
    }

    private static readonly int[] DefaultReminders = [-1, 0, 5, 10, 15, 30, 60, 1440];

    private static readonly int[] RefreshChoices = [5, 15, 30, 60, 180, 720];

    private FrameworkElement CalendarRow(CalendarInfo c, ICalendarSource source)
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
        var reminderBox = new ComboBox { ItemsSource = reminders, DisplayMemberPath = "Name", SelectedValuePath = "Value", SelectedValue = c.DefaultReminder ?? -1, Width = 170, Margin = new Thickness(8, 0, 0, 0), ToolTip = Loc.T("calendars.defaultReminder") };
        reminderBox.IsEnabled = !c.ReadOnly;
        reminderBox.SelectionChanged += (_, _) =>
        {
            if (_building || reminderBox.SelectedValue is not int value) return;
            Calendar.SetLook(c with { DefaultReminder = value < 0 ? null : value });
        };
        Grid.SetColumn(reminderBox, 2);
        grid.Children.Add(reminderBox);

        var shown = new ToggleSwitch { IsChecked = c.Visible, Margin = new Thickness(12, 0, 0, 0), ToolTip = Loc.T("calendars.visible"), VerticalAlignment = VerticalAlignment.Center };
        shown.Click += (_, _) => Calendar.SetLook(c with { Visible = shown.IsChecked == true });
        Grid.SetColumn(shown, 3);
        grid.Children.Add(shown);

        if (source.Kind == CalendarSourceKind.Local && Calendar.Local.Calendars.Count > 1)
        {
            var delete = CalendarUi.IconButton(SymbolRegular.Delete24, Loc.T("calendars.delete"), () => _ = DeleteOwnAsync(c), danger: true);
            Grid.SetColumn(delete, 4);
            grid.Children.Add(delete);
        }
        return grid;
    }

    private static FrameworkElement AddOwnRow()
    {
        var grid = new Grid { Margin = new Thickness(26, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new UiTextBox { PlaceholderText = Loc.T("calendars.newName"), ClearButtonEnabled = false };
        var add = CalendarUi.Button(Loc.T("calendars.add"), SymbolRegular.Add24, () =>
        {
            if (box.Text.Trim().Length == 0) return;
            Calendar.Local.AddCalendar(box.Text.Trim());
            box.Text = string.Empty;
        });
        add.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(add, 1);
        grid.Children.Add(box);
        grid.Children.Add(add);
        return grid;
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
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        Calendar.Local.RemoveCalendar(c.Id);
    }

    // ── subscriptions ────────────────────────────────────────────────────

    private void BuildSubscriptions()
    {
        var card = CalendarUi.Card(Root, Loc.T("calendars.subscriptions"), Loc.T("calendars.subscriptionsIntro"));
        foreach (var source in Calendar.Sources.OfType<IcsSubscription>())
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var settings = source.Settings;
            grid.Children.Add(CalendarUi.ColorDot(settings.Color, color =>
            {
                settings.Color = color;
                Calendar.UpdateSubscription(settings);
            }));
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = new UiTextBox { Text = settings.Name, ClearButtonEnabled = false };
            name.LostFocus += (_, _) =>
            {
                if (name.Text.Trim().Length == 0 || name.Text.Trim() == settings.Name) return;
                settings.Name = name.Text.Trim();
                Calendar.UpdateSubscription(settings);
            };
            texts.Children.Add(name);
            var status = source.Problem ?? (source.LastSync is { } last
                ? Loc.T("calendars.lastSync", last.ToLocalTime().ToString("g", Loc.Instance.Culture), source.Events.Count)
                : Loc.T("calendars.neverSynced"));
            texts.Children.Add(CalendarUi.Status(status, source.Problem is null ? "TextFillColorSecondaryBrush" : "Kd.Warning"));
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);

            var every = new ComboBox
            {
                ItemsSource = RefreshChoices.Select(m => new NamedChoice<int>(m, Loc.T("calendars.every", CalendarText.Minutes(m)))).ToList(),
                DisplayMemberPath = "Name", SelectedValuePath = "Value", SelectedValue = settings.RefreshMinutes, Width = 150, Margin = new Thickness(8, 0, 0, 0),
                ToolTip = Loc.T("calendars.refresh"), VerticalAlignment = VerticalAlignment.Top,
            };
            every.SelectionChanged += (_, _) =>
            {
                if (_building || every.SelectedValue is not int minutes) return;
                settings.RefreshMinutes = minutes;
                Calendar.UpdateSubscription(settings);
            };
            Grid.SetColumn(every, 2);
            grid.Children.Add(every);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            actions.Children.Add(CalendarUi.IconButton(SymbolRegular.ArrowSync24, Loc.T("agenda.sync"), () => _ = source.SyncAsync()));
            actions.Children.Add(CalendarUi.IconButton(SymbolRegular.Link24, Loc.T("calendars.changeUrl"), () => ChangeUrl(settings)));
            actions.Children.Add(CalendarUi.IconButton(SymbolRegular.Delete24, Loc.T("calendars.unsubscribe"), () => Calendar.RemoveSubscription(settings.Id), danger: true));
            Grid.SetColumn(actions, 3);
            grid.Children.Add(actions);
            card.Children.Add(grid);
        }

        // A new one
        card.Children.Add(CalendarUi.Label(Loc.T("calendars.newSubscription")));
        var nameBox = new UiTextBox { PlaceholderText = Loc.T("calendars.subscriptionName"), Margin = new Thickness(0, 0, 0, 6), ClearButtonEnabled = false };
        var urlBox = new UiTextBox { PlaceholderText = "webcal://… · https://….ics", ClearButtonEnabled = false };
        var error = CalendarUi.Status(string.Empty, "Kd.Danger");
        var add = CalendarUi.Button(Loc.T("calendars.subscribe"), SymbolRegular.CalendarAdd24, () =>
        {
            if (!IcsSubscription.IsValidUrl(urlBox.Text))
            {
                error.Text = Loc.T("calendars.badUrl");
                return;
            }
            var name = nameBox.Text.Trim().Length > 0 ? nameBox.Text.Trim() : Loc.T("calendars.subscriptionDefaultName");
            Calendar.AddSubscription(name, urlBox.Text);
        }, primary: true);
        add.Margin = new Thickness(0, 8, 0, 0);
        card.Children.Add(nameBox);
        card.Children.Add(urlBox);
        card.Children.Add(error);
        card.Children.Add(add);
        card.Children.Add(CalendarUi.GuideButton(this, "calendars.subscriptionGuide", Dialogs.GuideDialog.Ics, new Thickness(0, 10, 0, 0)));
    }

    private static void ChangeUrl(IcsSubscriptionSettings settings)
    {
        var box = new UiTextBox { PlaceholderText = "https://….ics", MinWidth = 420 };
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("calendars.changeUrl"),
            Content = box,
            PrimaryButtonText = Loc.T("common.accept"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        _ = dialog.ShowDialogAsync().ContinueWith(t =>
        {
            if (t.Result == Wpf.Ui.Controls.MessageBoxResult.Primary && IcsSubscription.IsValidUrl(box.Text)) Calendar.UpdateSubscription(settings, box.Text);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ── accounts ─────────────────────────────────────────────────────────

    private void BuildAccounts()
    {
        var card = CalendarUi.Card(Root, Loc.T("calendars.accounts"), Loc.T("calendars.accountsIntro"));
        var accounts = App.Current.Accounts.Accounts.Where(a => a.Uses.HasFlag(Core.Accounts.AccountUse.CalendarRead) || a.Uses.HasFlag(Core.Accounts.AccountUse.CalendarWrite)).ToList();
        if (accounts.Count == 0) card.Children.Add(CalendarUi.Caption(Loc.T("calendars.noAccounts")));
        foreach (var account in accounts)
        {
            var used = Calendar.Settings.Accounts.FirstOrDefault(a => a.AccountId == account.Id);
            var toggle = new ToggleSwitch { IsChecked = used is not null };
            toggle.Click += (_, _) =>
            {
                if (toggle.IsChecked == true) Calendar.UseAccount(account.Id, []);
                else Calendar.ForgetAccount(account.Id);
            };
            card.Children.Add(CalendarUi.Row(account.Display, account.Disconnected ? Loc.T("accounts.disconnected") : Loc.T("calendars.useAccount"), toggle, 0));
            if (used is null || Calendar.Source(CalendarService.AccountSourceId(account.Id)) is not RemoteCalendar source) continue;
            var all = source.AllCalendars;
            if (all.Count == 0)
            {
                card.Children.Add(CalendarUi.Caption(Loc.T("calendars.loadingAccount"), new Thickness(20, 0, 0, 8)));
                continue;
            }
            var chosen = source.Calendars.Select(c => c.Id).ToHashSet();
            var list = new WrapPanel { Margin = new Thickness(20, 0, 0, 8) };
            foreach (var c in all)
            {
                var check = new CheckBox { Content = c.Name + (c.ReadOnly ? " " + Loc.T("calendars.readOnlyMark") : string.Empty), IsChecked = chosen.Contains(c.Id), Margin = new Thickness(0, 0, 16, 4) };
                check.Click += (_, _) =>
                {
                    var now = source.Calendars.Select(x => x.Id).ToHashSet();
                    if (check.IsChecked == true) now.Add(c.Id);
                    else now.Remove(c.Id);
                    if (now.Count == 0) return;
                    Calendar.UseAccount(account.Id, now);
                };
                list.Children.Add(check);
            }
            card.Children.Add(list);
        }
        var go = CalendarUi.Button(Loc.T("calendars.manageAccounts"), SymbolRegular.PersonAccounts24, () => (Window.GetWindow(this) as MainWindow)?.Navigate("settings/accounts"));
        go.Margin = new Thickness(0, 6, 0, 0);
        card.Children.Add(go);
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

using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Localization;
using KDesktop.Core.Mail;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options › Correo (mail, phases 1–4): the accounts (with an app password, servers found by themselves; or with a
/// connected Google account), «Probar» that says what fails in words, which mails are important, what is
/// told, and what the assistants may do.
/// </summary>
public partial class MailSection : UserControl
{
    private static MailService Mail => App.Current.Mail;

    private static readonly int[] KeepChoices = [7, 14, 30, 60, 90];

    private bool _subscribed;

    public MailSection()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!_subscribed) Mail.Changed += OnChanged;
            _subscribed = true;
            Build();
        };
        Unloaded += (_, _) =>
        {
            Mail.Changed -= OnChanged;
            _subscribed = false;
        };
    }

    private void OnChanged() => Dispatcher.BeginInvoke(() =>
    {
        if (!(IsKeyboardFocusWithin && System.Windows.Input.Keyboard.FocusedElement is TextBox)) Build();
    });

    private void Build()
    {
        if (!IsLoaded) return;
        Root.Children.Clear();
        BuildAccounts();
        BuildImportant();
        BuildNotices();
        BuildAssistant();
    }

    private void BuildAccounts()
    {
        var card = CalendarUi.Card(Root, Loc.T("mailOptions.title"), Loc.T("mailOptions.intro"));
        foreach (var account in Mail.Accounts)
        {
            var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var texts = new StackPanel();
            texts.Children.Add(new TextBlock { Text = account.Display, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var detail = account.Use == MailUse.SendOnly ? Loc.T("mailOptions.sendOnly") : Loc.T("mailOptions.notify." + account.Notify);
            texts.Children.Add(CalendarUi.Status(detail, "TextFillColorSecondaryBrush"));
            if (Mail.Problem(account.Id) is { } problem) texts.Children.Add(CalendarUi.Status(problem, "Kd.Warning"));
            grid.Children.Add(texts);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var enabled = new ToggleSwitch { IsChecked = account.Enabled, ToolTip = Loc.T("mailOptions.enabled"), Margin = new Thickness(0, 0, 6, 0) };
            enabled.Click += (_, _) =>
            {
                account.Enabled = enabled.IsChecked == true;
                Mail.Save(account);
            };
            actions.Children.Add(enabled);
            actions.Children.Add(CalendarUi.IconButton(SymbolRegular.Edit24, Loc.T("mailOptions.edit"), () => Edit(account)));
            actions.Children.Add(CalendarUi.IconButton(SymbolRegular.Delete24, Loc.T("mailOptions.remove"), () => _ = RemoveAsync(account), danger: true));
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);
            card.Children.Add(grid);
        }
        if (Mail.Accounts.Count == 0) card.Children.Add(CalendarUi.Caption(Loc.T("mailOptions.none")));
        var add = CalendarUi.Button(Loc.T("mailOptions.add"), SymbolRegular.MailAdd24, () => Edit(new MailAccountSettings()), primary: true);
        add.Margin = new Thickness(0, 8, 0, 0);
        card.Children.Add(add);
        card.Children.Add(CalendarUi.GuideLink(this, "mailOptions.guide", Dialogs.GuideDialog.AppPassword, new Thickness(0, 10, 0, 0)));
    }

    /// <summary>The account in its window (5.0.430); saved, the page shows it.</summary>
    private void Edit(MailAccountSettings account)
    {
        if (Dialogs.MailAccountDialog.Edit(Window.GetWindow(this), account)) Build();
    }

    private static async Task RemoveAsync(MailAccountSettings account)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("mailOptions.remove"),
            Content = Loc.T("mailOptions.removeText", account.Display),
            PrimaryButtonText = Loc.T("common.remove"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowCenteredAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary) Mail.Remove(account.Id);
    }

    // ── important and the assistant ──────────────────────────────────────

    private void BuildImportant()
    {
        var card = CalendarUi.Card(Root, Loc.T("mailOptions.important"), Loc.T("mailOptions.importantIntro"));
        var rules = Mail.Settings.Important;
        card.Children.Add(CalendarUi.Label(Loc.T("mailOptions.senders")));
        var senders = new UiTextBox { Text = string.Join(", ", rules.Senders), PlaceholderText = Loc.T("mailOptions.sendersHint"), Margin = new Thickness(0, 0, 0, 8), ClearButtonEnabled = false };
        card.Children.Add(senders);
        card.Children.Add(CalendarUi.Label(Loc.T("mailOptions.keywords")));
        var keywords = new UiTextBox { Text = string.Join(", ", rules.Keywords), PlaceholderText = Loc.T("mailOptions.keywordsHint"), Margin = new Thickness(0, 0, 0, 8), ClearButtonEnabled = false };
        card.Children.Add(keywords);
        void Save()
        {
            rules.Senders = [.. senders.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            rules.Keywords = [.. keywords.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            Mail.SaveSettings();
        }
        senders.LostFocus += (_, _) => Save();
        keywords.LostFocus += (_, _) => Save();
    }

    /// <summary>How a new mail is told, as the reminders of the calendar (Opciones › Calendario › Avisos); which ones, each account says.</summary>
    private void BuildNotices()
    {
        var card = CalendarUi.Card(Root, Loc.T("mailOptions.notices"), Loc.T("mailOptions.noticesIntro"));
        var styles = Enum.GetValues<Core.Calendar.ReminderStyle>().Select(s => new NamedChoice<Core.Calendar.ReminderStyle>(s, Loc.T("enum.mailNoticeStyle." + s))).ToList();
        var style = new ComboBox { ItemsSource = styles, DisplayMemberPath = "Name", SelectedValuePath = "Value", SelectedValue = Mail.Settings.NoticeStyle };
        style.SelectionChanged += (_, _) =>
        {
            if (style.SelectedValue is not Core.Calendar.ReminderStyle value) return;
            Mail.Settings.NoticeStyle = value;
            Mail.SaveSettings();
        };
        card.Children.Add(CalendarUi.Row(Loc.T("calendars.reminderStyle"), Loc.T("mailOptions.noticeStyleHint"), style));
        var test = CalendarUi.Button(Loc.T("calendars.testReminder"), SymbolRegular.Alert24, () => App.Current.ShowMailNoticePreview());
        test.Margin = new Thickness(0, 6, 0, 0);
        card.Children.Add(test);
    }

    private void BuildAssistant()
    {
        var card = CalendarUi.Card(Root, Loc.T("mailOptions.assistant"), Loc.T("mailOptions.assistantIntro"));
        var send = new ToggleSwitch { IsChecked = Mail.Settings.AssistantMaySend };
        send.Click += (_, _) =>
        {
            Mail.Settings.AssistantMaySend = send.IsChecked == true;
            Mail.SaveSettings();
        };
        card.Children.Add(CalendarUi.Row(Loc.T("mailOptions.maySend"), Loc.T("mailOptions.maySendHint"), send, 0));
        var days = new ComboBox
        {
            DisplayMemberPath = "Name", SelectedValuePath = "Value",
            ItemsSource = KeepChoices.Select(d => new NamedChoice<int>(d, Loc.T("calendar.days", d))).ToList(),
            SelectedValue = Mail.Settings.KeepDays,
        };
        days.SelectionChanged += (_, _) =>
        {
            if (days.SelectedValue is not int value) return;
            Mail.Settings.KeepDays = value;
            Mail.SaveSettings();
        };
        card.Children.Add(CalendarUi.Row(Loc.T("mailOptions.keepDays"), Loc.T("mailOptions.keepDaysHint"), days, 160));
    }
}

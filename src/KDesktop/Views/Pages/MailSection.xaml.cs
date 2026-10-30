using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Accounts;
using KDesktop.Core.Localization;
using KDesktop.Core.Mail;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;
using UiPasswordBox = Wpf.Ui.Controls.PasswordBox;
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
    private MailAccountSettings? _editing;
    private string? _status;

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
        if (_editing is null && !(IsKeyboardFocusWithin && System.Windows.Input.Keyboard.FocusedElement is TextBox)) Build();
    });

    private void Build()
    {
        if (!IsLoaded) return;
        Root.Children.Clear();
        if (_editing is not null)
        {
            BuildEditor(_editing);
            return;
        }
        BuildAccounts();
        BuildImportant();
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

    private void Edit(MailAccountSettings account)
    {
        _editing = account;
        _status = null;
        Build();
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
        if (await box.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary) Mail.Remove(account.Id);
    }

    // ── one account ──────────────────────────────────────────────────────

    private void BuildEditor(MailAccountSettings a)
    {
        var isNew = Mail.Account(a.Id) is null;
        var card = CalendarUi.Card(Root, Loc.T(isNew ? "mailOptions.newTitle" : "mailOptions.editTitle"), Loc.T("mailOptions.editIntro"));
        UiTextBox Box(string label, string value, string? placeholder = null)
        {
            card.Children.Add(CalendarUi.Label(label));
            var box = new UiTextBox { Text = value, PlaceholderText = placeholder ?? string.Empty, Margin = new Thickness(0, 0, 0, 8), ClearButtonEnabled = false };
            card.Children.Add(box);
            return box;
        }
        var address = Box(Loc.T("mailOptions.address"), a.Address, "ana@gmail.com");
        var name = Box(Loc.T("mailOptions.name"), a.Name, Loc.T("mailOptions.nameHint"));
        var display = Box(Loc.T("mailOptions.displayName"), a.DisplayName, Loc.T("mailOptions.displayNameHint"));

        // How it signs in: an app password, or a connected account.
        var oauthAccounts = App.Current.Accounts.Accounts.Where(x => x.Uses.HasFlag(AccountUse.Mail)).ToList();
        var auth = new ComboBox { Margin = new Thickness(0, 0, 0, 8), DisplayMemberPath = "Name", SelectedValuePath = "Value" };
        var authChoices = new List<NamedChoice<string>> { new(string.Empty, Loc.T("mailOptions.authPassword")) };
        authChoices.AddRange(oauthAccounts.Select(x => new NamedChoice<string>(x.Id, Loc.T("mailOptions.authOAuth", x.Display))));
        auth.ItemsSource = authChoices;
        auth.SelectedValue = a.Auth == MailAuth.OAuth ? a.OAuthAccountId ?? string.Empty : string.Empty;
        card.Children.Add(CalendarUi.Label(Loc.T("mailOptions.auth")));
        card.Children.Add(auth);
        card.Children.Add(CalendarUi.Label(Loc.T("mailOptions.password")));
        var password = new UiPasswordBox { PlaceholderText = a.PasswordProtected.Length > 0 ? Loc.T("accounts.secretKept") : Loc.T("mailOptions.passwordHint"), Margin = new Thickness(0, 0, 0, 4) };
        card.Children.Add(password);
        var passwordHelp = CalendarUi.Caption(string.Empty, new Thickness(0, 0, 0, 8));
        card.Children.Add(passwordHelp);
        if (oauthAccounts.Count == 0) card.Children.Add(CalendarUi.Caption(Loc.T("mailOptions.oauthHow"), new Thickness(0, 0, 0, 8)));

        // Servers
        var servers = new WrapPanel();
        UiTextBox Small(string label, string value, double width)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 8) };
            panel.Children.Add(CalendarUi.Label(label));
            var box = new UiTextBox { Text = value, Width = width, ClearButtonEnabled = false };
            panel.Children.Add(box);
            servers.Children.Add(panel);
            return box;
        }
        var imapHost = Small(Loc.T("mailOptions.imap"), a.ImapHost, 230);
        var imapPort = Small(Loc.T("mailOptions.port"), a.ImapPort.ToString(System.Globalization.CultureInfo.InvariantCulture), 80);
        var smtpHost = Small(Loc.T("mailOptions.smtp"), a.SmtpHost, 230);
        var smtpPort = Small(Loc.T("mailOptions.port"), a.SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture), 80);
        var user = Small(Loc.T("mailOptions.user"), a.User, 230);
        card.Children.Add(servers);
        Wpf.Ui.Controls.Button detect = null!;
        detect = CalendarUi.Button(Loc.T("mailOptions.detect"), SymbolRegular.Search24, () => _ = DetectAsync());
        detect.Margin = new Thickness(0, 0, 0, 8);
        card.Children.Add(detect);

        async Task DetectAsync()
        {
            detect.IsEnabled = false;
            var found = await MailAutoConfig.FindAsync(address.Text.Trim(), Core.Calendar.CalendarService.CreateHttp());
            detect.IsEnabled = true;
            if (found is null)
            {
                passwordHelp.Text = Loc.T("mailOptions.notDetected");
                return;
            }
            imapHost.Text = found.ImapHost;
            imapPort.Text = found.ImapPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
            smtpHost.Text = found.SmtpHost;
            smtpPort.Text = found.SmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
            passwordHelp.Text = found.NeedsOAuth ? Loc.T("mailOptions.needsOAuth")
                : found.Provider is { } provider ? Loc.T("mailOptions.appPassword." + provider) is var help && !help.StartsWith("mailOptions.", StringComparison.Ordinal) ? help : Loc.T("mailOptions.appPassword.other")
                : Loc.T("mailOptions.appPassword.other");
        }
        address.LostFocus += (_, _) =>
        {
            if (imapHost.Text.Trim().Length == 0 && address.Text.Contains('@', StringComparison.Ordinal)) _ = DetectAsync();
        };

        // What for
        var use = new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "Value", Margin = new Thickness(0, 0, 0, 8),
            ItemsSource = Enum.GetValues<MailUse>().Select(u => new NamedChoice<MailUse>(u, Loc.T("mailOptions.use." + u))).ToList(), SelectedValue = a.Use };
        card.Children.Add(CalendarUi.Label(Loc.T("mailOptions.use")));
        card.Children.Add(use);
        var notify = new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "Value", Margin = new Thickness(0, 0, 0, 8),
            ItemsSource = Enum.GetValues<MailNotify>().Select(n => new NamedChoice<MailNotify>(n, Loc.T("mailOptions.notify." + n))).ToList(), SelectedValue = a.Notify };
        card.Children.Add(CalendarUi.Label(Loc.T("mailOptions.notify")));
        card.Children.Add(notify);
        var allowed = Box(Loc.T("mailOptions.allowed"), string.Join(", ", a.AllowedRecipients), Loc.T("mailOptions.allowedHint"));
        var markRead = new CheckBox { Content = Loc.T("mailOptions.markRead"), IsChecked = a.MarkReadByAssistant, Margin = new Thickness(0, 0, 0, 12) };
        card.Children.Add(markRead);

        var status = CalendarUi.Status(_status ?? string.Empty, _status is null ? "TextFillColorSecondaryBrush" : "Kd.Warning");
        card.Children.Add(status);

        MailAccountSettings Read()
        {
            var oauth = auth.SelectedValue as string;
            return new MailAccountSettings
            {
                Id = a.Id,
                Address = address.Text.Trim(),
                Name = name.Text.Trim(),
                DisplayName = display.Text.Trim(),
                ImapHost = imapHost.Text.Trim(),
                ImapPort = int.TryParse(imapPort.Text, out var ip) ? ip : 993,
                SmtpHost = smtpHost.Text.Trim(),
                SmtpPort = int.TryParse(smtpPort.Text, out var sp) ? sp : 587,
                User = user.Text.Trim(),
                Auth = string.IsNullOrEmpty(oauth) ? MailAuth.Password : MailAuth.OAuth,
                OAuthAccountId = string.IsNullOrEmpty(oauth) ? null : oauth,
                PasswordProtected = a.PasswordProtected,
                Use = use.SelectedValue is MailUse u ? u : MailUse.All,
                Notify = notify.SelectedValue is MailNotify n ? n : MailNotify.Important,
                AllowedRecipients = [.. allowed.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                MarkReadByAssistant = markRead.IsChecked == true,
                Folders = a.Folders,
                Enabled = a.Enabled,
            };
        }

        var buttons = new WrapPanel();
        Wpf.Ui.Controls.Button test = null!;
        test = CalendarUi.Button(Loc.T("mailOptions.test"), SymbolRegular.PlugConnected24, () => _ = TestAsync());
        async Task TestAsync()
        {
            test.IsEnabled = false;
            status.Text = Loc.T("mailOptions.testing");
            status.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            var problem = await Mail.TestAsync(Read(), password.Password);
            test.IsEnabled = true;
            status.Text = problem ?? Loc.T("mailOptions.testOk");
            status.SetResourceReference(TextBlock.ForegroundProperty, problem is null ? "Kd.Success" : "Kd.Danger");
        }
        buttons.Children.Add(test);
        buttons.Children.Add(CalendarUi.Button(Loc.T("agenda.save"), SymbolRegular.Save24, () =>
        {
            var account = Read();
            if (!account.Address.Contains('@', StringComparison.Ordinal) || account.ImapHost.Length == 0 || account.SmtpHost.Length == 0)
            {
                status.Text = Loc.T("mailOptions.incomplete");
                status.SetResourceReference(TextBlock.ForegroundProperty, "Kd.Danger");
                return;
            }
            Mail.Save(account, password.Password);
            _editing = null;
            Build();
        }, primary: true));
        buttons.Children.Add(CalendarUi.Button(Loc.T("common.cancel"), SymbolRegular.Dismiss24, () =>
        {
            _editing = null;
            Build();
        }));
        buttons.Margin = new Thickness(0, 8, 0, 0);
        card.Children.Add(buttons);
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

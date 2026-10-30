using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Accounts;
using KDesktop.Core.Localization;
using KDesktop.Core.Mail;
using KDesktop.Views.Pages;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using UiPasswordBox = Wpf.Ui.Controls.PasswordBox;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace KDesktop.Views.Dialogs;

/// <summary>
/// A mail account, new or to change (Opciones › Correo; in a window since 5.0.430): the address (the servers are found
/// by themselves), how it signs in (an app password or a connected Google account), the servers, what it is for and what
/// is told. «Probar» signs in to IMAP and SMTP and says what fails in words. Saving keeps it in <see cref="MailService"/>.
/// </summary>
public partial class MailAccountDialog : FluentWindow
{
    private static MailService Mail => App.Current.Mail;

    private readonly MailAccountSettings _account;
    private readonly UiTextBox _address;
    private readonly UiTextBox _name;
    private readonly UiTextBox _display;
    private readonly ComboBox _auth;
    private readonly UiPasswordBox _password;
    private readonly TextBlock _passwordHelp;
    private readonly UiTextBox _imapHost;
    private readonly UiTextBox _imapPort;
    private readonly UiTextBox _smtpHost;
    private readonly UiTextBox _smtpPort;
    private readonly UiTextBox _user;
    private readonly Wpf.Ui.Controls.Button _detect;
    private readonly ComboBox _use;
    private readonly ComboBox _notify;
    private readonly UiTextBox _allowed;
    private readonly CheckBox _markRead;

    private MailAccountDialog(MailAccountSettings a)
    {
        InitializeComponent();
        _account = a;
        TitleBar.Title = Loc.T(Mail.Account(a.Id) is null ? "mailOptions.newTitle" : "mailOptions.editTitle");
        Form.Children.Add(CalendarUi.Caption(Loc.T("mailOptions.editIntro"), new Thickness(0, 0, 0, 12)));

        UiTextBox Box(string label, string value, string? placeholder = null)
        {
            Form.Children.Add(CalendarUi.Label(label));
            var box = new UiTextBox { Text = value, PlaceholderText = placeholder ?? string.Empty, Margin = new Thickness(0, 0, 0, 12), ClearButtonEnabled = false };
            Form.Children.Add(box);
            return box;
        }
        _address = Box(Loc.T("mailOptions.address"), a.Address, "ana@gmail.com");
        _name = Box(Loc.T("mailOptions.name"), a.Name, Loc.T("mailOptions.nameHint"));
        _display = Box(Loc.T("mailOptions.displayName"), a.DisplayName, Loc.T("mailOptions.displayNameHint"));

        // How it signs in: an app password, or a connected account.
        var oauthAccounts = App.Current.Accounts.Accounts.Where(x => x.Uses.HasFlag(AccountUse.Mail)).ToList();
        var authChoices = new List<NamedChoice<string>> { new(string.Empty, Loc.T("mailOptions.authPassword")) };
        authChoices.AddRange(oauthAccounts.Select(x => new NamedChoice<string>(x.Id, Loc.T("mailOptions.authOAuth", x.Display))));
        _auth = new ComboBox { Margin = new Thickness(0, 0, 0, 12), DisplayMemberPath = "Name", SelectedValuePath = "Value", ItemsSource = authChoices, HorizontalAlignment = HorizontalAlignment.Stretch };
        _auth.SelectedValue = a.Auth == MailAuth.OAuth ? a.OAuthAccountId ?? string.Empty : string.Empty;
        Form.Children.Add(CalendarUi.Label(Loc.T("mailOptions.auth")));
        Form.Children.Add(_auth);
        Form.Children.Add(CalendarUi.Label(Loc.T("mailOptions.password")));
        _password = new UiPasswordBox { PlaceholderText = a.PasswordProtected.Length > 0 ? Loc.T("accounts.secretKept") : Loc.T("mailOptions.passwordHint"), Margin = new Thickness(0, 0, 0, 4) };
        Form.Children.Add(_password);
        _passwordHelp = CalendarUi.Caption(string.Empty, new Thickness(0, 0, 0, 4));
        Form.Children.Add(_passwordHelp);
        Form.Children.Add(CalendarUi.GuideLink(this, "mailOptions.guide", GuideDialog.AppPassword, new Thickness(0, 0, 0, 12)));
        if (oauthAccounts.Count == 0) Form.Children.Add(CalendarUi.Caption(Loc.T("mailOptions.oauthHow"), new Thickness(0, 0, 0, 12)));

        // Servers: found from the address; written by hand when they are not.
        var servers = new Grid();
        servers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        servers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        servers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        UiTextBox Cell(int row, int column, string label, string value)
        {
            while (servers.RowDefinitions.Count <= row) servers.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(CalendarUi.Label(label));
            var box = new UiTextBox { Text = value, ClearButtonEnabled = false };
            panel.Children.Add(box);
            Grid.SetRow(panel, row);
            Grid.SetColumn(panel, column);
            servers.Children.Add(panel);
            return box;
        }
        _imapHost = Cell(0, 0, Loc.T("mailOptions.imap"), a.ImapHost);
        _imapPort = Cell(0, 2, Loc.T("mailOptions.port"), a.ImapPort.ToString(CultureInfo.InvariantCulture));
        _smtpHost = Cell(1, 0, Loc.T("mailOptions.smtp"), a.SmtpHost);
        _smtpPort = Cell(1, 2, Loc.T("mailOptions.port"), a.SmtpPort.ToString(CultureInfo.InvariantCulture));
        Form.Children.Add(servers);
        _user = Box(Loc.T("mailOptions.user"), a.User);
        _detect = CalendarUi.Button(Loc.T("mailOptions.detect"), SymbolRegular.Search24, () => _ = DetectAsync());
        _detect.Margin = new Thickness(0, 0, 0, 12);
        _detect.HorizontalAlignment = HorizontalAlignment.Left;
        Form.Children.Add(_detect);
        _address.LostFocus += (_, _) =>
        {
            if (_imapHost.Text.Trim().Length == 0 && _address.Text.Contains('@', StringComparison.Ordinal)) _ = DetectAsync();
        };

        // What for and what is told.
        _use = new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "Value", Margin = new Thickness(0, 0, 0, 12), HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = Enum.GetValues<MailUse>().Select(u => new NamedChoice<MailUse>(u, Loc.T("mailOptions.use." + u))).ToList(), SelectedValue = a.Use };
        Form.Children.Add(CalendarUi.Label(Loc.T("mailOptions.use")));
        Form.Children.Add(_use);
        _notify = new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "Value", Margin = new Thickness(0, 0, 0, 12), HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = Enum.GetValues<MailNotify>().Select(n => new NamedChoice<MailNotify>(n, Loc.T("mailOptions.notify." + n))).ToList(), SelectedValue = a.Notify };
        Form.Children.Add(CalendarUi.Label(Loc.T("mailOptions.notify")));
        Form.Children.Add(_notify);
        _allowed = Box(Loc.T("mailOptions.allowed"), string.Join(", ", a.AllowedRecipients), Loc.T("mailOptions.allowedHint"));
        _markRead = new CheckBox { Content = Loc.T("mailOptions.markRead"), IsChecked = a.MarkReadByAssistant };
        Form.Children.Add(_markRead);
    }

    /// <summary>Opens the window for <paramref name="account"/> (a new one: <c>new MailAccountSettings()</c>); true when saved.</summary>
    public static bool Edit(Window? owner, MailAccountSettings account) => new MailAccountDialog(account) { Owner = owner }.ShowDialog() == true;

    private async Task DetectAsync()
    {
        _detect.IsEnabled = false;
        var found = await MailAutoConfig.FindAsync(_address.Text.Trim(), Core.Calendar.CalendarService.CreateHttp());
        _detect.IsEnabled = true;
        if (found is null)
        {
            _passwordHelp.Text = Loc.T("mailOptions.notDetected");
            return;
        }
        _imapHost.Text = found.ImapHost;
        _imapPort.Text = found.ImapPort.ToString(CultureInfo.InvariantCulture);
        _smtpHost.Text = found.SmtpHost;
        _smtpPort.Text = found.SmtpPort.ToString(CultureInfo.InvariantCulture);
        _passwordHelp.Text = found.NeedsOAuth ? Loc.T("mailOptions.needsOAuth")
            : found.Provider is { } provider ? Loc.T("mailOptions.appPassword." + provider) is var help && !help.StartsWith("mailOptions.", StringComparison.Ordinal) ? help : Loc.T("mailOptions.appPassword.other")
            : Loc.T("mailOptions.appPassword.other");
    }

    private MailAccountSettings Read()
    {
        var oauth = _auth.SelectedValue as string;
        return new MailAccountSettings
        {
            Id = _account.Id,
            Address = _address.Text.Trim(),
            Name = _name.Text.Trim(),
            DisplayName = _display.Text.Trim(),
            ImapHost = _imapHost.Text.Trim(),
            ImapPort = int.TryParse(_imapPort.Text, out var ip) ? ip : 993,
            SmtpHost = _smtpHost.Text.Trim(),
            SmtpPort = int.TryParse(_smtpPort.Text, out var sp) ? sp : 587,
            User = _user.Text.Trim(),
            Auth = string.IsNullOrEmpty(oauth) ? MailAuth.Password : MailAuth.OAuth,
            OAuthAccountId = string.IsNullOrEmpty(oauth) ? null : oauth,
            PasswordProtected = _account.PasswordProtected,
            Use = _use.SelectedValue is MailUse u ? u : MailUse.All,
            Notify = _notify.SelectedValue is MailNotify n ? n : MailNotify.Important,
            AllowedRecipients = [.. _allowed.Text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            MarkReadByAssistant = _markRead.IsChecked == true,
            Folders = _account.Folders,
            Enabled = _account.Enabled,
        };
    }

    private void Status(string text, string brush)
    {
        StatusText.Text = text;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, brush);
        StatusText.Visibility = Visibility.Visible;
    }

    private async void OnTest(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        Status(Loc.T("mailOptions.testing"), "TextFillColorSecondaryBrush");
        var problem = await Mail.TestAsync(Read(), _password.Password);
        TestButton.IsEnabled = true;
        Status(problem ?? Loc.T("mailOptions.testOk"), problem is null ? "Kd.Success" : "Kd.Danger");
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var account = Read();
        if (!account.Address.Contains('@', StringComparison.Ordinal) || account.ImapHost.Length == 0 || account.SmtpHost.Length == 0)
        {
            Status(Loc.T("mailOptions.incomplete"), "Kd.Danger");
            return;
        }
        Mail.Save(account, _password.Password);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

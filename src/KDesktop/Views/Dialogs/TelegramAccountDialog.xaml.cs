using System.Globalization;
using System.Windows;
using KDesktop.Core.Localization;
using KDesktop.Core.Remote.Telegram;
using KDesktop.Views.Pages;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using UiPasswordBox = Wpf.Ui.Controls.PasswordBox;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace KDesktop.Views.Dialogs;

/// <summary>
/// Logging in the Telegram account with a number (Opciones › Cuentas › Telegram; telegram, phase 6.2): the application of
/// my.telegram.org (api_id and api_hash, with the guide) and the number; then the code Telegram sends to the app (or by
/// SMS); then the password of the two-step verification if it has one. Says what fails in words; cancelling leaves nothing
/// half done.
/// </summary>
public partial class TelegramAccountDialog : FluentWindow
{
    private static TelegramService Telegram => App.Current.TelegramAccount;

    private readonly UiTextBox _apiId;
    private readonly UiPasswordBox _apiHash;
    private readonly UiTextBox _phone;
    private readonly UiTextBox _code = new() { Margin = new Thickness(0, 0, 0, 4), ClearButtonEnabled = false };
    private readonly UiPasswordBox _password = new() { Margin = new Thickness(0, 0, 0, 4) };
    private string _step = "start";
    private bool _done;

    private TelegramAccountDialog()
    {
        InitializeComponent();
        var account = Telegram.Account;
        TitleBar.Title = Loc.T("telegram.account.loginTitle");
        Form.Children.Add(CalendarUi.Caption(Loc.T("telegram.account.loginIntro"), new Thickness(0, 0, 0, 12)));
        Form.Children.Add(CalendarUi.Label(Loc.T("telegram.account.apiId")));
        _apiId = new UiTextBox { Text = account is { ApiId: > 0 } ? account.ApiId.ToString(CultureInfo.InvariantCulture) : string.Empty, PlaceholderText = "1234567", Margin = new Thickness(0, 0, 0, 8), ClearButtonEnabled = false };
        Form.Children.Add(_apiId);
        Form.Children.Add(CalendarUi.Label(Loc.T("telegram.account.apiHash")));
        _apiHash = new UiPasswordBox { PlaceholderText = account is null ? "0123456789abcdef…" : Loc.T("accounts.secretKept"), Margin = new Thickness(0, 0, 0, 4) };
        Form.Children.Add(_apiHash);
        Form.Children.Add(CalendarUi.GuideLink(this, "telegram.account.guide", GuideDialog.TelegramAccount, new Thickness(0, 0, 0, 12)));
        Form.Children.Add(CalendarUi.Label(Loc.T("telegram.account.phone")));
        _phone = new UiTextBox { Text = account?.Phone ?? string.Empty, PlaceholderText = "+34 600 000 000", Margin = new Thickness(0, 0, 0, 4), ClearButtonEnabled = false };
        Form.Children.Add(_phone);
        Form.Children.Add(CalendarUi.Caption(Loc.T("telegram.account.phoneHint"), new Thickness(0, 0, 0, 0)));
        Loaded += (_, _) => (_apiId.Text.Length == 0 ? (UIElement)_apiId : _phone).Focus();
        Closed += (_, _) =>
        {
            // Closed half done: nothing stays (the account that was there, if any, has to log in again).
            if (!_done && Telegram.LoggingIn) Telegram.CancelLogin();
        };
    }

    /// <summary>Opens the window; true when the account is in.</summary>
    public static bool Edit(Window? owner) => new TelegramAccountDialog { Owner = owner }.ShowDialog() == true;

    private void Status(string text, string brush)
    {
        StatusText.Text = text;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, brush);
        StatusText.Visibility = Visibility.Visible;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        Status(Loc.T("telegram.account.working"), "TextFillColorSecondaryBrush");
        try
        {
            string? need;
            if (_step == "start")
            {
                if (!int.TryParse(_apiId.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var apiId) || apiId <= 0)
                    throw new TelegramException(Loc.T("telegram.account.error.apiId"));
                var hash = _apiHash.Password.Trim();
                if (hash.Length == 0 && Telegram.Account is { } kept) hash = Core.Platform.Secrets.Unprotect(kept.ApiHashProtected) ?? string.Empty;
                if (hash.Length < 16) throw new TelegramException(Loc.T("telegram.account.error.apiHash"));
                if (_phone.Text.Count(char.IsAsciiDigit) < 6) throw new TelegramException(Loc.T("telegram.account.error.phone"));
                need = await Telegram.LoginAsync(apiId, hash, _phone.Text);
            }
            else need = await Telegram.LoginStepAsync(_step == "code" ? _code.Text : _password.Password);
            if (need is null)
            {
                _done = true;
                App.Current.Ui.ShowOsd(Loc.T("telegram.account.connected", Telegram.Account?.Shown ?? string.Empty), Core.Engine.OsdKind.Success);
                DialogResult = true;
                return;
            }
            ShowStep(need);
        }
        catch (Exception ex) when (ex is TelegramException or System.IO.IOException or TaskCanceledException)
        {
            Status(ex.Message, "Kd.Danger");
        }
        SaveButton.IsEnabled = true;
    }

    /// <summary>The next step: the code (the number can no longer change) or the password.</summary>
    private void ShowStep(string need)
    {
        _step = need;
        Form.Children.Clear();
        StatusText.Visibility = Visibility.Collapsed;
        if (need == "code")
        {
            Form.Children.Add(CalendarUi.Caption(Loc.T("telegram.account.codeIntro", _phone.Text.Trim()), new Thickness(0, 0, 0, 12)));
            Form.Children.Add(CalendarUi.Label(Loc.T("telegram.account.code")));
            Form.Children.Add(_code);
            _code.Focus();
        }
        else
        {
            Form.Children.Add(CalendarUi.Caption(Loc.T("telegram.account.passwordIntro"), new Thickness(0, 0, 0, 12)));
            Form.Children.Add(CalendarUi.Label(Loc.T("telegram.account.password")));
            Form.Children.Add(_password);
            _password.Focus();
        }
        SaveButton.Content = Loc.T("telegram.account.next");
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

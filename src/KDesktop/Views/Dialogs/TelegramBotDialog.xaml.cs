using System.Windows;
using KDesktop.Core.Localization;
using KDesktop.Core.Remote.Telegram;
using KDesktop.Views.Pages;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using UiPasswordBox = Wpf.Ui.Controls.PasswordBox;

namespace KDesktop.Views.Dialogs;

/// <summary>
/// The Telegram bot, new or with another token (Opciones › Cuentas › Telegram; telegram, phase 1): the token pasted from
/// @BotFather, checked with Telegram before keeping it (encrypted). Says what fails in words.
/// </summary>
public partial class TelegramBotDialog : FluentWindow
{
    private static TelegramService Telegram => App.Current.Telegram;

    private readonly UiPasswordBox _token;

    private TelegramBotDialog()
    {
        InitializeComponent();
        var bot = Telegram.Bot;
        TitleBar.Title = Loc.T(bot is null ? "telegram.bot.newTitle" : "telegram.bot.editTitle");
        Form.Children.Add(CalendarUi.Caption(Loc.T("telegram.bot.editIntro"), new Thickness(0, 0, 0, 12)));
        Form.Children.Add(CalendarUi.Label(Loc.T("telegram.bot.token")));
        _token = new UiPasswordBox { PlaceholderText = bot is null ? string.Empty : Loc.T("accounts.secretKept"), Margin = new Thickness(0, 0, 0, 4) };
        Form.Children.Add(_token);
        Form.Children.Add(CalendarUi.Caption(Loc.T("telegram.bot.tokenHint"), new Thickness(0, 0, 0, 8)));
        Form.Children.Add(CalendarUi.GuideLink(this, "telegram.guide", GuideDialog.Telegram, new Thickness(0, 0, 0, 0)));
        Loaded += (_, _) => _token.Focus();
    }

    /// <summary>Opens the window; true when a bot was connected.</summary>
    public static bool Edit(Window? owner) => new TelegramBotDialog { Owner = owner }.ShowDialog() == true;

    private void Status(string text, string brush)
    {
        StatusText.Text = text;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, brush);
        StatusText.Visibility = Visibility.Visible;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_token.Password.Trim().Length == 0)
        {
            Status(Loc.T("telegram.error.notToken"), "Kd.Danger");
            return;
        }
        SaveButton.IsEnabled = false;
        Status(Loc.T("telegram.bot.checking"), "TextFillColorSecondaryBrush");
        try
        {
            var me = await Telegram.SetBotAsync(_token.Password);
            App.Current.Ui.ShowOsd(Loc.T("telegram.bot.connected", "@" + me.Username), Core.Engine.OsdKind.Success);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is TelegramException or System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            SaveButton.IsEnabled = true;
            Status(ex is TaskCanceledException ? Loc.T("telegram.error.network") : ex.Message, "Kd.Danger");
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

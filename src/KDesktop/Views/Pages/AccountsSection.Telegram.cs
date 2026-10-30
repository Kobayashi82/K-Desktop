using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using KDesktop.Core.Remote.Telegram;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options › Cuentas › Telegram Bot (telegram, phase 1) and › Telegram (the account with a number, phase 6): the bot (its
/// token in a window, with the guide of @BotFather) or the account (logging in with its number, in a window with its
/// guide), how the connection is, linking the owner with a code (sent to it, or with the link or its QR), the test message
/// and what K-Desktop tells by Telegram by itself. Each page works with its own connection, with its own owner and contacts.
/// </summary>
public partial class AccountsSection
{
    /// <summary>The page of the account with a number (else, the bot's).</summary>
    private bool IsAccount => Part == "accounts/telegram";

    /// <summary>The connection of this page: the account's or the bot's.</summary>
    private TelegramService Telegram => IsAccount ? App.Current.TelegramAccount : App.Current.Telegram;

    private System.Windows.Threading.DispatcherTimer? _linkTimer;

    /// <summary>The bot's or the account's row in «Tus cuentas».</summary>
    private Grid TelegramRow(TelegramService telegram)
    {
        var account = telegram.Kind == TelegramVia.Account;
        var page = account ? "settings/accounts/telegram" : "settings/accounts/telegrambot";
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel();
        var name = account ? telegram.Account!.Shown : "@" + telegram.Bot!.Username;
        texts.Children.Add(new TextBlock { Text = Loc.T(account ? "settings.nav.accountsTelegramAccount" : "settings.nav.accountsTelegram") + " · " + name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        texts.Children.Add(CalendarUi.Status(telegram.Owner is { } owner ? Loc.T("telegram.list.owner", owner.Shown) : Loc.T("telegram.owner.none"), "TextFillColorSecondaryBrush"));
        if (StateText(telegram) is { } state && telegram.State != TelegramState.Connected) texts.Children.Add(state);
        grid.Children.Add(texts);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(CalendarUi.IconButton(SymbolRegular.Settings24, Loc.T("telegram.list.open"), () => (Window.GetWindow(this) as MainWindow)?.Navigate(page)));
        actions.Children.Add(account
            ? CalendarUi.IconButton(SymbolRegular.PlugDisconnected24, Loc.T("telegram.account.logout"), () => _ = LogOutAsync(telegram), danger: true)
            : CalendarUi.IconButton(SymbolRegular.PlugDisconnected24, Loc.T("telegram.bot.remove"), () => _ = RemoveBotAsync(telegram), danger: true));
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    private void BuildTelegram()
    {
        _linkTimer?.Stop();
        if (IsAccount) BuildAccount();
        else BuildBot();
        if (!Telegram.Configured) return;
        BuildOwner();
        // Who else may write to it only matters once it works for someone.
        if (Telegram.Owner is not null)
        {
            BuildTalk();
            BuildPeople();
        }
        BuildNotices();
    }

    /// <summary>
    /// Talking to the assistant by Telegram (telegram, phase 4.4): who answers (the one on the screen, or one of them; each
    /// contact may have their own), how (text, its voice as a voice note or both; phase 5.2), whether a voice note is
    /// answered first with what was understood, and whether what it did goes under the answer.
    /// </summary>
    private void BuildTalk()
    {
        var telegram = Telegram;
        var card = CalendarUi.Card(Root, Loc.T("telegram.talk.title"), Loc.T("telegram.talk.intro"));
        var who = new ComboBox
        {
            DisplayMemberPath = "Name", SelectedValuePath = "Id",
            ItemsSource = TelegramAssistantChoices(Loc.T("telegram.talk.onScreen")),
            SelectedValue = telegram.Settings.Assistant ?? string.Empty,
        };
        who.SelectionChanged += (_, _) =>
        {
            if (who.SelectedValue is string id) telegram.Update(s => s.Assistant = id.Length == 0 ? null : id);
        };
        card.Children.Add(CalendarUi.Row(Loc.T("telegram.talk.who"), Loc.T("telegram.talk.whoHint"), who, 240));
        // Text, its voice as a voice note, or both (telegram, phase 5.2).
        var answer = new ComboBox
        {
            DisplayMemberPath = "Name", SelectedValuePath = "Value",
            ItemsSource = TelegramAnswerChoices(),
            SelectedValue = telegram.Settings.Answer,
        };
        answer.SelectionChanged += (_, _) =>
        {
            if (answer.SelectedValue is TelegramAnswerKind kind) telegram.Update(s => s.Answer = kind);
        };
        card.Children.Add(CalendarUi.Row(Loc.T("telegram.talk.answer"), Loc.T("telegram.talk.answerHint"), answer, 240));
        var heard = new ToggleSwitch { IsChecked = telegram.Settings.ShowHeard };
        heard.Click += (_, _) => telegram.Update(s => s.ShowHeard = heard.IsChecked == true);
        card.Children.Add(CalendarUi.Row(Loc.T("telegram.talk.heard"), Loc.T("telegram.talk.heardHint"), heard, 0));
        var did = new ToggleSwitch { IsChecked = telegram.Settings.ShowDid };
        did.Click += (_, _) => telegram.Update(s => s.ShowDid = did.IsChecked == true);
        card.Children.Add(CalendarUi.Row(Loc.T("telegram.talk.did"), Loc.T("telegram.talk.didHint"), did, 0));
        if (App.Current.Assistant is null) card.Children.Add(CalendarUi.Status(Loc.T("telegram.talk.moduleOff"), "Kd.Warning"));
    }

    /// <summary>How the assistant may answer by Telegram (as it was written to, text, voice, both).</summary>
    internal static IReadOnlyList<NamedChoice<TelegramAnswerKind>> TelegramAnswerChoices() =>
        [.. Enum.GetValues<TelegramAnswerKind>().Select(k => new NamedChoice<TelegramAnswerKind>(k, Loc.T("telegram.answer." + k)))];

    /// <summary>The assistants that may answer by Telegram, after a first choice with an empty id.</summary>
    internal static IReadOnlyList<AssistantChoice> TelegramAssistantChoices(string first) =>
        [new(string.Empty, first, null), .. Lookups.AssistantList];

    private void BuildBot()
    {
        var telegram = Telegram;
        var card = CalendarUi.Card(Root, Loc.T("telegram.bot.title"), Loc.T("telegram.bot.intro"));
        if (telegram.Bot is not { } bot)
        {
            card.Children.Add(CalendarUi.Caption(Loc.T("telegram.bot.none")));
            var add = CalendarUi.Button(Loc.T("telegram.bot.add"), SymbolRegular.Add24, EditBot, primary: true);
            add.Margin = new Thickness(0, 8, 0, 0);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            card.Children.Add(add);
            card.Children.Add(CalendarUi.GuideLink(this, "telegram.guide", Dialogs.GuideDialog.Telegram, new Thickness(0, 10, 0, 0)));
            return;
        }
        card.Children.Add(ConnectionRow(telegram, $"@{bot.Username} · {bot.Name}", bot.Enabled, "telegram.bot.enabled",
            CalendarUi.IconButton(SymbolRegular.Edit24, Loc.T("telegram.bot.change"), EditBot),
            CalendarUi.IconButton(SymbolRegular.Delete24, Loc.T("telegram.bot.remove"), () => _ = RemoveBotAsync(telegram), danger: true)));
        card.Children.Add(CalendarUi.GuideLink(this, "telegram.guide", Dialogs.GuideDialog.Telegram, new Thickness(0, 6, 0, 0)));
    }

    /// <summary>
    /// The account with a number (telegram, phase 6): logging in (in a window, with the guide of my.telegram.org), who it is,
    /// how the connection is, whether Telegram limits it, switching it off and closing the session; and the advice of
    /// prudence (a real SIM, never writing to many).
    /// </summary>
    private void BuildAccount()
    {
        var telegram = Telegram;
        var card = CalendarUi.Card(Root, Loc.T("telegram.account.title"), Loc.T("telegram.account.intro"));
        if (telegram.Account is not { Id: > 0 } account)
        {
            card.Children.Add(CalendarUi.Caption(Loc.T("telegram.account.none")));
            var login = CalendarUi.Button(Loc.T("telegram.account.login"), SymbolRegular.Phone24, EditAccount, primary: true);
            login.Margin = new Thickness(0, 8, 0, 0);
            login.HorizontalAlignment = HorizontalAlignment.Left;
            card.Children.Add(login);
            card.Children.Add(CalendarUi.GuideLink(this, "telegram.account.guide", Dialogs.GuideDialog.TelegramAccount, new Thickness(0, 10, 0, 0)));
            card.Children.Add(CalendarUi.Caption(Loc.T("telegram.account.prudence"), new Thickness(0, 10, 0, 0)));
            return;
        }
        card.Children.Add(ConnectionRow(telegram, account.Shown + " · " + account.Phone, account.Enabled, "telegram.account.enabled",
            CalendarUi.IconButton(SymbolRegular.ArrowSync24, Loc.T("telegram.account.relogin"), EditAccount),
            CalendarUi.IconButton(SymbolRegular.PlugDisconnected24, Loc.T("telegram.account.logout"), () => _ = LogOutAsync(telegram), danger: true)));
        if (telegram.Warning is { } warning) card.Children.Add(CalendarUi.Status(warning, "Kd.Warning"));
        card.Children.Add(CalendarUi.Caption(Loc.T("telegram.account.prudence"), new Thickness(0, 6, 0, 0)));
    }

    /// <summary>The bot's or the account's row: who it is and how the connection is, the switch and its buttons.</summary>
    private static Grid ConnectionRow(TelegramService telegram, string name, bool enabled, string enabledKey, params FrameworkElement[] buttons)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (StateText(telegram) is { } state) texts.Children.Add(state);
        grid.Children.Add(texts);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var on = new ToggleSwitch { IsChecked = enabled, ToolTip = Loc.T(enabledKey), Margin = new Thickness(0, 0, 6, 0) };
        System.Windows.Automation.AutomationProperties.SetName(on, Loc.T(enabledKey));
        on.Click += (_, _) => telegram.SetEnabled(on.IsChecked == true);
        actions.Children.Add(on);
        foreach (var button in buttons) actions.Children.Add(button);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    /// <summary>«Conectado», «Conectando…», «Apagado» or what fails; null without a bot or an account.</summary>
    private static TextBlock? StateText(TelegramService telegram)
    {
        if (!telegram.Configured) return null;
        return telegram.State switch
        {
            TelegramState.Connected => CalendarUi.Status(Loc.T("telegram.state.connected"), "Kd.Success"),
            TelegramState.Connecting => CalendarUi.Status(Loc.T("telegram.state.connecting"), "TextFillColorSecondaryBrush"),
            TelegramState.Problem => CalendarUi.Status(telegram.Problem ?? Loc.T("telegram.state.problem"), "Kd.Warning"),
            _ => CalendarUi.Status(Loc.T("telegram.state.off"), "TextFillColorSecondaryBrush"),
        };
    }

    private void BuildOwner()
    {
        var telegram = Telegram;
        var card = CalendarUi.Card(Root, Loc.T("telegram.owner.title"), Loc.T(IsAccount ? "telegram.owner.introAccount" : "telegram.owner.intro"));
        if (telegram.Owner is { } owner)
        {
            var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var texts = new StackPanel();
            texts.Children.Add(new TextBlock { Text = owner.Shown, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            texts.Children.Add(CalendarUi.Status(Loc.T("telegram.owner.since", owner.Linked.ToLocalTime().ToString("d", Loc.Instance.Culture)), "TextFillColorSecondaryBrush"));
            grid.Children.Add(texts);
            var unlink = CalendarUi.IconButton(SymbolRegular.PersonDelete24, Loc.T("telegram.owner.unlink"), () => _ = UnlinkAsync(telegram, owner), danger: true);
            Grid.SetColumn(unlink, 1);
            grid.Children.Add(unlink);
            card.Children.Add(grid);
            var test = CalendarUi.Button(Loc.T("telegram.test"), SymbolRegular.Send24, () => _ = SendTestAsync(telegram), primary: true);
            test.Margin = new Thickness(0, 8, 0, 0);
            test.HorizontalAlignment = HorizontalAlignment.Left;
            test.IsEnabled = telegram.On;
            card.Children.Add(test);
            return;
        }
        if (telegram.LinkCode is { } code)
        {
            BuildLinking(card, code, telegram.LinkUrl);
            return;
        }
        card.Children.Add(CalendarUi.Caption(Loc.T("telegram.owner.none")));
        var link = CalendarUi.Button(Loc.T("telegram.link.start"), SymbolRegular.Link24, () =>
        {
            telegram.StartLinking();
            Build();
        }, primary: true);
        link.Margin = new Thickness(0, 8, 0, 0);
        link.HorizontalAlignment = HorizontalAlignment.Left;
        link.IsEnabled = telegram.On;
        card.Children.Add(link);
    }

    /// <summary>
    /// The code, big; the QR of the link (for the phone) and the buttons to open the link here or cancel. An account
    /// without an @user has no link: the code is written to its number.
    /// </summary>
    private void BuildLinking(StackPanel card, string code, string? url)
    {
        var telegram = Telegram;
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        var how = IsAccount
            ? Loc.T("telegram.link.howAccount", telegram.Account!.Name + (telegram.MeUsername.Length > 0 ? " (@" + telegram.MeUsername + ")" : string.Empty) + " · " + telegram.Account.Phone)
            : Loc.T("telegram.link.how", "@" + telegram.MeUsername);
        left.Children.Add(CalendarUi.Caption(how, new Thickness(0, 0, 0, 8)));
        var digits = new TextBlock { Text = code[..3] + " " + code[3..], FontSize = 32, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 0, 0, 8) };
        digits.SetResourceReference(TextBlock.ForegroundProperty, "Kd.AccentText");
        System.Windows.Automation.AutomationProperties.SetName(digits, code);
        left.Children.Add(digits);
        left.Children.Add(CalendarUi.Status(Loc.T("telegram.link.waiting"), "TextFillColorSecondaryBrush"));
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (url is not null) buttons.Children.Add(CalendarUi.Button(Loc.T("telegram.link.open"), SymbolRegular.Open24, () => ProcessLauncher.Launch(new LaunchSpec(url), asAdmin: false), primary: true));
        buttons.Children.Add(CalendarUi.Button(Loc.T("common.cancel"), SymbolRegular.Dismiss24, () =>
        {
            telegram.CancelLinking();
            Build();
        }));
        left.Children.Add(buttons);
        grid.Children.Add(left);
        if (url is not null)
        {
            var qr = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Top, ToolTip = Loc.T("telegram.link.qr") };
            qr.Child = new Image { Source = Dialogs.WebPairDialog.RenderQr(url), Width = 132, Height = 132 };
            RenderOptions.SetBitmapScalingMode(qr.Child, BitmapScalingMode.NearestNeighbor);
            Grid.SetColumn(qr, 1);
            grid.Children.Add(qr);
        }
        card.Children.Add(grid);
        // When the code runs out, the page goes back to «Vincular».
        _linkTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _linkTimer.Tick += (_, _) =>
        {
            if (telegram.LinkCode is not null) return;
            _linkTimer?.Stop();
            Build();
        };
        _linkTimer.Start();
    }

    private void BuildNotices()
    {
        var telegram = Telegram;
        var card = CalendarUi.Card(Root, Loc.T("telegram.notices.title"), Loc.T("telegram.notices.intro"));
        var mail = new ToggleSwitch { IsChecked = telegram.Settings.Notices.ImportantMail };
        mail.Click += (_, _) => telegram.Update(s => s.Notices.ImportantMail = mail.IsChecked == true);
        card.Children.Add(CalendarUi.Row(Loc.T("telegram.notices.mail"), Loc.T("telegram.notices.mailHint"), mail, 0));
    }

    private void EditBot()
    {
        if (Dialogs.TelegramBotDialog.Edit(Window.GetWindow(this))) Build();
    }

    private void EditAccount()
    {
        Dialogs.TelegramAccountDialog.Edit(Window.GetWindow(this));
        Build();
    }

    private static async Task SendTestAsync(TelegramService telegram)
    {
        var ok = await telegram.SendToOwnerAsync(Loc.T("telegram.test.text", Environment.MachineName));
        App.Current.Ui.ShowOsd(Loc.T(ok ? "telegram.test.sent" : "telegram.test.failed"), ok ? Core.Engine.OsdKind.Success : Core.Engine.OsdKind.Warning);
    }

    private static async Task RemoveBotAsync(TelegramService telegram)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("telegram.bot.remove"),
            Content = Loc.T("telegram.bot.removeText", "@" + telegram.Bot?.Username),
            PrimaryButtonText = Loc.T("accounts.disconnect"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowCenteredAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary) telegram.RemoveBot();
    }

    private static async Task LogOutAsync(TelegramService telegram)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("telegram.account.logout"),
            Content = Loc.T("telegram.account.logoutText", telegram.Account?.Shown ?? string.Empty),
            PrimaryButtonText = Loc.T("telegram.account.logoutButton"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowCenteredAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary) await telegram.LogOutAsync();
    }

    private static async Task UnlinkAsync(TelegramService telegram, TelegramOwner owner)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("telegram.owner.unlink"),
            Content = Loc.T("telegram.owner.unlinkText", owner.Shown),
            PrimaryButtonText = Loc.T("telegram.owner.unlinkButton"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowCenteredAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary) telegram.Unlink();
    }
}

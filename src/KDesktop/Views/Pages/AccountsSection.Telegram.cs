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
/// Options › Cuentas › Telegram (telegram, phase 1): the bot (its token in a window, with the guide of @BotFather), how the
/// connection is, linking the owner with a code (sent to the bot, or with the link or its QR), the test message and what
/// K-Desktop tells by Telegram by itself.
/// </summary>
public partial class AccountsSection
{
    private static TelegramService Telegram => App.Current.Telegram;

    private System.Windows.Threading.DispatcherTimer? _linkTimer;

    /// <summary>The bot's row in «Tus cuentas».</summary>
    private Grid TelegramRow()
    {
        var bot = Telegram.Bot!;
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = "Telegram · @" + bot.Username, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        texts.Children.Add(CalendarUi.Status(Telegram.Owner is { } owner ? Loc.T("telegram.list.owner", owner.Shown) : Loc.T("telegram.owner.none"), "TextFillColorSecondaryBrush"));
        if (StateText() is { } state && Telegram.State != TelegramState.Connected) texts.Children.Add(state);
        grid.Children.Add(texts);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(CalendarUi.IconButton(SymbolRegular.Settings24, Loc.T("telegram.list.open"), () => (Window.GetWindow(this) as MainWindow)?.Navigate("settings/accounts/telegram")));
        actions.Children.Add(CalendarUi.IconButton(SymbolRegular.PlugDisconnected24, Loc.T("telegram.bot.remove"), () => _ = RemoveBotAsync(), danger: true));
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    private void BuildTelegram()
    {
        _linkTimer?.Stop();
        BuildBot();
        if (Telegram.Bot is null) return;
        BuildOwner();
        // Who else may write to it only matters once it works for someone.
        if (Telegram.Owner is not null) BuildPeople();
        BuildNotices();
    }

    private void BuildBot()
    {
        var card = CalendarUi.Card(Root, Loc.T("telegram.bot.title"), Loc.T("telegram.bot.intro"));
        if (Telegram.Bot is not { } bot)
        {
            card.Children.Add(CalendarUi.Caption(Loc.T("telegram.bot.none")));
            var add = CalendarUi.Button(Loc.T("telegram.bot.add"), SymbolRegular.Add24, EditBot, primary: true);
            add.Margin = new Thickness(0, 8, 0, 0);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            card.Children.Add(add);
            card.Children.Add(CalendarUi.GuideLink(this, "telegram.guide", Dialogs.GuideDialog.Telegram, new Thickness(0, 10, 0, 0)));
            return;
        }
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = $"@{bot.Username} · {bot.Name}", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (StateText() is { } state) texts.Children.Add(state);
        grid.Children.Add(texts);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var enabled = new ToggleSwitch { IsChecked = bot.Enabled, ToolTip = Loc.T("telegram.bot.enabled"), Margin = new Thickness(0, 0, 6, 0) };
        System.Windows.Automation.AutomationProperties.SetName(enabled, Loc.T("telegram.bot.enabled"));
        enabled.Click += (_, _) => Telegram.SetEnabled(enabled.IsChecked == true);
        actions.Children.Add(enabled);
        actions.Children.Add(CalendarUi.IconButton(SymbolRegular.Edit24, Loc.T("telegram.bot.change"), EditBot));
        actions.Children.Add(CalendarUi.IconButton(SymbolRegular.Delete24, Loc.T("telegram.bot.remove"), () => _ = RemoveBotAsync(), danger: true));
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        card.Children.Add(grid);
        card.Children.Add(CalendarUi.GuideLink(this, "telegram.guide", Dialogs.GuideDialog.Telegram, new Thickness(0, 6, 0, 0)));
    }

    /// <summary>«Conectado», «Conectando…», «Apagado» or what fails; null without a bot.</summary>
    private static TextBlock? StateText()
    {
        if (Telegram.Bot is null) return null;
        return Telegram.State switch
        {
            TelegramState.Connected => CalendarUi.Status(Loc.T("telegram.state.connected"), "Kd.Success"),
            TelegramState.Connecting => CalendarUi.Status(Loc.T("telegram.state.connecting"), "TextFillColorSecondaryBrush"),
            TelegramState.Problem => CalendarUi.Status(Telegram.Problem ?? Loc.T("telegram.state.problem"), "Kd.Warning"),
            _ => CalendarUi.Status(Loc.T("telegram.state.off"), "TextFillColorSecondaryBrush"),
        };
    }

    private void BuildOwner()
    {
        var card = CalendarUi.Card(Root, Loc.T("telegram.owner.title"), Loc.T("telegram.owner.intro"));
        if (Telegram.Owner is { } owner)
        {
            var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var texts = new StackPanel();
            texts.Children.Add(new TextBlock { Text = owner.Shown, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            texts.Children.Add(CalendarUi.Status(Loc.T("telegram.owner.since", owner.Linked.ToLocalTime().ToString("d", Loc.Instance.Culture)), "TextFillColorSecondaryBrush"));
            grid.Children.Add(texts);
            var unlink = CalendarUi.IconButton(SymbolRegular.PersonDelete24, Loc.T("telegram.owner.unlink"), () => _ = UnlinkAsync(owner), danger: true);
            Grid.SetColumn(unlink, 1);
            grid.Children.Add(unlink);
            card.Children.Add(grid);
            var test = CalendarUi.Button(Loc.T("telegram.test"), SymbolRegular.Send24, () => _ = SendTestAsync(), primary: true);
            test.Margin = new Thickness(0, 8, 0, 0);
            test.HorizontalAlignment = HorizontalAlignment.Left;
            test.IsEnabled = Telegram.Bot is { Enabled: true };
            card.Children.Add(test);
            return;
        }
        if (Telegram.LinkCode is { } code && Telegram.LinkUrl is { } url)
        {
            BuildLinking(card, code, url);
            return;
        }
        card.Children.Add(CalendarUi.Caption(Loc.T("telegram.owner.none")));
        var link = CalendarUi.Button(Loc.T("telegram.link.start"), SymbolRegular.Link24, () =>
        {
            Telegram.StartLinking();
            Build();
        }, primary: true);
        link.Margin = new Thickness(0, 8, 0, 0);
        link.HorizontalAlignment = HorizontalAlignment.Left;
        link.IsEnabled = Telegram.Bot is { Enabled: true };
        card.Children.Add(link);
    }

    /// <summary>The code, big; the QR of the link (for the phone) and the buttons to open the link here or cancel.</summary>
    private void BuildLinking(StackPanel card, string code, string url)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(CalendarUi.Caption(Loc.T("telegram.link.how", "@" + Telegram.Bot!.Username), new Thickness(0, 0, 0, 8)));
        var digits = new TextBlock { Text = code[..3] + " " + code[3..], FontSize = 32, FontWeight = FontWeights.SemiBold, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 0, 0, 8) };
        digits.SetResourceReference(TextBlock.ForegroundProperty, "Kd.AccentText");
        System.Windows.Automation.AutomationProperties.SetName(digits, code);
        left.Children.Add(digits);
        left.Children.Add(CalendarUi.Status(Loc.T("telegram.link.waiting"), "TextFillColorSecondaryBrush"));
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(CalendarUi.Button(Loc.T("telegram.link.open"), SymbolRegular.Open24, () => ProcessLauncher.Launch(new LaunchSpec(url), asAdmin: false), primary: true));
        buttons.Children.Add(CalendarUi.Button(Loc.T("common.cancel"), SymbolRegular.Dismiss24, () =>
        {
            Telegram.CancelLinking();
            Build();
        }));
        left.Children.Add(buttons);
        grid.Children.Add(left);
        var qr = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(8), VerticalAlignment = VerticalAlignment.Top, ToolTip = Loc.T("telegram.link.qr") };
        qr.Child = new Image { Source = Dialogs.WebPairDialog.RenderQr(url), Width = 132, Height = 132 };
        RenderOptions.SetBitmapScalingMode(qr.Child, BitmapScalingMode.NearestNeighbor);
        Grid.SetColumn(qr, 1);
        grid.Children.Add(qr);
        card.Children.Add(grid);
        // When the code runs out, the page goes back to «Vincular».
        _linkTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _linkTimer.Tick += (_, _) =>
        {
            if (Telegram.LinkCode is not null) return;
            _linkTimer?.Stop();
            Build();
        };
        _linkTimer.Start();
    }

    private void BuildNotices()
    {
        var card = CalendarUi.Card(Root, Loc.T("telegram.notices.title"), Loc.T("telegram.notices.intro"));
        var mail = new ToggleSwitch { IsChecked = Telegram.Settings.Notices.ImportantMail };
        mail.Click += (_, _) =>
        {
            Telegram.Settings.Notices.ImportantMail = mail.IsChecked == true;
            Telegram.SaveSettings();
        };
        card.Children.Add(CalendarUi.Row(Loc.T("telegram.notices.mail"), Loc.T("telegram.notices.mailHint"), mail, 0));
    }

    private void EditBot()
    {
        if (Dialogs.TelegramBotDialog.Edit(Window.GetWindow(this))) Build();
    }

    private static async Task SendTestAsync()
    {
        var ok = await Telegram.SendToOwnerAsync(Loc.T("telegram.test.text", Environment.MachineName));
        App.Current.Ui.ShowOsd(Loc.T(ok ? "telegram.test.sent" : "telegram.test.failed"), ok ? Core.Engine.OsdKind.Success : Core.Engine.OsdKind.Warning);
    }

    private static async Task RemoveBotAsync()
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("telegram.bot.remove"),
            Content = Loc.T("telegram.bot.removeText", "@" + Telegram.Bot?.Username),
            PrimaryButtonText = Loc.T("accounts.disconnect"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowCenteredAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary) Telegram.RemoveBot();
    }

    private static async Task UnlinkAsync(TelegramOwner owner)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("telegram.owner.unlink"),
            Content = Loc.T("telegram.owner.unlinkText", owner.Shown),
            PrimaryButtonText = Loc.T("telegram.owner.unlinkButton"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowCenteredAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary) Telegram.Unlink();
    }
}

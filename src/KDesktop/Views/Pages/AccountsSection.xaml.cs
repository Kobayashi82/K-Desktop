using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Accounts;
using KDesktop.Core.Localization;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using UiPasswordBox = Wpf.Ui.Controls.PasswordBox;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options › Cuentas (calendar, phase 6; in two pages since 5.0.430, «Part»): <c>accounts</c>, the connected accounts
/// with what each one is used for, connecting one again when the server forgot it and disconnecting it (revoked in the
/// server); and <c>accounts/google</c>, connecting one (the browser opens; cancellable) and the application it connects
/// with (the user's own Google Client ID).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The token source lives only while a connection is under way, and is disposed then.")]
public partial class AccountsSection : UserControl
{
    private static AccountService Accounts => App.Current.Accounts;

    /// <summary>Which page it is: <c>accounts</c> (the connected ones) or <c>accounts/google</c>.</summary>
    public string Part { get; set; } = "accounts";

    private bool _subscribed;
    private CancellationTokenSource? _connecting;
    private string? _error;

    public AccountsSection()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!_subscribed) Accounts.Changed += OnChanged;
            _subscribed = true;
            Build();
        };
        Unloaded += (_, _) =>
        {
            Accounts.Changed -= OnChanged;
            _subscribed = false;
        };
    }

    private void OnChanged() => Dispatcher.BeginInvoke(() =>
    {
        if (IsKeyboardFocusWithin && Keyboard.FocusedElement is TextBox or PasswordBox) return;
        Build();
    });

    private static class Keyboard
    {
        public static IInputElement? FocusedElement => System.Windows.Input.Keyboard.FocusedElement;
    }

    private void Build()
    {
        if (!IsLoaded) return;
        Root.Children.Clear();
        if (Part == "accounts/google")
        {
            BuildConnect();
            BuildApps();
        }
        else BuildList();
    }

    private void BuildList()
    {
        var card = CalendarUi.Card(Root, Loc.T("accounts.title"), Loc.T("accounts.intro"));
        var all = Accounts.Accounts;
        if (all.Count == 0) card.Children.Add(CalendarUi.Caption(Loc.T("accounts.none")));
        foreach (var account in all)
        {
            var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var texts = new StackPanel();
            texts.Children.Add(new TextBlock { Text = "Google · " + account.Display, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var uses = new List<string>();
            if (account.Uses.HasFlag(AccountUse.CalendarWrite)) uses.Add(Loc.T("accounts.use.calendarWrite"));
            else if (account.Uses.HasFlag(AccountUse.CalendarRead)) uses.Add(Loc.T("accounts.use.calendarRead"));
            if (account.Uses.HasFlag(AccountUse.Mail)) uses.Add(Loc.T("accounts.use.mail"));
            texts.Children.Add(CalendarUi.Status(Loc.T("accounts.usedFor", string.Join(", ", uses)), "TextFillColorSecondaryBrush"));
            if (account.Disconnected) texts.Children.Add(CalendarUi.Status(Loc.T("accounts.disconnected"), "Kd.Warning"));
            grid.Children.Add(texts);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (Accounts.CanConnect(account.Provider))
                actions.Children.Add(CalendarUi.IconButton(SymbolRegular.ArrowSync24, Loc.T("accounts.reconnect"), () => _ = ConnectAsync(account.Provider, account.Uses, account.Email)));
            actions.Children.Add(CalendarUi.IconButton(SymbolRegular.PlugDisconnected24, Loc.T("accounts.disconnect"), () => _ = DisconnectAsync(account), danger: true));
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);
            card.Children.Add(grid);
        }
        card.Children.Add(CalendarUi.NavLink(this, "accounts.goGoogle", "settings/accounts/google", new Thickness(0, 8, 0, 0)));
    }

    private void BuildConnect()
    {
        var card = CalendarUi.Card(Root, Loc.T("accounts.connectTitle"), Loc.T("accounts.connectIntro"));
        if (_connecting is not null)
        {
            card.Children.Add(CalendarUi.Status(Loc.T("accounts.waiting"), "Kd.AccentText"));
            card.Children.Add(CalendarUi.Button(Loc.T("common.cancel"), SymbolRegular.Dismiss24, () => _connecting?.Cancel()).With(b => b.Margin = new Thickness(0, 8, 0, 0)));
            return;
        }
        if (_error is not null) card.Children.Add(CalendarUi.Status(_error, "Kd.Danger"));
        var calendarRead = new RadioButton { Content = Loc.T("accounts.use.calendarRead"), GroupName = "cal", Margin = new Thickness(0, 4, 16, 4) };
        var calendarWrite = new RadioButton { Content = Loc.T("accounts.use.calendarWrite"), GroupName = "cal", IsChecked = true, Margin = new Thickness(0, 4, 16, 4) };
        var noCalendar = new RadioButton { Content = Loc.T("accounts.use.noCalendar"), GroupName = "cal", Margin = new Thickness(0, 4, 16, 4) };
        var mail = new CheckBox { Content = Loc.T("accounts.use.mailLong"), Margin = new Thickness(0, 4, 0, 8) };
        var uses = new WrapPanel();
        uses.Children.Add(calendarWrite);
        uses.Children.Add(calendarRead);
        uses.Children.Add(noCalendar);
        card.Children.Add(CalendarUi.Label(Loc.T("accounts.forWhat")));
        card.Children.Add(uses);
        card.Children.Add(mail);
        AccountUse Chosen() => (calendarWrite.IsChecked == true ? AccountUse.CalendarRead | AccountUse.CalendarWrite : calendarRead.IsChecked == true ? AccountUse.CalendarRead : AccountUse.None)
                               | (mail.IsChecked == true ? AccountUse.Mail : AccountUse.None);
        var buttons = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var google = CalendarUi.Button(Loc.T("accounts.connectGoogle"), SymbolRegular.PersonAdd24, () => _ = ConnectAsync(AccountProvider.Google, Chosen(), null), primary: true);
        google.IsEnabled = Accounts.CanConnect(AccountProvider.Google);
        buttons.Children.Add(google);
        card.Children.Add(buttons);
        if (!Accounts.CanConnect(AccountProvider.Google)) card.Children.Add(CalendarUi.Caption(Loc.T("accounts.googleNeedsApp"), new Thickness(0, 8, 0, 0)));
        card.Children.Add(CalendarUi.Caption(Loc.T("accounts.alternative"), new Thickness(0, 8, 0, 0)));
    }

    private void BuildApps()
    {
        var card = CalendarUi.Card(Root, Loc.T("accounts.apps"), Loc.T("accounts.appsIntro"));
        card.Children.Add(CalendarUi.Label(Loc.T("accounts.googleClientId")));
        var googleId = new UiTextBox { Text = Accounts.GoogleClientId, PlaceholderText = "1234567890-abc….apps.googleusercontent.com", Margin = new Thickness(0, 0, 0, 8), ClearButtonEnabled = false };
        card.Children.Add(googleId);
        card.Children.Add(CalendarUi.Label(Loc.T("accounts.googleSecret")));
        var googleSecret = new UiPasswordBox { PlaceholderText = Accounts.HasGoogleSecret ? Loc.T("accounts.secretKept") : "GOCSPX-…", Margin = new Thickness(0, 0, 0, 8) };
        card.Children.Add(googleSecret);
        card.Children.Add(CalendarUi.GuideLink(this, "accounts.googleGuide", Dialogs.GuideDialog.Google, new Thickness(0, 0, 0, 12)));
        card.Children.Add(CalendarUi.Button(Loc.T("accounts.saveApps"), SymbolRegular.Save24, () =>
        {
            Accounts.SetApps(googleId.Text, googleSecret.Password);
            App.Current.Ui.ShowOsd(Loc.T("accounts.appsSaved"), Core.Engine.OsdKind.Success);
            Build();
        }));
    }

    private async Task ConnectAsync(AccountProvider provider, AccountUse uses, string? hint)
    {
        if (uses == AccountUse.None)
        {
            _error = Loc.T("accounts.chooseUse");
            Build();
            return;
        }
        _error = null;
        _connecting = new CancellationTokenSource();
        Build();
        try
        {
            var account = await Accounts.ConnectAsync(provider, uses, hint, _connecting.Token);
            // Its calendars are shown at once (Options › Calendarios chooses which).
            if (uses.HasFlag(AccountUse.CalendarRead) && App.Current.Calendar.Settings.Accounts.All(a => a.AccountId != account.Id))
                App.Current.Calendar.UseAccount(account.Id, []);
            App.Current.Ui.ShowOsd(Loc.T("accounts.connected", account.Email), Core.Engine.OsdKind.Success);
        }
        catch (OperationCanceledException)
        {
            _error = null;
        }
        catch (Exception ex) when (ex is AccountException or System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException)
        {
            _error = ex.Message;
            Core.Log.Warn($"Cuentas: no se ha podido conectar: {ex.Message}");
        }
        finally
        {
            _connecting.Dispose();
            _connecting = null;
        }
        Build();
    }

    private static async Task DisconnectAsync(Account account)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("accounts.disconnectTitle"),
            Content = Loc.T("accounts.disconnectGoogle", account.Email),
            PrimaryButtonText = Loc.T("accounts.disconnect"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        App.Current.Calendar.ForgetAccount(account.Id);
        App.Current.Mail.ForgetAccount(account.Id);
        await Accounts.DisconnectAsync(account.Id);
    }
}

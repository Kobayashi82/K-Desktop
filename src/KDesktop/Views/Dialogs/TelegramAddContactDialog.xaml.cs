using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Localization;
using KDesktop.Core.Remote.Telegram;
using KDesktop.Views.Pages;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace KDesktop.Views.Dialogs;

/// <summary>
/// A new contact for the Telegram account (Opciones › Cuentas › Telegram; telegram, phase 6.4): one of the people in its
/// contacts in Telegram, or someone by their number (added to its contacts in Telegram too, so it can write to them first).
/// They start with «Solo charla»; what they may do is chosen next, in the contact's window.
/// </summary>
public partial class TelegramAddContactDialog : FluentWindow
{
    private static TelegramService Telegram => App.Current.TelegramAccount;

    private readonly RadioButton _fromAgenda = new() { Content = Loc.T("telegram.add.fromAgenda"), GroupName = "how", IsChecked = true, Margin = new Thickness(0, 0, 16, 8) };
    private readonly RadioButton _byNumber = new() { Content = Loc.T("telegram.add.byNumber"), GroupName = "how", Margin = new Thickness(0, 0, 16, 8) };
    private readonly ComboBox _agenda = new() { DisplayMemberPath = "Shown", HorizontalAlignment = HorizontalAlignment.Stretch, IsEditable = true, IsTextSearchEnabled = true };
    private readonly StackPanel _agendaPanel = new();
    private readonly StackPanel _numberPanel = new() { Visibility = Visibility.Collapsed };
    private readonly UiTextBox _phone = new() { PlaceholderText = "+34 600 000 000", Margin = new Thickness(0, 0, 0, 8), ClearButtonEnabled = false };
    private readonly UiTextBox _first = new() { Margin = new Thickness(0, 0, 0, 8), ClearButtonEnabled = false };
    private readonly UiTextBox _last = new() { Margin = new Thickness(0, 0, 0, 4), ClearButtonEnabled = false };

    private TelegramAddContactDialog()
    {
        InitializeComponent();
        TitleBar.Title = Loc.T("telegram.add.title");
        Form.Children.Add(CalendarUi.Caption(Loc.T("telegram.add.intro"), new Thickness(0, 0, 0, 12)));
        var how = new WrapPanel();
        how.Children.Add(_fromAgenda);
        how.Children.Add(_byNumber);
        Form.Children.Add(how);

        _agendaPanel.Children.Add(CalendarUi.Label(Loc.T("telegram.add.who")));
        _agendaPanel.Children.Add(_agenda);
        System.Windows.Automation.AutomationProperties.SetName(_agenda, Loc.T("telegram.add.who"));
        Form.Children.Add(_agendaPanel);

        _numberPanel.Children.Add(CalendarUi.Label(Loc.T("telegram.account.phone")));
        _numberPanel.Children.Add(_phone);
        _numberPanel.Children.Add(CalendarUi.Label(Loc.T("telegram.add.firstName")));
        _numberPanel.Children.Add(_first);
        _numberPanel.Children.Add(CalendarUi.Label(Loc.T("telegram.add.lastName")));
        _numberPanel.Children.Add(_last);
        _numberPanel.Children.Add(CalendarUi.Caption(Loc.T("telegram.add.numberHint"), new Thickness(0, 4, 0, 0)));
        Form.Children.Add(_numberPanel);

        _fromAgenda.Checked += (_, _) => Switch();
        _byNumber.Checked += (_, _) => Switch();
        Loaded += async (_, _) => await LoadAgendaAsync();
    }

    /// <summary>Opens the window; who was added (to choose next what they may do), or null.</summary>
    public static TelegramUser? Add(Window? owner)
    {
        var dialog = new TelegramAddContactDialog { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Added : null;
    }

    private TelegramUser? Added { get; set; }

    private void Switch()
    {
        var agenda = _fromAgenda.IsChecked == true;
        _agendaPanel.Visibility = agenda ? Visibility.Visible : Visibility.Collapsed;
        _numberPanel.Visibility = agenda ? Visibility.Collapsed : Visibility.Visible;
        StatusText.Visibility = Visibility.Collapsed;
        (agenda ? (UIElement)_agenda : _phone).Focus();
    }

    /// <summary>Its contacts in Telegram that are not contacts here yet (nor the owner).</summary>
    private async Task LoadAgendaAsync()
    {
        Status(Loc.T("telegram.add.loading"), "TextFillColorSecondaryBrush");
        try
        {
            var known = Telegram.Read(s => s.Contacts.Select(c => c.Id).Append(s.Owner?.Id ?? 0).ToHashSet());
            var agenda = await Telegram.AgendaAsync();
            if (agenda is null)
            {
                Status(Loc.T("telegram.error.notReady"), "Kd.Warning");
                return;
            }
            var people = agenda.Where(u => !known.Contains(u.Id)).ToList();
            _agenda.ItemsSource = people;
            if (people.Count == 0)
            {
                Status(Loc.T("telegram.add.agendaEmpty"), "TextFillColorSecondaryBrush");
                _byNumber.IsChecked = true;
            }
            else StatusText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is TelegramException or System.IO.IOException or TaskCanceledException)
        {
            Status(ex.Message, "Kd.Warning");
        }
    }

    private void Status(string text, string brush)
    {
        StatusText.Text = text;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, brush);
        StatusText.Visibility = Visibility.Visible;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        TelegramUser? user;
        if (_fromAgenda.IsChecked == true)
        {
            if (_agenda.SelectedItem is not TelegramUser chosen)
            {
                Status(Loc.T("telegram.add.chooseOne"), "Kd.Danger");
                return;
            }
            user = chosen;
        }
        else
        {
            if (_phone.Text.Count(char.IsAsciiDigit) < 6 || _first.Text.Trim().Length == 0)
            {
                Status(Loc.T("telegram.add.needNumber"), "Kd.Danger");
                return;
            }
            SaveButton.IsEnabled = false;
            Status(Loc.T("telegram.account.working"), "TextFillColorSecondaryBrush");
            try
            {
                user = await Telegram.AddToAgendaAsync(_phone.Text, _first.Text, _last.Text);
            }
            catch (Exception ex) when (ex is TelegramException or System.IO.IOException or TaskCanceledException)
            {
                SaveButton.IsEnabled = true;
                Status(ex.Message, "Kd.Danger");
                return;
            }
            SaveButton.IsEnabled = true;
            if (user is null)
            {
                Status(Loc.T("telegram.add.noTelegram", _phone.Text.Trim()), "Kd.Danger");
                return;
            }
        }
        if (!Telegram.AddContact(user, TelegramLevel.ChatOnly))
        {
            Status(Loc.T("telegram.add.isOwner"), "Kd.Danger");
            return;
        }
        Added = user;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

using System.Windows;
using System.Windows.Controls;
using KDesktop.Core.Calendar;
using KDesktop.Core.Localization;
using KDesktop.Views.Pages;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Dialogs;

/// <summary>
/// A .ics subscription in a window (Opciones › Calendario, 5.0.430): new («Suscribirse») or one there to change (its
/// name, its address —kept encrypted, so it is only written to change it— and how often it is read).
/// </summary>
public partial class SubscriptionDialog : FluentWindow
{
    private static CalendarService Calendar => App.Current.Calendar;

    private static readonly int[] RefreshChoices = [5, 15, 30, 60, 180, 720];

    private readonly IcsSubscriptionSettings? _editing;

    private SubscriptionDialog(IcsSubscriptionSettings? editing)
    {
        InitializeComponent();
        _editing = editing;
        TitleBar.Title = Loc.T(editing is null ? "calendars.newSubscription" : "calendars.editSubscription");
        SaveButton.Content = Loc.T(editing is null ? "calendars.subscribe" : "agenda.save");
        var minutes = editing?.RefreshMinutes ?? 15;
        var choices = RefreshChoices.Select(m => new NamedChoice<int>(m, Loc.T("calendars.every", CalendarText.Minutes(m)))).ToList();
        if (choices.All(c => c.Value != minutes)) choices.Add(new(minutes, Loc.T("calendars.every", CalendarText.Minutes(minutes))));
        EveryBox.ItemsSource = choices;
        EveryBox.SelectedValue = minutes;
        if (editing is not null)
        {
            NameBox.Text = editing.Name;
            UrlBox.PlaceholderText = Loc.T("calendars.urlKept");
        }
        // The guide of where the address is, as in the other pages.
        var panel = (Panel)GuideLine.Parent;
        var at = panel.Children.IndexOf(GuideLine);
        panel.Children.RemoveAt(at);
        panel.Children.Insert(at, CalendarUi.GuideLink(this, "calendars.subscriptionGuide", GuideDialog.Ics, new Thickness(0, 0, 0, 12)));
    }

    /// <summary>A new subscription (<paramref name="editing"/> null) or a change to one; true when saved.</summary>
    public static bool Show(Window? owner, IcsSubscriptionSettings? editing = null) => new SubscriptionDialog(editing) { Owner = owner }.ShowDialog() == true;

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var url = UrlBox.Text.Trim();
        if ((_editing is null || url.Length > 0) && !IcsSubscription.IsValidUrl(url))
        {
            ErrorText.Text = Loc.T("calendars.badUrl");
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        var name = NameBox.Text.Trim().Length > 0 ? NameBox.Text.Trim() : _editing?.Name ?? Loc.T("calendars.subscriptionDefaultName");
        var minutes = EveryBox.SelectedValue is int m ? m : 15;
        if (_editing is null)
        {
            var added = Calendar.AddSubscription(name, url);
            if (added.Settings.RefreshMinutes != minutes)
            {
                added.Settings.RefreshMinutes = minutes;
                Calendar.UpdateSubscription(added.Settings);
            }
        }
        else
        {
            _editing.Name = name;
            _editing.RefreshMinutes = minutes;
            Calendar.UpdateSubscription(_editing, url.Length > 0 ? url : null);
        }
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

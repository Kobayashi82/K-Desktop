using System.Windows;
using KDesktop.Core.Calendar;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Dialogs;

/// <summary>Chooses one calendar of a list.</summary>
public partial class CalendarPickDialog : FluentWindow
{
    public CalendarPickDialog() => InitializeComponent();

    public static CalendarInfo? Pick(Window? owner, IReadOnlyList<CalendarInfo> calendars, string question)
    {
        if (calendars.Count == 0) return null;
        var dlg = new CalendarPickDialog { Owner = owner };
        dlg.Question.Text = question;
        var options = calendars.Select(c => new CalendarEventDialog.CalendarOption(c)).ToList();
        dlg.CalendarBox.ItemsSource = options;
        dlg.CalendarBox.SelectedIndex = 0;
        return dlg.ShowDialog() == true ? (dlg.CalendarBox.SelectedItem as CalendarEventDialog.CalendarOption)?.Info : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

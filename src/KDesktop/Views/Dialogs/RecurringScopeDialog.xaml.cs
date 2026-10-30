using System.Windows;
using KDesktop.Core.Calendar;
using KDesktop.Core.Localization;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Dialogs;

/// <summary>«Solo esta», «Esta y las siguientes» or «Todas», for a change or a delete of a cita that repeats.</summary>
public partial class RecurringScopeDialog : FluentWindow
{
    private EditScope? _result;

    public RecurringScopeDialog() => InitializeComponent();

    /// <summary><paramref name="allowed"/>: the choices offered (null: all three; a new rule cannot be for one only).</summary>
    public static EditScope? Ask(Window? owner, IReadOnlyCollection<EditScope>? allowed, bool delete)
    {
        var dlg = new RecurringScopeDialog { Owner = owner };
        dlg.TitleBar.Title = Loc.T(delete ? "agenda.scope.delete" : "agenda.scope.change");
        if (delete)
        {
            dlg.OkButton.Content = Loc.T("common.delete");
            dlg.OkButton.Appearance = ControlAppearance.Danger;
        }
        if (allowed is not null)
        {
            dlg.ThisOption.Visibility = allowed.Contains(EditScope.This) ? Visibility.Visible : Visibility.Collapsed;
            dlg.FollowingOption.Visibility = allowed.Contains(EditScope.ThisAndFollowing) ? Visibility.Visible : Visibility.Collapsed;
            dlg.AllOption.Visibility = allowed.Contains(EditScope.All) ? Visibility.Visible : Visibility.Collapsed;
            if (!allowed.Contains(EditScope.This)) dlg.FollowingOption.IsChecked = true;
        }
        return dlg.ShowDialog() == true ? dlg._result : null;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        _result = ThisOption.IsChecked == true ? EditScope.This : FollowingOption.IsChecked == true ? EditScope.ThisAndFollowing : EditScope.All;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

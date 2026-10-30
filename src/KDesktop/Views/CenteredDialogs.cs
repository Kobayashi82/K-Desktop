using System.Windows;

namespace KDesktop;

/// <summary>
/// Confirmations and messages over K-Desktop, not in the middle of the screen (5.0.431): the window that asks gets K-Desktop's
/// window as its owner —the active one, or the main one when it is shown— and opens centred on it. With no window of
/// K-Desktop in sight (from the tray, an assistant), it stays in the middle of the screen.
/// </summary>
internal static class CenteredDialogs
{
    /// <summary>Shows <paramref name="box"/> as a dialog centred on K-Desktop.</summary>
    public static Task<Wpf.Ui.Controls.MessageBoxResult> ShowCenteredAsync(this Wpf.Ui.Controls.MessageBox box)
    {
        Place(box);
        return box.ShowDialogAsync();
    }

    /// <summary>Gives <paramref name="window"/> K-Desktop's window as its owner and centres it there (when there is one in sight).</summary>
    public static void Place(Window window)
    {
        if (window.Owner is not null || Owner(window) is not { } owner) return;
        window.Owner = owner;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    private static Window? Owner(Window window)
    {
        if (Application.Current is not { } app) return null;
        var windows = app.Windows.OfType<Window>().Where(w => w != window && w.IsVisible && w.WindowState != WindowState.Minimized).ToList();
        return windows.FirstOrDefault(w => w.IsActive) ?? windows.FirstOrDefault(w => w == app.MainWindow);
    }
}

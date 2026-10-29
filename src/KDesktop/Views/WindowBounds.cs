using System.Globalization;
using System.Windows;

namespace KDesktop.Views;

/// <summary>
/// Where a window was («x,y,w,h» and «,max» when maximized), kept in the settings: the main window and the chat window
/// (D3). It opens there again only if that place is still on a screen (a monitor may be gone).
/// </summary>
internal static class WindowBounds
{
    /// <summary>Puts <paramref name="window"/> where <paramref name="saved"/> says; false when there is nothing usable.</summary>
    public static bool Apply(Window window, string? saved)
    {
        var parts = saved?.Split(',');
        if (parts is not { Length: >= 4 }) return false;
        var n = new double[4];
        for (var i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i])) return false;
        var rect = new Rect(n[0], n[1], Math.Max(window.MinWidth, n[2]), Math.Max(window.MinHeight, n[3]));
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visible = Rect.Intersect(rect, screen);
        if (visible.IsEmpty || visible.Width < 200 || visible.Height < 120) return false;

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = rect.Left;
        window.Top = rect.Top;
        window.Width = rect.Width;
        window.Height = rect.Height;
        if (parts.Length > 4 && parts[4] == "max") window.WindowState = WindowState.Maximized;
        return true;
    }

    public static string Save(Window window)
    {
        // Maximized or minimized: keep the normal-state rectangle so restoring it later looks right.
        var r = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight) : window.RestoreBounds;
        var inv = CultureInfo.InvariantCulture;
        return string.Join(",", new[] { r.Left, r.Top, r.Width, r.Height }.Select(v => Math.Round(v).ToString(inv)))
               + (window.WindowState == WindowState.Maximized ? ",max" : "");
    }
}

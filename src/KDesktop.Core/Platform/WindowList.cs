using KDesktop.Core.Native;

namespace KDesktop.Core.Platform;

/// <summary>One open window as <c>list_windows</c> tells it: its name, title, state and where it is.</summary>
/// <param name="Monitor">Its monitor, 1, 2… (left to right, like <see cref="WindowLayout.WorkAreas"/>).</param>
/// <param name="Place">Where on its monitor: <c>left</c>, <c>right</c>, <c>top</c>, <c>bottom</c>, <c>center</c>, the corners, or <c>full</c>.</param>
/// <param name="Share">How much of its monitor it covers, in %.</param>
public sealed record ListedWindow(string Name, string Process, string Title, bool InFront, bool Minimized, bool Maximized, int Monitor, string Place, int Share);

/// <summary>The open windows with their state and place (B7, <c>list_windows</c>), the one in front first.</summary>
public static class WindowList
{
    /// <summary>The real windows (never K-Desktop's own).</summary>
    public static (IReadOnlyList<ListedWindow> Windows, int Monitors) Read()
    {
        var areas = WindowLayout.WorkAreas();
        var own = Path.GetFileNameWithoutExtension(AppPaths.ExecutablePath);
        var front = Win32.GetForegroundWindow();
        var result = new List<ListedWindow>();
        foreach (var window in AppFinder.OpenWindows())
        {
            if (window.Process.Equals(own, StringComparison.OrdinalIgnoreCase)) continue;
            var minimized = Win32.IsIconic(window.Hwnd);
            var maximized = !minimized && WindowLayout.IsMaximized(window.Hwnd);
            var area = WindowLayout.WorkAreaOf(window.Hwnd);
            var monitor = Math.Max(0, areas.ToList().IndexOf(area)) + 1;
            var (place, share) = minimized ? ("full", 0) : PlaceIn(WindowLayout.VisibleBounds(window.Hwnd), area);
            result.Add(new ListedWindow(window.DisplayName, window.Process, window.Title, window.Hwnd == front, minimized, maximized, monitor, place, share));
        }
        return (result, areas.Count);
    }

    /// <summary>Where a window is on its monitor (by its middle) and how much of it it covers.</summary>
    public static (string Place, int Share) PlaceIn(ScreenRect window, ScreenRect area)
    {
        if (area.Width <= 0 || area.Height <= 0) return ("center", 0);
        var left = Math.Max(window.Left, area.Left);
        var top = Math.Max(window.Top, area.Top);
        var right = Math.Min(window.Left + window.Width, area.Left + area.Width);
        var bottom = Math.Min(window.Top + window.Height, area.Top + area.Height);
        var covered = Math.Max(0, right - left) * (double)Math.Max(0, bottom - top);
        var share = (int)Math.Round(covered * 100 / ((double)area.Width * area.Height));
        if (share >= 90) return ("full", share);
        var x = (window.Left + window.Width / 2.0 - area.Left) / area.Width;
        var y = (window.Top + window.Height / 2.0 - area.Top) / area.Height;
        var wide = window.Width >= area.Width * 0.8;
        var tall = window.Height >= area.Height * 0.8;
        var column = wide ? 1 : x < 0.4 ? 0 : x > 0.6 ? 2 : 1;
        var row = tall ? 1 : y < 0.4 ? 0 : y > 0.6 ? 2 : 1;
        var place = (column, row) switch
        {
            (0, 0) => "topLeft",
            (2, 0) => "topRight",
            (0, 2) => "bottomLeft",
            (2, 2) => "bottomRight",
            (0, _) => "left",
            (2, _) => "right",
            (_, 0) => "top",
            (_, 2) => "bottom",
            _ => "center",
        };
        return (place, share);
    }
}

using KDesktop.Core.Platform;

namespace KDesktop.Core.Input;

public enum ScreenCorner { TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>
/// Esquinas activas (E4): the corners of the monitors where the cursor stops, because past it there is no monitor neither
/// across nor up or down. With two screens, the corners where they meet don't count (the cursor goes on to the other one),
/// but the one left loose by a taller or shifted screen does.
/// </summary>
public static class HotCorners
{
    /// <summary>The cursor this close to a corner (pixels, each way) is in it.</summary>
    public const int Reach = 3;

    /// <summary>After firing, a corner waits until the cursor is this far away (pixels) before firing again.</summary>
    public const int Rearm = 40;

    /// <summary>The corner the cursor is in (null: none).</summary>
    public static ScreenCorner? At(int x, int y, IReadOnlyList<ScreenRect> monitors)
    {
        foreach (var m in monitors)
        {
            if (!Inside(m, x, y)) continue;
            bool left = x < m.Left + Reach, right = x >= m.Right - Reach, top = y < m.Top + Reach, bottom = y >= m.Bottom - Reach;
            ScreenCorner? corner = (left, right, top, bottom) switch
            {
                (true, _, true, _) => ScreenCorner.TopLeft,
                (_, true, true, _) => ScreenCorner.TopRight,
                (true, _, _, true) => ScreenCorner.BottomLeft,
                (_, true, _, true) => ScreenCorner.BottomRight,
                _ => null,
            };
            return corner is { } c && IsFree(m, c, monitors) ? c : null;
        }
        return null;
    }

    /// <summary>The cursor stops at that corner of the monitor: past it, across and up or down, there is no monitor.</summary>
    public static bool IsFree(ScreenRect monitor, ScreenCorner corner, IReadOnlyList<ScreenRect> monitors)
    {
        var (x, y) = Point(monitor, corner);
        var left = corner is ScreenCorner.TopLeft or ScreenCorner.BottomLeft;
        var top = corner is ScreenCorner.TopLeft or ScreenCorner.TopRight;
        return !monitors.Any(m => Inside(m, left ? x - 1 : x + 1, y)) && !monitors.Any(m => Inside(m, x, top ? y - 1 : y + 1));
    }

    /// <summary>The pixel of the corner.</summary>
    public static (int X, int Y) Point(ScreenRect monitor, ScreenCorner corner) => corner switch
    {
        ScreenCorner.TopLeft => (monitor.Left, monitor.Top),
        ScreenCorner.TopRight => (monitor.Right - 1, monitor.Top),
        ScreenCorner.BottomLeft => (monitor.Left, monitor.Bottom - 1),
        _ => (monitor.Right - 1, monitor.Bottom - 1),
    };

    /// <summary>How a corner is written in a hotkey: <c>Corner TopLeft</c>.</summary>
    public static string Hotkey(ScreenCorner corner) => Input.Hotkey.CornerPrefix + corner;

    private static bool Inside(ScreenRect m, int x, int y) => x >= m.Left && x < m.Right && y >= m.Top && y < m.Bottom;
}

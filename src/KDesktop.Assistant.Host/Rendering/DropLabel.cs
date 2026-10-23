using System.Globalization;
using System.Windows;
using System.Windows.Media;
using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Host.Rendering;

/// <summary>
/// The label by the cursor while files are dragged over an assistant (SDK 1.17): «Give it to Lander» with a copy badge,
/// as Windows writes it under its own picture, which is hidden there (it covered the assistant). Below and to the right
/// of the cursor, inside the work area. Engine thread.
/// </summary>
internal sealed class DropLabel(ISurface surface) : IDisposable
{
    private const double FontSize = 12;
    private const double Height = 24;
    private const double Badge = 16;

    private static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private (string Text, Point At, bool Dark, double Scale) _drawn;

    public void Show(string text, Point at, bool dark, MonitorInfo? monitor)
    {
        var scale = monitor?.DpiScale ?? 1;
        if (_drawn == (text, at, dark, scale)) return;
        _drawn = (text, at, dark, scale);

        var ink = dark ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x1B, 0x1B, 0x1F);
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, FontSize * scale, Brush(ink), scale);
        var height = Height * scale;
        var badge = Badge * scale;
        var pad = (height - badge) / 2;
        var width = Math.Ceiling(pad + badge + 6 * scale + formatted.WidthIncludingTrailingWhitespace + 10 * scale);
        var left = at.X + 12 * scale;
        var top = at.Y + 22 * scale;
        if (monitor?.WorkArea is { } work)
        {
            left = Math.Min(left, work.Right - width - 2);
            if (top + height > work.Bottom) top = at.Y - height - 8 * scale;
        }
        var body = new Rect(left, top, width, height);
        var shadowPad = (int)Math.Ceiling(8 * scale);
        var area = new PixelRect((int)Math.Floor(left) - shadowPad, (int)Math.Floor(top) - shadowPad,
            (int)Math.Ceiling(left + width) + shadowPad, (int)Math.Ceiling(top + height) + shadowPad);

        var fill = dark ? Color.FromArgb(246, 0x2B, 0x2B, 0x2E) : Color.FromArgb(250, 0xFF, 0xFF, 0xFF);
        var stroke = new Pen(Brush(dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0)), Math.Max(1, scale));
        stroke.Freeze();
        var accent = dark ? Color.FromRgb(0x60, 0xCD, 0xFF) : Color.FromRgb(0x00, 0x5F, 0xB8);
        var center = new Point(left + pad + badge / 2, top + height / 2);
        var plus = new Pen(Brush(dark ? Color.FromRgb(0x1B, 0x1B, 0x1F) : Colors.White), 1.6 * scale) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        plus.Freeze();
        var arm = badge * 0.26;

        surface.Present(area, dc =>
        {
            dc.DrawRoundedRectangle(Brush(Color.FromArgb(dark ? (byte)80 : (byte)36, 0, 0, 0)), null,
                new Rect(body.X, body.Y + 2 * scale, body.Width, body.Height), height / 2, height / 2);
            dc.DrawRoundedRectangle(Brush(fill), stroke, body, height / 2, height / 2);
            dc.DrawEllipse(Brush(accent), null, center, badge / 2, badge / 2);
            dc.DrawLine(plus, new Point(center.X - arm, center.Y), new Point(center.X + arm, center.Y));
            dc.DrawLine(plus, new Point(center.X, center.Y - arm), new Point(center.X, center.Y + arm));
            dc.DrawText(formatted, new Point(left + pad + badge + 6 * scale, top + (height - formatted.Height) / 2));
        });
    }

    public void Hide()
    {
        _drawn = default;
        surface.Hide();
    }

    public void Dispose() => surface.Dispose();

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

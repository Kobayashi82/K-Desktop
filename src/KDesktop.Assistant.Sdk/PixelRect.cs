using System.Windows;

namespace KDesktop.Assistant.Sdk;

/// <summary>
/// A rectangle in physical screen pixels (the virtual desktop: every monitor, whatever its scaling). All the
/// coordinates the SDK hands out or takes are in these units.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public Point Center => new((Left + Right) / 2.0, (Top + Bottom) / 2.0);

    public static PixelRect FromSize(int x, int y, int width, int height) => new(x, y, x + width, y + height);

    public bool Contains(Point p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

    public bool Intersects(PixelRect other) => other.Left < Right && Left < other.Right && other.Top < Bottom && Top < other.Bottom;

    public PixelRect Offset(int dx, int dy) => new(Left + dx, Top + dy, Right + dx, Bottom + dy);

    public PixelRect Inflate(int amount) => new(Left - amount, Top - amount, Right + amount, Bottom + amount);

    public override string ToString() => $"{Left},{Top} {Width}x{Height}";
}

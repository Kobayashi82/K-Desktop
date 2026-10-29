using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using KDesktop.Core.Input;
using KDesktop.Core.Model;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace KDesktop.Core.Platform;

/// <summary>«Marcar los clics» (F2): a circle that grows and fades where the mouse was clicked, drawn on the frames (never on the screen).</summary>
public sealed partial class ScreenRecorder
{
    /// <summary>How long a click shows.</summary>
    private static readonly long ClickTicks = Stopwatch.Frequency * 45 / 100;

    private readonly List<(long At, int X, int Y, MouseButton Button)> _clicks = []; // recorded ticks and screen pixels
    private ID2D1Factory1? _d2dFactory;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _d2d;
    private ID2D1SolidColorBrush? _clickBrush;
    private float _clickScale = 1;

    /// <summary>A click of the mouse at that point of the screen (the hook's thread: only noted here).</summary>
    public void AddClick(int x, int y, MouseButton button)
    {
        if (!_options.Clicks || _stopping || _end is not null || _pausedAt is not null || IsHiddenFromCapture(x, y)) return;
        lock (_clicks) _clicks.Add((RecordedTicks(), x, y, button));
    }

    /// <summary>The recording bar is left out of the video: a click on it is not marked either.</summary>
    private static bool IsHiddenFromCapture(int x, int y)
    {
        var hwnd = WindowFromPoint(new POINT { X = x, Y = y });
        if (hwnd == IntPtr.Zero) return false;
        return GetWindowDisplayAffinity(GetAncestor(hwnd, 2 /* GA_ROOT */), out var affinity) && affinity == 0x11 /* WDA_EXCLUDEFROMCAPTURE */;
    }

    /// <summary>The size of the circles follows the monitor's scale.</summary>
    private void PrepareClicks(IntPtr monitor)
    {
        if (!_options.Clicks) return;
        if (GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0) _clickScale = dpi / 96f;
    }

    /// <summary>Draws the clicks that show at <paramref name="due"/> on the frame about to be encoded.</summary>
    private void DrawClicks(ID3D11Texture2D texture, long due)
    {
        (long At, int X, int Y, MouseButton Button)[] showing;
        lock (_clicks)
        {
            if (_clicks.Count == 0) return;
            _clicks.RemoveAll(c => due - c.At > ClickTicks);
            showing = [.. _clicks.Where(c => c.At <= due)];
        }
        if (showing.Length == 0) return;

        // Where the video's top left is on the screen now (a window may have moved).
        var origin = _target.Kind == CaptureArea.ActiveWindow && _target.Window != IntPtr.Zero
            ? WindowLayout.VisibleBounds(_target.Window)
            : _target.Area;

        lock (_lock)
        {
            if (_device is null) return;
            var d2d = Direct2D();
            _clickBrush ??= d2d.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
            using var surface = texture.QueryInterface<IDXGISurface>();
            using var bitmap = d2d.CreateBitmapFromDxgiSurface(surface,
                new BitmapProperties1(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96,
                    BitmapOptions.Target | BitmapOptions.CannotDraw));
            d2d.Target = bitmap;
            d2d.BeginDraw();
            foreach (var click in showing)
            {
                var t = (float)((due - click.At) / (double)ClickTicks);
                var grow = 1 - (1 - t) * (1 - t); // fast at first, then slow
                var radius = _clickScale * (10 + 16 * grow);
                var fade = 1 - t;
                // Left: yellow; right and the others: blue.
                var (r, g, b) = click.Button == MouseButton.Left ? (1f, 0.78f, 0.1f) : (0.25f, 0.6f, 1f);
                var center = new Vector2(click.X - origin.Left, click.Y - origin.Top);
                var ellipse = new Ellipse(center, radius, radius);
                _clickBrush!.Color = new Color4(r, g, b, 0.35f * fade);
                d2d.FillEllipse(ellipse, _clickBrush);
                _clickBrush.Color = new Color4(r, g, b, 0.9f * fade);
                d2d.DrawEllipse(ellipse, _clickBrush, 3 * _clickScale);
            }
            d2d.EndDraw();
            d2d.Target = null;
        }
    }

    /// <summary>Direct2D on the recorder's device (under <see cref="_lock"/>), made the first time it is needed.</summary>
    private ID2D1DeviceContext Direct2D()
    {
        if (_d2d is not null) return _d2d;
        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
        using var dxgi = _device!.QueryInterface<IDXGIDevice>();
        _d2dDevice = _d2dFactory.CreateDevice(dxgi);
        return _d2d = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
    }

    private void DisposeClicks()
    {
        DisposeAnimation();
        _clickBrush?.Dispose();
        _clickBrush = null;
        _d2d?.Dispose();
        _d2d = null;
        _d2dDevice?.Dispose();
        _d2dDevice = null;
        _d2dFactory?.Dispose();
        _d2dFactory = null;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr WindowFromPoint(POINT point);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}

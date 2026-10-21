using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Assistant.Sdk;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.World;

/// <summary>
/// Moving and reading windows from the assistant process (<see cref="IWindowServices"/>). Windows of elevated
/// processes are out of reach of a normal-user process, and Windows only lets K-Desktop bring a window to the front:
/// those go to K-Desktop (<c>window</c>, stage 6.5; the moves of a drag, the latest one at most every
/// <see cref="MoveEvery"/>).
/// </summary>
/// <param name="send">Sends a message to K-Desktop (null: no K-Desktop, those cannot be done).</param>
internal sealed unsafe class HostWindowServices(WindowScanner scanner, Func<KDesktop.Core.Assistant.Pipe.PipeMessage, bool>? send = null) : IWindowServices
{
    /// <summary>The moves of an elevated window go to K-Desktop no more often than this (the latest one wins).</summary>
    public const int MoveEvery = 33;

    private readonly Dictionary<long, PixelRect> _moves = [];
    private bool _flushing;
    private long _sentAt;
    private int _next;

    public bool TryMove(long handle, PixelRect bounds)
    {
        var hwnd = (nint)handle;
        if (bounds.IsEmpty || !IsWindow(hwnd) || IsZoomed(hwnd)) return false;
        if (scanner.ProcessOf(hwnd).Elevated) return MoveThroughKDesktop(handle, bounds);
        // The bounds are DWM's (no invisible borders); SetWindowPos wants the window rectangle, borders included.
        if (!GetWindowRect(hwnd, out var outer) || !WindowScanner.TryGetBounds(hwnd, out var frame)) return false;
        int left = frame.Left - outer.Left, top = frame.Top - outer.Top, right = outer.Right - frame.Right, bottom = outer.Bottom - frame.Bottom;
        return SetWindowPos(hwnd, 0, bounds.Left - left, bounds.Top - top, bounds.Width + left + right, bounds.Height + top + bottom,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    public bool TryCommand(long handle, WindowCommand command)
    {
        var hwnd = (nint)handle;
        if (!IsWindow(hwnd)) return false;
        if (command == WindowCommand.Activate || scanner.ProcessOf(hwnd).Elevated)
            return Ask(handle, command.ToString().ToLowerInvariant(), null);
        // ShowWindow answers whether it was visible before, not whether it worked.
        ShowWindow(hwnd, command switch
        {
            WindowCommand.Minimize => 6,  // SW_MINIMIZE
            WindowCommand.Maximize => 3,  // SW_SHOWMAXIMIZED
            _ => 9,                       // SW_RESTORE
        });
        return true;
    }

    /// <summary>The latest move of each elevated window, to K-Desktop a little later (a drag sends one per frame).</summary>
    private bool MoveThroughKDesktop(long handle, PixelRect bounds)
    {
        if (send is null) return false;
        int wait;
        lock (_moves)
        {
            _moves[handle] = bounds;
            if (_flushing) return true;
            _flushing = true;
            wait = (int)Math.Clamp(MoveEvery - (Environment.TickCount64 - _sentAt), 0, MoveEvery);
        }
        _ = Task.Delay(wait).ContinueWith(_ => FlushMoves(), TaskScheduler.Default);
        return true;
    }

    private void FlushMoves()
    {
        List<KeyValuePair<long, PixelRect>> moves;
        lock (_moves)
        {
            moves = [.. _moves];
            _moves.Clear();
            _flushing = false;
            _sentAt = Environment.TickCount64;
        }
        foreach (var (handle, bounds) in moves)
            Ask(handle, "move", new KDesktop.Core.Assistant.Pipe.StepRect(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
    }

    private bool Ask(long handle, string op, KDesktop.Core.Assistant.Pipe.StepRect? rect) =>
        send?.Invoke(new KDesktop.Core.Assistant.Pipe.WindowMessage("w-" + Interlocked.Increment(ref _next), handle, op, rect)) == true;

    /// <summary>
    /// From DWM (<c>DWMWA_CAPTION_BUTTON_BOUNDS</c>, relative to the window rectangle): works with standard windows
    /// and with most that draw their own title bar. The area is split in three: minimize, maximize, close.
    /// </summary>
    public CaptionButtons? GetCaptionButtons(long handle)
    {
        var hwnd = (nint)handle;
        RECT area;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CAPTION_BUTTON_BOUNDS, &area, (uint)sizeof(RECT)) != 0 || !GetWindowRect(hwnd, out var outer))
            return null;
        var width = area.Right - area.Left;
        if (width < 30 || area.Bottom - area.Top < 8) return null;
        int top = outer.Top + area.Top, bottom = outer.Top + area.Bottom, left = outer.Left + area.Left, third = width / 3;
        return new CaptionButtons(
            new PixelRect(left, top, left + third, bottom),
            new PixelRect(left + third, top, left + 2 * third, bottom),
            new PixelRect(left + 2 * third, top, outer.Left + area.Right, bottom));
    }

    /// <summary>
    /// The window as it draws itself (<c>PrintWindow</c> with DWM, so covered parts are there too); when it will not
    /// (elevated windows refuse a normal-user process), what the screen shows of it. Scaled to fit
    /// <paramref name="maxSize"/> and frozen.
    /// </summary>
    public ImageSource? Thumbnail(long handle, int maxSize)
    {
        var hwnd = (nint)handle;
        if (maxSize < 1 || !IsWindow(hwnd) || !GetWindowRect(hwnd, out var outer) || !WindowScanner.TryGetBounds(hwnd, out var frame)) return null;
        int width = outer.Right - outer.Left, height = outer.Bottom - outer.Top;
        if (width < 1 || height < 1 || width > 16384 || height > 16384) return null;

        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = width,
            biHeight = -height, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };
        var bitmap = CreateDIBSection(screen, &header, 0, out var bits, 0, 0);
        try
        {
            if (bitmap == 0 || bits is null) return null;
            var previous = SelectObject(memory, bitmap);
            if (!PrintWindow(hwnd, memory, PW_RENDERFULLCONTENT))
                BitBlt(memory, 0, 0, width, height, screen, outer.Left, outer.Top, SRCCOPY);
            SelectObject(memory, previous);

            // Without the invisible borders and the shadow: what DWM calls the window.
            var crop = new Int32Rect(frame.Left - outer.Left, frame.Top - outer.Top, frame.Right - frame.Left, frame.Bottom - frame.Top);
            if (crop.X < 0 || crop.Y < 0 || crop.Width < 1 || crop.Height < 1 || crop.X + crop.Width > width || crop.Y + crop.Height > height)
                crop = new Int32Rect(0, 0, width, height);
            var stride = width * 4;
            var full = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, (nint)bits, stride * height, stride);
            BitmapSource picture = new CroppedBitmap(full, crop);
            var scale = Math.Min(1.0, maxSize / (double)Math.Max(crop.Width, crop.Height));
            if (scale < 1) picture = new TransformedBitmap(picture, new ScaleTransform(scale, scale));
            // Copied out of the DIB before it goes, and frozen so any thread can use it.
            var result = new WriteableBitmap(picture);
            result.Freeze();
            return result;
        }
        finally
        {
            if (bitmap != 0) DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    /// <summary>The large icon of the window's executable, cached per executable.</summary>
    public ImageSource? AppIcon(long handle)
    {
        var path = WindowScanner.PathOf((nint)handle);
        if (path is null) return null;
        lock (_icons)
        {
            if (_icons.TryGetValue(path, out var cached)) return cached;
            if (_icons.Count > 128) _icons.Clear();
            return _icons[path] = ExtractIcon(path);
        }
    }

    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);

    private static BitmapSource? ExtractIcon(string path)
    {
        var info = default(SHFILEINFOW);
        if (SHGetFileInfo(path, 0, &info, (uint)sizeof(SHFILEINFOW), SHGFI_ICON | SHGFI_LARGEICON) == 0 || info.hIcon == 0) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or COMException)
        {
            return null;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }
}

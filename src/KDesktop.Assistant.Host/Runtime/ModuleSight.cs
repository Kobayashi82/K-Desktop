using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Assistant.Host.World;
using KDesktop.Assistant.Sdk;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The tools' eyes (stage 8.3): a picture of the user's window in front (never K-Desktop's: its chat or the write box
/// may be in front when they write) or of the monitor with the mouse, as JPEG for the model; and the address of the page
/// open in the browser in front, read with UI Automation from its address bar. Nothing is kept.
/// </summary>
internal sealed partial class ModuleSight(IWindowServices windows) : IScreen
{
    /// <summary>The longest side of the picture the model gets (Qwen's projector reads text well at this size).</summary>
    private const int MaxSide = 1600;

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "opera", "vivaldi", "chromium", "firefox", "librewolf", "floorp", "waterfox", "zen", "arc", "yandex", "thorium",
    };

    private readonly WindowScanner _scanner = new(new HashSet<nint>());
    private readonly Lock _gate = new();

    public Task<ScreenPicture?> CaptureAsync(bool wholeScreen, CancellationToken cancellationToken) =>
        OnSta(() => (wholeScreen ? null : WindowPicture()) ?? MonitorPicture(), cancellationToken);

    public async Task<string?> BrowserAddressAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(BrowserAddress, cancellationToken).WaitAsync(TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Log.Warn("Asistente: la barra de direcciones del navegador no contesta");
            return null;
        }
    }

    /// <summary>The user's window in front: the foreground one unless it is K-Desktop's; then the first below that is not always on top.</summary>
    private WindowInfo? UserWindow()
    {
        lock (_gate)
        {
            if (_scanner.TryDescribe(GetForegroundWindow(), out var front) && !Ours(front)) return front;
            foreach (var hwnd in WindowScanner.TopLevel())
            {
                if (((uint)(long)GetWindowLongPtr(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0) continue;
                if (_scanner.TryDescribe(hwnd, out var info) && !Ours(info)) return info;
            }
            return null;
        }
    }

    private static bool Ours(WindowInfo window) => string.Equals(window.AppId, "kdesktop", StringComparison.OrdinalIgnoreCase);

    private ScreenPicture? WindowPicture()
    {
        if (UserWindow() is not { } window || windows.Thumbnail(window.Handle, MaxSide) is not BitmapSource picture) return null;
        return new ScreenPicture(Jpeg(picture), $"the window «{window.Title}» of {window.AppId}", picture.PixelWidth, picture.PixelHeight, window.Title);
    }

    /// <summary>The monitor with the mouse, as it is seen (with what is on top, the assistants' layer too).</summary>
    private static unsafe ScreenPicture? MonitorPicture()
    {
        GetCursorPos(out var cursor);
        var monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info)) return null;
        var bounds = info.rcMonitor;
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        if (width < 1 || height < 1) return null;

        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var header = new BITMAPINFOHEADER { biSize = (uint)sizeof(BITMAPINFOHEADER), biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32 };
        var bitmap = CreateDIBSection(screen, &header, 0, out var bits, 0, 0);
        try
        {
            if (bitmap == 0 || bits is null) return null;
            var previous = SelectObject(memory, bitmap);
            var copied = BitBlt(memory, 0, 0, width, height, screen, bounds.Left, bounds.Top, SRCCOPY);
            SelectObject(memory, previous);
            if (!copied) return null;
            var stride = width * 4;
            BitmapSource picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, (nint)bits, stride * height, stride);
            var scale = Math.Min(1.0, MaxSide / (double)Math.Max(width, height));
            if (scale < 1) picture = new TransformedBitmap(picture, new ScaleTransform(scale, scale));
            var copy = new WriteableBitmap(picture);
            copy.Freeze();
            return new ScreenPicture(Jpeg(copy), (info.dwFlags & 1) != 0 ? "the main monitor" : "a monitor", copy.PixelWidth, copy.PixelHeight);
        }
        finally
        {
            if (bitmap != 0) DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }

    private static byte[] Jpeg(BitmapSource picture)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// The address bar of the browser in front: Firefox names it «urlbar-input»; in Chrome, Edge and the other
    /// Chromium ones it is the first box to write in.
    /// </summary>
    private string? BrowserAddress()
    {
        if (UserWindow() is not { AppId: { } app } window || !Browsers.Contains(app)) return null;
        try
        {
            var root = AutomationElement.FromHandle((nint)window.Handle);
            var edit = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "urlbar-input"))
                       ?? root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (edit?.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) == true && pattern is ValuePattern value
                && value.Current.Value is { Length: > 0 } address)
            {
                Log.Info($"Asistente: página abierta en {app}: {address}");
                return address;
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or COMException or InvalidOperationException)
        {
            Log.Warn($"Asistente: no se ha podido leer la dirección de {app}: {ex.Message}");
        }
        return null;
    }

    /// <summary>WPF's imaging on a thread of its own (it wants one apartment for the whole picture).</summary>
    private static Task<T?> OnSta<T>(Func<T?> work, CancellationToken cancellationToken)
    {
        var done = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { done.TrySetResult(work()); }
            catch (Exception ex) { done.TrySetException(ex); }
        }) { IsBackground = true, Name = "Asistente: captura" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task.WaitAsync(cancellationToken);
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(POINT point, uint flags);
}

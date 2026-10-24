using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Assistant.Host.World;
using KDesktop.Assistant.Sdk;
using KDesktop.Assistant.Tools.Builtin;
using KDesktop.Core;
using KDesktop.Core.Platform;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The tools' eyes (stage 8.3): a picture of the user's window in front (never K-Desktop's: its chat or the write box
/// may be in front when they write) or of the monitor with the mouse, as JPEG for the model and, to read its text
/// (stage 10.1), at full size; and the address of the page open in the browser in front, read with UI Automation from
/// its address bar. Nothing is kept.
/// </summary>
internal sealed class ModuleSight(IWindowServices windows) : IScreen
{
    /// <summary>The longest side of the picture the model gets (Qwen's projector reads text well at this size).</summary>
    private const int MaxSide = 1600;

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "brave", "opera", "vivaldi", "chromium", "firefox", "librewolf", "floorp", "waterfox", "zen", "arc", "yandex", "thorium",
    };

    private readonly WindowScanner _scanner = new(new HashSet<nint>());
    private readonly Lock _gate = new();

    public Task<ScreenPicture?> CaptureAsync(bool wholeScreen, ScreenUse use, CancellationToken cancellationToken) =>
        OnSta(() => (wholeScreen ? null : WindowPicture(use)) ?? MonitorPicture(use), cancellationToken);

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

    /// <summary>
    /// The user's window in front: the foreground one unless it is K-Desktop's; then the first below that is not always on top.
    /// Any thread (also for the gesture of a tool on it, stage 13.5).
    /// </summary>
    public WindowInfo? UserWindow()
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

    private static bool Ours(WindowInfo window) => string.Equals(window.Process, "kdesktop", StringComparison.OrdinalIgnoreCase);

    private ScreenPicture? WindowPicture(ScreenUse use)
    {
        // At full size when its text is read (the OCR wants every pixel), else only as big as the model gets it.
        var size = use.HasFlag(ScreenUse.Read) ? int.MaxValue : MaxSide;
        if (UserWindow() is not { } window || windows.Thumbnail(window.Handle, size) is not BitmapSource picture) return null;
        var pixels = use.HasFlag(ScreenUse.Read) ? Inside(Pixels(picture), (nint)window.Handle) : null;
        return Picture(picture, use, $"the window «{window.Title}» of {window.Process}", window.Title, pixels) with { Area = WindowLayout.VisibleBounds((nint)window.Handle) };
    }

    /// <summary>
    /// Only what is inside the window, to read it: its title already goes apart, and the OCR would read the title bar's
    /// buttons as letters («x»). The picture is of the visible bounds (what DWM calls the window), at full size.
    /// </summary>
    private static BgraImage Inside(BgraImage whole, nint hwnd)
    {
        var frame = WindowLayout.VisibleBounds(hwnd);
        var client = WindowLayout.ClientBounds(hwnd);
        int x = client.Left - frame.Left, y = client.Top - frame.Top;
        if (frame.Width != whole.Width || frame.Height != whole.Height || x < 0 || y < 0 || client.Width < 16 || client.Height < 16
            || x + client.Width > whole.Width || y + client.Height > whole.Height) return whole;
        return whole.Crop(x, y, client.Width, client.Height);
    }

    /// <summary>The monitor with the mouse, as it is seen (with what is on top, the assistants' layer too).</summary>
    private static ScreenPicture? MonitorPicture(ScreenUse use)
    {
        var area = ScreenCapture.MonitorAtCursor();
        if (area.Width < 1 || area.Height < 1) return null;
        var image = ScreenCapture.Capture(area);
        var picture = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Stride);
        return Picture(picture, use, area.Left == 0 && area.Top == 0 ? "the main monitor" : "a monitor", null, image) with { Area = area };
    }

    /// <summary>The JPEG for the model (at most <see cref="MaxSide"/>) and the pixels to read, as <paramref name="use"/> asks.</summary>
    private static ScreenPicture Picture(BitmapSource picture, ScreenUse use, string what, string? window, BgraImage? pixels = null)
    {
        if (use.HasFlag(ScreenUse.Read)) pixels ??= Pixels(picture);
        else pixels = null;
        BitmapSource seen = picture;
        var scale = Math.Min(1.0, MaxSide / (double)Math.Max(picture.PixelWidth, picture.PixelHeight));
        if (scale < 1) seen = new TransformedBitmap(picture, new ScaleTransform(scale, scale));
        var jpeg = use.HasFlag(ScreenUse.See) ? Jpeg(seen) : [];
        return new ScreenPicture(jpeg, what, (int)Math.Round(picture.PixelWidth * scale), (int)Math.Round(picture.PixelHeight * scale), window, pixels);
    }

    /// <summary>The picture as BGRA pixels, opaque (a window drawn by PrintWindow leaves the fourth byte at random).</summary>
    private static BgraImage Pixels(BitmapSource picture)
    {
        if (picture.Format != PixelFormats.Bgra32 && picture.Format != PixelFormats.Bgr32) picture = new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
        int width = picture.PixelWidth, height = picture.PixelHeight;
        var bytes = new byte[width * height * 4];
        picture.CopyPixels(bytes, width * 4, 0);
        for (var i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
        return new BgraImage(width, height, bytes);
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
        if (UserWindow() is not { Process: { } app } window || !Browsers.Contains(app)) return null;
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

}

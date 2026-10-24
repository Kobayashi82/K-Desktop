using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KDesktop.Core;
using KDesktop.Core.Imaging;

namespace KDesktop.Views.Assistant;

/// <summary>
/// Plays a clip of a showcase in an <see cref="Image"/> (stage 16.3): a lossless WebP with <see cref="WebPReader"/>, frame by
/// frame on one bitmap and over and over (a short pause between), never all its frames in memory; anything else (a lossy
/// WebP, a GIF, a PNG) only its first picture, through Windows' decoders. One clip at a time: a new one stops the one before.
/// </summary>
internal sealed class ClipPlayer(Image image) : IDisposable
{
    /// <summary>The pause before a clip starts again.</summary>
    private static readonly TimeSpan Again = TimeSpan.FromSeconds(0.8);

    private CancellationTokenSource? _playing;

    /// <summary>The clip could not be shown (UI thread).</summary>
    public event Action? Failed;

    public void Play(byte[] file)
    {
        Stop();
        var playing = new CancellationTokenSource();
        _playing = playing;
        _ = RunAsync(file, playing.Token);
    }

    public void Stop()
    {
        _playing?.Cancel();
        _playing?.Dispose();
        _playing = null;
    }

    private async Task RunAsync(byte[] file, CancellationToken stop)
    {
        try
        {
            if (WebPReader.IsWebP(file) && await AnimateAsync(file, stop).ConfigureAwait(true)) return;
            if (stop.IsCancellationRequested) return;
            image.Source = FirstPicture(file);
            if (image.Source is null) Failed?.Invoke();
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>A WebP this reader knows, played until stopped; false when it is not one (lossy: Windows shows it).</summary>
    private async Task<bool> AnimateAsync(byte[] file, CancellationToken stop)
    {
        int width, height;
        IEnumerable<WebPFrame> frames;
        try { (width, height, frames) = WebPReader.Open(file); }
        catch (NotSupportedException) { return false; }
        catch (InvalidDataException ex)
        {
            Log.Warn($"Asistentes: catálogo, clip que no se puede leer ({ex.Message})");
            return false;
        }

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var whole = new Int32Rect(0, 0, width, height);
        var dispatcher = image.Dispatcher;
        image.Source = bitmap;
        return await Task.Run(async () =>
        {
            while (true)
            {
                var clock = Stopwatch.StartNew();
                double due = 0;
                var shown = 0;
                try
                {
                    foreach (var frame in frames)
                    {
                        // The frame is the reader's canvas: it is written before the next one is decoded.
                        await dispatcher.InvokeAsync(() => bitmap.WritePixels(whole, frame.Bgra, width * 4, 0), DispatcherPriority.Render, stop);
                        shown++;
                        due += Math.Max(frame.Milliseconds, 20);
                        var wait = due - clock.Elapsed.TotalMilliseconds;
                        if (wait > 1) await Task.Delay(TimeSpan.FromMilliseconds(wait), stop).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when ((ex is NotSupportedException or InvalidDataException) && shown == 0)
                {
                    return false;
                }
                // A still picture: nothing more to do.
                if (shown <= 1) return true;
                await Task.Delay(Again, stop).ConfigureAwait(false);
            }
        }, stop).ConfigureAwait(true);
    }

    /// <summary>Its first picture with Windows' decoders (WebP with the codec of Windows, GIF, PNG…), or null.</summary>
    internal static BitmapSource? FirstPicture(byte[] file)
    {
        try
        {
            var decoder = BitmapDecoder.Create(new MemoryStream(file), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException or IOException)
        {
            return null;
        }
    }

    public void Dispose() => Stop();
}

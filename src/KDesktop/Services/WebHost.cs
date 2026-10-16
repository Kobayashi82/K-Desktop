using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KDesktop.Core.Achievements;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Remote.Web;

namespace KDesktop.Services;

/// <summary>The web server's view of the app: UI-thread access and the icons, drawn to PNG.</summary>
public sealed class WebHost(Dispatcher dispatcher, CommandEngine engine, AchievementService achievements) : IWebHost
{
    /// <summary>Pixels of the PNGs: sharp on a phone at 2-3× for a ~44 px tile icon.</summary>
    private const int IconSize = 96;

    /// <summary>IconProvider caches its images, so the same image → the same PNG without drawing it again.</summary>
    private readonly ConditionalWeakTable<ImageSource, WebIcon> _png = new();

    public CommandEngine Engine => engine;

    public T Invoke<T>(Func<T> func) => dispatcher.CheckAccess() ? func() : dispatcher.Invoke(func);

    public WebIcon? Icon(Command? command, QuickMenuItem? item = null, bool light = false)
    {
        // The web app has its own theme: icons for its background, not for the desktop app's.
        var source = IconProvider.For(light ? IconProvider.IconBackground.Light : IconProvider.IconBackground.Dark,
            () => item is not null ? IconProvider.ForMenuItem(item, command) : command is not null ? IconProvider.ForCommand(command) : null);
        if (source is null) return null;
        if (_png.TryGetValue(source, out var cached)) return cached;
        try
        {
            var icon = Render(source);
            _png.AddOrUpdate(source, icon);
            return icon;
        }
        catch (Exception ex)
        {
            Core.Log.Warn("Web: no se pudo convertir un icono a PNG: " + ex.Message);
            return null;
        }
    }

    public IReadOnlyDictionary<string, long> Runs() => achievements.Snapshot().Data.CommandRuns;

    private static WebIcon Render(ImageSource source)
    {
        // Fit inside the square keeping the proportions.
        var w = source.Width > 0 ? source.Width : 1;
        var h = source.Height > 0 ? source.Height : 1;
        var scale = IconSize / Math.Max(w, h);
        var rect = new Rect((IconSize - w * scale) / 2, (IconSize - h * scale) / 2, w * scale, h * scale);

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen()) dc.DrawImage(source, rect);
        var bitmap = new RenderTargetBitmap(IconSize, IconSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var png = stream.ToArray();
        return new WebIcon(Convert.ToHexString(SHA256.HashData(png))[..20].ToLowerInvariant(), png);
    }
}

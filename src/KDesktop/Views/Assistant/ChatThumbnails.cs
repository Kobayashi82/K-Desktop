using System.IO;
using System.Windows.Media.Imaging;
using KDesktop.Core;

namespace KDesktop.Views.Assistant;

/// <summary>
/// The thumbnails of the pictures of the chat with the assistants: read once (the chat is drawn again with each piece of an
/// answer), small and without keeping the file open; the last ones are kept.
/// </summary>
internal static class ChatThumbnails
{
    private const int Kept = 60;

    /// <summary>The width they are decoded at (twice what is shown, for screens with scaling).</summary>
    private const int Width = 480;

    private static readonly Dictionary<string, (DateTime Written, BitmapSource Picture)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> Order = new();

    /// <summary>The thumbnail of <paramref name="path"/>; null when it is not there or cannot be read.</summary>
    public static BitmapSource? Of(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            if (Cache.TryGetValue(path, out var kept) && kept.Written == info.LastWriteTimeUtc) return kept.Picture;
            var picture = new BitmapImage();
            picture.BeginInit();
            picture.CacheOption = BitmapCacheOption.OnLoad;
            picture.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            picture.DecodePixelWidth = Width;
            picture.UriSource = new Uri(path);
            picture.EndInit();
            picture.Freeze();
            if (!Cache.ContainsKey(path)) Order.Enqueue(path);
            Cache[path] = (info.LastWriteTimeUtc, picture);
            while (Order.Count > Kept) Cache.Remove(Order.Dequeue());
            return picture;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or UriFormatException or System.Runtime.InteropServices.COMException)
        {
            Log.Warn($"Asistente: no se ha podido leer la imagen {path}: {ex.Message}");
            return null;
        }
    }
}

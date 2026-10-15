using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Core;
using KDesktop.Core.Model;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace KDesktop.Services;

/// <summary>
/// Icons of the websites opened by "Open web" actions (and "Run" actions that point to a URL). Each one is downloaded
/// once from the site itself — its &lt;link rel="icon"&gt; tags, else /favicon.ico; no third-party service sees the
/// URLs — and kept as %APPDATA%\K-Desktop 5\Favicons\&lt;host&gt;.png. A site that answers without a usable icon is
/// retried after a week; network errors are retried on the next run.
/// </summary>
public static class Favicons
{
    private const int MaxPixels = 256;
    private const int PageLimit = 512 * 1024, ImageLimit = 1024 * 1024;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromDays(7);

    private static readonly ConcurrentDictionary<string, byte> Started = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Throttle = new(4);
    private static readonly HttpClient Http = CreateClient();

    private static readonly Regex LinkTag = new(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex Attribute = new(@"([\w:-]+)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly Regex SizeToken = new(@"^(\d+)x(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Option "Icons of the websites"; when off nothing is downloaded or shown.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Raised (on a worker thread) after a new icon has been saved.</summary>
    public static event Action? Downloaded;

    /// <summary>The site's icon if it is already on disk; otherwise starts downloading it and returns null.</summary>
    public static ImageSource? Get(string? url)
    {
        if (!Enabled || Parse(url) is not { } uri) return null;
        var key = Key(uri);
        var file = FileFor(key);
        if (File.Exists(file)) return IconProvider.FromFile(file);
        Fetch(uri, key);
        return null;
    }

    /// <summary>Downloads in the background the icons still missing for these actions.</summary>
    public static void Prefetch(IEnumerable<CommandAction> actions)
    {
        if (!Enabled) return;
        foreach (var uri in actions.Select(UrlOf).Select(Parse).OfType<Uri>())
        {
            var key = Key(uri);
            if (!File.Exists(FileFor(key))) Fetch(uri, key);
        }
    }

    /// <summary>Deletes the downloaded icons so they are fetched again.</summary>
    public static void Clear()
    {
        try
        {
            if (Directory.Exists(AppPaths.FaviconsDir)) Directory.Delete(AppPaths.FaviconsDir, recursive: true);
        }
        catch (Exception ex) { Log.Warn("No se pudo vaciar la carpeta de favicons: " + ex.Message); }
        Started.Clear();
        IconProvider.Invalidate();
    }

    private static string? UrlOf(CommandAction action) => action switch
    {
        OpenUrlAction a => a.Url,
        RunAppAction a when IconProvider.LooksLikeUrl(a.Path) => a.Path,
        _ => null,
    };

    /// <summary>"www.site.com/x" or "https://site.com/x" → absolute http(s) URI; null for anything else.</summary>
    private static Uri? Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var s = url.Trim().Trim('"');
        if (!s.Contains("://", StringComparison.Ordinal)) s = "https://" + s;
        return Uri.TryCreate(s, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
               uri.Host.Length > 0 ? uri : null;
    }

    /// <summary>
    /// One icon per site: the host without "www." plus the port when it is not the default one, since every port of a
    /// home server or localhost is usually a different application. Safe as a file name.
    /// </summary>
    private static string Key(Uri uri)
    {
        var host = uri.IdnHost.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        if (!uri.IsDefaultPort) host += "_" + uri.Port;
        return string.Concat(host.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }

    private static string FileFor(string key) => Path.Combine(AppPaths.FaviconsDir, key + ".png");

    private static string MarkerFor(string key) => Path.Combine(AppPaths.FaviconsDir, key + ".none");

    private static void Fetch(Uri page, string key)
    {
        var marker = MarkerFor(key);
        if (File.Exists(marker) && File.GetLastWriteTimeUtc(marker) + RetryAfter > DateTime.UtcNow) return;
        if (!Started.TryAdd(key, 0)) return;
        _ = Task.Run(() => DownloadAsync(page, key));
    }

    private static async Task DownloadAsync(Uri page, string key)
    {
        await Throttle.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var candidate in await CandidatesAsync(page).ConfigureAwait(false))
            {
                if (await DownloadImageAsync(candidate).ConfigureAwait(false) is not { } png) continue;
                Directory.CreateDirectory(AppPaths.FaviconsDir);
                await File.WriteAllBytesAsync(FileFor(key), png).ConfigureAwait(false);
                File.Delete(MarkerFor(key));
                Downloaded?.Invoke();
                return;
            }
            // The site answered but has no usable icon (or only SVG): do not ask again for a while.
            Directory.CreateDirectory(AppPaths.FaviconsDir);
            await File.WriteAllTextAsync(MarkerFor(key), page.ToString()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Usually a site that is down or offline (local services often are): tried again on the next run.
            Log.Info($"Favicon de {key} no disponible: {ex.Message}");
        }
        finally
        {
            Throttle.Release();
        }
    }

    /// <summary>Icon URLs declared by the page, best first, then /favicon.ico. Throws on network errors.</summary>
    private static async Task<IReadOnlyList<Uri>> CandidatesAsync(Uri page)
    {
        var found = new List<(Uri Url, int Size)>();
        var origin = page;
        using (var response = await Http.GetAsync(page, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
        {
            origin = response.RequestMessage?.RequestUri ?? page; // after redirects (site.com → www.site.com…)
            var type = response.Content.Headers.ContentType?.MediaType;
            if (response.IsSuccessStatusCode && (type is null || type.Contains("html", StringComparison.OrdinalIgnoreCase)))
            {
                var html = Encoding.UTF8.GetString(await ReadAsync(response, PageLimit).ConfigureAwait(false));
                var head = html.IndexOf("</head", StringComparison.OrdinalIgnoreCase);
                var links = ParseLinks(head > 0 ? html[..head] : html, origin).ToList();
                found.AddRange(links.Where(l => !l.Manifest).Select(l => (l.Url, l.Size)));
                // Web apps (home servers, PWAs) often declare their icons only in the manifest.
                foreach (var manifest in links.Where(l => l.Manifest).Take(1))
                    found.AddRange(await ManifestIconsAsync(manifest.Url).ConfigureAwait(false));
            }
        }

        var list = found.OrderByDescending(c => Score(c.Size)).Select(c => c.Url).ToList();
        list.Add(new Uri(origin, "/favicon.ico"));
        return list.Distinct().Take(6).ToList();
    }

    private static IEnumerable<(Uri Url, int Size, bool Manifest)> ParseLinks(string html, Uri baseUri)
    {
        foreach (Match tag in LinkTag.Matches(html))
        {
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match a in Attribute.Matches(tag.Value))
                attrs.TryAdd(a.Groups[1].Value, WebUtility.HtmlDecode(a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value));

            var rel = attrs.GetValueOrDefault("rel", "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var touch = rel.Any(r => r.StartsWith("apple-touch-icon", StringComparison.Ordinal));
            var manifest = rel.Contains("manifest");
            if (!touch && !manifest && !rel.Contains("icon")) continue; // "mask-icon" and the like are monochrome SVGs

            if (Resolve(baseUri, attrs.GetValueOrDefault("href")) is not { } url) continue;
            var svg = attrs.GetValueOrDefault("type", "").Contains("svg", StringComparison.OrdinalIgnoreCase);
            yield return (url, SizeOf(attrs.GetValueOrDefault("sizes"), svg || IsSvgPath(url), touch ? 180 : 16), manifest);
        }
    }

    /// <summary>Icons listed in a web app manifest ("icons": [{ "src", "sizes", "type", "purpose" }]).</summary>
    private static async Task<IEnumerable<(Uri, int)>> ManifestIconsAsync(Uri manifest)
    {
        try
        {
            using var response = await Http.GetAsync(manifest, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return [];
            using var doc = System.Text.Json.JsonDocument.Parse(await ReadAsync(response, PageLimit).ConfigureAwait(false));
            if (!doc.RootElement.TryGetProperty("icons", out var icons) || icons.ValueKind != System.Text.Json.JsonValueKind.Array) return [];

            var result = new List<(Uri, int)>();
            foreach (var icon in icons.EnumerateArray())
            {
                string? Text(string name) => icon.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
                if (Text("purpose")?.Contains("monochrome", StringComparison.OrdinalIgnoreCase) == true) continue;
                if (Resolve(manifest, Text("src")) is not { } url) continue;
                var svg = Text("type")?.Contains("svg", StringComparison.OrdinalIgnoreCase) == true || IsSvgPath(url);
                result.Add((url, SizeOf(Text("sizes"), svg, 64)));
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    private static Uri? Resolve(Uri baseUri, string? href)
    {
        href = href?.Trim();
        if (string.IsNullOrEmpty(href) || !Uri.TryCreate(baseUri, href, out var url)) return null;
        return url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp || url.Scheme == "data" ? url : null;
    }

    private static bool IsSvgPath(Uri url) => url.Scheme == "data"
        ? url.OriginalString.StartsWith("data:image/svg", StringComparison.OrdinalIgnoreCase)
        : url.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);

    /// <summary>Largest of "32x32 16x16"; vectors count as 128 px (they scale cleanly).</summary>
    private static int SizeOf(string? sizes, bool svg, int unknown) => svg ? 128 :
        (sizes ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => SizeToken.Match(s)).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(unknown).Max();

    /// <summary>Prefers the smallest icon of at least 64 px (they are shown at 20-48 px), then the biggest one.</summary>
    private static int Score(int size) => size >= 64 ? (size <= MaxPixels ? 2000 - size : 1000 - size / 10) : size;

    private static async Task<byte[]?> DownloadImageAsync(Uri url)
    {
        try
        {
            byte[] bytes;
            if (url.Scheme == "data")
            {
                var s = url.OriginalString;
                var comma = s.IndexOf(',');
                if (comma < 0) return null;
                var payload = Uri.UnescapeDataString(s[(comma + 1)..]);
                bytes = s[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase)
                    ? Convert.FromBase64String(payload)
                    : Encoding.UTF8.GetBytes(payload); // "data:image/svg+xml,%3Csvg…"
            }
            else
            {
                using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                bytes = await ReadAsync(response, ImageLimit).ConfigureAwait(false);
            }
            return ToPng(bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Decodes .svg/.ico/.png/.jpg/.gif/.bmp/.webp, keeps the biggest frame (max 256 px) and encodes it as PNG.</summary>
    private static byte[]? ToPng(byte[] bytes)
    {
        if (LooksLikeSvg(bytes)) return SvgToPng(bytes);
        using var input = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).ThenByDescending(f => f.Format.BitsPerPixel).FirstOrDefault();
        if (frame is null || frame.PixelWidth < 10 || frame.PixelHeight < 10) return null;

        BitmapSource image = frame;
        var largest = Math.Max(frame.PixelWidth, frame.PixelHeight);
        if (largest > MaxPixels)
        {
            var scale = (double)MaxPixels / largest;
            image = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static bool LooksLikeSvg(byte[] bytes)
    {
        var start = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 1024)).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return start.StartsWith('<') && start.Contains("<svg", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Renders an SVG at 128 px (keeping its proportions) on an STA thread, as WPF drawing requires.</summary>
    private static byte[]? SvgToPng(byte[] svg)
    {
        const int size = 128;
        byte[]? png = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var reader = new FileSvgReader(new WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = true }, false);
                using var input = new MemoryStream(svg);
                var drawing = reader.Read(input);
                var bounds = drawing?.Bounds ?? Rect.Empty;
                if (drawing is null || bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) return;

                var scale = size / Math.Max(bounds.Width, bounds.Height);
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.PushTransform(new TranslateTransform((size - bounds.Width * scale) / 2, (size - bounds.Height * scale) / 2));
                    dc.PushTransform(new ScaleTransform(scale, scale));
                    dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
                    dc.DrawDrawing(drawing);
                }
                var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = new MemoryStream();
                encoder.Save(output);
                png = output.ToArray();
            }
            catch (Exception ex)
            {
                Log.Info("No se pudo dibujar un favicon SVG: " + ex.Message);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread.Join(TimeSpan.FromSeconds(10)) ? png : null;
    }

    private static async Task<byte[]> ReadAsync(HttpResponseMessage response, int limit)
    {
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while (buffer.Length < limit && (read = await stream.ReadAsync(chunk).ConfigureAwait(false)) > 0)
            buffer.Write(chunk, 0, read);
        return buffer.ToArray();
    }

    /// <summary>localhost, private/link-local IPs and .local/.lan names.</summary>
    private static bool IsLocalHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || !host.Contains('.') ||
            host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!IPAddress.TryParse(host.Trim('[', ']'), out var ip)) return false;
        if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal) return true;
        var b = ip.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254));
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            // Home servers usually have self-signed certificates; only an icon is downloaded, so accept them there.
            SslOptions = { RemoteCertificateValidationCallback = (sender, _, _, errors) =>
                errors == SslPolicyErrors.None || (sender is SslStream ssl && IsLocalHost(ssl.TargetHostName)) },
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        // Some sites only answer to browsers.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,image/avif,image/webp,image/*;q=0.9,*/*;q=0.8");
        return client;
    }
}

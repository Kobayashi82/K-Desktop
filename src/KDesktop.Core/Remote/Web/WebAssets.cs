using System.Collections.Frozen;
using System.Reflection;
using System.Security.Cryptography;

namespace KDesktop.Core.Remote.Web;

/// <summary>One file of the web app.</summary>
internal sealed record WebAsset(byte[] Bytes, string ContentType, string ETag);

/// <summary>The web app's files, embedded in KDesktop.Core.dll as "Web/…" (see the csproj).</summary>
internal static class WebAssets
{
    private const string Folder = "Web/";

    private static readonly Lazy<FrozenDictionary<string, WebAsset>> Files = new(Load);

    public static WebAsset? Get(string path) =>
        Files.Value.TryGetValue(path.TrimStart('/'), out var asset) ? asset : null;

    /// <summary>
    /// The web app manifest, built on request so its shortcuts use the PC's language. Every URL is relative to the
    /// manifest, so it works under any base path.
    /// </summary>
    public static WebAsset Manifest()
    {
        var manifest = new
        {
            id = "./",
            name = "K-Desktop",
            short_name = "K-Desktop",
            description = Localization.Loc.T("settings.webEnabledHint"),
            start_url = "./",
            scope = "./",
            display = "standalone",
            background_color = "#161618",
            theme_color = "#1c1c1e",
            icons = new object[]
            {
                new { src = "icons/icon-192.png", sizes = "192x192", type = "image/png" },
                new { src = "icons/icon-512.png", sizes = "512x512", type = "image/png" },
                new { src = "icons/maskable-512.png", sizes = "512x512", type = "image/png", purpose = "maskable" },
            },
            shortcuts = new object[]
            {
                new { name = Localization.Loc.T("pwa.tab.all"), url = "./#tab=all", icons = new[] { new { src = "icons/icon-192.png", sizes = "192x192" } } },
                new { name = Localization.Loc.T("pwa.tab.pad"), url = "./#tab=pad", icons = new[] { new { src = "icons/icon-192.png", sizes = "192x192" } } },
                new { name = Localization.Loc.T("pwa.tab.audio"), url = "./#tab=audio", icons = new[] { new { src = "icons/icon-192.png", sizes = "192x192" } } },
            },
        };
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(manifest);
        return new WebAsset(bytes, ContentType(".webmanifest"), "\"" + Convert.ToHexString(SHA256.HashData(bytes))[..16] + "\"");
    }

    private static FrozenDictionary<string, WebAsset> Load()
    {
        var assembly = typeof(WebAssets).Assembly;
        var files = new Dictionary<string, WebAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            // %(RecursiveDir) brings Windows separators.
            var path = name.Replace(Path.DirectorySeparatorChar, '/');
            if (!path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)) continue;
            using var stream = assembly.GetManifestResourceStream(name)!;
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            var etag = "\"" + Convert.ToHexString(SHA256.HashData(bytes))[..16] + "\"";
            files[path[Folder.Length..]] = new WebAsset(bytes, ContentType(path), etag);
        }
        return files.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".webmanifest" => "application/manifest+json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream",
    };
}

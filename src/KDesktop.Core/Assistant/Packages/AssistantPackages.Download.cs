using System.Net.Http;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Assistant.Packages;

public static partial class AssistantPackages
{
    private static readonly Lazy<HttpClient> Http = new(() => new HttpClient { Timeout = TimeSpan.FromMinutes(10) });

    /// <summary>Where downloaded packages wait for their card (deleted when it closes).</summary>
    public static string DownloadFolder => Path.Combine(Path.GetTempPath(), "K-Desktop", "Assistants");

    /// <summary>
    /// Whether <paramref name="url"/> can be the <c>updates</c> address of a package: https, or http to this PC only
    /// (an author trying out their updates with a local server).
    /// </summary>
    public static bool IsPublishAddress(string? url, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? uri)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }

    /// <summary>
    /// Downloads a package from the <c>updates</c> address of its manifest (see <see cref="IsPublishAddress"/>) to a
    /// temporary file, for its card: nothing is installed or loaded here. Throws <see cref="PackageException"/> with what
    /// went wrong.
    /// </summary>
    public static async Task<string> DownloadAsync(string url, string name, CancellationToken ct = default)
    {
        if (!IsPublishAddress(url, out var uri))
            throw Fail(PackageProblem.NotAPackage, "assistantPkg.badUrl", name, url);
        Directory.CreateDirectory(DownloadFolder);
        var file = Path.Combine(DownloadFolder, $"{Guid.NewGuid():N}{Extension}");
        try
        {
            using var response = await Http.Value.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxSize) throw Fail(PackageProblem.TooBig, "assistantPkg.tooBig", url);
            var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                var target = File.Create(file);
                await using (target.ConfigureAwait(false))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        total += read;
                        if (total > MaxSize) throw Fail(PackageProblem.TooBig, "assistantPkg.tooBig", url);
                        await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    }
                }
            }
            Log.Info($"Asistentes: «{name}» descargado de {url}");
            return file;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            TryDeleteFile(file);
            Log.Warn($"Asistentes: no se ha podido descargar «{name}» de {url} ({ex.Message})");
            throw new PackageException(PackageProblem.NotAPackage, Loc.T("assistantPkg.downloadFailed", name, ex.Message), ex);
        }
        catch
        {
            TryDeleteFile(file);
            throw;
        }
    }

    /// <summary>Deletes a downloaded package once its card is closed (only files in <see cref="DownloadFolder"/>).</summary>
    public static void ForgetDownload(string file)
    {
        var folder = Path.GetFullPath(DownloadFolder) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(file).StartsWith(folder, StringComparison.OrdinalIgnoreCase)) TryDeleteFile(file);
    }

    private static void TryDeleteFile(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* it is in the temporary folder */ }
    }
}

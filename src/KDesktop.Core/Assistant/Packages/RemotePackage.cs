using System.IO.Compression;
using System.Net.Http;
using System.Text.Json.Nodes;
using KDesktop.Core.Assistant.Voice;
using KDesktop.Core.Localization;

namespace KDesktop.Core.Assistant.Packages;

/// <summary>
/// A package published on the web, opened without downloading it (stage 16.3): its directory and its assistant.json, and
/// then each file only when it is asked for (HTTP ranges, as <see cref="AssistantUpdates.ReadPackageAsync"/>; the whole
/// package into memory only from a server that does not serve ranges). For the catalog's detail window, which shows the
/// clips one by one. One file at a time (the zip is not shared between threads).
/// </summary>
public sealed class RemotePackage : IDisposable
{
    private readonly Stream _stream;
    private readonly ZipArchive _zip;
    private readonly HttpRangeStream? _ranges;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _disposed;

    private RemotePackage(Stream stream, HttpRangeStream? ranges, ZipArchive zip, AssistantManifest manifest)
    {
        _stream = stream;
        _ranges = ranges;
        _zip = zip;
        Manifest = manifest;
    }

    public AssistantManifest Manifest { get; }

    /// <summary>How big the package is (bytes).</summary>
    public long Size => _ranges?.Length ?? _stream.Length;

    /// <summary>Bytes downloaded so far (the whole package when the server sends no ranges).</summary>
    public long Fetched => _ranges?.Fetched ?? _stream.Length;

    /// <summary>Opens the package published at <paramref name="url"/>. Throws when it cannot be reached or is not a package.</summary>
    public static async Task<RemotePackage> OpenAsync(string url, HttpClient http, CancellationToken ct)
    {
        if (!AssistantPackages.IsPublishAddress(url, out var uri)) throw new ArgumentException(Loc.T("assistantUpdates.badUrl"));
        var ranges = await HttpRangeStream.OpenAsync(http, uri, ct).ConfigureAwait(false);
        Stream stream = ranges ?? (Stream)await AssistantUpdates.WholeAsync(http, uri, ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                try
                {
                    var manifest = AssistantUpdates.ManifestIn(zip);
                    return new RemotePackage(stream, ranges, zip, manifest);
                }
                catch
                {
                    zip.Dispose();
                    throw;
                }
            }, ct).ConfigureAwait(false);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Whether the package has that file.</summary>
    public bool Has(string path) => Find(path) is not null;

    /// <summary>
    /// The bytes of one of its files (null when it does not have it, its path is not safe or it is bigger than
    /// <paramref name="max"/>). Throws when the server fails meanwhile.
    /// </summary>
    public async Task<byte[]?> ReadAsync(string path, long max, CancellationToken ct)
    {
        if (!AssistantPackages.IsSafePath(path)) return null;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Find(path) is not { } entry || entry.Length > max) return null;
            return await Task.Run(() =>
            {
                // Its local header (30 bytes, its name and maybe an extra field) and its data, in one request.
                _ranges?.Expect(entry.CompressedLength + 30 + entry.FullName.Length * 3 + 1024);
                using var memory = new MemoryStream((int)entry.Length);
                using (var source = entry.Open()) source.CopyTo(memory);
                return memory.ToArray();
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            // Closed meanwhile: this read was the last one.
            if (_disposed) Close();
            _gate.Release();
        }
    }

    /// <summary>The voice it brings (its profile, from its <c>voice</c> file), or null.</summary>
    public async Task<VoiceProfile?> ReadVoiceAsync(CancellationToken ct)
    {
        if (Manifest.Voice is not { } path || await ReadAsync(path, 256 * 1024, ct).ConfigureAwait(false) is not { } bytes) return null;
        try
        {
            return JsonNode.Parse(bytes) is JsonObject json ? VoiceProfile.Parse(json, Path.GetFileNameWithoutExtension(path)) : null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private ZipArchiveEntry? Find(string path)
    {
        path = path.Replace('\\', '/');
        return _zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Closes it now, or, when a file is being read, as soon as that read ends (it never waits for the server).</summary>
    public void Dispose()
    {
        _disposed = true;
        if (!_gate.Wait(0)) return;
        try { Close(); }
        finally { _gate.Release(); }
    }

    private void Close()
    {
        if (_closed) return;
        _closed = true;
        _zip.Dispose();
        _stream.Dispose();
    }

    private bool _closed;
}

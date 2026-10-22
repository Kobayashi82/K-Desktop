using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace KDesktop.Core.Assistant.Packages;

/// <summary>
/// A read-only, seekable view of a file on a web server that only fetches the parts that are read (HTTP Range), in
/// blocks of 64 KB. <see cref="System.IO.Compression.ZipArchive"/> reads a package's directory and its manifest through
/// it without downloading the package (stage 9.5): a few KB instead of megabytes.
/// </summary>
internal sealed class HttpRangeStream : Stream
{
    private const int Block = 64 * 1024;

    private readonly HttpClient _http;
    private readonly Uri _url;
    private readonly CancellationToken _ct;
    private readonly Dictionary<long, byte[]> _blocks = [];
    private long _position;

    private HttpRangeStream(HttpClient http, Uri url, long length, CancellationToken ct)
    {
        _http = http;
        _url = url;
        Length = length;
        _ct = ct;
    }

    /// <summary>Bytes downloaded so far.</summary>
    public long Fetched { get; private set; }

    /// <summary>
    /// Opens <paramref name="url"/> asking for its first byte; null when the server sends the whole file instead (it
    /// does not serve ranges). Throws <see cref="HttpRequestException"/> when it answers with an error.
    /// </summary>
    public static async Task<HttpRangeStream?> OpenAsync(HttpClient http, Uri url, CancellationToken ct)
    {
        using var request = Request(url, 0, 0);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.Length is not { } length) return null;
        // GitHub sends a release download elsewhere (a signed address): from now on, ask there directly.
        return new HttpRangeStream(http, response.RequestMessage?.RequestUri ?? url, length, ct);
    }

    private static HttpRequestMessage Request(Uri url, long from, long to)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(from, to);
        // No caches in between: always the package published now.
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return request;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length { get; }

    public override long Position
    {
        get => _position;
        set => _position = Math.Clamp(value, 0, Length);
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        _ => Length + offset,
    };

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var done = 0;
        while (done < buffer.Length && _position < Length)
        {
            var index = _position / Block;
            var block = BlockAt(index);
            var at = (int)(_position - index * Block);
            var n = Math.Min(buffer.Length - done, block.Length - at);
            block.AsSpan(at, n).CopyTo(buffer[done..]);
            done += n;
            _position += n;
        }
        return done;
    }

    private byte[] BlockAt(long index)
    {
        if (_blocks.TryGetValue(index, out var cached)) return cached;
        var from = index * Block;
        var to = Math.Min(from + Block, Length) - 1;
        using var request = Request(_url, from, to);
        using var response = _http.Send(request, HttpCompletionOption.ResponseHeadersRead, _ct);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.PartialContent) throw new IOException("El servidor dejó de enviar trozos del archivo");
        var data = new byte[to - from + 1];
        using (var stream = response.Content.ReadAsStream(_ct)) stream.ReadExactly(data);
        Fetched += data.Length;
        return _blocks[index] = data;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

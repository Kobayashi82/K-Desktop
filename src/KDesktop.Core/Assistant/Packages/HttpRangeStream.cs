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

    /// <summary>The most blocks asked for at once (1 MB), when it is being read straight through.</summary>
    private const int MostAhead = 16;

    /// <summary>The block after the last one fetched, and how many were fetched then (reading on doubles it).</summary>
    private long _next = -1;
    private int _ahead = 1;

    /// <summary>The most asked for at once when whoever reads says how much is coming (<see cref="Expect"/>): 8 MB.</summary>
    private const int MostExpected = 128;

    private long _expected;

    /// <summary>
    /// The next read that has to download goes on for about <paramref name="bytes"/> (a file of the package: its header and
    /// its data): it is asked for in one request, without reading past it.
    /// </summary>
    public void Expect(long bytes) => _expected = bytes;

    private byte[] BlockAt(long index)
    {
        if (_blocks.TryGetValue(index, out var cached)) return cached;
        // Reading straight through (a clip, a file of the package): more blocks in one request each time; or, when it was
        // said how much is coming, all of it at once.
        _ahead = _expected > 0 ? (int)Math.Clamp((_position - index * Block + _expected + Block - 1) / Block, 1, MostExpected)
            : index == _next ? Math.Min(_ahead * 2, MostAhead) : 1;
        _expected = 0;
        var count = 1;
        while (count < _ahead && !_blocks.ContainsKey(index + count) && (index + count) * Block < Length) count++;
        var from = index * Block;
        var to = Math.Min(from + (long)count * Block, Length) - 1;
        using var request = Request(_url, from, to);
        using var response = _http.Send(request, HttpCompletionOption.ResponseHeadersRead, _ct);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.PartialContent) throw new IOException("El servidor dejó de enviar trozos del archivo");
        using (var stream = response.Content.ReadAsStream(_ct))
            for (var i = 0; i < count; i++)
            {
                var start = from + (long)i * Block;
                var data = new byte[Math.Min(Block, Length - start)];
                stream.ReadExactly(data);
                Fetched += data.Length;
                _blocks[index + i] = data;
            }
        _next = index + count;
        return _blocks[index];
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

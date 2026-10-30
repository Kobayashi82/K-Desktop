using System.Net;
using System.Security.Cryptography;
using System.Text;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Remote.Web;

/// <summary>
/// What plays on the PC, for the Multimedia tab and the phone's media notification (5.0.407): the programs that tell
/// Windows what they play, with their cover, controls and position. <c>GET api/media</c>, <c>POST api/media {app, op}</c>
/// or <c>{app, position}</c> (seconds), <c>GET api/media/art?app=…</c>. While a phone has the app open, a <c>media</c>
/// event says that something changed (a track, a pause, a program that starts or stops playing).
/// </summary>
public sealed partial class WebServer
{
    private static readonly TimeSpan MediaQuiet = TimeSpan.FromMilliseconds(350);

    private readonly Lock _mediaLock = new();
    private IDisposable? _mediaWatch;
    private bool _mediaStarting;
    private Timer? _mediaTimer;

    /// <summary>GET: the programs with something to play, the one playing first.</summary>
    private static async Task MediaStateAsync(HttpListenerResponse response) =>
        await WriteJsonAsync(response, await MediaStateAsync().ConfigureAwait(false)).ConfigureAwait(false);

    private static async Task<object> MediaStateAsync()
    {
        IReadOnlyList<MediaNow> list;
        try { list = await MediaSessions.ListAsync().ConfigureAwait(false); }
        catch (Exception ex) when (ex is TimeoutException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log.Warn("Web: no se pudo leer qué suena: " + ex.Message);
            list = [];
        }
        return new
        {
            sessions = list.Select(m => new
            {
                m.App,
                m.AppId,
                m.Title,
                m.Artist,
                m.Album,
                State = m.State.ToString().ToLowerInvariant(),
                Position = m.Position?.TotalSeconds,
                Length = m.Length?.TotalSeconds,
                m.Current,
                m.CanPrevious,
                m.CanNext,
                m.CanSeek,
                // Changes with the track: the phone keeps the cover it already has.
                Art = m.HasArt ? ArtKey(m) : null,
            }),
        };
    }

    private static string ArtKey(MediaNow m) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{m.AppId}\n{m.Title}\n{m.Artist}\n{m.Album}")))[..16].ToLowerInvariant();

    /// <summary>POST {app, op: play|pause|toggle|next|previous|stop} or {app, position}: does it and answers the new state.</summary>
    private static async Task MediaChangeAsync(HttpListenerContext context)
    {
        var body = await ReadJsonAsync(context.Request).ConfigureAwait(false);
        var app = Str(body, "app");
        if (string.IsNullOrEmpty(app))
        {
            await WriteJsonAsync(context.Response, new { error = "app" }, 400).ConfigureAwait(false);
            return;
        }
        var done = false;
        try
        {
            if (Str(body, "op") is { } name)
            {
                MediaOp? op = name switch
                {
                    "play" => MediaOp.Play,
                    "pause" => MediaOp.Pause,
                    "toggle" => MediaOp.Toggle,
                    "next" => MediaOp.Next,
                    "previous" => MediaOp.Previous,
                    "stop" => MediaOp.Stop,
                    _ => null,
                };
                if (op is null)
                {
                    await WriteJsonAsync(context.Response, new { error = "op" }, 400).ConfigureAwait(false);
                    return;
                }
                done = await MediaSessions.ControlAppAsync(app, op.Value).ConfigureAwait(false);
            }
            else if (body is { } b && b.TryGetProperty("position", out var p) && p.TryGetDouble(out var seconds) && double.IsFinite(seconds))
            {
                done = await MediaSessions.SeekAsync(app, TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 86400))).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is TimeoutException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log.Warn("Web: no se pudo controlar lo que suena: " + ex.Message);
        }
        // The player takes a moment to say its new state.
        if (done) await Task.Delay(250).ConfigureAwait(false);
        await WriteJsonAsync(context.Response, new { done, state = await MediaStateAsync().ConfigureAwait(false) }).ConfigureAwait(false);
    }

    /// <summary>GET api/media/art?app=…: the cover of what that program plays now.</summary>
    private static async Task MediaArtAsync(HttpListenerContext context)
    {
        var app = context.Request.QueryString["app"];
        var art = string.IsNullOrEmpty(app) ? null : await MediaSessions.ArtAsync(app).ConfigureAwait(false);
        if (art is not var (bytes, type))
        {
            await WriteJsonAsync(context.Response, new { error = "notFound" }, 404).ConfigureAwait(false);
            return;
        }
        var response = context.Response;
        response.ContentType = type;
        // The address carries the key of the track (k): the same address is the same cover.
        response.Headers["Cache-Control"] = "private, max-age=86400";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
    }

    /// <summary>With the first open app, starts watching what plays (once; it costs nothing while nothing changes).</summary>
    private void WatchMedia()
    {
        lock (_mediaLock)
        {
            if (_mediaWatch is not null || _mediaStarting || State != WebServerState.Running || NoMediaWatch) return;
            _mediaStarting = true;
        }
        _ = Task.Run(async () =>
        {
            IDisposable? watch = null;
            try { watch = await MediaSessions.WatchAsync(MediaChanged).ConfigureAwait(false); }
            catch (Exception ex) { Log.Warn("Web: no se puede seguir qué suena: " + ex.Message); }
            lock (_mediaLock)
            {
                _mediaStarting = false;
                if (State == WebServerState.Running) _mediaWatch = watch;
                else watch?.Dispose();
            }
        });
    }

    /// <summary>Tests: no media watcher (Windows' media controls are not there for them).</summary>
    internal bool NoMediaWatch { get; set; }

    /// <summary>Changes come in bursts (title, then artist, then cover…): one event once they calm down.</summary>
    private void MediaChanged()
    {
        lock (_mediaLock)
        {
            _mediaTimer ??= new Timer(_ => Broadcast("media"));
            _mediaTimer.Change(MediaQuiet, Timeout.InfiniteTimeSpan);
        }
    }

    private void StopMediaWatch()
    {
        lock (_mediaLock)
        {
            _mediaWatch?.Dispose();
            _mediaWatch = null;
            _mediaTimer?.Dispose();
            _mediaTimer = null;
        }
    }
}

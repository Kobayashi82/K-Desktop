using System.Diagnostics;
using Windows.Media.Control;

namespace KDesktop.Core.Platform;

public enum MediaState { Playing, Paused, Stopped, Other }

public enum MediaOp { Play, Pause, Toggle, Next, Previous, Stop }

/// <summary>What one program is playing (or has paused), as Windows' media controls know it.</summary>
/// <param name="App">Its name for people («Spotify», «Firefox»).</param>
/// <param name="AppId">How Windows names it (an app id or «Spotify.exe»).</param>
/// <param name="Current">The one Windows' media keys would act on now.</param>
/// <param name="CanPrevious">Its player has a previous track now (the same for next and for moving in the track).</param>
/// <param name="HasArt">It has a cover (<see cref="MediaSessions.ArtAsync"/>).</param>
public sealed record MediaNow(string App, string AppId, string? Title, string? Artist, string? Album, MediaState State,
    TimeSpan? Position, TimeSpan? Length, bool Current, bool CanPrevious = true, bool CanNext = true, bool CanSeek = false,
    bool HasArt = false);

/// <summary>
/// Stage 10.2: what is playing and controlling it, with the media controls of Windows (SMTC, the same that the media
/// keys and the volume flyout use): every program that tells Windows what it plays (Spotify, browsers, AIMP, the Media
/// Player…). Thread-safe; nothing is kept between calls.
/// </summary>
public static class MediaSessions
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);

    /// <summary>The programs with something to play now: the ones playing first, then the current one.</summary>
    public static async Task<IReadOnlyList<MediaNow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var (manager, sessions) = await SessionsAsync(cancellationToken).ConfigureAwait(false);
        var current = manager.GetCurrentSession()?.SourceAppUserModelId;
        var list = new List<MediaNow>();
        foreach (var session in sessions)
            if (await DescribeAsync(session, session.SourceAppUserModelId == current, cancellationToken).ConfigureAwait(false) is { } now) list.Add(now);
        return [.. list.OrderBy(m => m.State == MediaState.Playing ? 0 : 1).ThenBy(m => m.Current ? 0 : 1)];
    }

    /// <summary>
    /// Plays, pauses, skips… in <paramref name="app"/> (said by the user: «spotify», «el navegador»), or without it: a pause
    /// or stop in every program playing, the rest in the one playing (else the current one). Returns the ones it acted on
    /// (empty: nothing to act on, or no program by that name).
    /// </summary>
    public static async Task<IReadOnlyList<MediaNow>> ControlAsync(MediaOp op, string? app, CancellationToken cancellationToken = default)
    {
        var (manager, sessions) = await SessionsAsync(cancellationToken).ConfigureAwait(false);
        var current = manager.GetCurrentSession()?.SourceAppUserModelId;
        var known = new List<Known>();
        foreach (var session in sessions)
            if (await DescribeAsync(session, session.SourceAppUserModelId == current, cancellationToken).ConfigureAwait(false) is { } now) known.Add(new Known(session, now));

        List<Known> targets;
        if (!string.IsNullOrWhiteSpace(app))
        {
            targets = AppFinder.Best(app, known, k => [k.Now.App, k.Now.AppId]) is { } named ? [named] : [];
        }
        else if (op is MediaOp.Pause or MediaOp.Stop) targets = [.. known.Where(k => k.Now.State == MediaState.Playing)];
        else
        {
            var one = known.FirstOrDefault(k => k.Now.State == MediaState.Playing) ?? known.FirstOrDefault(k => k.Now.Current) ?? known.FirstOrDefault();
            targets = one is null ? [] : [one];
        }

        var done = new List<MediaNow>();
        foreach (var (session, now) in targets)
        {
            var asked = op switch
            {
                MediaOp.Play => session.TryPlayAsync(),
                MediaOp.Pause => session.TryPauseAsync(),
                MediaOp.Toggle => session.TryTogglePlayPauseAsync(),
                MediaOp.Next => session.TrySkipNextAsync(),
                MediaOp.Previous => session.TrySkipPreviousAsync(),
                _ => session.TryStopAsync(),
            };
            try
            {
                if (await asked.AsTask(cancellationToken).WaitAsync(Wait, cancellationToken).ConfigureAwait(false)) done.Add(now);
                else Log.Info($"Multimedia: {now.App} no ha aceptado «{op}»");
            }
            catch (TimeoutException) { Log.Warn($"Multimedia: {now.App} no contesta a «{op}»"); }
        }
        return done;
    }

    /// <summary>Plays, pauses, skips… in exactly the program <paramref name="appId"/>; false when it is not there or refused.</summary>
    public static async Task<bool> ControlAppAsync(string appId, MediaOp op, CancellationToken cancellationToken = default)
    {
        if (await SessionOfAsync(appId, cancellationToken).ConfigureAwait(false) is not { } session) return false;
        var asked = op switch
        {
            MediaOp.Play => session.TryPlayAsync(),
            MediaOp.Pause => session.TryPauseAsync(),
            MediaOp.Toggle => session.TryTogglePlayPauseAsync(),
            MediaOp.Next => session.TrySkipNextAsync(),
            MediaOp.Previous => session.TrySkipPreviousAsync(),
            _ => session.TryStopAsync(),
        };
        try { return await asked.AsTask(cancellationToken).WaitAsync(Wait, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { return false; }
    }

    /// <summary>Moves to <paramref name="position"/> of what <paramref name="appId"/> plays; false when its player cannot.</summary>
    public static async Task<bool> SeekAsync(string appId, TimeSpan position, CancellationToken cancellationToken = default)
    {
        if (await SessionOfAsync(appId, cancellationToken).ConfigureAwait(false) is not { } session) return false;
        if (session.GetTimelineProperties() is { } timeline) position += timeline.StartTime;
        try
        {
            return await session.TryChangePlaybackPositionAsync(Math.Max(0, position.Ticks)).AsTask(cancellationToken)
                .WaitAsync(Wait, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException) { return false; }
    }

    /// <summary>The cover of what <paramref name="appId"/> plays (the bytes and their type), or null.</summary>
    public static async Task<(byte[] Bytes, string ContentType)?> ArtAsync(string appId, CancellationToken cancellationToken = default)
    {
        if (await SessionOfAsync(appId, cancellationToken).ConfigureAwait(false) is not { } session) return null;
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken).WaitAsync(Wait, cancellationToken).ConfigureAwait(false);
            if (properties?.Thumbnail is not { } thumbnail) return null;
            using var stream = await thumbnail.OpenReadAsync().AsTask(cancellationToken).WaitAsync(Wait, cancellationToken).ConfigureAwait(false);
            if (stream.Size is 0 or > MaxArt) return null;
            using var reader = new Windows.Storage.Streams.DataReader(stream);
            await reader.LoadAsync((uint)stream.Size).AsTask(cancellationToken).WaitAsync(Wait, cancellationToken).ConfigureAwait(false);
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);
            return (bytes, string.IsNullOrEmpty(stream.ContentType) ? "image/png" : stream.ContentType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Info($"Multimedia: sin carátula de {appId} ({ex.Message})");
            return null;
        }
    }

    private const int MaxArt = 4 * 1024 * 1024;

    private static async Task<GlobalSystemMediaTransportControlsSession?> SessionOfAsync(string appId, CancellationToken cancellationToken)
    {
        var (_, sessions) = await SessionsAsync(cancellationToken).ConfigureAwait(false);
        return sessions.FirstOrDefault(s => string.Equals(s.SourceAppUserModelId, appId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Calls <paramref name="changed"/> whenever a program starts or stops playing, changes track or pauses (not for the
    /// position moving on). Dispose to stop.
    /// </summary>
    public static async Task<IDisposable> WatchAsync(Action changed, CancellationToken cancellationToken = default)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken).WaitAsync(Wait, cancellationToken).ConfigureAwait(false);
        return new Watcher(manager, changed);
    }

    private sealed class Watcher : IDisposable
    {
        private readonly GlobalSystemMediaTransportControlsSessionManager _manager;
        private readonly Action _changed;
        private readonly Lock _lock = new();
        private List<GlobalSystemMediaTransportControlsSession> _sessions = [];
        private bool _disposed;

        public Watcher(GlobalSystemMediaTransportControlsSessionManager manager, Action changed)
        {
            _manager = manager;
            _changed = changed;
            manager.SessionsChanged += OnSessions;
            manager.CurrentSessionChanged += OnCurrent;
            Hook();
        }

        private void Hook()
        {
            lock (_lock)
            {
                Unhook();
                if (_disposed) return;
                _sessions = [.. _manager.GetSessions()];
                foreach (var s in _sessions)
                {
                    s.MediaPropertiesChanged += OnMedia;
                    s.PlaybackInfoChanged += OnPlayback;
                }
            }
        }

        private void Unhook()
        {
            foreach (var s in _sessions)
            {
                s.MediaPropertiesChanged -= OnMedia;
                s.PlaybackInfoChanged -= OnPlayback;
            }
            _sessions = [];
        }

        private void OnSessions(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        {
            Hook();
            Raise();
        }

        private void OnCurrent(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => Raise();

        private void OnMedia(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => Raise();

        private void OnPlayback(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => Raise();

        private void Raise()
        {
            if (_disposed) return;
            try { _changed(); }
            catch (Exception ex) { Log.Warn("Multimedia: " + ex.Message); }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _disposed = true;
                _manager.SessionsChanged -= OnSessions;
                _manager.CurrentSessionChanged -= OnCurrent;
                Unhook();
            }
        }
    }

    private sealed record Known(GlobalSystemMediaTransportControlsSession Session, MediaNow Now);

    private static async Task<(GlobalSystemMediaTransportControlsSessionManager, IReadOnlyList<GlobalSystemMediaTransportControlsSession>)> SessionsAsync(CancellationToken cancellationToken)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancellationToken).WaitAsync(Wait, cancellationToken).ConfigureAwait(false);
        return (manager, [.. manager.GetSessions()]);
    }

    private static async Task<MediaNow?> DescribeAsync(GlobalSystemMediaTransportControlsSession session, bool current, CancellationToken cancellationToken)
    {
        try
        {
            var id = session.SourceAppUserModelId ?? string.Empty;
            var playback = session.GetPlaybackInfo();
            var state = playback?.PlaybackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaState.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaState.Paused,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => MediaState.Stopped,
                _ => MediaState.Other,
            };
            string? title = null, artist = null, album = null;
            var hasArt = false;
            try
            {
                // Some programs never answer this: a moment at most.
                var properties = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken).WaitAsync(Wait, cancellationToken).ConfigureAwait(false);
                title = Clean(properties?.Title);
                artist = Clean(properties?.Artist) ?? Clean(properties?.AlbumArtist);
                album = Clean(properties?.AlbumTitle);
                hasArt = properties?.Thumbnail is not null;
            }
            catch (TimeoutException) { }
            TimeSpan? position = null, length = null;
            if (session.GetTimelineProperties() is { } timeline && timeline.EndTime > TimeSpan.Zero)
            {
                length = timeline.EndTime - timeline.StartTime;
                var at = timeline.Position;
                // The position is as of its last update: playing, it went on since.
                if (state == MediaState.Playing && timeline.LastUpdatedTime > DateTimeOffset.MinValue)
                    at += DateTimeOffset.Now - timeline.LastUpdatedTime;
                position = at < TimeSpan.Zero ? TimeSpan.Zero : at > length ? length : at;
            }
            var controls = playback?.Controls;
            return new MediaNow(NameOf(id), id, title, artist, album, state, position, length, current,
                controls?.IsPreviousEnabled ?? true, controls?.IsNextEnabled ?? true,
                controls?.IsPlaybackPositionEnabled == true && length is not null, hasArt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Multimedia: no se ha podido leer una sesión ({ex.Message})");
            return null;
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// The name of a program for people: the Start menu's for an app id («Chrome», «MSEdge», Firefox's code, Store apps);
    /// for «Spotify.exe», what that program says it is; else the id without «.exe».
    /// </summary>
    public static string NameOf(string appId)
    {
        if (appId.Length == 0) return "?";
        InstalledApps.Refresh();
        if (InstalledApps.Current.FirstOrDefault(a => a.Id.Equals(appId, StringComparison.OrdinalIgnoreCase)) is { } app) return app.Name;
        var exe = appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? appId[..^4] : null;
        if (exe is not null)
        {
            var processes = Process.GetProcessesByName(exe);
            try
            {
                foreach (var process in processes)
                    if (AppFinder.DescriptionOf((uint)process.Id) is { Length: > 0 } description) return description;
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
            return exe;
        }
        // A Store app without its entry: «Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic» → «ZuneMusic».
        var package = appId.Split('!')[0].Split('_')[0];
        return package.Contains('.', StringComparison.Ordinal) ? package[(package.LastIndexOf('.') + 1)..] : package;
    }
}

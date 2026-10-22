using System.Diagnostics;
using Windows.Media.Control;

namespace KDesktop.Core.Platform;

public enum MediaState { Playing, Paused, Stopped, Other }

public enum MediaOp { Play, Pause, Toggle, Next, Previous, Stop }

/// <summary>What one program is playing (or has paused), as Windows' media controls know it.</summary>
/// <param name="App">Its name for people («Spotify», «Firefox»).</param>
/// <param name="AppId">How Windows names it (an app id or «Spotify.exe»).</param>
/// <param name="Current">The one Windows' media keys would act on now.</param>
public sealed record MediaNow(string App, string AppId, string? Title, string? Artist, string? Album, MediaState State,
    TimeSpan? Position, TimeSpan? Length, bool Current);

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
            try
            {
                // Some programs never answer this: a moment at most.
                var properties = await session.TryGetMediaPropertiesAsync().AsTask(cancellationToken).WaitAsync(Wait, cancellationToken).ConfigureAwait(false);
                title = Clean(properties?.Title);
                artist = Clean(properties?.Artist) ?? Clean(properties?.AlbumArtist);
                album = Clean(properties?.AlbumTitle);
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
            return new MediaNow(NameOf(id), id, title, artist, album, state, position, length, current);
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

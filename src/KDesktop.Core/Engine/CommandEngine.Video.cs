using System.Globalization;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Engine;

/// <summary>«Grabar vídeo» (F2): one recording at a time, with its bar (time, pause, stop) and the tray to stop it.</summary>
public sealed partial class CommandEngine
{
    private readonly Lock _videoLock = new();
    private volatile ScreenRecorder? _video;
    private IDisposable? _videoBar;
    private Task<(string Path, TimeSpan Length)?>? _videoStopping;

    /// <summary>A video recording started or stopped (the tray shows it).</summary>
    public event Action? VideoChanged;

    public bool IsRecordingVideo => _video is not null;

    private async Task VideoRecordAsync(VideoRecordAction a, ExecutionContext ctx)
    {
        Gave(a, string.Empty);
        var mode = a.Mode == RecordMode.Toggle ? (IsRecordingVideo ? RecordMode.Stop : RecordMode.Start) : a.Mode;
        if (mode == RecordMode.Stop)
        {
            if (await StopVideoAsync(a.ShowOsd).ConfigureAwait(false) is { } done) Gave(a, done.Path);
            return;
        }
        if (IsRecordingVideo) return;
        if (!ScreenRecorder.IsSupported)
        {
            _ui.ShowOsd(Loc.T("osd.videoUnsupported"), OsdKind.Warning, look: OsdLook.Of("Video24"));
            throw new OperationCanceledException();
        }
        if (await VideoTargetAsync(a.Area, ctx).ConfigureAwait(false) is not { } target) throw new OperationCanceledException(); // cancelled

        var s = Config.Settings;
        var sound = a.Sound switch
        {
            VideoSound.Output => AudioSource.Output,
            VideoSound.Microphone => AudioSource.Microphone,
            VideoSound.Both => AudioSource.Both,
            _ => (AudioSource?)null,
        };
        var options = new VideoOptions(s.VideoFps, s.VideoQuality, s.VideoCursor, sound, RecordOptions.MicGain, s.VideoClicks);
        var front = ctx.Foreground.ProcessId == (uint)Environment.ProcessId ? ForegroundWindow.None : ctx.Foreground;
        var path = NewFileNamed(FolderFor(a.Folder, s.VideoFolder, Environment.SpecialFolder.MyVideos), FileName(s.VideoName, DefaultVideoName, front, DateTime.Now), ".mp4");
        ScreenRecorder recorder;
        try
        {
            recorder = await Task.Run(() => ScreenRecorder.Start(target, path, options)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or SharpGen.Runtime.SharpGenException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Log.Error("Grabación de vídeo: no ha podido empezar", ex);
            _ui.ShowOsd(Loc.T("osd.videoFailed"), OsdKind.Warning, look: OsdLook.Of("Video24"));
            throw new OperationCanceledException();
        }
        lock (_videoLock) _video = recorder;
        recorder.Failed += error =>
        {
            _ui.ShowOsd(Loc.T("osd.videoFailed"), OsdKind.Warning, look: OsdLook.Of("Video24"));
            _ = StopVideoAsync(showOsd: false);
        };
        var size = string.Create(CultureInfo.InvariantCulture, $"{target.Area.Width}×{target.Area.Height}");
        _videoBar = _ui.ShowRecordingBar(() => size, () => _ = StopVideoAsync(showOsd: true), recorder.Pause);
        VideoChanged?.Invoke();
    }

    /// <summary>A physical click (from the mouse hook): marked on the video while one is recording.</summary>
    public void VideoClick(MouseInput e) => _video?.AddClick(e.X, e.Y, e.Button);

    /// <summary>What to record: the region chosen with the mouse, the window in front or the monitor of the mouse; null when cancelled.</summary>
    private async Task<VideoTarget?> VideoTargetAsync(CaptureArea area, ExecutionContext ctx)
    {
        if (ctx.FromPopup) await Task.Delay(250, ctx.Token).ConfigureAwait(false);
        switch (area)
        {
            case CaptureArea.ActiveWindow when ctx.Foreground.RootHandle != IntPtr.Zero && ctx.Foreground.ProcessId != (uint)Environment.ProcessId:
                return new VideoTarget(CaptureArea.ActiveWindow, WindowLayout.VisibleBounds(ctx.Foreground.RootHandle), ctx.Foreground.RootHandle);
            case CaptureArea.Region:
                var desktop = ScreenCapture.VirtualScreen;
                var image = await Task.Run(() => ScreenCapture.Capture(desktop), ctx.Token).ConfigureAwait(false);
                if (await _ui.SelectRegionAsync(image, desktop).ConfigureAwait(false) is not { } region || region.Width < 16 || region.Height < 16) return null;
                // A video is of one monitor: the part of the region on the monitor that has most of it.
                var monitor = ScreenCapture.MonitorAt(region.CenterX, region.Top + region.Height / 2);
                var left = Math.Max(region.Left, monitor.Left);
                var top = Math.Max(region.Top, monitor.Top);
                var right = Math.Min(region.Right, monitor.Right);
                var bottom = Math.Min(region.Bottom, monitor.Bottom);
                return right - left < 16 || bottom - top < 16 ? null : new VideoTarget(CaptureArea.Region, new ScreenRect(left, top, right - left, bottom - top));
            default:
                return new VideoTarget(CaptureArea.Monitor, ScreenCapture.MonitorAtCursor());
        }
    }

    /// <summary>Stops the recording (the bar, the tray, the same hotkey); the file and its length, null when none was recording.</summary>
    public Task<(string Path, TimeSpan Length)?> StopVideoAsync(bool showOsd = true)
    {
        lock (_videoLock)
        {
            if (_videoStopping is not null) return _videoStopping;
            if (_video is not { } recorder) return Task.FromResult<(string, TimeSpan)?>(null);
            return _videoStopping = StopAsync(recorder);
        }

        async Task<(string Path, TimeSpan Length)?> StopAsync(ScreenRecorder recorder)
        {
            _videoBar?.Dispose();
            _videoBar = null;
            TimeSpan length;
            try { length = await recorder.StopAsync().ConfigureAwait(false); }
            finally
            {
                lock (_videoLock)
                {
                    _video = null;
                    _videoStopping = null;
                }
                VideoChanged?.Invoke();
            }
            var saved = File.Exists(recorder.Path) && new FileInfo(recorder.Path).Length > 0;
            if (!saved)
            {
                _ui.ShowOsd(Loc.T("osd.videoFailed"), OsdKind.Warning, look: OsdLook.Of("Video24"));
                return null;
            }
            if (showOsd)
            {
                var shown = length.ToString(length.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);
                _ui.ShowOsd(Loc.T("osd.videoSaved", Path.GetFileName(recorder.Path), shown), OsdKind.Success, look: OsdLook.Of("Video24"));
            }
            return (recorder.Path, length);
        }
    }
}

using System.Globalization;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Engine;

/// <summary>Screenshots, audio recordings and the zoom.</summary>
public sealed partial class CommandEngine
{
    /// <summary>The folder of an action, or the one of the options, or the default one (…\K-Desktop).</summary>
    public static string FolderFor(string? own, string? option, Environment.SpecialFolder fallback)
    {
        var chosen = !string.IsNullOrWhiteSpace(own) ? own : option;
        return !string.IsNullOrWhiteSpace(chosen)
            ? Environment.ExpandEnvironmentVariables(chosen.Trim().Trim('"'))
            : Path.Combine(Environment.GetFolderPath(fallback), "K-Desktop");
    }

    /// <summary>«Captura 2026-10-16 23.40.12.png» (with a number if it already exists).</summary>
    private static string NewFile(string folder, string prefix, string extension)
    {
        Directory.CreateDirectory(folder);
        var name = $"{prefix} {DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture)}";
        var path = Path.Combine(folder, name + extension);
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{name} ({i}){extension}");
        return path;
    }

    // ── Captura de pantalla ──────────────────────────────────────────────

    private async Task ScreenshotAsync(ScreenshotAction a, ExecutionContext ctx)
    {
        // From the QuickMenu or the launcher: let them go away first.
        if (ctx.FromPopup) await Task.Delay(250, ctx.Token).ConfigureAwait(false);

        var desktop = ScreenCapture.VirtualScreen;
        var area = a.Area switch
        {
            CaptureArea.Monitor => ScreenCapture.MonitorAtCursor(),
            CaptureArea.ActiveWindow when ctx.Foreground.RootHandle != IntPtr.Zero && ctx.Foreground.ProcessId != (uint)Environment.ProcessId
                => Intersect(WindowLayout.VisibleBounds(ctx.Foreground.RootHandle), desktop),
            CaptureArea.ActiveWindow => ScreenCapture.MonitorAtCursor(),
            _ => desktop,
        };
        var image = await Task.Run(() => ScreenCapture.Capture(area), ctx.Token).ConfigureAwait(false);

        if (a.Area == CaptureArea.Region)
        {
            if (await _ui.SelectRegionAsync(image, area).ConfigureAwait(false) is not { } region || region.Width < 2 || region.Height < 2)
                return; // cancelled
            image = image.Crop(region.Left - area.Left, region.Top - area.Top, region.Width, region.Height);
        }

        var copied = a.Copy && _ui.SetClipboardImage(image);
        string? path = null;
        if (a.Save || a.Open)
        {
            path = NewFile(FolderFor(a.Folder, Config.Settings.CaptureFolder, Environment.SpecialFolder.MyPictures), Loc.T("capture.filePrefix"), ".png");
            var png = await Task.Run(() => Png.Encode(image), ctx.Token).ConfigureAwait(false);
            await File.WriteAllBytesAsync(path, png, ctx.Token).ConfigureAwait(false);
            Log.Info($"Captura: {image.Width}×{image.Height} → {path}");
        }
        if (a.Open && path is not null) ProcessLauncher.Launch(new LaunchSpec(path), asAdmin: false);
        if (!a.ShowOsd) return;
        var size = string.Create(CultureInfo.InvariantCulture, $"{image.Width}×{image.Height}");
        var text = path is not null && a.Save
            ? Loc.T(copied ? "osd.captureSavedCopied" : "osd.captureSaved", Path.GetFileName(path), size)
            : copied ? Loc.T("osd.captureCopied", size) : null;
        if (text is not null) _ui.ShowOsd(text, OsdKind.Success);
    }

    private static ScreenRect Intersect(ScreenRect a, ScreenRect b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        return right > left && bottom > top ? new ScreenRect(left, top, right - left, bottom - top) : b;
    }

    // ── Grabar audio ─────────────────────────────────────────────────────

    private void AudioRecord(AudioRecordAction a)
    {
        var mode = a.Mode == RecordMode.Toggle ? (Recording.IsRecording ? RecordMode.Stop : RecordMode.Start) : a.Mode;
        var folder = FolderFor(a.Folder, Config.Settings.RecordFolder, Environment.SpecialFolder.MyMusic);
        switch (mode)
        {
            case RecordMode.Start:
                if (Recording.IsRecording) return;
                Recording.Start(a.Source, NewFile(folder, Loc.T("record.filePrefix"), ".wav"));
                if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.recordStarted", Loc.T($"enum.audioSource.{a.Source}")), OsdKind.Warning);
                break;
            case RecordMode.Stop:
                if (Recording.Stop() is not { } done) return;
                if (a.ShowOsd)
                {
                    var length = done.Length.ToString(done.Length.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);
                    _ui.ShowOsd(Loc.T("osd.recordSaved", Path.GetFileName(done.Path), length), OsdKind.Success);
                }
                break;
            case RecordMode.SaveReplay:
                var seconds = Config.Settings.AudioReplaySeconds;
                if (!Recording.SaveReplay(NewFile(folder, Loc.T("record.replayPrefix"), ".wav")))
                {
                    Log.Warn("Repetición de audio: está apagada (Opciones › Capturas)");
                    if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.replayOff"), OsdKind.Warning);
                }
                else if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.replaySaved", seconds), OsdKind.Success);
                break;
        }
    }

    // ── Lupa ─────────────────────────────────────────────────────────────

    private void Magnify(MagnifierAction a)
    {
        var zoom = a.Zoom > 1 ? a.Zoom : Config.Settings.ZoomLevel;
        switch (a.Mode)
        {
            case SwitchMode.On: Magnifier.On(zoom); break;
            case SwitchMode.Off: Magnifier.Off(); break;
            default: Magnifier.Toggle(zoom); break;
        }
    }
}

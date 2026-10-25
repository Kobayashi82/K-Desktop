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
        if (await CaptureAsync(a.Area, ctx).ConfigureAwait(false) is not { } image) return; // cancelled

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
        if (text is not null) _ui.ShowOsd(text, OsdKind.Success, look: OsdLook.Of("Screenshot24"));
    }

    /// <summary>
    /// What the screen shows in <paramref name="what"/>; a region is chosen with the mouse (null: cancelled).
    /// <paramref name="inside"/>: of the active window, only what is inside (to read it, without its title or buttons).
    /// </summary>
    private async Task<BgraImage?> CaptureAsync(CaptureArea what, ExecutionContext ctx, bool inside = false)
    {
        // From the QuickMenu or the launcher: let them go away first.
        if (ctx.FromPopup) await Task.Delay(250, ctx.Token).ConfigureAwait(false);

        var desktop = ScreenCapture.VirtualScreen;
        var area = what switch
        {
            CaptureArea.Monitor => ScreenCapture.MonitorAtCursor(),
            CaptureArea.ActiveWindow when ctx.Foreground.RootHandle != IntPtr.Zero && ctx.Foreground.ProcessId != (uint)Environment.ProcessId
                => Intersect(inside ? WindowLayout.ClientBounds(ctx.Foreground.RootHandle) : WindowLayout.VisibleBounds(ctx.Foreground.RootHandle), desktop),
            CaptureArea.ActiveWindow => ScreenCapture.MonitorAtCursor(),
            _ => desktop,
        };
        var image = await Task.Run(() => ScreenCapture.Capture(area), ctx.Token).ConfigureAwait(false);
        if (what != CaptureArea.Region) return image;
        if (await _ui.SelectRegionAsync(image, area).ConfigureAwait(false) is not { } region || region.Width < 2 || region.Height < 2)
            return null;
        return image.Crop(region.Left - area.Left, region.Top - area.Top, region.Width, region.Height);
    }

    // ── Copiar texto de la pantalla ──────────────────────────────────────

    private async Task ScreenTextAsync(ScreenTextAction a, ExecutionContext ctx)
    {
        if (!ScreenText.Available)
        {
            Log.Warn("Texto de la pantalla: Windows no tiene ningún idioma con reconocimiento de texto (OCR)");
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.screenTextNoOcr"), OsdKind.Warning, look: OsdLook.Of("ScanText24"));
            return;
        }
        if (await CaptureAsync(a.Area, ctx, inside: true).ConfigureAwait(false) is not { } image) return; // cancelled

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var language = a.Language.Length > 0 ? a.Language : Loc.Instance.Language;
        if (await ScreenText.ReadAsync(image, language, a.SingleLine, ctx.Token).ConfigureAwait(false) is not { } read) return;
        Log.Info($"Texto de la pantalla: {image.Width}×{image.Height}, {read.Lines} líneas, {read.Text.Length} letras ({read.Language}) en {watch.ElapsedMilliseconds} ms");
        if (read.Text.Length == 0)
        {
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.screenTextNone"), OsdKind.Warning, look: OsdLook.Of("ScanText24"));
            return;
        }
        var copied = _ui.SetClipboardText(read.Text);
        if (!a.ShowOsd) return;
        _ui.ShowOsd(copied ? Loc.T(read.Lines == 1 ? "osd.screenTextCopiedOne" : "osd.screenTextCopied", read.Lines, Preview(read.Text)) : Loc.T("osd.screenTextNotCopied"),
            copied ? OsdKind.Success : OsdKind.Warning, look: OsdLook.Of("ScanText24"));
    }

    /// <summary>The beginning of what was read, on one line, for the OSD.</summary>
    private static string Preview(string text)
    {
        var flat = string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return flat.Length <= 90 ? flat : flat[..90].TrimEnd() + "…";
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
                if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.recordStarted", Loc.T($"enum.audioSource.{a.Source}")), OsdKind.Warning, look: OsdLook.Of("MicPulse24"));
                break;
            case RecordMode.Stop:
                if (Recording.Stop() is not { } done) return;
                if (a.ShowOsd)
                {
                    var length = done.Length.ToString(done.Length.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);
                    _ui.ShowOsd(Loc.T("osd.recordSaved", Path.GetFileName(done.Path), length), OsdKind.Success, look: OsdLook.Of("MicPulse24"));
                }
                break;
            case RecordMode.SaveReplay:
                var seconds = Config.Settings.AudioReplaySeconds;
                if (!Recording.SaveReplay(NewFile(folder, Loc.T("record.replayPrefix"), ".wav")))
                {
                    Log.Warn("Repetición de audio: está apagada (Opciones › Capturas)");
                    if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.replayOff"), OsdKind.Warning, look: OsdLook.Of("History24"));
                }
                else if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.replaySaved", seconds), OsdKind.Success, look: OsdLook.Of("History24"));
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

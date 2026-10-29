using System.Diagnostics;
using System.Globalization;
using KDesktop.Core.Imaging;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Voice;

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
    private static string NewFile(string folder, string prefix, string extension, string? finalExtension = null) =>
        NewFileNamed(folder, $"{prefix} {DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture)}", extension, finalExtension);

    /// <summary>
    /// <paramref name="name"/> in <paramref name="folder"/>, with a number if it already exists — also when it exists with
    /// <paramref name="finalExtension"/>, the one it will have (a recording that becomes an MP3).
    /// </summary>
    private static string NewFileNamed(string folder, string name, string extension, string? finalExtension = null)
    {
        Directory.CreateDirectory(folder);
        bool Taken(string path) => File.Exists(path) || (finalExtension is not null && File.Exists(Path.ChangeExtension(path, finalExtension)));
        var path = Path.Combine(folder, name + extension);
        for (var i = 2; Taken(path); i++) path = Path.Combine(folder, $"{name} ({i}){extension}");
        return path;
    }

    /// <summary>
    /// The name of a screenshot from the template of Settings › Capturas: {date}, {time}, {app} (the program in front) and
    /// {title} (its window's title), without what a file name can't have.
    /// </summary>
    public static string CaptureName(string? template, ForegroundWindow front, DateTime when) =>
        CaptureName(template, () => AppName(front), () => front.Title, when);

    /// <summary>The same with the program and title given (asked only if the template has them).</summary>
    public static string CaptureName(string? template, Func<string> app, Func<string> title, DateTime when)
    {
        if (string.IsNullOrWhiteSpace(template)) template = DefaultCaptureName;
        var name = template
            .Replace("{date}", when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", when.ToString("HH.mm.ss", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        if (name.Contains("{app}", StringComparison.OrdinalIgnoreCase)) name = name.Replace("{app}", app(), StringComparison.OrdinalIgnoreCase);
        if (name.Contains("{title}", StringComparison.OrdinalIgnoreCase)) name = name.Replace("{title}", title(), StringComparison.OrdinalIgnoreCase);
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? ' ' : c).ToArray());
        clean = string.Join(' ', clean.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd('.');
        if (clean.Length > 120) clean = clean[..120].TrimEnd();
        return clean.Length > 0 ? clean : CaptureName(DefaultCaptureName, app, title, when);
    }

    /// <summary>«Captura {date} {time}» in K-Desktop's language.</summary>
    public static string DefaultCaptureName => Loc.T("capture.filePrefix") + " {date} {time}";

    /// <summary>The program's own name («Google Chrome»), else its process's («Chrome»); nothing without a window in front.</summary>
    private static string AppName(ForegroundWindow front)
    {
        if (front.ProcessId == 0) return string.Empty;
        try
        {
            if (Native.Win32.GetProcessPath(front.ProcessId) is { } path
                && FileVersionInfo.GetVersionInfo(path).FileDescription is { Length: > 0 } description)
                return description.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* its own name below */ }
        var process = front.ProcessName;
        return process.Length > 0 ? char.ToUpperInvariant(process[0]) + process[1..] : string.Empty;
    }

    // ── Captura de pantalla ──────────────────────────────────────────────

    private async Task ScreenshotAsync(ScreenshotAction a, ExecutionContext ctx)
    {
        var s = Config.Settings;
        // Time to open a menu or move the mouse first.
        if (s.CaptureDelay > 0) await Task.Delay(TimeSpan.FromSeconds(Math.Min(s.CaptureDelay, 10)), ctx.Token).ConfigureAwait(false);
        if (await CaptureAsync(a.Area, ctx, pointer: s.CaptureCursor) is not { } shot) return; // cancelled
        var (image, area) = shot;
        if (s.CaptureSound) Sounds.Play("Camera");
        if (s.CaptureFlash) _ui.FlashArea(area);

        var copied = (a.Copy || s.CaptureAlwaysCopy) && _ui.SetClipboardImage(image);
        string? path = null;
        if (a.Save || a.Open)
        {
            var front = ctx.Foreground.ProcessId == (uint)Environment.ProcessId ? ForegroundWindow.None : ctx.Foreground;
            path = NewFileNamed(FolderFor(a.Folder, s.CaptureFolder, Environment.SpecialFolder.MyPictures),
                CaptureName(s.CaptureName, front, DateTime.Now), ImageFiles.ExtensionFor(image, s.CaptureFormat));
            var bytes = await ImageFiles.EncodeAsync(image, s.CaptureFormat, s.CaptureQuality, ctx.Token).ConfigureAwait(false);
            await File.WriteAllBytesAsync(path, bytes, ctx.Token).ConfigureAwait(false);
            Log.Info($"Captura: {image.Width}×{image.Height} → {path} ({bytes.Length / 1024} KB)");
        }
        Gave(a, path ?? string.Empty);
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
    private async Task<BgraImage?> CaptureAsync(CaptureArea what, ExecutionContext ctx, bool inside = false) =>
        (await CaptureAsync(what, ctx, pointer: false, inside).ConfigureAwait(false))?.Image;

    /// <summary>The same, with the part of the screen it is and, if <paramref name="pointer"/>, the mouse pointer drawn on it.</summary>
    private async Task<(BgraImage Image, ScreenRect Area)?> CaptureAsync(CaptureArea what, ExecutionContext ctx, bool pointer, bool inside = false)
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
        // The pointer as it was when the picture was taken (a region's frozen screen is shown without it).
        using var cursor = pointer ? ScreenCapture.CurrentPointer() : null;
        var image = await Task.Run(() => ScreenCapture.Capture(area), ctx.Token).ConfigureAwait(false);
        if (what == CaptureArea.Region)
        {
            if (await _ui.SelectRegionAsync(image, area).ConfigureAwait(false) is not { } region || region.Width < 2 || region.Height < 2)
                return null;
            image = image.Crop(region.Left - area.Left, region.Top - area.Top, region.Width, region.Height);
            area = new ScreenRect(region.Left, region.Top, image.Width, image.Height);
        }
        cursor?.DrawOn(image, area);
        return (image, area);
    }

    // ── Copiar texto de la pantalla ──────────────────────────────────────

    /// <summary>
    /// «Copiar texto de la pantalla». The region left with Esc, or no text read (5.0.374): the rest of the command, which
    /// would work on that text («Traducir la pantalla»), is not run.
    /// </summary>
    private async Task ScreenTextAsync(ScreenTextAction a, ExecutionContext ctx)
    {
        if (!ScreenText.Available)
        {
            Log.Warn("Texto de la pantalla: Windows no tiene ningún idioma con reconocimiento de texto (OCR)");
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.screenTextNoOcr"), OsdKind.Warning, look: OsdLook.Of("ScanText24"));
            return;
        }
        Gave(a, string.Empty); // until it reads something
        if (await CaptureAsync(a.Area, ctx, inside: true).ConfigureAwait(false) is not { } image) throw new OperationCanceledException(); // cancelled

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var language = a.Language.Length > 0 ? a.Language : Loc.Instance.Language;
        if (await ScreenText.ReadAsync(image, language, a.SingleLine, ctx.Token).ConfigureAwait(false) is not { } read) return;
        Gave(a, read.Text);
        Log.Info($"Texto de la pantalla: {image.Width}×{image.Height}, {read.Lines} líneas, {read.Text.Length} letras ({read.Language}) en {watch.ElapsedMilliseconds} ms");
        if (read.Text.Length == 0)
        {
            _ui.ShowOsd(Loc.T("osd.screenTextNone"), OsdKind.Warning, look: OsdLook.Of("ScanText24"));
            throw new OperationCanceledException();
        }
        var copied = _ui.SetClipboardText(read.Text);
        if (!a.ShowOsd) return;
        _ui.ShowOsd(copied ? Loc.T(read.Lines == 1 ? "osd.screenTextCopiedOne" : "osd.screenTextCopied", read.Lines, Preview(read.Text)) : Loc.T("osd.screenTextNotCopied"),
            copied ? OsdKind.Success : OsdKind.Warning, look: OsdLook.Of("ScanText24"));
    }

    // ── Cuentagotas y Regla ──────────────────────────────────────────────

    /// <summary>The whole desktop as it is now, for the frozen screen of «Cuentagotas» and «Regla».</summary>
    private static async Task<(BgraImage Image, ScreenRect Area)> FreezeAsync(ExecutionContext ctx)
    {
        // From the QuickMenu or the launcher: let them go away first.
        if (ctx.FromPopup) await Task.Delay(250, ctx.Token).ConfigureAwait(false);
        var area = ScreenCapture.VirtualScreen;
        return (await Task.Run(() => ScreenCapture.Capture(area), ctx.Token).ConfigureAwait(false), area);
    }

    /// <summary>«Cuentagotas»: the colour clicked, copied and in {resultado}. Esc: the rest of the command is not run.</summary>
    private async Task ColorPickerAsync(ColorPickerAction a, ExecutionContext ctx)
    {
        Gave(a, string.Empty);
        var (image, area) = await FreezeAsync(ctx).ConfigureAwait(false);
        if (await _ui.PickColorAsync(image, area, a.Format).ConfigureAwait(false) is not { } color) throw new OperationCanceledException();
        var text = color.Format(a.Format);
        Gave(a, text);
        Log.Info($"Cuentagotas: {text}");
        var copied = _ui.SetClipboardText(text);
        if (a.ShowOsd || !copied)
            _ui.ShowOsd(Loc.T(copied ? "osd.colorCopied" : "osd.copyBusy", text), copied ? OsdKind.Success : OsdKind.Warning, look: OsdLook.Of("Eyedropper24"));
    }

    /// <summary>«Regla»: «ancho × alto» of what was measured, copied and in {resultado}. Esc: the rest is not run.</summary>
    private async Task RulerAsync(RulerAction a, ExecutionContext ctx)
    {
        Gave(a, string.Empty);
        var (image, area) = await FreezeAsync(ctx).ConfigureAwait(false);
        if (await _ui.MeasureAsync(image, area).ConfigureAwait(false) is not { } size) throw new OperationCanceledException();
        var text = ScreenMeasure.Text(size.Width, size.Height);
        Gave(a, text);
        Log.Info($"Regla: {text}");
        var copied = _ui.SetClipboardText(text);
        if (a.ShowOsd || !copied)
            _ui.ShowOsd(Loc.T(copied ? "osd.rulerCopied" : "osd.copyBusy", text), copied ? OsdKind.Success : OsdKind.Warning, look: OsdLook.Of("Ruler24"));
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

    /// <summary>How recordings are kept now (Settings › Grabación de audio).</summary>
    public RecordOptions RecordOptions => new(Config.Settings.RecordFormat, Config.Settings.RecordMicVolume, Config.Settings.RecordSplitMinutes);

    /// <summary>Parakeet, for «Al terminar, transcribir» (the voice's: one model in memory); null: never transcribed.</summary>
    public SpeechToText? SpeechToText { get; set; }

    private async Task AudioRecordAsync(AudioRecordAction a, ExecutionContext ctx)
    {
        if (await Task.Run(() => AudioRecord(a)).ConfigureAwait(false) is { } file && a.GivesResult)
            await TranscribeRecordingAsync(a, file, ctx).ConfigureAwait(false);
    }

    /// <summary>
    /// «Al terminar, transcribir»: once the file is converted, its text into a .txt next to it and into {resultado}, with
    /// the OSD while it goes (Esc stops it). Not transcribed (no voice models, unreadable, nobody speaking): the rest of the
    /// command, which needed the text, is not run.
    /// </summary>
    private async Task TranscribeRecordingAsync(AudioRecordAction a, string file, ExecutionContext ctx)
    {
        if (SpeechToText is null || !VoiceModels.Ready)
        {
            _ui.ShowOsd(Loc.T("voice.noModels"), OsdKind.Warning, look: OsdLook.Of("MicPulse24"));
            throw new OperationCanceledException();
        }
        var cts = BeginCancellable(ctx.Token);
        string text;
        using (_ui.ShowPersistentOsd(Loc.T("osd.recordTranscribing"), icon: "MicPulse24"))
        {
            try
            {
                file = await Recording.ReadyAsync(file).WaitAsync(cts.Token).ConfigureAwait(false);
                text = await AudioTranscriber.TranscribeAsync(SpeechToText, file, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !ctx.Token.IsCancellationRequested)
            {
                CountdownCancelled();
                throw;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                Log.Warn($"'{ctx.Command.Name}': no se pudo transcribir {file} ({ex.Message})");
                _ui.ShowOsd(Loc.T("osd.recordTranscribeFailed"), OsdKind.Warning, look: OsdLook.Of("MicPulse24"));
                throw new OperationCanceledException();
            }
            finally { EndCancellable(cts); }
        }
        if (text.Length == 0)
        {
            _ui.ShowOsd(Loc.T("osd.recordNoSpeech"), OsdKind.Warning, look: OsdLook.Of("MicPulse24"));
            throw new OperationCanceledException();
        }
        var txt = Path.ChangeExtension(file, ".txt");
        try { await File.WriteAllTextAsync(txt, text, ctx.Token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Warn($"Transcripción: no se pudo guardar {txt} ({ex.Message})"); }
        Gave(a, text);
        if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.recordTranscribed", Path.GetFileName(txt)), OsdKind.Success, look: OsdLook.Of("MicPulse24"));
    }

    /// <summary>Does the action; the file that ended (stopped, or the replay saved), if any.</summary>
    private string? AudioRecord(AudioRecordAction a)
    {
        var mode = a.Mode == RecordMode.Toggle ? (Recording.IsRecording ? RecordMode.Stop : RecordMode.Start) : a.Mode;
        var folder = FolderFor(a.Folder, Config.Settings.RecordFolder, Environment.SpecialFolder.MyMusic);
        var options = RecordOptions;
        var final = AudioEncoders.Extension(options.Format);
        switch (mode)
        {
            case RecordMode.Start:
                if (Recording.IsRecording) return null;
                Recording.Start(a.Source, () => NewFile(folder, Loc.T("record.filePrefix"), ".wav", final), options);
                if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.recordStarted", Loc.T($"enum.audioSource.{a.Source}")), OsdKind.Warning, look: OsdLook.Of("MicPulse24"));
                break;
            case RecordMode.Stop:
                if (Recording.Stop() is not { } done) return null;
                if (a.ShowOsd && !a.GivesResult)
                {
                    var length = done.Length.ToString(done.Length.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);
                    _ui.ShowOsd(Loc.T("osd.recordSaved", Path.GetFileName(done.Path), length), OsdKind.Success, look: OsdLook.Of("MicPulse24"));
                }
                return done.Path;
            case RecordMode.SaveReplay:
                var seconds = Config.Settings.AudioReplaySeconds;
                if (Recording.SaveReplay(NewFile(folder, Loc.T("record.replayPrefix"), ".wav", final), options.Format) is not { } saved)
                {
                    Log.Warn("Repetición de audio: está apagada (Opciones › Grabación de audio)");
                    if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.replayOff"), OsdKind.Warning, look: OsdLook.Of("History24"));
                    return null;
                }
                if (a.ShowOsd && !a.GivesResult) _ui.ShowOsd(Loc.T("osd.replaySaved", seconds), OsdKind.Success, look: OsdLook.Of("History24"));
                return saved;
        }
        return null;
    }

    // ── Lupa ─────────────────────────────────────────────────────────────

    /// <summary>How the lupa looks and behaves now (Settings › Lupa).</summary>
    public MagnifierOptions MagnifierOptions
    {
        get
        {
            var s = Config.Settings;
            return new(s.ZoomMode, s.ZoomLensSize, s.ZoomBorder, s.ZoomBorderColor, s.ZoomSmooth, s.ZoomColors, s.ZoomFollowText, s.ZoomAutoOff);
        }
    }

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

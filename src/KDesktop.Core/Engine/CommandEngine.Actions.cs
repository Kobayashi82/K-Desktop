using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Engine;

public sealed partial class CommandEngine
{
    private Task ExecuteActionAsync(CommandAction action, ExecutionContext ctx) => action switch
    {
        RunAppAction a => RunAppAsync(a),
        OpenFolderAction a => OpenFolderAsync(a),
        OpenUrlAction a => OpenUrlAsync(a),
        AppControlAction a => AppControlAsync(a, ctx),
        RunScriptAction a => RunScriptAsync(a, ctx),
        SendInputAction a => SendInputAsync(a, ctx),
        VolumeAction a => VolumeAsync(a, ctx),
        PlaySoundAction a => a.Wait ? Sounds.PlayAndWaitAsync(a.Sound, ctx.Token) : Run(() => Sounds.Play(a.Sound)),
        TopMostAction a => Run(() => TopMost(a, ctx)),
        HiddenFilesAction a => Run(() => HiddenFiles(a)),
        ToggleDisabledInAction a => Run(() => ToggleDisabledIn(a, ctx)),
        ShowMessageAction a => ShowMessageAsync(a, ctx),
        ShowNotificationAction a => ShowNotificationAsync(a, ctx),
        FilesAction a => FilesAsync(a, ctx),
        PowerAction a => PowerAsync(a, ctx),
        OpenQuickMenuAction a => Run(() => _ui.ShowQuickMenu(a.MenuId, ctx.Foreground)),
        CommandControlAction a => CommandControlAsync(a, ctx),
        WaitAction a => WaitAsync(a, ctx),
        PlayMacroAction a => PlayMacroAsync(a, ctx),
        AudioOutputAction a => AudioOutputAsync(a), // also «Entrada de audio»
        KBoardAction a => Run(() => KBoard(a)),
        HttpRequestAction a => HttpRequestAsync(a, ctx),
        WindowAction a => Run(() => Window(a, ctx)),
        ClipboardAction a => ClipboardAsync(a, ctx),
        WakeOnLanAction a => WakeOnLanAsync(a, ctx),
        DisplayAction a => DisplayAsync(a),
        PowerPlanAction a => Run(() => PowerPlan(a)),
        WindowsThemeAction a => Run(() => WindowsTheme(a)),
        RadioAction a => Run(() => Radio(a)),
        MediaAction a => MediaAsync(a, ctx),
        KeepAwakeAction a => Run(() => StayAwake(a)),
        SendToPhoneAction a => Run(() => SendToPhone(a)),
        ScreenshotAction a => ScreenshotAsync(a, ctx),
        ScreenTextAction a => ScreenTextAsync(a, ctx),
        AudioRecordAction a => Run(() => AudioRecord(a)),
        MagnifierAction a => Run(() => Magnify(a)),
        DictateAction a => Run(() => _ui.Dictate(a.Mode)),
        AssistantExtraAction a => Run(() => _ui.RunAssistantExtra(a.Assistant, a.Extra, a.BringOut)),
        AssistantAskAction a => AssistantAskAsync(a, ctx),
        AssistantSayAction a => Run(() => _ui.AssistantSay(a.Assistant, TextVariables.Expand(a.Text), a.CatchEye)),
        AssistantBrainAction a => Run(() => _ui.AssistantBrain(a.Op)),
        _ => Task.CompletedTask,
    };

    private static Task Run(Action action) => Task.Run(action);

    /// <summary>What an action gives, for the next ones: {resultado} and, if it has a name, {name}.</summary>
    private static void Gave(CommandAction action, string value) => CommandVariables.Current?.SetResult(value, action.SaveAs);

    // ── Aplicaciones ─────────────────────────────────────────────────────

    private static Task RunAppAsync(RunAppAction a)
    {
        if (string.IsNullOrWhiteSpace(a.Path)) return Task.CompletedTask;
        var spec = new LaunchSpec(TextVariables.Expand(a.Path), TextVariables.Expand(a.Arguments), a.WorkingDirectory, a.WindowMode, a.Priority, a.TopMost);
        return Task.Run(() => Launched(ProcessLauncher.Launch(spec, a.RunAsAdmin), spec.FileName));
    }

    /// <summary>It could not be opened: the action fails (the «Registro» says why, and «¿qué falló?» finds it).</summary>
    private static void Launched(bool ok, string what)
    {
        if (!ok) throw new InvalidOperationException(Localization.Loc.T("run.launchFailed", what.Trim().Trim('"')));
    }

    private static Task OpenFolderAsync(OpenFolderAction a) => Task.Run(() =>
    {
        if (a.Special is { } special)
        {
            if (!ShellTools.OpenSpecial(special)) Log.Warn($"Ubicación especial desconocida: {special}");
            return;
        }
        var path = Environment.ExpandEnvironmentVariables(TextVariables.Expand(a.Path).Trim().Trim('"'));
        if (path.Length == 0) return;
        if (Directory.Exists(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            Launched(ProcessLauncher.Launch(new LaunchSpec("explorer.exe", $"\"{path}\""), asAdmin: false), path);
        else if (File.Exists(path))
            Launched(ProcessLauncher.Launch(new LaunchSpec(path), asAdmin: false), path);
        else
            throw new InvalidOperationException(Localization.Loc.T("run.folderMissing", path));
    });

    private static Task OpenUrlAsync(OpenUrlAction a) => Task.Run(() =>
    {
        var url = NormalizeUrl(TextVariables.ExpandUrl(a.Url));
        if (url.Length == 0) return;
        var browser = Browsers.Get(a.Browser);
        var exe = browser?.Executable;
        // «Predeterminado» with a new window or incognito: those need to know which browser it is.
        if (browser is null && (a.NewWindow || a.Incognito))
        {
            if (Browsers.Default() is { } found) (browser, exe) = found;
            else Log.Warn("No se reconoce el navegador predeterminado: la página se abre sin ventana nueva ni incógnito");
        }
        if (browser is not null)
        {
            var args = new List<string>();
            if (a.Incognito) args.Add(browser.IncognitoArg);
            else if (a.NewWindow) args.Add(browser.NewWindowArg);
            args.Add($"\"{url}\"");
            Launched(ProcessLauncher.Launch(new LaunchSpec(exe!, string.Join(' ', args)), asAdmin: false), browser.Name);
        }
        else
        {
            Launched(ProcessLauncher.Launch(new LaunchSpec(url), asAdmin: false), url);
        }
    });

    public static string NormalizeUrl(string url)
    {
        url = url.Trim();
        if (url.Length == 0) return url;
        return url.Contains("://", StringComparison.Ordinal) || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            ? url
            : "https://" + url;
    }

    private async Task AppControlAsync(AppControlAction a, ExecutionContext ctx)
    {
        var name = ProcessNames.Normalize(a.ProcessName);
        if (name.Length == 0) return;
        switch (a.Mode)
        {
            case AppControlMode.Activate:
                await Task.Run(() => WindowTools.Activate(name)).ConfigureAwait(false);
                if (a.Force) await WaitUntilAsync(() => ForegroundWindow.Capture().ProcessName == name, TimeSpan.FromSeconds(3), ctx.Token).ConfigureAwait(false);
                break;

            case AppControlMode.Close:
                if (a.Force && a.AskFirst && await WindowTools.CloseAsync(name, false, ctx.Token).ConfigureAwait(false)) break;
                await WindowTools.CloseAsync(name, a.Force, ctx.Token).ConfigureAwait(false);
                break;

            case AppControlMode.TopMost:
                if (await Task.Run(() => WindowTools.SetTopMost(name, a.TopMost)).ConfigureAwait(false) is { } on)
                    _ui.ShowOsd(Loc.T(on ? "osd.topMostOn" : "osd.topMostOff", name), on ? OsdKind.Success : OsdKind.Info, look: OsdLook.Of(on ? "Pin24" : "PinOff24"));
                break;

            case AppControlMode.WaitFor:
                Func<bool> condition = a.WaitFor switch
                {
                    WaitCondition.Activated => () => ForegroundWindow.Capture().ProcessName == name,
                    WaitCondition.Opened => () => WindowTools.FindProcesses(name) is { Length: > 0 } p && Dispose(p),
                    _ => () => WindowTools.FindProcesses(name) is var p && !Dispose(p),
                };
                var cts = BeginCancellable(ctx.Token);
                try
                {
                    var timeout = a.TimeoutSeconds > 0 ? TimeSpan.FromSeconds(a.TimeoutSeconds) : Timeout.InfiniteTimeSpan;
                    if (!await WaitUntilAsync(condition, timeout, cts.Token).ConfigureAwait(false))
                        throw new OperationCanceledException(); // timed out: stop the command
                }
                finally { EndCancellable(cts); }
                break;
        }

        // Disposes the processes and returns whether there were any.
        static bool Dispose(System.Diagnostics.Process[] processes)
        {
            foreach (var p in processes) p.Dispose();
            return processes.Length > 0;
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken token)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (timeout != Timeout.InfiniteTimeSpan && Environment.TickCount64 - start > timeout.TotalMilliseconds) return false;
            await Task.Delay(150, token).ConfigureAwait(false);
        }
        return true;
    }

    private async Task RunScriptAsync(RunScriptAction a, ExecutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(a.Script)) return;
        Directory.CreateDirectory(AppPaths.TempDir);
        // With variables, the script of this run (they go in as they are); those files do not stay around.
        var script = TextVariables.Expand(a.Script);
        var prefix = script == a.Script ? "script-" : "script-v-";
        if (prefix != "script-") DeleteOld(prefix);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(a.Shell + script)))[..16];
        // What it writes, for {resultado}: waited for and hidden (see GivesResult).
        var output = a.GivesResult ? Path.Combine(AppPaths.TempDir, $"script-out-{Guid.NewGuid():N}.txt") : null;

        LaunchSpec spec;
        switch (a.Shell)
        {
            case ScriptShell.Cmd:
            {
                // A .cmd is a .bat: the same commands.
                var file = Path.Combine(AppPaths.TempDir, $"{prefix}{hash}.cmd");
                // Its output kept for {resultado}: only what the script writes, not each line it runs.
                await File.WriteAllTextAsync(file, "@chcp 65001 >nul\r\n" + (output is null ? "" : "@echo off\r\n") + script.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                    new UTF8Encoding(false), ctx.Token).ConfigureAwait(false);
                // Visible, the window stays open to read what it wrote (as PowerShell's), except when the command waits for it.
                // The doubled quotes: cmd takes off the outer pair and keeps the path's own (it has spaces).
                spec = output is not null ? new LaunchSpec("cmd.exe", $"/C \"\"{file}\" > \"{output}\" 2>&1\"", null, a.WindowMode)
                    : a.WindowMode == WindowMode.Hidden || a.WaitForExit
                    ? new LaunchSpec(file, "", null, a.WindowMode)
                    : new LaunchSpec("cmd.exe", $"/K \"\"{file}\"\"", null, a.WindowMode);
                break;
            }
            case ScriptShell.VBScript:
            {
                // Windows Script Host reads Unicode with its BOM (UTF-8 would break the accents); wscript has no console.
                var file = Path.Combine(AppPaths.TempDir, $"{prefix}{hash}.vbs");
                await File.WriteAllTextAsync(file, script.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                    new UnicodeEncoding(false, true), ctx.Token).ConfigureAwait(false);
                spec = new LaunchSpec("wscript.exe", $"//nologo \"{file}\"", null, a.WindowMode);
                break;
            }
            default:
            {
                var file = Path.Combine(AppPaths.TempDir, $"{prefix}{hash}.ps1");
                await File.WriteAllTextAsync(file, script, new UTF8Encoding(true), ctx.Token).ConfigureAwait(false);
                // A visible window stays open to read what it wrote, except when the command waits for it to end.
                var noExit = a.WindowMode == WindowMode.Hidden || a.WaitForExit ? "" : "-NoExit ";
                spec = output is not null
                    ? new LaunchSpec("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"& '{Quoted(file)}' *>&1 | Out-File -LiteralPath '{Quoted(output)}' -Encoding utf8 -Width 4096\"", null, a.WindowMode)
                    : new LaunchSpec("powershell.exe", $"-NoProfile {noExit}-ExecutionPolicy Bypass -File \"{file}\"", null, a.WindowMode);
                break;
            }
        }

        if (!a.WaitForExit)
        {
            await Task.Run(() => ProcessLauncher.Launch(spec, a.RunAsAdmin)).ConfigureAwait(false);
            return;
        }
        var cts = BeginCancellable(ctx.Token); // Esc stops waiting (the script goes on)
        try { await ProcessLauncher.LaunchAndWaitAsync(spec, a.RunAsAdmin, cts.Token).ConfigureAwait(false); }
        finally { EndCancellable(cts); }
        if (output is null) return;
        try
        {
            Gave(a, File.Exists(output) ? (await File.ReadAllTextAsync(output, ctx.Token).ConfigureAwait(false)).Trim() : string.Empty);
            File.Delete(output);
        }
        catch (IOException ex)
        {
            Log.Warn($"Script: no se pudo leer lo que escribió ({ex.Message})");
        }

        // A path inside single quotes, for PowerShell.
        static string Quoted(string path) => path.Replace("'", "''", StringComparison.Ordinal);
    }

    /// <summary>The scripts with variables of earlier runs (one per run): gone after an hour.</summary>
    private static void DeleteOld(string prefix)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.TempDir, prefix + "*"))
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromHours(1)) File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Script: no se pudieron borrar los antiguos ({ex.Message})");
        }
    }

    private async Task HttpRequestAsync(HttpRequestAction a, ExecutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(a.Url)) return;
        var cts = BeginCancellable(ctx.Token); // Esc cancels it
        try
        {
            var result = await HttpRequests.SendAsync(a, cts.Token).ConfigureAwait(false);
            Gave(a, result.Body);
            var text = Loc.T("http.result", a.Summary, result.Describe());
            if (result.Success)
            {
                Log.Info($"Petición web: {a.Summary} → {result.Status} ({result.Milliseconds} ms)");
                if (a.ShowResult == HttpResultDisplay.Always) _ui.ShowOsd(text, OsdKind.Success, look: OsdLook.Of("ArrowSwap24"));
            }
            else
            {
                Log.Warn(text);
                if (a.ShowResult != HttpResultDisplay.Never) _ui.ShowOsd(text, OsdKind.Warning, look: OsdLook.Of("ArrowSwap24"));
            }
        }
        finally { EndCancellable(cts); }
    }

    // ── Entrada y sonido ─────────────────────────────────────────────────

    private static async Task SendInputAsync(SendInputAction a, ExecutionContext ctx)
    {
        if (string.IsNullOrEmpty(a.Value)) return;

        // From the QuickMenu, give the focus back to the window the menu was opened over.
        if (ctx.FromPopup && ctx.Foreground.Handle != IntPtr.Zero)
        {
            WindowTools.ForceForeground(ctx.Foreground.Handle);
            await Task.Delay(60, ctx.Token).ConfigureAwait(false);
        }

        switch (a.Mode)
        {
            case SendInputMode.Keys:
                if (!InputSimulator.SendCombo(a.Value)) Log.Warn($"Combinación no válida: {a.Value}");
                break;
            case SendInputMode.Text:
                InputSimulator.SendText(TextVariables.Expand(a.Value));
                break;
            case SendInputMode.MouseMoveTo when TryParsePoint(a.Value, out var x, out var y):
                InputSimulator.MoveTo(x, y);
                break;
            case SendInputMode.MouseMoveBy when TryParsePoint(a.Value, out var dx, out var dy):
                InputSimulator.MoveBy(dx, dy);
                break;
        }
        // Let the target application process the input before the next action.
        await Task.Delay(40, ctx.Token).ConfigureAwait(false);
    }

    public static bool TryParsePoint(string value, out int x, out int y)
    {
        x = y = 0;
        var parts = value.Split([',', ';', ' ', 'x', 'X'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 &&
               int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x) &&
               int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y);
    }

    private Task VolumeAsync(VolumeAction a, ExecutionContext ctx) => Task.Run(() =>
    {
        if (a.Target != VolumeTarget.System || a.Device.Length > 0)
        {
            OtherVolume(a, ctx);
            return;
        }
        switch (a.Mode)
        {
            case VolumeMode.Set:
                var v = AudioTools.SetVolume(a.Value);
                if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.volume", v), look: OsdLook.Bar("Speaker224", v));
                break;
            case VolumeMode.Up or VolumeMode.Down when a.Value <= 0:
                InputSimulator.KeyPress(a.Mode == VolumeMode.Up ? 0xAF : 0xAE); // native volume flyout
                break;
            case VolumeMode.Up or VolumeMode.Down:
                var level = AudioTools.StepVolume(a.Mode == VolumeMode.Up ? a.Value : -a.Value);
                if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.volume", level), look: OsdLook.Bar("Speaker224", level));
                break;
            case VolumeMode.ToggleMute:
                var muted = AudioTools.ToggleMute();
                if (a.ShowOsd) _ui.ShowOsd(Loc.T(muted ? "osd.muted" : "osd.unmuted"), look: new OsdLook(muted ? "SpeakerMute24" : "Speaker224", Topic: OsdTopic.Levels));
                break;
            case VolumeMode.Mute or VolumeMode.Unmute:
                AudioTools.SetMute(a.Mode == VolumeMode.Mute);
                if (a.ShowOsd) _ui.ShowOsd(Loc.T(a.Mode == VolumeMode.Mute ? "osd.muted" : "osd.unmuted"), look: new OsdLook(a.Mode == VolumeMode.Mute ? "SpeakerMute24" : "Speaker224", Topic: OsdTopic.Levels));
                break;
        }
    });

    private async Task AudioOutputAsync(AudioOutputAction a)
    {
        var rotation = a.Devices.Select(d => d.Id).ToList();
        var device = await Task.Run(() => AudioDevices.Next(rotation, a.Communications, a.IsInput)).ConfigureAwait(false);
        var icon = a.IsInput ? "Mic24" : "HeadphonesSoundWave24";
        if (device is null)
        {
            if (a.ShowOsd) _ui.ShowOsd(Loc.T(a.IsInput ? "osd.audioInNone" : "osd.audioNone"), OsdKind.Warning, look: OsdLook.Of(icon));
            return;
        }
        if (a.ShowOsd) _ui.ShowOsd(Loc.T(a.IsInput ? "osd.audioInput" : "osd.audioOutput", device.Name), OsdKind.Success, look: OsdLook.Of(icon));
        if (!a.IsInput && !string.IsNullOrWhiteSpace(a.Sound))
        {
            // Give Windows a moment to move the audio streams to the new device.
            await Task.Delay(250).ConfigureAwait(false);
            Sounds.Play(a.Sound);
        }
    }

    // ── Ventanas y sistema ───────────────────────────────────────────────

    private void TopMost(TopMostAction a, ExecutionContext ctx)
    {
        var hwnd = ctx.Foreground.RootHandle;
        if (hwnd == IntPtr.Zero || ctx.Foreground.ProcessId == (uint)Environment.ProcessId) return;
        var on = WindowTools.ToggleTopMost(hwnd);
        if (a.ShowOsd)
            _ui.ShowOsd(Loc.T(on ? "osd.topMostOn" : "osd.topMostOff", Shorten(ctx.Foreground.Title, 48)), on ? OsdKind.Success : OsdKind.Info, look: OsdLook.Of(on ? "Pin24" : "PinOff24"));
    }

    private void HiddenFiles(HiddenFilesAction a)
    {
        var visible = ShellTools.ToggleHiddenFiles();
        if (a.ShowOsd) _ui.ShowOsd(Loc.T(visible ? "osd.hiddenShown" : "osd.hiddenHidden"), look: OsdLook.Of(visible ? "Eye24" : "EyeOff24"));
    }

    private void ToggleDisabledIn(ToggleDisabledInAction a, ExecutionContext ctx) => ToggleDisabledIn(ctx.Foreground, a.ShowOsd);

    /// <summary>Adds <paramref name="window"/>'s app to «Desactivar en», or takes it out (its hotkey in the options).</summary>
    public void ToggleDisabledIn(ForegroundWindow window, bool showOsd = true)
    {
        var process = window.ProcessName;
        if (process.Length == 0 || window.ProcessId == (uint)Environment.ProcessId) return;
        var list = Config.Settings.DisabledIn;
        var disable = !list.Any(p => ProcessNames.Normalize(p) == process);
        _ui.RunOnUi(() =>
        {
            if (disable) list.Add(process);
            else foreach (var p in list.Where(p => ProcessNames.Normalize(p) == process).ToList()) list.Remove(p);
        });
        if (showOsd)
            _ui.ShowOsd(Loc.T(disable ? "osd.disabledIn" : "osd.enabledIn", process), disable ? OsdKind.Warning : OsdKind.Success, look: OsdLook.Of(disable ? "Prohibited24" : "Flash24"));
    }

    private async Task ShowMessageAsync(ShowMessageAction a, ExecutionContext ctx)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0.3, a.Seconds));
        var text = TextVariables.Expand(a.Text);
        if (!a.Countdown && !a.Wait)
        {
            _ui.ShowOsd(text, OsdKind.Info, duration, OsdLook.Of("Chat24"));
            return;
        }
        var cts = BeginCancellable(ctx.Token);
        var osd = _ui.ShowPersistentOsd(text, a.Countdown ? DateTime.Now + duration : null, "Chat24");
        var task = Task.Delay(duration, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled && a.Countdown) CountdownCancelled();
            osd.Dispose();
            EndCancellable(cts);
        }, TaskScheduler.Default);
        if (a.Wait) await task.ConfigureAwait(false);
    }

    /// <summary>«Mostrar notificación»: a card in a corner; with «Esperar», the next action waits until it is gone.</summary>
    private async Task ShowNotificationAsync(ShowNotificationAction a, ExecutionContext ctx)
    {
        var target = TextVariables.Expand(a.Target).Trim();
        Action? clicked = a.OnClick switch
        {
            NotifyClick.Open when target.Length > 0 => () => ProcessLauncher.Launch(
                new LaunchSpec(target.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? NormalizeUrl(target) : target), asAdmin: false),
            NotifyClick.RunCommand when Config.FindCommand(a.CommandId) is { } command && !ReferenceEquals(command, ctx.Command) =>
                () => _ = RunAsync(command, TriggerSource.Command, ctx.Foreground),
            _ => null,
        };
        var notification = new Notification(
            TextVariables.Expand(a.Title), TextVariables.Expand(a.Text), a.Icon, a.Color, a.Sound,
            a.Seconds > 0 ? TimeSpan.FromSeconds(a.Seconds) : null,
            a.Place == NotifyPlace.Settings ? null : Enum.Parse<NotifyCorner>(a.Place.ToString()),
            clicked);
        var shown = _ui.ShowNotificationAsync(notification);
        if (a.Wait) await shown.WaitAsync(ctx.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// «Archivos y carpetas»: the work goes on its own thread with its progress on the OSD (Esc stops it); with «Esperar»
    /// (the usual), the next action waits until it is done.
    /// </summary>
    private async Task FilesAsync(FilesAction a, ExecutionContext ctx)
    {
        var job = new FileJob(a.Operation, [.. a.Sources.Select(s => TextVariables.Expand(s))], TextVariables.Expand(a.Destination),
            a.Subfolders, TextVariables.Expand(a.NewName), a.Exists, a.Recycle, a.BackupMode, a.MirrorDelete, a.BackupZip, a.Keep);
        var cts = BeginCancellable(ctx.Token);
        var work = RunFilesAsync(job, a.ShowOsd, cts);
        if (a.Wait) await work.ConfigureAwait(false);
    }

    private async Task RunFilesAsync(FileJob job, bool showOsd, CancellationTokenSource cts)
    {
        const string icon = "DocumentCopy24";
        var verb = Loc.T($"enum.fileOp.{job.Operation}");
        var osd = showOsd ? _ui.ShowProgressOsd(Loc.T("files.working", verb), icon) : null;
        var progress = osd is null ? null : new FilesProgress(osd, verb);
        try
        {
            var result = await FileJobs.RunAsync(job, progress, cts.Token).ConfigureAwait(false);
            Log.Info($"Archivos ({job.Operation}): {result.Done} hechos, {result.Skipped} sin cambios, {result.Failed} con error, {result.Bytes} bytes" +
                (result.Refused is null ? "" : $", rechazado: {result.Refused} {result.RefusedArg}"));
            // Without the OSD, only what went wrong is said.
            if (showOsd || result.Refused is not null || result.Failed > 0) ShowFilesResult(result, verb, icon);
        }
        catch (OperationCanceledException)
        {
            _ui.ShowOsd(Loc.T("files.cancelled", verb), OsdKind.Warning, look: OsdLook.Of(icon));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Archivos ({job.Operation})", ex);
            _ui.ShowOsd(Loc.T("files.error", verb, ex.Message), OsdKind.Warning, look: OsdLook.Of(icon));
        }
        finally
        {
            osd?.Dispose();
            EndCancellable(cts);
        }
    }

    private void ShowFilesResult(FileJobResult result, string verb, string icon)
    {
        if (result.Refused is { } refused)
        {
            _ui.ShowOsd(Loc.T(refused, result.RefusedArg ?? string.Empty), OsdKind.Warning, TimeSpan.FromSeconds(5), OsdLook.Of(icon));
            return;
        }
        if (result.Done == 0 && result.Failed == 0)
        {
            _ui.ShowOsd(Loc.T(result.Skipped > 0 ? "files.upToDate" : "files.nothing", verb), OsdKind.Info, look: OsdLook.Of(icon));
            return;
        }
        var text = Loc.T("files.done", verb, result.Done);
        if (result.Bytes > 0) text += $" · {FileJobs.Size(result.Bytes, Loc.Instance.Culture)}";
        if (result.Skipped > 0) text += " · " + Loc.T("files.skipped", result.Skipped);
        if (result.Failed > 0) text += " · " + Loc.T("files.failed", result.Failed);
        _ui.ShowOsd(text, result.Failed > 0 ? OsdKind.Warning : OsdKind.Success, look: OsdLook.Of(icon));
    }

    /// <summary>Passes the progress to the OSD at most ten times a second.</summary>
    private sealed class FilesProgress(IProgressOsd osd, string verb) : IProgress<FileJobProgress>
    {
        private long _last;

        public void Report(FileJobProgress value)
        {
            var now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _last) < 100 && value.Bytes < value.Total) return;
            Interlocked.Exchange(ref _last, now);
            var culture = Loc.Instance.Culture;
            var level = value.Total > 0 ? (double)value.Bytes / value.Total : 0;
            osd.Update(Loc.T("files.progress", verb, FileJobs.Size(value.Bytes, culture), FileJobs.Size(value.Total, culture)), level);
        }
    }

    private async Task PowerAsync(PowerAction a, ExecutionContext ctx)
    {
        var name = PowerNames.Of(a.Mode);
        if (a.DelaySeconds > 0)
        {
            var cts = BeginCancellable(ctx.Token);
            var osd = a.ShowOsd ? _ui.ShowPersistentOsd(Loc.T("osd.powerCountdown", name), DateTime.Now.AddSeconds(a.DelaySeconds), "Power24") : null;
            // An assistant that ran it says so now, during the countdown (after it, the PC is off).
            ExecutionFeedback.Current?.Release();
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(a.DelaySeconds), cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (osd is not null) CountdownCancelled();
                _ui.ShowOsd(Loc.T("osd.powerCancelled", name), OsdKind.Warning, look: OsdLook.Of("Power24"));
                throw;
            }
            finally
            {
                osd?.Dispose();
                EndCancellable(cts);
            }
        }
        ExecutionFeedback.Current?.Release();
        if (a.Mode is PowerMode.Shutdown or PowerMode.Restart or PowerMode.LogOff)
        {
            await RunExitCommandsAsync().ConfigureAwait(false);
            _store.Flush();
        }
        PowerTools.Execute(a.Mode);
    }

    // ── K-Desktop ────────────────────────────────────────────────────────

    private async Task CommandControlAsync(CommandControlAction a, ExecutionContext ctx)
    {
        if (a.Target == CommandControlTarget.Event)
        {
            await EventControlAsync(a, ctx).ConfigureAwait(false);
            return;
        }
        if (Config.FindCommand(a.CommandId) is not { } target) return;
        switch (a.Mode)
        {
            case CommandControlMode.Run:
                if (ReferenceEquals(target, ctx.Command)) return;
                await RunAsync(target, TriggerSource.Command, ctx.Foreground, false, ctx.Depth + 1, ctx.Choreography, CommandVariables.Current, ctx.ChoreographyToken).ConfigureAwait(false);
                break;
            case CommandControlMode.Enable:
                _ui.RunOnUi(() => target.Enabled = true);
                break;
            case CommandControlMode.Disable:
                _ui.RunOnUi(() => target.Enabled = false);
                break;
            case CommandControlMode.Toggle:
                var enabled = !target.Enabled;
                _ui.RunOnUi(() => target.Enabled = enabled);
                _ui.ShowOsd(Loc.T(enabled ? "osd.commandEnabled" : "osd.commandDisabled", target.Name), look: OsdLook.Of("Flash24"));
                break;
        }
    }

    /// <summary>«Comandos» on an event: run its commands now (no conditions), or switch it on / off.</summary>
    private async Task EventControlAsync(CommandControlAction a, ExecutionContext ctx)
    {
        if (Config.FindEvent(a.EventId) is not { } rule) return;
        switch (a.Mode)
        {
            case CommandControlMode.Run:
                await RunEventAsync(rule, false, TriggerSource.Command, ctx.Foreground, ctx.Depth + 1, ctx.Choreography, cancellationToken: ctx.ChoreographyToken).ConfigureAwait(false);
                break;
            case CommandControlMode.Enable:
                _ui.RunOnUi(() => rule.Enabled = true);
                break;
            case CommandControlMode.Disable:
                _ui.RunOnUi(() => rule.Enabled = false);
                break;
            case CommandControlMode.Toggle:
                var enabled = !rule.Enabled;
                _ui.RunOnUi(() => rule.Enabled = enabled);
                _ui.ShowOsd(Loc.T(enabled ? "osd.eventEnabled" : "osd.eventDisabled", rule.Name), look: OsdLook.Of("CalendarClock24"));
                break;
        }
    }

    private void KBoard(KBoardAction a)
    {
        if (Config.FindKBoard(a.ProfileId) is not { } profile) return;
        var active = a.Mode switch
        {
            KBoardActionMode.Activate => true,
            KBoardActionMode.Deactivate => false,
            _ => !profile.Active,
        };
        _ui.RunOnUi(() => profile.Active = active);
        if (a.ShowOsd)
            _ui.ShowOsd(Loc.T(active ? "osd.kboardOn" : "osd.kboardOff", profile.Name), active ? OsdKind.Success : OsdKind.Info, look: OsdLook.Of("KeyboardLayoutFloat24"));
    }

    private async Task WaitAsync(WaitAction a, ExecutionContext ctx)
    {
        var duration = TimeSpan.FromSeconds(Math.Max(0, a.Seconds));
        var visible = duration.TotalSeconds >= 1 && (a.Countdown || !string.IsNullOrWhiteSpace(a.Message));
        if (!visible)
        {
            await Task.Delay(duration, ctx.Token).ConfigureAwait(false);
            return;
        }
        var cts = BeginCancellable(ctx.Token);
        var osd = _ui.ShowPersistentOsd(TextVariables.Expand(a.Message), a.Countdown ? DateTime.Now + duration : null, "Timer24");
        try
        {
            await Task.Delay(duration, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (a.Countdown)
        {
            CountdownCancelled();
            throw;
        }
        finally
        {
            osd.Dispose();
            EndCancellable(cts);
        }
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}

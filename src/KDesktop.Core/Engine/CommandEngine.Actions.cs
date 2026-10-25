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
        PowerAction a => PowerAsync(a, ctx),
        OpenQuickMenuAction a => Run(() => _ui.ShowQuickMenu(a.MenuId, ctx.Foreground)),
        CommandControlAction a => CommandControlAsync(a, ctx),
        WaitAction a => WaitAsync(a, ctx),
        PlayMacroAction a => PlayMacroAsync(a, ctx),
        AudioOutputAction a => AudioOutputAsync(a),
        KBoardAction a => Run(() => KBoard(a)),
        HttpRequestAction a => HttpRequestAsync(a, ctx),
        WindowAction a => Run(() => Window(a, ctx)),
        ClipboardAction a => ClipboardAsync(a, ctx),
        WakeOnLanAction a => WakeOnLanAsync(a, ctx),
        DisplayAction a => DisplayAsync(a),
        PowerPlanAction a => Run(() => PowerPlan(a)),
        WindowsThemeAction a => Run(() => WindowsTheme(a)),
        RadioAction a => Run(() => Radio(a)),
        ScreenshotAction a => ScreenshotAsync(a, ctx),
        ScreenTextAction a => ScreenTextAsync(a, ctx),
        AudioRecordAction a => Run(() => AudioRecord(a)),
        MagnifierAction a => Run(() => Magnify(a)),
        DictateAction a => Run(() => _ui.Dictate(a.Mode)),
        AssistantExtraAction a => Run(() => _ui.RunAssistantExtra(a.Assistant, a.Extra)),
        AssistantAskAction a => Run(() => _ui.AskAssistant(a.Assistant, a.Text)),
        AssistantSayAction a => Run(() => _ui.AssistantSay(a.Assistant, a.Text)),
        AssistantBrainAction a => Run(() => _ui.AssistantBrain(a.Op)),
        _ => Task.CompletedTask,
    };

    private static Task Run(Action action) => Task.Run(action);

    // ── Aplicaciones ─────────────────────────────────────────────────────

    private static Task RunAppAsync(RunAppAction a)
    {
        if (string.IsNullOrWhiteSpace(a.Path)) return Task.CompletedTask;
        var spec = new LaunchSpec(a.Path, a.Arguments, a.WorkingDirectory, a.WindowMode, a.Priority, a.TopMost);
        return Task.Run(() => ProcessLauncher.Launch(spec, a.RunAsAdmin));
    }

    private static Task OpenFolderAsync(OpenFolderAction a) => Task.Run(() =>
    {
        if (a.Special is { } special)
        {
            if (!ShellTools.OpenSpecial(special)) Log.Warn($"Ubicación especial desconocida: {special}");
            return;
        }
        var path = Environment.ExpandEnvironmentVariables(a.Path.Trim().Trim('"'));
        if (path.Length == 0) return;
        if (Directory.Exists(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            ProcessLauncher.Launch(new LaunchSpec("explorer.exe", $"\"{path}\""), asAdmin: false);
        else if (File.Exists(path))
            ProcessLauncher.Launch(new LaunchSpec(path), asAdmin: false);
        else
            Log.Warn($"La carpeta no existe: {path}");
    });

    private static Task OpenUrlAsync(OpenUrlAction a) => Task.Run(() =>
    {
        var url = NormalizeUrl(a.Url);
        if (url.Length == 0) return;
        if (Browsers.Get(a.Browser) is { } browser)
        {
            var args = new List<string>();
            if (a.Incognito) args.Add(browser.IncognitoArg);
            else if (a.NewWindow) args.Add(browser.NewWindowArg);
            args.Add($"\"{url}\"");
            ProcessLauncher.Launch(new LaunchSpec(browser.Executable, string.Join(' ', args)), asAdmin: false);
        }
        else
        {
            ProcessLauncher.Launch(new LaunchSpec(url), asAdmin: false);
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

    private static async Task RunScriptAsync(RunScriptAction a, ExecutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(a.Script)) return;
        Directory.CreateDirectory(AppPaths.TempDir);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(a.Shell + a.Script)))[..16];

        LaunchSpec spec;
        if (a.Shell == ScriptShell.Cmd)
        {
            var file = Path.Combine(AppPaths.TempDir, $"script-{hash}.cmd");
            await File.WriteAllTextAsync(file, "@chcp 65001 >nul\r\n" + a.Script.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                new UTF8Encoding(false), ctx.Token).ConfigureAwait(false);
            spec = new LaunchSpec(file, "", null, a.WindowMode);
        }
        else
        {
            var file = Path.Combine(AppPaths.TempDir, $"script-{hash}.ps1");
            await File.WriteAllTextAsync(file, a.Script, new UTF8Encoding(true), ctx.Token).ConfigureAwait(false);
            var noExit = a.WindowMode == WindowMode.Hidden ? "" : "-NoExit ";
            spec = new LaunchSpec("powershell.exe", $"-NoProfile {noExit}-ExecutionPolicy Bypass -File \"{file}\"", null, a.WindowMode);
        }

        if (a.WaitForExit && (a.RunAsAdmin || !ProcessLauncher.IsElevated))
        {
            var psi = new System.Diagnostics.ProcessStartInfo(spec.FileName, spec.Arguments)
            {
                UseShellExecute = true,
                WindowStyle = a.WindowMode == WindowMode.Hidden ? System.Diagnostics.ProcessWindowStyle.Hidden : System.Diagnostics.ProcessWindowStyle.Normal,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is not null) await p.WaitForExitAsync(ctx.Token).ConfigureAwait(false);
        }
        else
        {
            await Task.Run(() => ProcessLauncher.Launch(spec, a.RunAsAdmin)).ConfigureAwait(false);
        }
    }

    private async Task HttpRequestAsync(HttpRequestAction a, ExecutionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(a.Url)) return;
        var cts = BeginCancellable(ctx.Token); // Esc cancels it
        try
        {
            var result = await HttpRequests.SendAsync(a, cts.Token).ConfigureAwait(false);
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
        if (a.Target != VolumeTarget.System)
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
        var device = await Task.Run(() => AudioDevices.Next(rotation, a.Communications)).ConfigureAwait(false);
        if (device is null)
        {
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.audioNone"), OsdKind.Warning, look: OsdLook.Of("HeadphonesSoundWave24"));
            return;
        }
        if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.audioOutput", device.Name), OsdKind.Success, look: OsdLook.Of("HeadphonesSoundWave24"));
        if (!string.IsNullOrWhiteSpace(a.Sound))
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
        if (!a.Countdown && !a.Wait)
        {
            _ui.ShowOsd(a.Text, OsdKind.Info, duration, OsdLook.Of("Chat24"));
            return;
        }
        var cts = BeginCancellable(ctx.Token);
        var osd = _ui.ShowPersistentOsd(a.Text, a.Countdown ? DateTime.Now + duration : null, "Chat24");
        var task = Task.Delay(duration, cts.Token).ContinueWith(_ => { osd.Dispose(); EndCancellable(cts); }, TaskScheduler.Default);
        if (a.Wait) await task.ConfigureAwait(false);
    }

    private async Task PowerAsync(PowerAction a, ExecutionContext ctx)
    {
        var name = PowerNames.Of(a.Mode);
        if (a.DelaySeconds > 0)
        {
            var cts = BeginCancellable(ctx.Token);
            var osd = a.ShowOsd ? _ui.ShowPersistentOsd(Loc.T("osd.powerCountdown", name), DateTime.Now.AddSeconds(a.DelaySeconds), "Power24") : null;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(a.DelaySeconds), cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _ui.ShowOsd(Loc.T("osd.powerCancelled", name), OsdKind.Warning, look: OsdLook.Of("Power24"));
                throw;
            }
            finally
            {
                osd?.Dispose();
                EndCancellable(cts);
            }
        }
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
                await RunAsync(target, TriggerSource.Command, ctx.Foreground, false, ctx.Depth + 1, ctx.Choreography, ctx.ChoreographyToken).ConfigureAwait(false);
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
                await RunEventAsync(rule, false, TriggerSource.Command, ctx.Foreground, ctx.Depth + 1, ctx.Choreography, ctx.ChoreographyToken).ConfigureAwait(false);
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
        var osd = _ui.ShowPersistentOsd(a.Message, a.Countdown ? DateTime.Now + duration : null, "Timer24");
        try
        {
            await Task.Delay(duration, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            osd.Dispose();
            EndCancellable(cts);
        }
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}

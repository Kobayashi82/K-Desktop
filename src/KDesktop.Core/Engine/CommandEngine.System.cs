using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Native;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Engine;

/// <summary>The actions that change the PC itself: other volumes, windows, clipboard, screens, power plan, theme, radios.</summary>
public sealed partial class CommandEngine
{
    // ── Volumen: micrófono y programas ───────────────────────────────────

    private void OtherVolume(VolumeAction a, ExecutionContext ctx)
    {
        var step = a.Value > 0 ? a.Value : VolumeAction.DefaultStep;
        LevelState Change(LevelState now) => a.Mode switch
        {
            VolumeMode.Set => new LevelState(a.Value, a.Value == 0 && now.Muted),
            VolumeMode.Up => new LevelState(now.Level + step, false),
            VolumeMode.Down => now with { Level = now.Level - step },
            VolumeMode.Mute => now with { Muted = true },
            VolumeMode.Unmute => now with { Muted = false },
            _ => now with { Muted = !now.Muted },
        };

        // A chosen speaker: like the default one, without the media keys (they only reach the default one).
        if (a.Target == VolumeTarget.System)
        {
            if (DeviceVolume.Change(a.Device, Change) is not { } speaker)
            {
                if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.deviceMissing", Or(a.DeviceName)), OsdKind.Warning, look: OsdLook.Of("SpeakerOff24"));
                return;
            }
            if (!a.ShowOsd) return;
            if (a.Mode is VolumeMode.ToggleMute or VolumeMode.Mute or VolumeMode.Unmute)
                _ui.ShowOsd(Loc.T(speaker.Muted ? "osd.muted" : "osd.unmuted"), look: new OsdLook(speaker.Muted ? "SpeakerMute24" : "Speaker224", Topic: OsdTopic.Levels));
            else
                _ui.ShowOsd(Loc.T("osd.volume", speaker.Level), look: OsdLook.Bar("Speaker224", speaker.Level));
            return;
        }

        if (a.Target == VolumeTarget.Microphone)
        {
            var chosen = a.Device.Length > 0;
            if ((chosen ? DeviceVolume.Change(a.Device, Change) : MicrophoneTools.Change(Change)) is not { } mic)
            {
                if (a.ShowOsd) _ui.ShowOsd(chosen ? Loc.T("osd.deviceMissing", Or(a.DeviceName)) : Loc.T("osd.noMicrophone"), OsdKind.Warning, look: OsdLook.Of("MicOff24"));
                return;
            }
            if (!a.ShowOsd) return;
            if (a.Mode is VolumeMode.ToggleMute or VolumeMode.Mute or VolumeMode.Unmute)
                _ui.ShowOsd(Loc.T(mic.Muted ? "osd.micMuted" : "osd.micOn"), mic.Muted ? OsdKind.Warning : OsdKind.Success, look: new OsdLook(mic.Muted ? "MicOff24" : "Mic24", Topic: OsdTopic.Levels));
            else
                _ui.ShowOsd(Loc.T("osd.micLevel", mic.Level), look: OsdLook.Bar("Mic24", mic.Level));
            return;
        }

        var process = a.Target == VolumeTarget.ForegroundApp ? ctx.Foreground.ProcessName : ProcessNames.Normalize(a.ProcessName);
        if (process.Length == 0 || (a.Target == VolumeTarget.ForegroundApp && ctx.Foreground.ProcessId == (uint)Environment.ProcessId)) return;
        if (AppVolume.Change(process, Change) is not { } app)
        {
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.appNoSound", process), OsdKind.Warning, look: OsdLook.Of("SpeakerOff24"));
            return;
        }
        if (!a.ShowOsd) return;
        _ui.ShowOsd(app.Muted ? Loc.T("osd.appMuted", process) : Loc.T("osd.appLevel", process, app.Level), app.Muted ? OsdKind.Warning : OsdKind.Info,
            look: app.Muted ? new OsdLook("SpeakerMute24", Topic: OsdTopic.Levels) : OsdLook.Bar("Speaker224", app.Level));
    }

    private static string Or(string name) => name.Length > 0 ? name : "?";

    // ── Disposición de ventanas ──────────────────────────────────────────

    /// <summary>«Disposición de ventanas» (E1): the windows of a disposición to their places; the OSD says what was not open.</summary>
    private async Task ArrangeAsync(ArrangementAction a, ExecutionContext ctx)
    {
        if (Config.FindArrangement(a.ArrangementId) is not { } arrangement)
        {
            _ui.ShowOsd(Loc.T("osd.arrangementMissing"), OsdKind.Warning, look: OsdLook.Of("WindowApps24"));
            return;
        }
        var result = await WindowArrangements.ApplyAsync(arrangement, path => ProcessLauncher.Launch(new LaunchSpec(path), asAdmin: false), ctx.Token).ConfigureAwait(false);
        if (!a.ShowOsd) return;
        if (result.Missing.Count > 0)
            _ui.ShowOsd(Loc.T("osd.arrangementNotOpen", arrangement.Name, string.Join(", ", result.Missing.Distinct(StringComparer.OrdinalIgnoreCase))), OsdKind.Warning,
                look: OsdLook.Of("WindowApps24"));
        else _ui.ShowOsd(Loc.T("osd.arrangementDone", arrangement.Name), OsdKind.Success, look: OsdLook.Of("WindowApps24"));
    }

    // ── Ventana ──────────────────────────────────────────────────────────

    private void Window(WindowAction a, ExecutionContext ctx)
    {
        var hwnd = a.FindWindow(ctx.Foreground.RootHandle);
        if (hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd))
        {
            if (a.Target == WindowTarget.Process) Log.Warn($"Ventana: {a.ProcessName} no tiene ninguna ventana abierta");
            return;
        }
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == (uint)Environment.ProcessId || hwnd == Win32.GetShellWindow()) return; // never K-Desktop or the desktop itself

        string? osd = null;
        switch (a.Operation)
        {
            case WindowOperation.Maximize:
                WindowLayout.Show(hwnd, Win32.SW_SHOWMAXIMIZED);
                break;
            case WindowOperation.Minimize:
                WindowLayout.Show(hwnd, Win32.SW_MINIMIZE);
                break;
            case WindowOperation.Restore:
                WindowLayout.Show(hwnd, Win32.SW_RESTORE);
                break;
            case WindowOperation.ToggleMaximize:
                WindowLayout.Show(hwnd, WindowLayout.IsMaximized(hwnd) ? Win32.SW_RESTORE : Win32.SW_SHOWMAXIMIZED);
                break;
            case WindowOperation.Snap:
                WindowLayout.MoveTo(hwnd, a.Place ?? WindowLayout.SnapRect(WindowLayout.WorkAreaOf(hwnd), a.Snap, a.X, a.Y, a.Width, a.Height));
                break;
            case WindowOperation.Center:
                WindowLayout.ShowNormal(hwnd); // minimised, it would measure its little box in the taskbar
                WindowLayout.MoveTo(hwnd, WindowLayout.Centered(WindowLayout.VisibleBounds(hwnd), WindowLayout.WorkAreaOf(hwnd)));
                break;
            case WindowOperation.MoveToMonitor:
                if (!WindowLayout.MoveToMonitor(hwnd, a.Monitor, a.MonitorNumber)) osd = Loc.T("osd.windowOneMonitor");
                break;
            case WindowOperation.Close:
                Win32.PostMessage(hwnd, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                break;
            case WindowOperation.Opacity:
                var opacity = WindowLayout.ToggleOpacity(hwnd, a.Opacity);
                osd = Loc.T("osd.windowOpacity", opacity);
                break;
        }
        // From the QuickMenu or the launcher, the window keeps the focus it had.
        if (a.Target == WindowTarget.Process && a.Operation is not (WindowOperation.Minimize or WindowOperation.Close)) WindowTools.ForceForeground(hwnd);
        if (a.ShowOsd) _ui.ShowOsd(osd ?? a.Summary, look: OsdLook.Of("WindowNew24"));
        else if (osd is not null && a.Operation == WindowOperation.MoveToMonitor) Log.Info("Ventana: solo hay un monitor");
    }

    // ── Portapapeles ─────────────────────────────────────────────────────

    private async Task ClipboardAsync(ClipboardAction a, ExecutionContext ctx)
    {
        if (a.Mode == ClipboardMode.Clear)
        {
            _ui.SetClipboardText(string.Empty);
            return;
        }
        if (a.Mode == ClipboardMode.ShowHistory)
        {
            _ui.ShowClipboardHistory(ctx.Foreground);
            return;
        }

        // From the QuickMenu, give the focus back to the window the menu was opened over (to copy and paste there).
        if (ctx.FromPopup && ctx.Foreground.Handle != IntPtr.Zero && (a.Paste || a.CopyFirst))
        {
            WindowTools.ForceForeground(ctx.Foreground.Handle);
            await Task.Delay(60, ctx.Token).ConfigureAwait(false);
        }

        string text;
        if (a.Mode == ClipboardMode.SetText)
        {
            text = TextVariables.Expand(a.Text);
        }
        else
        {
            if (a.CopyFirst && !await CopySelectionAsync(ctx.Token).ConfigureAwait(false))
            {
                _ui.ShowOsd(Loc.T("osd.clipboardNoSelection"), OsdKind.Warning, look: OsdLook.Of("ClipboardTextEdit24"));
                return;
            }
            var current = TextVariables.ClipboardText();
            if (current.Length == 0)
            {
                _ui.ShowOsd(Loc.T("osd.clipboardEmpty"), OsdKind.Warning, look: OsdLook.Of("ClipboardTextEdit24"));
                return;
            }
            text = ClipboardText.Transform(a.Mode, current) ?? current;
        }

        Gave(a, text);
        if (!_ui.SetClipboardText(text))
        {
            Log.Warn("Portapapeles: no se pudo escribir (otro programa lo tiene abierto)");
            return;
        }
        if (a.Paste)
        {
            await Task.Delay(40, ctx.Token).ConfigureAwait(false);
            InputSimulator.SendCombo("CTRL + V");
            await Task.Delay(60, ctx.Token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The text selected in the window the command was run over (empty: none), borrowing the clipboard: it is put back
    /// as it was.
    /// </summary>
    private static async Task<string> CopySelectionTextAsync(ExecutionContext ctx)
    {
        if (ctx.FromPopup && ctx.Foreground.Handle != IntPtr.Zero)
        {
            WindowTools.ForceForeground(ctx.Foreground.Handle);
            await Task.Delay(60, ctx.Token).ConfigureAwait(false);
        }
        var saved = ClipboardSnapshot.Take();
        try
        {
            return await CopySelectionAsync(ctx.Token).ConfigureAwait(false) ? TextVariables.ClipboardText() : string.Empty;
        }
        finally
        {
            saved?.Restore();
        }
    }

    /// <summary>Ctrl+C and waits until the clipboard changes (the program copied something).</summary>
    private static async Task<bool> CopySelectionAsync(CancellationToken token)
    {
        var before = Win32.GetClipboardSequenceNumber();
        InputSimulator.SendCombo("CTRL + C");
        for (var i = 0; i < 25; i++)
        {
            await Task.Delay(30, token).ConfigureAwait(false);
            if (Win32.GetClipboardSequenceNumber() != before)
            {
                await Task.Delay(30, token).ConfigureAwait(false); // let it finish writing every format
                return true;
            }
        }
        return false;
    }

    // ── Despertar equipo ─────────────────────────────────────────────────

    private async Task WakeOnLanAsync(WakeOnLanAction a, ExecutionContext ctx)
    {
        var name = string.IsNullOrWhiteSpace(a.Host) ? a.Mac.Trim() : a.Host.Trim();
        if (WakeOnLan.ParseMac(a.Mac) is not { } mac)
        {
            Log.Warn($"Despertar equipo: la MAC «{a.Mac}» no es válida");
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.wolBadMac"), OsdKind.Warning, look: OsdLook.Of("DesktopPulse24"));
            return;
        }
        var sent = await WakeOnLan.SendAsync(mac, a.Address, a.Port, ctx.Token).ConfigureAwait(false);
        Log.Info($"Despertar equipo: paquete mágico para {WakeOnLan.FormatMac(mac)} enviado a {sent} destino(s)");
        if (a.WaitSeconds <= 0 || string.IsNullOrWhiteSpace(a.Host))
        {
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.wolSent", name), OsdKind.Success, look: OsdLook.Of("DesktopPulse24"));
            return;
        }

        var cts = BeginCancellable(ctx.Token); // Esc stops waiting (and the command)
        var osd = a.ShowOsd ? _ui.ShowPersistentOsd(Loc.T("osd.wolWaiting", name), DateTime.Now.AddSeconds(a.WaitSeconds), "DesktopPulse24") : null;
        try
        {
            var awake = await WakeOnLan.WaitForHostAsync(a.Host, TimeSpan.FromSeconds(a.WaitSeconds), cts.Token).ConfigureAwait(false);
            osd?.Dispose();
            osd = null;
            if (!awake)
            {
                Log.Warn($"Despertar equipo: {name} no respondió en {a.WaitSeconds} s");
                if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.wolNoAnswer", name, a.WaitSeconds), OsdKind.Warning, look: OsdLook.Of("DesktopPulse24"));
                throw new OperationCanceledException(); // the rest of the command needs it awake
            }
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.wolAwake", name), OsdKind.Success, look: OsdLook.Of("DesktopPulse24"));
        }
        // Stopped while waiting (not «it did not answer», which ends the countdown itself).
        catch (OperationCanceledException) when (osd is not null && cts.IsCancellationRequested)
        {
            CountdownCancelled();
            throw;
        }
        finally
        {
            osd?.Dispose();
            EndCancellable(cts);
        }
    }

    // ── Pantalla ─────────────────────────────────────────────────────────

    private async Task DisplayAsync(DisplayAction a)
    {
        switch (a.Operation)
        {
            // Some of them only (all ticked is the same as none: Windows turns them off for real).
            case DisplayOperation.MonitorOff when DisplayTools.Monitors().Any(m => !a.OffMonitors.Contains(m.DeviceName)) && a.OffMonitors.Count > 0:
                _ui.BlankMonitors([.. a.OffMonitors]);
                break;

            case DisplayOperation.MonitorOff:
                // A moment for the hand to leave the keyboard: the key release would wake them up again.
                await Task.Delay(600).ConfigureAwait(false);
                DisplayTools.MonitorOff();
                break;

            case DisplayOperation.Projection:
                if (await Task.Run(() => DisplayTools.Project(a.Projection)).ConfigureAwait(false) && a.ShowOsd)
                    _ui.ShowOsd(Loc.T($"enum.projection.{a.Projection}"), look: OsdLook.Of("Desktop24"));
                break;

            case DisplayOperation.Brightness:
                var level = await Task.Run(() => DisplayTools.ChangeBrightness(a.BrightnessMode, a.Brightness)).ConfigureAwait(false);
                if (level is null)
                {
                    Log.Warn("Brillo: ninguna pantalla deja cambiarlo (los monitores externos necesitan DDC/CI activado en su menú)");
                    if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.brightnessNone"), OsdKind.Warning, look: OsdLook.Of("BrightnessHigh24"));
                }
                else if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.brightness", level.Value), look: OsdLook.Bar("BrightnessHigh24", level.Value));
                break;

            case DisplayOperation.Mode:
                if (string.IsNullOrWhiteSpace(a.Monitor)) return;
                var mode = await Task.Run(() => DisplayTools.SetMode(a.Monitor, a.Width, a.Height, a.Frequency)).ConfigureAwait(false);
                if (mode is null)
                {
                    if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.displayModeFailed", DisplayAction.DescribeMode(a.Width, a.Height, a.Frequency)), OsdKind.Warning, look: OsdLook.Of("Desktop24"));
                }
                else if (a.ShowOsd) _ui.ShowOsd(mode.ToString(), OsdKind.Success, look: OsdLook.Of("Desktop24"));
                break;
        }
    }

    // ── Plan de energía, tema y radios ───────────────────────────────────

    private void PowerPlan(PowerPlanAction a)
    {
        PowerPlan? plan;
        if (a.Mode == PowerPlanMode.Cycle) plan = PowerPlans.Next();
        else if (Guid.TryParse(a.PlanId, out var id)) plan = PowerPlans.All().FirstOrDefault(p => p.Id == id);
        else plan = null;
        if (plan is null)
        {
            Log.Warn($"Plan de energía: «{a.PlanName}» ya no existe");
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.powerPlanMissing", a.PlanName), OsdKind.Warning, look: OsdLook.Of("Battery1024"));
            return;
        }
        if (PowerPlans.SetActive(plan.Id) && a.ShowOsd) _ui.ShowOsd(Loc.T("osd.powerPlan", plan.Name), OsdKind.Success, look: OsdLook.Of("Battery1024"));
    }

    private void WindowsTheme(WindowsThemeAction a)
    {
        if (!a.Apps && !a.System) return;
        var light = a.Mode switch
        {
            ThemeChoice.Light => true,
            ThemeChoice.Dark => false,
            _ => !Platform.WindowsTheme.AppsLight(),
        };
        Platform.WindowsTheme.Set(light, a.Apps, a.System);
        if (a.ShowOsd) _ui.ShowOsd(Loc.T(light ? "osd.themeLight" : "osd.themeDark"), look: OsdLook.Of(light ? "WeatherSunny24" : "WeatherMoon24"));
    }

    private void Radio(RadioAction a)
    {
        var name = Loc.T($"enum.radio.{a.Radio}");
        bool? on;
        try { on = Radios.Switch(a.Radio, a.Mode); }
        catch (Exception ex)
        {
            Log.Error($"No se pudo cambiar {name}", ex);
            on = null;
        }
        if (!a.ShowOsd) return;
        if (on is { } state) _ui.ShowOsd(Loc.T(state ? "osd.radioOn" : "osd.radioOff", name), state ? OsdKind.Success : OsdKind.Info, look: OsdLook.Of(a.Radio == RadioDevice.Bluetooth ? "Bluetooth24" : "Wifi124"));
        else _ui.ShowOsd(Loc.T("osd.radioNone", name), OsdKind.Warning, look: OsdLook.Of(a.Radio == RadioDevice.Bluetooth ? "Bluetooth24" : "Wifi124"));
    }

    // ── Multimedia ───────────────────────────────────────────────────────

    private async Task MediaAsync(MediaAction a, ExecutionContext ctx)
    {
        var op = a.Control switch
        {
            MediaControl.Play => MediaOp.Play,
            MediaControl.Pause => MediaOp.Pause,
            MediaControl.Next => MediaOp.Next,
            MediaControl.Previous => MediaOp.Previous,
            MediaControl.Stop => MediaOp.Stop,
            _ => MediaOp.Toggle,
        };
        var app = TextVariables.Expand(a.App).Trim();
        IReadOnlyList<MediaNow> done;
        try { done = await MediaSessions.ControlAsync(op, app.Length == 0 ? null : app, ctx.Token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Multimedia: no se han podido usar los controles de Windows ({ex.Message})");
            done = [];
        }
        var icon = MediaIcon(a.Control);
        if (done.Count == 0)
        {
            // A player that does not tell Windows what it plays still answers the media keys (they reach any player).
            var sent = app.Length == 0 && await Task.Run(() => MediaKeys(a.Control), ctx.Token).ConfigureAwait(false);
            if (!a.ShowOsd) return;
            if (sent) _ui.ShowOsd(Loc.T($"enum.mediaControl.{a.Control}"), look: OsdLook.Of(icon));
            else _ui.ShowOsd(app.Length == 0 ? Loc.T("osd.mediaNone") : Loc.T("osd.mediaNoApp", app), OsdKind.Warning, look: OsdLook.Of("PlayCircle24"));
            return;
        }
        if (!a.ShowOsd) return;
        if (a.Control is MediaControl.Pause or MediaControl.Stop)
        {
            _ui.ShowOsd($"{Loc.T($"enum.mediaControl.{a.Control}")}  ·  {string.Join(", ", done.Select(d => d.App).Distinct())}", look: OsdLook.Of(icon));
            return;
        }
        // What plays now (another song after «siguiente»), a moment later, when the player has said it.
        var player = done[0];
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(700).ConfigureAwait(false);
                var now = (await MediaSessions.ListAsync().ConfigureAwait(false)).FirstOrDefault(m => m.AppId == player.AppId) ?? player;
                var paused = a.Control == MediaControl.Toggle && now.State != MediaState.Playing;
                var song = string.Join(" — ", new[] { now.Title, now.Artist }.Where(t => !string.IsNullOrEmpty(t)));
                var text = paused || song.Length == 0
                    ? $"{Loc.T(paused ? "enum.mediaControl.Pause" : $"enum.mediaControl.{a.Control}")}  ·  {now.App}"
                    : $"{song}  ·  {now.App}";
                _ui.ShowOsd(text, look: OsdLook.Of(paused ? "Pause24" : icon));
            }
            catch (Exception ex) { Log.Warn($"Multimedia: no se ha podido leer qué suena ({ex.Message})"); }
        });
    }

    private static string MediaIcon(MediaControl control) => control switch
    {
        MediaControl.Play => "Play24",
        MediaControl.Pause => "Pause24",
        MediaControl.Next => "Next24",
        MediaControl.Previous => "Previous24",
        MediaControl.Stop => "Stop24",
        _ => "PlayCircle24",
    };

    private const int MediaStopKey = 0xB2;

    /// <summary>The media key for <paramref name="control"/>; false when nothing was sent (pause with nothing sounding…).</summary>
    private static bool MediaKeys(MediaControl control)
    {
        switch (control)
        {
            case MediaControl.Play: return MediaPlayback.Resume();
            case MediaControl.Pause: return MediaPlayback.Pause();
            case MediaControl.Next: MediaPlayback.Next(); return true;
            case MediaControl.Previous: MediaPlayback.Previous(); return true;
            case MediaControl.Stop: InputSimulator.KeyPress(MediaStopKey); return true;
            default: MediaPlayback.PlayPause(); return true;
        }
    }

    // ── Enviar al móvil ──────────────────────────────────────────────────

    private void SendToPhone(SendToPhoneAction a)
    {
        var sent = Remote.Web.PhoneInbox.Current.Send(TextVariables.Expand(a.Title), TextVariables.Expand(a.Text), TextVariables.Expand(a.Url),
            TextVariables.Expand(a.File), "K-Desktop", TextVariables.Expand(a.Device));
        if (sent.Reason is { } reason)
        {
            Log.Warn($"Enviar al móvil: no se envió ({reason})");
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.phone." + reason), OsdKind.Warning, look: OsdLook.Of("Phone24"));
            return;
        }
        if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.phone.sent", string.Join(", ", sent.Names)), OsdKind.Success, look: OsdLook.Of("Phone24"));
    }

    // ── Mantener despierto ───────────────────────────────────────────────

    private void StayAwake(KeepAwakeAction a)
    {
        var on = a.Mode switch
        {
            SwitchMode.On => true,
            SwitchMode.Off => false,
            _ => !Awake.IsOn,
        };
        if (!on)
        {
            if (Awake.Stop() && a.ShowOsd) _ui.ShowOsd(Loc.T("osd.awakeOff"), look: OsdLook.Of("WeatherMoon24"));
            return;
        }
        var app = a.For == AwakeFor.WhileApp ? ProcessNames.Normalize(TextVariables.Expand(a.ProcessName)) : null;
        if (app is not null && (app.Length == 0 || !ProcessSnapshot.IsRunning(app)))
        {
            Log.Info($"Mantener despierto: «{app}» no está abierto");
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.awakeNoApp", app.Length == 0 ? "?" : app), OsdKind.Warning, look: OsdLook.Of("DrinkCoffee24"));
            return;
        }
        TimeSpan? duration = a.For == AwakeFor.Minutes ? TimeSpan.FromMinutes(Math.Max(1, a.Minutes)) : null;
        if (!Awake.Start(duration, app, a.Screen))
        {
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.awakeFailed"), OsdKind.Warning, look: OsdLook.Of("DrinkCoffee24"));
            return;
        }
        if (a.ShowOsd) _ui.ShowOsd(DescribeAwake(Awake), OsdKind.Success, look: OsdLook.Of("DrinkCoffee24"));
    }

    /// <summary>«Despierto hasta las 15:30», «… mientras vlc esté abierto», «… hasta que lo apagues» (OSD and tray).</summary>
    public static string DescribeAwake(KeepAwake awake) =>
        awake.Until is { } until ? Loc.T("osd.awakeUntil", until.ToString("t", Loc.Instance.Culture))
        : awake.WhileApp is { } app ? Loc.T("osd.awakeWhileApp", app)
        : Loc.T("osd.awakeUntilOff");
}

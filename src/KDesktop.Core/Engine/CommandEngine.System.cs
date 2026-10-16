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

        if (a.Target == VolumeTarget.Microphone)
        {
            if (MicrophoneTools.Change(Change) is not { } mic)
            {
                if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.noMicrophone"), OsdKind.Warning);
                return;
            }
            if (!a.ShowOsd) return;
            if (a.Mode is VolumeMode.ToggleMute or VolumeMode.Mute or VolumeMode.Unmute)
                _ui.ShowOsd(Loc.T(mic.Muted ? "osd.micMuted" : "osd.micOn"), mic.Muted ? OsdKind.Warning : OsdKind.Success);
            else
                _ui.ShowOsd(Loc.T("osd.micLevel", mic.Level));
            return;
        }

        var process = a.Target == VolumeTarget.ForegroundApp ? ctx.Foreground.ProcessName : ProcessNames.Normalize(a.ProcessName);
        if (process.Length == 0 || (a.Target == VolumeTarget.ForegroundApp && ctx.Foreground.ProcessId == (uint)Environment.ProcessId)) return;
        if (AppVolume.Change(process, Change) is not { } app)
        {
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.appNoSound", process), OsdKind.Warning);
            return;
        }
        if (!a.ShowOsd) return;
        _ui.ShowOsd(app.Muted ? Loc.T("osd.appMuted", process) : Loc.T("osd.appLevel", process, app.Level), app.Muted ? OsdKind.Warning : OsdKind.Info);
    }

    // ── Ventana ──────────────────────────────────────────────────────────

    private void Window(WindowAction a, ExecutionContext ctx)
    {
        var hwnd = WindowLayout.Find(a.Target, a.ProcessName, ctx.Foreground.RootHandle);
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
                var area = WindowLayout.WorkAreaOf(hwnd);
                WindowLayout.MoveTo(hwnd, WindowLayout.SnapRect(area, a.Snap, a.X, a.Y, a.Width, a.Height));
                break;
            case WindowOperation.Center:
                if (WindowLayout.IsMaximized(hwnd)) WindowLayout.Show(hwnd, Win32.SW_RESTORE);
                WindowLayout.MoveTo(hwnd, WindowLayout.Centered(WindowLayout.VisibleBounds(hwnd), WindowLayout.WorkAreaOf(hwnd)));
                break;
            case WindowOperation.MoveToMonitor:
                if (!WindowLayout.MoveToMonitor(hwnd, a.Monitor, a.MonitorNumber)) osd = Loc.T("osd.windowOneMonitor");
                break;
            case WindowOperation.Opacity:
                var opacity = WindowLayout.ToggleOpacity(hwnd, a.Opacity);
                osd = Loc.T("osd.windowOpacity", opacity);
                break;
        }
        // From the QuickMenu or the launcher, the window keeps the focus it had.
        if (a.Target == WindowTarget.Process && a.Operation is not WindowOperation.Minimize) WindowTools.ForceForeground(hwnd);
        if (a.ShowOsd) _ui.ShowOsd(osd ?? a.Summary);
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
                _ui.ShowOsd(Loc.T("osd.clipboardNoSelection"), OsdKind.Warning);
                return;
            }
            var current = TextVariables.ClipboardText();
            if (current.Length == 0)
            {
                _ui.ShowOsd(Loc.T("osd.clipboardEmpty"), OsdKind.Warning);
                return;
            }
            text = ClipboardText.Transform(a.Mode, current) ?? current;
        }

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
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.wolBadMac"), OsdKind.Warning);
            return;
        }
        var sent = await WakeOnLan.SendAsync(mac, a.Address, a.Port, ctx.Token).ConfigureAwait(false);
        Log.Info($"Despertar equipo: paquete mágico para {WakeOnLan.FormatMac(mac)} enviado a {sent} destino(s)");
        if (a.WaitSeconds <= 0 || string.IsNullOrWhiteSpace(a.Host))
        {
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.wolSent", name), OsdKind.Success);
            return;
        }

        var cts = BeginCancellable(ctx.Token); // Esc stops waiting (and the command)
        var osd = a.ShowOsd ? _ui.ShowPersistentOsd(Loc.T("osd.wolWaiting", name), DateTime.Now.AddSeconds(a.WaitSeconds)) : null;
        try
        {
            var awake = await WakeOnLan.WaitForHostAsync(a.Host, TimeSpan.FromSeconds(a.WaitSeconds), cts.Token).ConfigureAwait(false);
            osd?.Dispose();
            osd = null;
            if (!awake)
            {
                Log.Warn($"Despertar equipo: {name} no respondió en {a.WaitSeconds} s");
                if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.wolNoAnswer", name, a.WaitSeconds), OsdKind.Warning);
                throw new OperationCanceledException(); // the rest of the command needs it awake
            }
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.wolAwake", name), OsdKind.Success);
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
            case DisplayOperation.MonitorOff:
                // A moment for the hand to leave the keyboard: the key release would wake them up again.
                await Task.Delay(600).ConfigureAwait(false);
                DisplayTools.MonitorOff();
                break;

            case DisplayOperation.Projection:
                if (await Task.Run(() => DisplayTools.Project(a.Projection)).ConfigureAwait(false) && a.ShowOsd)
                    _ui.ShowOsd(Loc.T($"enum.projection.{a.Projection}"));
                break;

            case DisplayOperation.Brightness:
                var level = await Task.Run(() => DisplayTools.ChangeBrightness(a.BrightnessMode, a.Brightness)).ConfigureAwait(false);
                if (level is null)
                {
                    Log.Warn("Brillo: ninguna pantalla deja cambiarlo (los monitores externos necesitan DDC/CI activado en su menú)");
                    if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.brightnessNone"), OsdKind.Warning);
                }
                else if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.brightness", level.Value));
                break;

            case DisplayOperation.Mode:
                if (string.IsNullOrWhiteSpace(a.Monitor)) return;
                var mode = await Task.Run(() => DisplayTools.SetMode(a.Monitor, a.Width, a.Height, a.Frequency)).ConfigureAwait(false);
                if (mode is null)
                {
                    if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.displayModeFailed", DisplayAction.DescribeMode(a.Width, a.Height, a.Frequency)), OsdKind.Warning);
                }
                else if (a.ShowOsd) _ui.ShowOsd(mode.ToString(), OsdKind.Success);
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
            if (a.ShowOsd) _ui.ShowOsd(Loc.T("osd.powerPlanMissing", a.PlanName), OsdKind.Warning);
            return;
        }
        if (PowerPlans.SetActive(plan.Id) && a.ShowOsd) _ui.ShowOsd(Loc.T("osd.powerPlan", plan.Name), OsdKind.Success);
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
        if (a.ShowOsd) _ui.ShowOsd(Loc.T(light ? "osd.themeLight" : "osd.themeDark"));
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
        if (on is { } state) _ui.ShowOsd(Loc.T(state ? "osd.radioOn" : "osd.radioOff", name), state ? OsdKind.Success : OsdKind.Info);
        else _ui.ShowOsd(Loc.T("osd.radioNone", name), OsdKind.Warning);
    }
}

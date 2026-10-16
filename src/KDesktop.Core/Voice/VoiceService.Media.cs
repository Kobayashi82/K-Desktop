using System.Diagnostics;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Voice;

public sealed partial class VoiceService
{
    /// <summary>Runs a built-in media order; returns what to tell the user.</summary>
    private async Task<(string Message, OsdKind Kind)> RunMediaAsync(MediaOrder order)
    {
        var settings = _config().Settings;
        try
        {
            switch (order.Kind)
            {
                case MediaOrderKind.Pause:
                    return await Task.Run(MediaPlayback.Pause).ConfigureAwait(false)
                        ? (Loc.T("voice.media.done.Pause"), OsdKind.Success)
                        : (Loc.T("voice.media.nothingPlaying"), OsdKind.Info);
                case MediaOrderKind.Resume:
                    return await ResumeAsync().ConfigureAwait(false);
                case MediaOrderKind.Next:
                    MediaPlayback.Next();
                    return (Loc.T("voice.media.done.Next"), OsdKind.Success);
                case MediaOrderKind.Previous:
                    MediaPlayback.Previous();
                    return (Loc.T("voice.media.done.Previous"), OsdKind.Success);
                case MediaOrderKind.VolumeUp or MediaOrderKind.VolumeDown:
                    var step = order.Value ?? StepFor(order.Amount, settings.VoiceMediaStep);
                    var level = await Task.Run(() => AudioTools.StepVolume(order.Kind == MediaOrderKind.VolumeUp ? step : -step)).ConfigureAwait(false);
                    return (Loc.T("osd.volume", level), OsdKind.Success);
                case MediaOrderKind.VolumeSet:
                    var set = await Task.Run(() => AudioTools.SetVolume(order.Value ?? 0)).ConfigureAwait(false);
                    return (Loc.T("osd.volume", set), OsdKind.Success);
                case MediaOrderKind.Mute or MediaOrderKind.Unmute:
                    var mute = order.Kind == MediaOrderKind.Mute;
                    await Task.Run(() => AudioTools.SetMute(mute)).ConfigureAwait(false);
                    return (Loc.T(mute ? "osd.muted" : "osd.unmuted"), OsdKind.Success);
                case MediaOrderKind.MusicOn:
                    return await MusicOnAsync(settings).ConfigureAwait(false);
                case MediaOrderKind.MusicOff:
                    return await MusicOffAsync(settings).ConfigureAwait(false);
                case MediaOrderKind.Cancel:
                    return CancelPending?.Invoke() == true
                        ? (Loc.T("voice.media.done.Cancel"), OsdKind.Success)
                        : (Loc.T("voice.media.nothingToCancel"), OsdKind.Info);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error en la orden multimedia {order.Kind}", ex);
        }
        return (Loc.T("voice.media.error"), OsdKind.Warning);
    }

    /// <summary>Cancels the countdowns and waits in progress (as Escape does); false when there was none.</summary>
    public Func<bool>? CancelPending { get; set; }

    /// <summary>«un poco» is half the usual step, «mucho» twice it.</summary>
    private static int StepFor(MediaAmount amount, int step)
    {
        step = Math.Clamp(step, 1, 100);
        return amount switch
        {
            MediaAmount.Little => Math.Max(1, step / 2),
            MediaAmount.Lot => Math.Min(100, step * 2),
            _ => step,
        };
    }

    private static async Task<(string, OsdKind)> ResumeAsync() =>
        await Task.Run(MediaPlayback.Resume).ConfigureAwait(false)
            ? (Loc.T("voice.media.done.Resume"), OsdKind.Success)
            : (Loc.T("voice.media.alreadyPlaying"), OsdKind.Info);

    /// <summary>
    /// «Pon música»: the chosen command; or the chosen player, opened when it is closed and made to play (resumed when
    /// it was paused); with nothing chosen, resumes whatever player there is.
    /// </summary>
    private async Task<(string, OsdKind)> MusicOnAsync(AppSettings settings)
    {
        switch (settings.VoiceMusicOn)
        {
            case MusicHandler.Command:
                return await RunMusicCommandAsync(settings.VoiceMusicOnCommand).ConfigureAwait(false);
            case MusicHandler.Program when settings.VoiceMusicOnProgram.Trim() is { Length: > 0 } path:
                var name = PlayerProcess(path);
                if (IsRunning(name))
                {
                    if (MediaPlayback.IsPlaying(name) == true) return (Loc.T("voice.media.alreadyPlaying"), OsdKind.Info);
                    await PlayAsync(name).ConfigureAwait(false);
                    return (Loc.T("voice.media.done.Resume"), OsdKind.Success);
                }
                await _runAction(new RunAppAction { Path = path }).ConfigureAwait(false);
                _ = Task.Run(() => PlayOnceOpenAsync(name));
                return (Loc.T("voice.media.done.MusicOn"), OsdKind.Success);
            default:
                return await ResumeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// «Cierra la música» closes the player (pausing is «para», «detén», «pausa»…): the chosen process, or the player
    /// of «pon música»; or runs the chosen command. With no player chosen there is nothing to close, so it pauses.
    /// </summary>
    private async Task<(string, OsdKind)> MusicOffAsync(AppSettings settings)
    {
        switch (settings.VoiceMusicOff)
        {
            case MusicHandler.Command:
                return await RunMusicCommandAsync(settings.VoiceMusicOffCommand).ConfigureAwait(false);
            case MusicHandler.Program when ProcessNames.Normalize(settings.VoiceMusicOffProcess) is { Length: > 0 } process:
                return await CloseAsync(process).ConfigureAwait(false);
            case not MusicHandler.Command when settings.VoiceMusicOn == MusicHandler.Program && settings.VoiceMusicOnProgram.Trim() is { Length: > 0 } path:
                return await CloseAsync(PlayerProcess(path)).ConfigureAwait(false);
        }
        return await Task.Run(MediaPlayback.Pause).ConfigureAwait(false)
            ? (Loc.T("voice.media.done.Pause"), OsdKind.Success)
            : (Loc.T("voice.media.nothingPlaying"), OsdKind.Info);
    }

    private async Task<(string, OsdKind)> CloseAsync(string process)
    {
        if (!IsRunning(process)) return (Loc.T("voice.media.notRunning", process), OsdKind.Info);
        await _runAction(new AppControlAction { Mode = AppControlMode.Close, ProcessName = process }).ConfigureAwait(false);
        return IsRunning(process)
            ? (Loc.T("voice.media.notClosed", process), OsdKind.Warning)
            : (Loc.T("voice.media.done.MusicOff"), OsdKind.Success);
    }

    /// <summary>The process name of the player (the target of a shortcut when it is one).</summary>
    private static string PlayerProcess(string path) =>
        ProcessNames.Normalize(Path.GetFileName(ShellTools.ShortcutTarget(path) ?? path));

    /// <summary>
    /// Makes the player sound: play / pause to its window; if after a while it still doesn't (it ignores that message)
    /// and nothing else sounds, the media key, which goes to the player in use.
    /// </summary>
    private static async Task PlayAsync(string name)
    {
        if (MediaPlayback.PlayPauseIn(name) && await SoundsWithinAsync(name, 2500).ConfigureAwait(false)) return;
        if (MediaPlayback.IsPlaying() == false) MediaPlayback.PlayPause();
    }

    /// <summary>A player just opened: once its window is up, if it hasn't started on its own, it is made to play.</summary>
    private static async Task PlayOnceOpenAsync(string name)
    {
        try
        {
            var watch = Stopwatch.StartNew();
            while (!HasWindow(name))
            {
                if (watch.Elapsed > TimeSpan.FromSeconds(20)) return;
                await Task.Delay(250).ConfigureAwait(false);
            }
            if (await SoundsWithinAsync(name, 2500).ConfigureAwait(false)) return; // it plays on its own
            await PlayAsync(name).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn($"No se pudo poner a sonar {name}: {ex.Message}");
        }
    }

    private static async Task<bool> SoundsWithinAsync(string name, int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            if (MediaPlayback.IsPlaying(name, 200) == true) return true;
            await Task.Delay(100).ConfigureAwait(false);
        }
        return false;
    }

    private static bool HasWindow(string name)
    {
        var processes = WindowTools.FindProcesses(name);
        try
        {
            return processes.Any(p => p.MainWindowHandle != IntPtr.Zero) ||
                   WindowTools.TopLevelWindows(processes.Select(p => (uint)p.Id).ToHashSet()).Count > 0;
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }
    }

    private async Task<(string, OsdKind)> RunMusicCommandAsync(string? id)
    {
        if (_config().FindCommand(id) is not { } command) return (Loc.T("voice.media.noCommand"), OsdKind.Warning);
        await _run(command).ConfigureAwait(false);
        return (command.Name, OsdKind.Success);
    }

    private static bool IsRunning(string name)
    {
        var processes = WindowTools.FindProcesses(name);
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }
}

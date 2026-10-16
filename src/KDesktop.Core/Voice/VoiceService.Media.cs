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
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error en la orden multimedia {order.Kind}", ex);
        }
        return (Loc.T("voice.media.error"), OsdKind.Warning);
    }

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
    /// «Pon música»: the chosen command, or the chosen player (when it is open already, it just resumes); with nothing
    /// chosen, resumes whatever player there is.
    /// </summary>
    private async Task<(string, OsdKind)> MusicOnAsync(AppSettings settings)
    {
        switch (settings.VoiceMusicOn)
        {
            case MusicHandler.Command:
                return await RunMusicCommandAsync(settings.VoiceMusicOnCommand).ConfigureAwait(false);
            case MusicHandler.Program when settings.VoiceMusicOnProgram.Trim() is { Length: > 0 } path:
                var name = ProcessNames.Normalize(Path.GetFileName(path));
                if (IsRunning(name)) return await ResumeAsync().ConfigureAwait(false);
                await _runAction(new RunAppAction { Path = path }).ConfigureAwait(false);
                return (Loc.T("voice.media.done.MusicOn"), OsdKind.Success);
            default:
                return await ResumeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>«Quita la música»: the chosen command, or closes the chosen player; with nothing chosen, pauses.</summary>
    private async Task<(string, OsdKind)> MusicOffAsync(AppSettings settings)
    {
        switch (settings.VoiceMusicOff)
        {
            case MusicHandler.Command:
                return await RunMusicCommandAsync(settings.VoiceMusicOffCommand).ConfigureAwait(false);
            case MusicHandler.Program:
                // Without a process of its own, the player that «pon música» opens.
                var name = ProcessNames.Normalize(settings.VoiceMusicOffProcess);
                if (name.Length == 0 && settings.VoiceMusicOn == MusicHandler.Program)
                    name = ProcessNames.Normalize(Path.GetFileName(settings.VoiceMusicOnProgram.Trim()));
                if (name.Length == 0) break;
                if (!IsRunning(name)) return (Loc.T("voice.media.notRunning", name), OsdKind.Info);
                await _runAction(new AppControlAction { Mode = AppControlMode.Close, ProcessName = name }).ConfigureAwait(false);
                return (Loc.T("voice.media.done.MusicOff"), OsdKind.Success);
        }
        return await Task.Run(MediaPlayback.Pause).ConfigureAwait(false)
            ? (Loc.T("voice.media.done.Pause"), OsdKind.Success)
            : (Loc.T("voice.media.nothingPlaying"), OsdKind.Info);
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

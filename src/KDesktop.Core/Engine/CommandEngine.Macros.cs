using System.Collections.Concurrent;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Engine;

public sealed partial class CommandEngine
{
    private readonly ConcurrentDictionary<CancellationTokenSource, byte> _macros = new();

    /// <summary>True while a macro is playing (SHIFT + Escape stops it).</summary>
    public bool IsPlayingMacro => !_macros.IsEmpty;

    /// <summary>Stops every macro that is playing.</summary>
    public void StopMacros()
    {
        foreach (var cts in _macros.Keys)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    private async Task PlayMacroAsync(PlayMacroAction a, ExecutionContext ctx)
    {
        if (Config.FindMacro(a.MacroId) is not { } macro) return;

        // From the QuickMenu, give the focus back to the window the menu was opened over.
        if (ctx.FromPopup && ctx.Foreground.Handle != IntPtr.Zero)
        {
            WindowTools.ForceForeground(ctx.Foreground.Handle);
            await Task.Delay(80, ctx.Token).ConfigureAwait(false);
        }
        var times = a.UntilStopped ? int.MaxValue : Math.Max(1, a.Repeat);
        await PlayMacroAsync(macro, times, a.Speed, a.RepeatDelay, a.ShowOsd, ctx.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Plays a macro <paramref name="times"/> times (int.MaxValue = until stopped). SHIFT + Escape stops it and,
    /// inside a command, also the rest of the command.
    /// </summary>
    public async Task PlayMacroAsync(Macro macro, int times, double speed, int repeatDelay, bool showOsd, CancellationToken token)
    {
        MacroStep[] steps;
        try { steps = macro.Steps.Select(s => s.Clone()).ToArray(); }
        catch (InvalidOperationException) { return; } // edited right now in the editor
        if (steps.Length == 0) return;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _macros[cts] = 0;
        IDisposable? osd = null;
        try
        {
            for (var i = 0; i < times; i++)
            {
                if (showOsd)
                {
                    var text = times == int.MaxValue ? Loc.T("osd.macroLoop", macro.Name, i + 1)
                        : times > 1 ? Loc.T("osd.macroRepeat", macro.Name, i + 1, times)
                        : Loc.T("osd.macro", macro.Name);
                    var previous = osd;
                    osd = _ui.ShowPersistentOsd(text, icon: "Record24");
                    previous?.Dispose();
                }
                await MacroPlayer.PlayAsync(steps, speed, macro.GameMode, cts.Token).ConfigureAwait(false);
                if (i + 1 < times)
                    await Task.Delay(Math.Max(repeatDelay, times == int.MaxValue ? 20 : 0), cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !token.IsCancellationRequested)
        {
            _ui.ShowOsd(Loc.T("osd.macroStopped", macro.Name), OsdKind.Warning, look: OsdLook.Of("Record24"));
            throw;
        }
        finally
        {
            osd?.Dispose();
            _macros.TryRemove(cts, out _);
            cts.Dispose();
        }
    }
}

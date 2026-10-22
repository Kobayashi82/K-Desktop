using KDesktop.Assistant.Brain;
using KDesktop.Assistant.Brain.Talk;
using KDesktop.Assistant.Host.World;
using KDesktop.Core;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>What <see cref="ModuleGames"/> looks at, so the tests can play a game without one.</summary>
/// <param name="Enabled">Options → Brain → «Liberar la gráfica al jugar».</param>
/// <param name="Loaded">A model of this PC is in the graphics card.</param>
/// <param name="Answering">An assistant is answering (the brain itself keeps the card busy then).</param>
/// <param name="LastActive">When an order last started or ended.</param>
/// <param name="Screen">Whether something covers a whole monitor, and whether it is an exclusive game.</param>
/// <param name="Load">The graphics card's use (%), null when it cannot be read (not NVIDIA).</param>
/// <param name="Free">Takes the models of this PC out of the graphics card.</param>
internal sealed record GameProbe(Func<bool> Enabled, Func<bool> Loaded, Func<bool> Answering, Func<DateTimeOffset> LastActive,
    Func<(bool Fullscreen, bool Exclusive)> Screen, Func<int?> Load, Func<string, Task> Free);

/// <summary>
/// Stage 12: a game on the whole screen gets the whole graphics card. When something covers a monitor and it is an
/// exclusive game, or the graphics card stays busy with it (<see cref="BusyLoad"/> for <see cref="BusyChecks"/> looks in a
/// row), the brain's models of this PC leave it; they load again when someone talks to an assistant. A film does not
/// count (it decodes video, which leaves the card nearly idle), nor the brain itself answering: never while an assistant
/// answers or <see cref="Quiet"/> after. Options → Brain → «Liberar la gráfica al jugar».
/// </summary>
internal sealed class ModuleGames : IDisposable
{
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(10);

    /// <summary>Graphics card use (%) that is a game and not a film or the desktop.</summary>
    public const int BusyLoad = 50;

    public const int BusyChecks = 3;

    /// <summary>After an answer, the card was busy with the brain: this long before looking at it again.</summary>
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(60);

    private readonly GameProbe _probe;
    private readonly Timer _timer;
    private int _busy;
    private int _checking;

    public ModuleGames(BrainService brain, TalkService talk) : this(new GameProbe(
        () => brain.FreeForGames, () => brain.HasLocalLoaded, () => talk.IsAnyAnswering, () => talk.LastActive,
        SystemStatus.Now, () => PcStatus.ReadGpus().Select(g => g.Load).Max(), brain.FreeLocalAsync), Every)
    {
    }

    /// <param name="every">How often it looks (<see cref="Timeout.InfiniteTimeSpan"/>: only when <see cref="CheckAsync"/> is called).</param>
    internal ModuleGames(GameProbe probe, TimeSpan every)
    {
        _probe = probe;
        _timer = new Timer(_ => _ = CheckAsync(), null, every, every);
    }

    /// <summary>One look; whether it freed the graphics card.</summary>
    internal async Task<bool> CheckAsync()
    {
        if (Interlocked.Exchange(ref _checking, 1) == 1) return false;
        try
        {
            if (!_probe.Enabled() || !_probe.Loaded() || _probe.Answering() || DateTimeOffset.UtcNow - _probe.LastActive() < Quiet)
            {
                _busy = 0;
                return false;
            }
            var (fullscreen, exclusive) = _probe.Screen();
            var load = fullscreen && !exclusive ? _probe.Load() : null;
            if (!fullscreen || (!exclusive && (load is null || load < BusyLoad)))
            {
                _busy = 0;
                return false;
            }
            if (!exclusive && ++_busy < BusyChecks) return false;
            _busy = 0;
            Log.Info($"Asistente: un juego a pantalla completa{(exclusive ? " (exclusivo)" : $" con la gráfica al {load} %")}: el cerebro sale de la gráfica");
            await _probe.Free("juego a pantalla completa").ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Asistente: no se ha podido mirar si hay un juego: {ex.Message}");
            return false;
        }
        finally
        {
            Volatile.Write(ref _checking, 0);
        }
    }

    public void Dispose() => _timer.Dispose();
}

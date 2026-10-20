using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>The desktop as the assistant sees it (<see cref="IWorld"/>). Kept up to date by the world watcher.</summary>
internal sealed class HostWorld(Func<double> clock) : IWorld
{
    public WorldSnapshot Current { get; private set; } = WorldSnapshot.Empty;

    public SystemState System { get; private set; } = new();

    public event Action<WorldSnapshot>? Changed;

    /// <summary>Until when (engine clock) the windows are rescanned every frame (an assistant standing on one that moves).</summary>
    public double CloseWatchUntil { get; private set; }

    public void WatchClosely(double seconds) => CloseWatchUntil = Math.Max(CloseWatchUntil, clock() + seconds);

    internal void Publish(WorldSnapshot snapshot)
    {
        Current = snapshot;
        try { Changed?.Invoke(snapshot); }
        catch (Exception ex) { KDesktop.Core.Log.Error("Asistente: error atendiendo un cambio del mundo", ex); }
    }

    internal void Publish(SystemState state) => System = state;
}

using KDesktop.Assistant.Host.World;
using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The desktop as the assistants see it (<see cref="IWorld"/>), shared by all of them; each one gets it through its own
/// <see cref="WorldView"/>. Kept up to date by the world watcher.
/// </summary>
internal sealed class HostWorld(Func<double> clock) : IWorld, IDisposable
{
    private readonly MusicWatcher _music = new(clock);
    /// <summary>Who listens to the music (the watcher runs while someone does).</summary>
    private readonly HashSet<object> _listeners = [];

    public WorldSnapshot Current { get; private set; } = WorldSnapshot.Empty;

    public SystemState System { get; private set; } = new();

    public event Action<WorldSnapshot>? Changed;

    /// <summary>Until when (engine clock) the windows are rescanned every frame (an assistant standing on one that moves).</summary>
    public double CloseWatchUntil { get; private set; }

    public void WatchClosely(double seconds) => CloseWatchUntil = Math.Max(CloseWatchUntil, clock() + seconds);

    public MusicBeat Music => _music.Read(clock());

    public void ListenToMusic(bool listen) => Listen(this, listen);

    /// <summary>One more (or one less) listening to the music.</summary>
    internal void Listen(object who, bool listen)
    {
        lock (_listeners)
        {
            if (listen) _listeners.Add(who);
            else _listeners.Remove(who);
            _music.SetActive(_listeners.Count > 0);
        }
    }

    internal void Publish(WorldSnapshot snapshot)
    {
        Current = snapshot;
        try { Changed?.Invoke(snapshot); }
        catch (Exception ex) { KDesktop.Core.Log.Error("Asistente: error atendiendo un cambio del mundo", ex); }
    }

    internal void Publish(SystemState state) => System = state;

    /// <summary>Every assistant's body (each one sees the others' through its <see cref="WorldView"/>).</summary>
    public IReadOnlyList<OtherAssistant> Others { get; private set; } = [];

    /// <summary>The bodies after a tick (the same list stays if nothing changed).</summary>
    internal void Publish(IReadOnlyList<OtherAssistant> bodies)
    {
        if (!bodies.SequenceEqual(Others)) Others = bodies;
    }

    public void Dispose() => _music.Dispose();
}

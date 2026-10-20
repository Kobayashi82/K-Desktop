using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>The desktop as the assistant sees it (<see cref="IWorld"/>). Filled by the world watcher (stage 1.5).</summary>
internal sealed class HostWorld : IWorld
{
    public WorldSnapshot Current { get; private set; } = WorldSnapshot.Empty;

    public SystemState System { get; private set; } = new();

    public event Action<WorldSnapshot>? Changed;

    /// <summary>Until when the windows are rescanned every frame (an assistant standing on one that moves).</summary>
    public double CloseWatchUntil { get; private set; }

    public void WatchClosely(double seconds) => CloseWatchUntil = Math.Max(CloseWatchUntil, Environment.TickCount64 / 1000.0 + seconds);

    internal void Publish(WorldSnapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke(snapshot);
    }

    internal void Publish(SystemState state) => System = state;
}

/// <summary>Moving windows from the assistant process (<see cref="IWindowServices"/>). Filled in stage 1.5.</summary>
internal sealed class HostWindowServices : IWindowServices
{
    public bool TryMove(long handle, PixelRect bounds) => false;

    public CaptionButtons? GetCaptionButtons(long handle) => null;

    public System.Windows.Media.ImageSource? Thumbnail(long handle, int maxSize) => null;

    public System.Windows.Media.ImageSource? AppIcon(long handle) => null;
}

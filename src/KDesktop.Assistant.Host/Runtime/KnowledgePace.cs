using KDesktop.Assistant.Host.World;
using KDesktop.Core;
using KDesktop.Core.Platform;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// When reading the user's documents waits (stage 19.1): K-Desktop paused, an assistant answering (and a moment after: the
/// user may go on talking), or a game on the whole screen —as <see cref="ModuleGames"/> sees it: an exclusive one, or
/// something covering a monitor with the graphics card busy; a film does not stop it—.
/// </summary>
internal sealed class KnowledgePace(ModuleTalk talk)
{
    private static readonly TimeSpan AfterTalk = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan GameLook = TimeSpan.FromSeconds(10);

    private DateTime _lookedAt = DateTime.MinValue;
    private bool _game;

    /// <summary><c>kdesktop</c>, <c>busy</c>, <c>game</c> or null.</summary>
    public string? WaitReason()
    {
        if (talk.Paused) return "kdesktop";
        if (talk.Service.IsAnyAnswering || DateTimeOffset.UtcNow - talk.Service.LastActive < AfterTalk) return "busy";
        return Game() ? "game" : null;
    }

    private bool Game()
    {
        if (DateTime.UtcNow - _lookedAt < GameLook) return _game;
        _lookedAt = DateTime.UtcNow;
        try
        {
            var (fullscreen, exclusive) = SystemStatus.Now();
            _game = fullscreen && (exclusive || PcStatus.ReadGpus().Select(g => g.Load).Max() is >= ModuleGames.BusyLoad);
        }
        catch (Exception ex)
        {
            Log.Warn($"Asistente: no se ha podido mirar si hay un juego: {ex.Message}");
            _game = false;
        }
        return _game;
    }
}

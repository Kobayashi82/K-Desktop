namespace KDesktop.Core.Input;

/// <summary>
/// What the assistant process gets from the hook. It runs as the normal user, so it cannot hook input itself while an
/// elevated window is in front: K-Desktop swallows the keys of its game while it plays and lets it see the wheel.
/// </summary>
public sealed partial class HotkeyRouter
{
    private volatile IReadOnlySet<int>? _gameKeys;
    private volatile Action<int, bool>? _gameKeyChanged;

    /// <summary>Captured keys swallowed on the way down, so their release is swallowed too (hook thread).</summary>
    private readonly HashSet<int> _gameHeld = [];

    /// <summary>
    /// Swallows these keys (virtual-key codes) when they are pressed without Ctrl, Alt or Win, and tells
    /// <paramref name="changed"/> about each change, on the hook thread (keep it cheap). Null or empty: stop. A key held
    /// when it stops is still swallowed on its release (the application never saw it go down).
    /// </summary>
    public void SetGameKeys(IReadOnlySet<int>? keys, Action<int, bool>? changed)
    {
        _gameKeyChanged = changed;
        _gameKeys = keys is { Count: > 0 } ? keys : null;
    }

    /// <summary>Every physical turn of the wheel, on the hook thread (it is not swallowed). Keep it cheap.</summary>
    public Action<MouseInput>? WheelObserver { get; set; }

    /// <summary>Returns true when the key belongs to the game and was swallowed.</summary>
    private bool GameKey(KeyboardInput e)
    {
        var vk = e.Vk;
        if (!e.IsDown)
        {
            if (!_gameHeld.Remove(vk)) return false;
            _gameKeyChanged?.Invoke(vk, false);
            return true;
        }
        if (_gameKeys is not { } keys || !keys.Contains(vk)) return false;
        if ((CurrentModifiers() & (Modifiers.Ctrl | Modifiers.Alt | Modifiers.Win)) != 0) return false;
        // Auto-repeat is swallowed without telling: the key is still down.
        if (_gameHeld.Add(vk)) _gameKeyChanged?.Invoke(vk, true);
        return true;
    }
}

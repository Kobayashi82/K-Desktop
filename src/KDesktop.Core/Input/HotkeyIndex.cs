using KDesktop.Core.Model;

namespace KDesktop.Core.Input;

/// <summary>
/// Immutable lookup table hotkey → commands, rebuilt whenever the configuration changes.
/// Safe to query from the input-hook thread while the UI edits the live model.
/// </summary>
public sealed class HotkeyIndex
{
    private sealed record Entry(Command Command, string[] Processes, bool IgnoreDisabledApps, bool Block);

    public static readonly HotkeyIndex Empty = new([], new AppSettings());

    private readonly Dictionary<string, Entry[]> _map;
    private readonly HashSet<string> _disabledIn;
    private readonly HashSet<string> _sideButtonsBlockedIn;
    private readonly bool _voiceAnywhere, _dictationAnywhere, _launcherAnywhere, _clipboardAnywhere, _showWindowAnywhere, _exitAnywhere, _pauseAnywhere;

    /// <summary>
    /// The voice / dictation / launcher / open / exit / pause / «Desactivar en» hotkey is swallowed (the application in
    /// front never gets it).
    /// </summary>
    public bool VoiceBlock { get; }
    public bool DictationBlock { get; }
    public bool LauncherBlock { get; }
    public bool ClipboardBlock { get; }
    public bool ShowWindowBlock { get; }
    public bool ExitBlock { get; }
    public bool PauseBlock { get; }
    public bool DisabledInBlock { get; }

    public string ShowWindowHotkey { get; }
    public string ExitHotkey { get; }

    /// <summary>Hold to talk (Settings › Voice); empty when unset or paused.</summary>
    public string VoiceHotkey { get; }

    /// <summary>
    /// Hold to talk to one assistant (5.0.113): hotkey → its id, only those on the desktop; empty when paused or with the
    /// module off.
    /// </summary>
    private readonly Dictionary<string, string> _talkKeys;

    /// <summary>Who <see cref="IsTalkKey"/> gives for the dictation key (stage 10.5): what is said is written, not an order.</summary>
    public const string Dictation = "@dictation";

    /// <summary>Opens the launcher (Settings › Lanzador); empty when unset or paused.</summary>
    public string LauncherHotkey { get; }

    /// <summary>Opens the clipboard history (Settings › Portapapeles); empty when unset, paused or the history is off.</summary>
    public string ClipboardHotkey { get; }

    /// <summary>Turns «Desactivar en» on/off for the app in front (Settings › Desactivar en); empty when unset or paused.</summary>
    public string DisabledInHotkey { get; }

    /// <summary>Pauses or resumes K-Desktop (Settings › General); it stays while paused. Empty when unset.</summary>
    public string PauseHotkey { get; }
    public bool BlockSideButtons { get; }

    /// <param name="paused">K-Desktop paused: only its own hotkeys (show window, exit) stay; no commands, no blocking.</param>
    public HotkeyIndex(IEnumerable<Command> commands, AppSettings settings, bool paused = false)
    {
        var map = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var cmd in paused ? [] : commands.ToArray())
        {
            if (!cmd.Enabled) continue;
            var entry = new Entry(
                cmd,
                cmd.Processes.Select(ProcessNames.Normalize).Where(p => p.Length > 0).ToArray(),
                cmd.IgnoreDisabledApps,
                cmd.BlockKey);
            foreach (var hk in cmd.Hotkeys.ToArray())
            {
                var key = Hotkey.Normalize(hk);
                if (key.Length == 0) continue;
                if (!map.TryGetValue(key, out var list)) map[key] = list = [];
                list.Add(entry);
            }
        }
        _map = map.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        _disabledIn = settings.DisabledIn.Select(ProcessNames.Normalize).ToHashSet();
        _sideButtonsBlockedIn = settings.BlockSideButtonsIn.Select(ProcessNames.Normalize).ToHashSet();
        ShowWindowHotkey = Hotkey.Normalize(settings.ShowWindowHotkey);
        ExitHotkey = Hotkey.Normalize(settings.ExitHotkey);
        var listens = !paused && settings.VoiceEnabled;
        VoiceHotkey = !listens || string.IsNullOrWhiteSpace(settings.VoiceHotkey) ? string.Empty : Hotkey.Normalize(settings.VoiceHotkey);
        _talkKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (listens && Hotkey.Normalize(settings.DictationHotkey) is { Length: > 0 } dictation) _talkKeys[dictation] = Dictation;
        if (listens && settings.AssistantEnabled)
            foreach (var (assistant, key) in settings.AssistantTalkHotkeys)
                if (settings.AssistantsOnDesktop.Contains(assistant) && Hotkey.Normalize(key) is { Length: > 0 } normal) _talkKeys.TryAdd(normal, assistant);
        DisabledInHotkey = paused || string.IsNullOrWhiteSpace(settings.DisabledInHotkey) ? string.Empty : Hotkey.Normalize(settings.DisabledInHotkey);
        PauseHotkey = string.IsNullOrWhiteSpace(settings.PauseHotkey) ? string.Empty : Hotkey.Normalize(settings.PauseHotkey);
        LauncherHotkey = paused || string.IsNullOrWhiteSpace(settings.LauncherHotkey) ? string.Empty : Hotkey.Normalize(settings.LauncherHotkey);
        ClipboardHotkey = paused || !settings.ClipboardHistory || string.IsNullOrWhiteSpace(settings.ClipboardHotkey) ? string.Empty : Hotkey.Normalize(settings.ClipboardHotkey);
        BlockSideButtons = settings.BlockSideButtons && !paused;
        _voiceAnywhere = settings.VoiceIgnoreDisabledApps;
        _dictationAnywhere = settings.DictationIgnoreDisabledApps;
        _launcherAnywhere = settings.LauncherIgnoreDisabledApps;
        _clipboardAnywhere = settings.ClipboardIgnoreDisabledApps;
        VoiceBlock = settings.VoiceBlockKey;
        DictationBlock = settings.DictationBlockKey;
        LauncherBlock = settings.LauncherBlockKey;
        ClipboardBlock = settings.ClipboardBlockKey;
        _showWindowAnywhere = settings.ShowWindowIgnoreDisabledApps;
        _exitAnywhere = settings.ExitIgnoreDisabledApps;
        ShowWindowBlock = settings.ShowWindowBlockKey;
        ExitBlock = settings.ExitBlockKey;
        _pauseAnywhere = settings.PauseIgnoreDisabledApps;
        PauseBlock = settings.PauseBlockKey;
        DisabledInBlock = settings.DisabledInBlockKey;
        HasCorners = _map.Keys.Any(Hotkey.IsCorner);
        HotCornerDelay = Math.Clamp(settings.HotCornerDelay, 0, 3000);
        HasGestures = settings.MouseGestures && _map.Keys.Any(Hotkey.IsGesture);
        GestureButton = settings.GestureButton is MouseButton.Middle or MouseButton.X1 or MouseButton.X2 ? settings.GestureButton : MouseButton.Right;
        _gesturesOffIn = settings.GesturesOffIn.Select(ProcessNames.Normalize).ToHashSet();
        CustomGestures = settings.CustomGestures.Select(g => new CustomGesture { Name = g.Name, Samples = [.. g.Samples] }).ToArray();
    }

    /// <summary>Some command runs from a screen corner (E4): the mouse movements are looked at.</summary>
    public bool HasCorners { get; }

    /// <summary>Milliseconds the cursor stays in a corner before its command runs.</summary>
    public int HotCornerDelay { get; }

    /// <summary>Some command runs with a gesture and gestures are on (E4): the gesture button is held back to see.</summary>
    public bool HasGestures { get; }

    public MouseButton GestureButton { get; }

    /// <summary>The user's gestures, copied (the hook thread reads them while the interface edits the live ones).</summary>
    public IReadOnlyList<CustomGesture> CustomGestures { get; }

    private readonly HashSet<string> _gesturesOffIn;

    public bool GesturesOffIn(string process) => _gesturesOffIn.Contains(process);

    /// <summary>Something answers to <paramref name="hotkey"/> alone (a command or one of K-Desktop's own keys): it is not held back for gestures.</summary>
    public bool TakesAlone(string hotkey) =>
        _map.ContainsKey(hotkey) || _talkKeys.ContainsKey(hotkey) ||
        new[] { VoiceHotkey, LauncherHotkey, ClipboardHotkey, ShowWindowHotkey, ExitHotkey, PauseHotkey, DisabledInHotkey }.Contains(hotkey, StringComparer.OrdinalIgnoreCase);

    public bool Contains(string hotkey) => _map.ContainsKey(hotkey);

    /// <summary>The pause hotkey, and it may work here (like <see cref="IsLauncherHotkey"/>).</summary>
    public bool IsPauseHotkey(string hotkey, Func<string> process) =>
        PauseHotkey.Length > 0 && hotkey.Equals(PauseHotkey, StringComparison.OrdinalIgnoreCase) && (_pauseAnywhere || !DisabledHere(process));

    public bool IsDisabledInHotkey(string hotkey) =>
        DisabledInHotkey.Length > 0 && hotkey.Equals(DisabledInHotkey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The launcher hotkey, and it may work here: not in the applications of «Desactivar en» unless its «Ejecutar
    /// siempre» is on (<paramref name="process"/> is asked only then). Elsewhere the key reaches the application.
    /// </summary>
    public bool IsLauncherHotkey(string hotkey, Func<string> process) =>
        LauncherHotkey.Length > 0 && hotkey.Equals(LauncherHotkey, StringComparison.OrdinalIgnoreCase) && (_launcherAnywhere || !DisabledHere(process));

    /// <summary>The clipboard history's hotkey, and it may work here (like <see cref="IsLauncherHotkey"/>).</summary>
    public bool IsClipboardHotkey(string hotkey, Func<string> process) =>
        ClipboardHotkey.Length > 0 && hotkey.Equals(ClipboardHotkey, StringComparison.OrdinalIgnoreCase) && (_clipboardAnywhere || !DisabledHere(process));

    /// <summary>The open-window hotkey, and it may work here (like <see cref="IsLauncherHotkey"/>).</summary>
    public bool IsShowWindowHotkey(string hotkey, Func<string> process) =>
        ShowWindowHotkey.Length > 0 && hotkey.Equals(ShowWindowHotkey, StringComparison.OrdinalIgnoreCase) && (_showWindowAnywhere || !DisabledHere(process));

    /// <summary>The exit hotkey, and it may work here (like <see cref="IsLauncherHotkey"/>).</summary>
    public bool IsExitHotkey(string hotkey, Func<string> process) =>
        ExitHotkey.Length > 0 && hotkey.Equals(ExitHotkey, StringComparison.OrdinalIgnoreCase) && (_exitAnywhere || !DisabledHere(process));

    /// <summary>The voice hotkey, and it may work here (like <see cref="IsLauncherHotkey"/>).</summary>
    public bool IsVoiceHotkey(string hotkey, Func<string> process) =>
        VoiceHotkey.Length > 0 && hotkey.Equals(VoiceHotkey, StringComparison.OrdinalIgnoreCase) && (_voiceAnywhere || !DisabledHere(process));

    /// <summary>
    /// Whether a hotkey listens (like <see cref="IsVoiceHotkey"/>): K-Desktop's voice key (<paramref name="assistant"/>
    /// null: its commands) or the key to talk to an assistant on the desktop (its id).
    /// </summary>
    public bool IsTalkKey(string hotkey, Func<string> process, out string? assistant)
    {
        assistant = null;
        if (IsVoiceHotkey(hotkey, process)) return true;
        if (_talkKeys.TryGetValue(hotkey, out var who) && ((who == Dictation ? _dictationAnywhere : _voiceAnywhere) || !DisabledHere(process)))
        {
            assistant = who;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Whether a key that <see cref="IsTalkKey"/> took is swallowed: the voice and dictation keys by their own «Bloquear
    /// tecla», an assistant's always.
    /// </summary>
    public bool BlocksTalk(string? assistant) => assistant switch { null => VoiceBlock, Dictation => DictationBlock, _ => true };

    private bool DisabledHere(Func<string> process) => _disabledIn.Count > 0 && _disabledIn.Contains(process());

    public bool IsDisabledIn(string process) => _disabledIn.Contains(process);

    public bool BlocksSideButtonsIn(string process) => BlockSideButtons && _sideButtonsBlockedIn.Contains(process);

    /// <summary>
    /// Commands that should run for <paramref name="hotkey"/> in <paramref name="process"/>. The specific one wins:
    /// when a command limited to some programs matches, the commands for every program with that hotkey don't run.
    /// </summary>
    public IReadOnlyList<Command> Resolve(string hotkey, string process, out bool block)
    {
        block = false;
        if (!_map.TryGetValue(hotkey, out var entries)) return [];
        var disabledHere = _disabledIn.Contains(process);
        var specific = false;
        foreach (var e in entries)
            if (e.Processes.Length > 0 && Matches(e, process, disabledHere)) { specific = true; break; }

        List<Command>? result = null;
        foreach (var e in entries)
        {
            if (!Matches(e, process, disabledHere)) continue;
            if (specific && e.Processes.Length == 0) continue;
            (result ??= []).Add(e.Command);
            block |= e.Block;
        }
        return result ?? (IReadOnlyList<Command>)[];
    }

    private static bool Matches(Entry e, string process, bool disabledHere) =>
        (e.Processes.Length == 0 || Array.IndexOf(e.Processes, process) >= 0) && (!disabledHere || e.IgnoreDisabledApps);
}

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

    public string ShowWindowHotkey { get; }
    public string ExitHotkey { get; }

    /// <summary>Hold to talk (Settings › Voice); empty when unset or paused.</summary>
    public string VoiceHotkey { get; }

    /// <summary>Opens the launcher (Settings › Lanzador); empty when unset or paused.</summary>
    public string LauncherHotkey { get; }

    /// <summary>Turns «Desactivar en» on/off for the app in front (Settings › Desactivar en); empty when unset or paused.</summary>
    public string DisabledInHotkey { get; }
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
        VoiceHotkey = paused || string.IsNullOrWhiteSpace(settings.VoiceHotkey) ? string.Empty : Hotkey.Normalize(settings.VoiceHotkey);
        DisabledInHotkey = paused || string.IsNullOrWhiteSpace(settings.DisabledInHotkey) ? string.Empty : Hotkey.Normalize(settings.DisabledInHotkey);
        LauncherHotkey = paused || string.IsNullOrWhiteSpace(settings.LauncherHotkey) ? string.Empty : Hotkey.Normalize(settings.LauncherHotkey);
        BlockSideButtons = settings.BlockSideButtons && !paused;
    }

    public bool Contains(string hotkey) => _map.ContainsKey(hotkey);

    public bool IsDisabledInHotkey(string hotkey) =>
        DisabledInHotkey.Length > 0 && hotkey.Equals(DisabledInHotkey, StringComparison.OrdinalIgnoreCase);

    public bool IsLauncherHotkey(string hotkey) =>
        LauncherHotkey.Length > 0 && hotkey.Equals(LauncherHotkey, StringComparison.OrdinalIgnoreCase);

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

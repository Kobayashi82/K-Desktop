using KDesktop.Core.Model;

namespace KDesktop.Core.Input;

/// <summary>
/// Immutable lookup table hotkey → commands, rebuilt whenever the configuration changes.
/// Safe to query from the input-hook thread while the UI edits the live model.
/// </summary>
public sealed class HotkeyIndex
{
    private sealed record Entry(Command Command, string[] Processes, bool IgnoreDisabledApps, bool CanToggleDisabled, bool Block);

    public static readonly HotkeyIndex Empty = new([], new AppSettings());

    private readonly Dictionary<string, Entry[]> _map;
    private readonly HashSet<string> _disabledIn;
    private readonly HashSet<string> _sideButtonsBlockedIn;

    public string ShowWindowHotkey { get; }
    public string ExitHotkey { get; }
    public bool BlockSideButtons { get; }

    public HotkeyIndex(IEnumerable<Command> commands, AppSettings settings)
    {
        var map = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var cmd in commands.ToArray())
        {
            if (!cmd.Enabled) continue;
            var entry = new Entry(
                cmd,
                cmd.Processes.Select(ProcessNames.Normalize).Where(p => p.Length > 0).ToArray(),
                cmd.IgnoreDisabledApps,
                cmd.Actions.Any(a => a is ToggleDisabledInAction && a.Enabled),
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
        BlockSideButtons = settings.BlockSideButtons;
    }

    public bool Contains(string hotkey) => _map.ContainsKey(hotkey);

    public bool IsDisabledIn(string process) => _disabledIn.Contains(process);

    public bool BlocksSideButtonsIn(string process) => BlockSideButtons && _sideButtonsBlockedIn.Contains(process);

    /// <summary>Commands that should run for <paramref name="hotkey"/> in <paramref name="process"/>.</summary>
    public IReadOnlyList<Command> Resolve(string hotkey, string process, out bool block)
    {
        block = false;
        if (!_map.TryGetValue(hotkey, out var entries)) return [];
        var disabledHere = _disabledIn.Contains(process);
        List<Command>? result = null;
        foreach (var e in entries)
        {
            if (e.Processes.Length > 0 && Array.IndexOf(e.Processes, process) < 0) continue;
            if (disabledHere && !e.IgnoreDisabledApps && !e.CanToggleDisabled) continue;
            (result ??= []).Add(e.Command);
            block |= e.Block;
        }
        return result ?? (IReadOnlyList<Command>)[];
    }
}

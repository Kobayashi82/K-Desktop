using KDesktop.Core.Model;

namespace KDesktop.Core.Input;

/// <summary>
/// Immutable lookup table "key / button / wheel" → remapping, rebuilt whenever the K-Board profiles change.
/// Queried from the input-hook thread, so everything the hook needs is precomputed here. Only the on/off state of
/// each profile is read live, so switching a profile takes effect immediately.
/// </summary>
public sealed class KBoardIndex
{
    public enum OutputType { None, Key, Button, Wheel }

    /// <summary>A compiled remapping.</summary>
    public sealed class Rule
    {
        public required KBoardProfile Profile { get; init; }
        public required string[] Processes { get; init; }
        public required KeyRemap Remap { get; init; }
        public required RemapKind Kind { get; init; }

        /// <summary>Modifiers that are part of the source ("CTRL + J"): the application must not see them.</summary>
        public Modifiers FromMods { get; init; }

        /// <summary>Modifiers pressed together with the output ("CTRL + C").</summary>
        public Modifiers ToMods { get; init; }

        public OutputType Output { get; init; }
        public int Vk { get; init; }
        public bool Extended { get; init; }
        public MouseButton Button { get; init; }
        public int Wheel { get; init; }
        public bool Horizontal { get; init; }
        public string Text { get; init; } = string.Empty;
        public Command? Command { get; init; }

        /// <summary>The output is a single modifier key (CapsLock → LCtrl): it counts as a held modifier.</summary>
        public Modifiers OutputModifier => Output == OutputType.Key ? ModifierOfVk(Vk) : Modifiers.None;
    }

    public static readonly KBoardIndex Empty = new([], [], []);

    private readonly Dictionary<string, Rule[]> _map;
    private readonly HashSet<string> _disabledIn;

    public bool IsEmpty => _map.Count == 0;

    public KBoardIndex(IEnumerable<KBoardProfile> profiles, IEnumerable<Command> commands, IEnumerable<string> disabledIn)
    {
        var commandById = commands.ToDictionary(c => c.Id);
        var map = new Dictionary<string, List<Rule>>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles.ToArray())
        {
            var processes = profile.Processes.Select(ProcessNames.Normalize).Where(p => p.Length > 0).ToArray();
            foreach (var remap in profile.Remaps.ToArray())
            {
                if (!remap.Enabled) continue;
                var from = Hotkey.Normalize(remap.From);
                if (from.Length == 0) continue;
                var rule = Compile(profile, processes, remap, from, commandById);
                if (rule is null) continue;
                if (!map.TryGetValue(from, out var list)) map[from] = list = [];
                list.Add(rule);
            }
        }
        _map = map.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
        _disabledIn = disabledIn.Select(ProcessNames.Normalize).ToHashSet();
    }

    /// <summary>
    /// Finds the remapping for <paramref name="name"/> ("J", "Mouse 4", "Wheel Up") with the modifiers currently held.
    /// An exact match ("CTRL + J") wins; otherwise a remap without modifiers applies whatever is held (Shift + A → Shift + B).
    /// </summary>
    public Rule? Resolve(Modifiers mods, string name, Func<string> foregroundProcess)
    {
        string? process = null;
        string Process() => process ??= foregroundProcess();

        if (mods != Modifiers.None && Match(Hotkey.Format(mods, name), Process) is { } exact) return exact;
        return Match(name, Process);
    }

    private Rule? Match(string key, Func<string> process)
    {
        if (!_map.TryGetValue(key, out var rules)) return null;
        foreach (var r in rules)
        {
            if (!r.Profile.Active) continue;
            if (r.Processes.Length > 0)
            {
                if (Array.IndexOf(r.Processes, process()) < 0) continue;
            }
            else if (_disabledIn.Count > 0 && _disabledIn.Contains(process()))
            {
                continue; // profiles for "every application" respect "Desactivar en"
            }
            return r;
        }
        return null;
    }

    private static Rule? Compile(KBoardProfile profile, string[] processes, KeyRemap remap, string from, Dictionary<string, Command> commands)
    {
        var (fromMods, _) = Hotkey.Parse(from);
        switch (remap.Kind)
        {
            case RemapKind.Disable:
                return new Rule { Profile = profile, Processes = processes, Remap = remap, Kind = remap.Kind, FromMods = fromMods };
            case RemapKind.Text:
                return string.IsNullOrEmpty(remap.To) ? null
                    : new Rule { Profile = profile, Processes = processes, Remap = remap, Kind = remap.Kind, FromMods = fromMods, Text = remap.To };
            case RemapKind.Command:
                return remap.CommandId is { } id && commands.TryGetValue(id, out var cmd)
                    ? new Rule { Profile = profile, Processes = processes, Remap = remap, Kind = remap.Kind, FromMods = fromMods, Command = cmd }
                    : null;
        }

        // Keys: a single key, button or wheel direction, with optional modifiers.
        var (toMods, main) = Hotkey.Parse(remap.To);
        if (main.Length == 0)
        {
            // Only modifiers ("CTRL", "CTRL + SHIFT"): the last one becomes the key itself (left variant).
            if (toMods == Modifiers.None) return null;
            var last = new[] { Modifiers.Win, Modifiers.Shift, Modifiers.Alt, Modifiers.Ctrl }.First(m => toMods.HasFlag(m));
            toMods &= ~last;
            main = KeyNames.FromVk(LeftVk(last));
        }
        main = main.Split(Hotkey.Separator)[^1];

        var rule = new Rule { Profile = profile, Processes = processes, Remap = remap, Kind = remap.Kind, FromMods = fromMods, ToMods = toMods };
        switch (main)
        {
            case "Wheel Up": return With(rule, OutputType.Wheel, wheel: 120);
            case "Wheel Down": return With(rule, OutputType.Wheel, wheel: -120);
            case "HWheel Up": return With(rule, OutputType.Wheel, wheel: 120, horizontal: true);
            case "HWheel Down": return With(rule, OutputType.Wheel, wheel: -120, horizontal: true);
        }
        if (main.StartsWith("Mouse ", StringComparison.Ordinal))
        {
            var button = main[^1] switch
            {
                '1' => MouseButton.Left, '2' => MouseButton.Right, '3' => MouseButton.Middle,
                '4' => MouseButton.X1, '5' => MouseButton.X2, _ => MouseButton.None,
            };
            return button == MouseButton.None ? null : With(rule, OutputType.Button, button: button);
        }
        var vk = KeyNames.ToVk(main);
        return vk == 0 ? null : With(rule, OutputType.Key, vk: vk, extended: main == "NumPadEnter" || KeyNames.IsExtendedVk(vk));
    }

    private static Rule With(Rule r, OutputType output, int vk = 0, bool extended = false, MouseButton button = MouseButton.None, int wheel = 0, bool horizontal = false) => new()
    {
        Profile = r.Profile, Processes = r.Processes, Remap = r.Remap, Kind = r.Kind, FromMods = r.FromMods, ToMods = r.ToMods,
        Output = output, Vk = vk, Extended = extended, Button = button, Wheel = wheel, Horizontal = horizontal,
    };

    public static int LeftVk(Modifiers m) => m switch
    {
        Modifiers.Ctrl => KeyNames.VK_LCONTROL,
        Modifiers.Alt => KeyNames.VK_LMENU,
        Modifiers.Shift => KeyNames.VK_LSHIFT,
        _ => KeyNames.VK_LWIN,
    };

    public static Modifiers ModifierOfVk(int vk) => vk switch
    {
        KeyNames.VK_CONTROL or KeyNames.VK_LCONTROL or KeyNames.VK_RCONTROL => Modifiers.Ctrl,
        KeyNames.VK_MENU or KeyNames.VK_LMENU or KeyNames.VK_RMENU => Modifiers.Alt,
        KeyNames.VK_SHIFT or KeyNames.VK_LSHIFT or KeyNames.VK_RSHIFT => Modifiers.Shift,
        KeyNames.VK_LWIN or KeyNames.VK_RWIN => Modifiers.Win,
        _ => Modifiers.None,
    };
}

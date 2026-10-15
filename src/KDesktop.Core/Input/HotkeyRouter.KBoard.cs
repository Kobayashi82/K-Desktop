using KDesktop.Core.Model;

namespace KDesktop.Core.Input;

/// <summary>
/// K-Board: remaps keys, mouse buttons and the wheel. Runs on the hook thread before hotkeys are processed.
/// The physical event is swallowed and the replacement is injected right away (SendInput inside the hook keeps the
/// order with the following physical events). Keys are remapped as down / up pairs, so holding works (CapsLock → Ctrl,
/// a mouse button → a key in a game…), and whatever was pressed is released exactly on the physical release, even if
/// the profile is switched off in between.
/// </summary>
public sealed partial class HotkeyRouter
{
    private volatile KBoardIndex _kboard = KBoardIndex.Empty;

    private sealed class Held(KBoardIndex.Rule rule)
    {
        public KBoardIndex.Rule Rule { get; } = rule;

        /// <summary>Modifier keys of the source that were released so the application does not see them.</summary>
        public List<int> Released { get; } = [];

        /// <summary>Modifier keys pressed for the output (to release them in reverse order).</summary>
        public List<int> PressedMods { get; } = [];
    }

    private readonly Dictionary<int, Held> _heldKeys = [];
    private readonly Dictionary<MouseButton, Held> _heldButtons = [];

    /// <summary>Modifier keys physically held that reached Windows (left / right distinguished).</summary>
    private readonly HashSet<int> _physicalModVks = [];

    /// <summary>Modifiers currently held down by K-Board outputs (they count for hotkeys and other remaps).</summary>
    private Modifiers _kbOutputMods;

    public void UpdateKBoard(KBoardIndex index) => _kboard = index;

    /// <summary>A key or button was remapped (statistics). Raised on a worker thread.</summary>
    public event Action? KBoardUsed;

    // ── keyboard ─────────────────────────────────────────────────────────

    /// <summary>Returns true when K-Board handled (and swallowed) the event, false when it must continue.</summary>
    private bool KBoardKey(KeyboardInput e)
    {
        var vk = e.Vk;
        if (!e.IsDown)
        {
            if (_heldKeys.Remove(vk, out var held))
            {
                Release(held);
                return true;
            }
            if (KeyNames.IsModifierVk(vk)) _physicalModVks.Remove(vk);
            return false;
        }

        if (_heldKeys.TryGetValue(vk, out var repeat))
        {
            Repeat(repeat);
            return true;
        }

        var index = _kboard;
        if (!index.IsEmpty && _capture is null && _windowPicker is null && _recorder is null)
        {
            var rule = index.Resolve(KBoardMods(), KeyNames.FromVk(vk, e.Extended), CurrentProcess);
            if (rule is not null)
            {
                var held = Press(rule);
                if (rule.Kind is RemapKind.Keys) _heldKeys[vk] = held;
                else
                {
                    // One-shot outputs: done on the press, the release is just swallowed.
                    Release(held);
                    _heldKeys[vk] = new Held(DisabledRule(rule));
                }
                RecomputeOutputMods();
                return true;
            }
        }
        if (KeyNames.IsModifierVk(vk)) _physicalModVks.Add(vk);
        return false;
    }

    // ── mouse ────────────────────────────────────────────────────────────

    private bool KBoardMouse(MouseInput e)
    {
        switch (e.Message)
        {
            case MouseMessage.Up:
                if (!_heldButtons.Remove(e.Button, out var held)) return false;
                Release(held);
                return true;

            case MouseMessage.Down:
            {
                if (_heldButtons.ContainsKey(e.Button)) return true;
                var index = _kboard;
                if (index.IsEmpty || _capture is not null || _windowPicker is not null || _recorder is not null) return false;
                var rule = index.Resolve(KBoardMods(), ButtonName(e.Button), CurrentProcess);
                if (rule is null) return false;
                var pressed = Press(rule);
                if (rule.Kind is RemapKind.Keys) _heldButtons[e.Button] = pressed;
                else
                {
                    Release(pressed);
                    _heldButtons[e.Button] = new Held(DisabledRule(rule));
                }
                RecomputeOutputMods();
                return true;
            }

            case MouseMessage.Wheel or MouseMessage.HWheel:
            {
                var index = _kboard;
                if (index.IsEmpty || _capture is not null || _recorder is not null) return false;
                var name = (e.Message == MouseMessage.Wheel ? "Wheel " : "HWheel ") + (e.WheelDelta > 0 ? "Up" : "Down");
                var rule = index.Resolve(KBoardMods(), name, CurrentProcess);
                if (rule is null) return false;
                Release(Press(rule)); // a wheel notch has no "up": press and release at once
                return true;
            }

            default:
                return false;
        }
    }

    // ── output ───────────────────────────────────────────────────────────

    private Held Press(KBoardIndex.Rule rule)
    {
        var held = new Held(rule);
        Post(() => KBoardUsed?.Invoke());

        if (rule.Kind == RemapKind.Command)
        {
            if (rule.Command is { } command)
            {
                var fg = ForegroundWindow.Capture();
                Post(() => Triggered?.Invoke(new HotkeyTrigger(rule.Remap.From, [command], false, fg)));
            }
            // Swallowing a key while ALT/WIN is held would make their release open the menu bar / Start menu.
            if ((KBoardMods() & (Modifiers.Alt | Modifiers.Win)) != 0) InputSimulator.SendMenuMask();
            return held;
        }

        // The application must not see the modifiers that belong to the source ("CTRL + J" → Down, not Ctrl + Down).
        if (rule.FromMods != Modifiers.None)
        {
            var release = LogicalModVks().Where(v => (KBoardIndex.ModifierOfVk(v) & rule.FromMods) != 0).ToList();
            if (release.Any(v => KBoardIndex.ModifierOfVk(v) is Modifiers.Alt or Modifiers.Win)) InputSimulator.SendMenuMask();
            foreach (var v in release) InputSimulator.KeyUp(v);
            held.Released.AddRange(release);
        }

        switch (rule.Kind)
        {
            case RemapKind.Disable:
                if (rule.FromMods == Modifiers.None && (KBoardMods() & (Modifiers.Alt | Modifiers.Win)) != 0) InputSimulator.SendMenuMask();
                break;

            case RemapKind.Text:
                InputSimulator.SendText(rule.Text, releaseModifiers: false);
                break;

            case RemapKind.Keys:
                foreach (var flag in new[] { Modifiers.Ctrl, Modifiers.Alt, Modifiers.Shift, Modifiers.Win })
                {
                    if (!rule.ToMods.HasFlag(flag)) continue;
                    var alreadyDown = (LogicalMods() & flag) != 0 && !held.Released.Any(v => KBoardIndex.ModifierOfVk(v) == flag);
                    if (alreadyDown) continue;
                    var vk = KBoardIndex.LeftVk(flag);
                    InputSimulator.KeyDown(vk);
                    held.PressedMods.Add(vk);
                }
                switch (rule.Output)
                {
                    case KBoardIndex.OutputType.Key: InputSimulator.KeyDown(rule.Vk, rule.Extended); break;
                    case KBoardIndex.OutputType.Button: InputSimulator.MouseDown(rule.Button); break;
                    case KBoardIndex.OutputType.Wheel: InputSimulator.Wheel(rule.Wheel, rule.Horizontal); break;
                }
                break;
        }
        return held;
    }

    private void Repeat(Held held)
    {
        var rule = held.Rule;
        if (rule.Kind != RemapKind.Keys) return;
        switch (rule.Output)
        {
            case KBoardIndex.OutputType.Key: InputSimulator.KeyDown(rule.Vk, rule.Extended); break;
            case KBoardIndex.OutputType.Wheel: InputSimulator.Wheel(rule.Wheel, rule.Horizontal); break;
        }
    }

    private void Release(Held held)
    {
        var rule = held.Rule;
        if (rule.Kind == RemapKind.Keys)
        {
            switch (rule.Output)
            {
                case KBoardIndex.OutputType.Key: InputSimulator.KeyUp(rule.Vk, rule.Extended); break;
                case KBoardIndex.OutputType.Button: InputSimulator.MouseUp(rule.Button); break;
            }
            for (var i = held.PressedMods.Count - 1; i >= 0; i--) InputSimulator.KeyUp(held.PressedMods[i]);
        }
        RecomputeOutputMods();

        // Give back the source modifiers that are still held, so the application sees the real state again.
        var restore = held.Released.Where(v => _physicalModVks.Contains(v) || OutputHolds(v)).ToList();
        foreach (var v in restore) InputSimulator.KeyDown(v);
        if (restore.Any(v => KBoardIndex.ModifierOfVk(v) is Modifiers.Alt or Modifiers.Win)) InputSimulator.SendMenuMask();
    }

    /// <summary>Rule that swallows the release of a key whose remap already ran on the press.</summary>
    private static KBoardIndex.Rule DisabledRule(KBoardIndex.Rule rule) => new()
    {
        Profile = rule.Profile, Processes = rule.Processes, Remap = rule.Remap, Kind = RemapKind.Disable,
    };

    // ── modifier bookkeeping ─────────────────────────────────────────────

    /// <summary>Modifiers as the applications see them: physical ones plus the ones K-Board outputs hold.</summary>
    private Modifiers KBoardMods()
    {
        var mods = _kbOutputMods;
        foreach (var vk in _physicalModVks) mods |= KBoardIndex.ModifierOfVk(vk);
        return mods;
    }

    /// <summary>Modifiers the applications really see now: like <see cref="KBoardMods"/> minus the ones a remap released.</summary>
    private Modifiers LogicalMods()
    {
        var released = _heldKeys.Values.Concat(_heldButtons.Values).SelectMany(h => h.Released).ToHashSet();
        var mods = _kbOutputMods;
        foreach (var vk in _physicalModVks)
            if (!released.Contains(vk)) mods |= KBoardIndex.ModifierOfVk(vk);
        return mods;
    }

    private IEnumerable<int> LogicalModVks()
    {
        foreach (var vk in _physicalModVks) yield return vk;
        foreach (var held in _heldKeys.Values.Concat(_heldButtons.Values))
        {
            if (held.Rule.OutputModifier != Modifiers.None) yield return held.Rule.Vk;
            foreach (var vk in held.PressedMods) yield return vk;
        }
    }

    private bool OutputHolds(int vk) =>
        _heldKeys.Values.Concat(_heldButtons.Values).Any(h => h.Rule.Vk == vk && h.Rule.OutputModifier != Modifiers.None || h.PressedMods.Contains(vk));

    /// <summary>Call after <see cref="_heldKeys"/> or <see cref="_heldButtons"/> change.</summary>
    private void RecomputeOutputMods()
    {
        var mods = Modifiers.None;
        foreach (var held in _heldKeys.Values.Concat(_heldButtons.Values))
        {
            mods |= held.Rule.OutputModifier;
            foreach (var vk in held.PressedMods) mods |= KBoardIndex.ModifierOfVk(vk);
        }
        _kbOutputMods = mods;
    }

    private static string CurrentProcess() => ForegroundWindow.Capture().ProcessName;
}

using KDesktop.Core.Model;
using KDesktop.Core.Native;

namespace KDesktop.Core.Input;

public enum BuiltInHotkey { ShowWindow, Exit }

public sealed record HotkeyTrigger(string Hotkey, IReadOnlyList<Command> Commands, bool IsRepeat, ForegroundWindow Foreground);

/// <summary>
/// Turns raw hook events into hotkeys ("CTRL + ALT + O", "Mouse 4 + Mouse 1", "CTRL + Wheel Up"),
/// decides synchronously whether the event must be swallowed and dispatches the work off the hook thread.
/// </summary>
public sealed partial class HotkeyRouter : IDisposable
{
    private readonly InputHook _hook = new();
    private volatile HotkeyIndex _index = HotkeyIndex.Empty;
    private volatile Func<string, bool, bool>? _capture;

    // Keyboard state (only touched on the hook thread).
    private Modifiers _physicalMods;
    private int _lastDownVk;
    private readonly HashSet<int> _blockedKeys = [];

    /// <summary>Physical state of every key, written on the hook thread and read from anywhere.</summary>
    private readonly bool[] _down = new bool[256];

    /// <summary>True while the key is physically held (swallowed or not).</summary>
    public bool IsKeyHeld(int vk) => vk is > 0 and < 256 && Volatile.Read(ref _down[vk]);

    // Mouse state (only touched on the hook thread).
    private readonly List<string> _chord = [];
    private bool _armed;
    private bool _captureMouse;
    private readonly HashSet<MouseButton> _blockedButtons = [];

    public event Action<HotkeyTrigger>? Triggered;
    public event Action<BuiltInHotkey>? BuiltIn;

    /// <summary>
    /// The voice hotkey was pressed. The argument tells whether it is still held (hold to talk); for the wheel it
    /// is always false (a tap).
    /// </summary>
    public event Action<Func<bool>>? VoiceRequested;

    // Mouse button used as the voice hotkey while it is held (hook thread writes, the voice session reads).
    private MouseButton? _voiceButton;
    private volatile bool _voiceButtonHeld;

    /// <summary>Asked when Escape is pressed: return true if something cancellable is running.</summary>
    public Func<bool>? HasCancellable { get; set; }
    public event Action? CancelRequested;

    /// <summary>Asked when SHIFT + Escape is pressed: return true if a macro is playing.</summary>
    public Func<bool>? IsPlayingMacro { get; set; }
    public event Action? StopMacrosRequested;

    private volatile IInputRecorder? _recorder;

    /// <summary>
    /// Sends every physical keyboard/mouse event (mouse movements included) to <paramref name="recorder"/>
    /// instead of processing hotkeys. Pass null to stop.
    /// </summary>
    public void SetRecorder(IInputRecorder? recorder)
    {
        _recorder = recorder;
        _hook.ForwardMouseMove = recorder is not null;
    }

    public bool IsRecording => _recorder is not null;

    public HotkeyRouter()
    {
        _hook.KeyboardHandler = OnKey;
        _hook.MouseHandler = OnMouse;
    }

    public void Start() => _hook.Start();

    public bool IsRunning => _hook.IsRunning;

    public void Reinstall() => _hook.Reinstall();

    public void UpdateIndex(HotkeyIndex index) => _index = index;

    /// <summary>
    /// Starts capturing hotkeys for the editor. The callback receives (hotkey, isFinal) on the hook thread and
    /// returns true to swallow the input. Pass null to stop capturing.
    /// </summary>
    public void SetCapture(Func<string, bool, bool>? callback)
    {
        _capture = callback;
    }

    public bool IsCapturing => _capture is not null;

    private volatile Action<ForegroundWindow?>? _windowPicker;
    private bool _swallowPickerUp;

    /// <summary>
    /// The next left click (anywhere) selects the window under the cursor instead of reaching it;
    /// <paramref name="picked"/> receives it on a worker thread (null if cancelled with Escape).
    /// </summary>
    public void PickWindow(Action<ForegroundWindow?> picked) => _windowPicker = picked;

    // ── keyboard ─────────────────────────────────────────────────────────

    private bool OnKey(KeyboardInput e)
    {
        if (e.Injected) return false;
        var vk = e.Vk;
        if (vk is > 0 and < 256) Volatile.Write(ref _down[vk], e.IsDown);
        if (KBoardKey(e)) return true;

        if (_recorder is { } recorder)
        {
            if (KeyNames.IsModifierVk(vk))
            {
                var f = ModifierOf(vk);
                if (e.IsDown) _physicalMods |= f; else _physicalMods &= ~f;
            }
            return recorder.OnKey(e);
        }

        if (KeyNames.IsModifierVk(vk))
        {
            var flag = ModifierOf(vk);
            if (e.IsDown) _physicalMods |= flag; else _physicalMods &= ~flag;
            if (_capture is { } preview && e.IsDown) preview(Hotkey.Format(_physicalMods, string.Empty), false);
            return false;
        }

        if (!e.IsDown)
        {
            if (_lastDownVk == vk) _lastDownVk = 0;
            return _blockedKeys.Remove(vk);
        }

        var mods = CurrentModifiers();
        var hotkey = Hotkey.Format(mods, KeyNames.FromVk(vk, e.Extended));
        var repeat = _lastDownVk == vk;
        _lastDownVk = vk;

        if (vk == KeyNames.VK_ESCAPE && mods == Modifiers.Shift && IsPlayingMacro?.Invoke() == true)
        {
            Post(() => StopMacrosRequested?.Invoke());
            return Block(vk, mods);
        }

        if (vk == KeyNames.VK_ESCAPE && _windowPicker is { } cancelPick)
        {
            _windowPicker = null;
            Post(() => cancelPick(null));
            return Block(vk, mods);
        }

        if (_capture is { } capture)
            return capture(hotkey, true) && Block(vk, mods);

        var index = _index;
        if (index.ShowWindowHotkey.Length > 0 && hotkey.Equals(index.ShowWindowHotkey, StringComparison.OrdinalIgnoreCase))
        {
            if (!repeat) Post(() => BuiltIn?.Invoke(BuiltInHotkey.ShowWindow));
            return Block(vk, mods);
        }
        if (index.ExitHotkey.Length > 0 && hotkey.Equals(index.ExitHotkey, StringComparison.OrdinalIgnoreCase))
        {
            if (!repeat) Post(() => BuiltIn?.Invoke(BuiltInHotkey.Exit));
            return Block(vk, mods);
        }

        if (index.VoiceHotkey.Length > 0 && hotkey.Equals(index.VoiceHotkey, StringComparison.OrdinalIgnoreCase))
        {
            if (!repeat) Post(() => VoiceRequested?.Invoke(() => IsKeyHeld(vk)));
            return Block(vk, mods);
        }

        if (vk == KeyNames.VK_ESCAPE && mods == Modifiers.None && HasCancellable?.Invoke() == true)
        {
            Post(() => CancelRequested?.Invoke());
            return Block(vk, mods);
        }

        if (!index.Contains(hotkey)) return false;
        var fg = ForegroundWindow.Capture();
        var commands = index.Resolve(hotkey, fg.ProcessName, out var block);
        if (commands.Count > 0) Post(() => Triggered?.Invoke(new HotkeyTrigger(hotkey, commands, repeat, fg)));
        return block && Block(vk, mods);
    }

    private bool Block(int vk, Modifiers mods)
    {
        _blockedKeys.Add(vk);
        // Swallowing a key while ALT/WIN is held would make their release open the menu bar / Start menu.
        if ((mods & (Modifiers.Alt | Modifiers.Win)) != 0) Post(InputSimulator.SendMenuMask);
        return true;
    }

    private Modifiers CurrentModifiers()
    {
        // Reconcile with the real key state in case a key-up was missed (secure desktop, hook reinstall…).
        foreach (var (flag, a, b) in ModifierKeys)
        {
            if ((_physicalMods & flag) != 0 && !Win32.IsKeyDown(a) && !Win32.IsKeyDown(b) &&
                Environment.TickCount64 - InputSimulatorState.LastModifierRelease > 2000)
                _physicalMods &= ~flag;
        }
        return _physicalMods | _kbOutputMods;
    }

    private static readonly (Modifiers Flag, int A, int B)[] ModifierKeys =
    [
        (Modifiers.Ctrl, KeyNames.VK_LCONTROL, KeyNames.VK_RCONTROL),
        (Modifiers.Alt, KeyNames.VK_LMENU, KeyNames.VK_RMENU),
        (Modifiers.Shift, KeyNames.VK_LSHIFT, KeyNames.VK_RSHIFT),
        (Modifiers.Win, KeyNames.VK_LWIN, KeyNames.VK_RWIN),
    ];

    private static Modifiers ModifierOf(int vk) => vk switch
    {
        KeyNames.VK_CONTROL or KeyNames.VK_LCONTROL or KeyNames.VK_RCONTROL => Modifiers.Ctrl,
        KeyNames.VK_MENU or KeyNames.VK_LMENU or KeyNames.VK_RMENU => Modifiers.Alt,
        KeyNames.VK_SHIFT or KeyNames.VK_LSHIFT or KeyNames.VK_RSHIFT => Modifiers.Shift,
        _ => Modifiers.Win,
    };

    // ── mouse ────────────────────────────────────────────────────────────

    private bool OnMouse(MouseInput e)
    {
        if (e.Injected) return false;
        if (KBoardMouse(e)) return true;
        if (_recorder is { } recorder) return recorder.OnMouse(e);

        if (e.Message == MouseMessage.Down && e.Button == MouseButton.Left && _windowPicker is { } picker)
        {
            _windowPicker = null;
            _swallowPickerUp = true;
            var hwnd = Win32.WindowFromPoint(new Win32.POINT { X = e.X, Y = e.Y });
            var window = ForegroundWindow.FromHandle(hwnd == IntPtr.Zero ? IntPtr.Zero : Win32.GetAncestor(hwnd, Win32.GA_ROOT));
            Post(() => picker(window));
            return true;
        }
        if (e.Message == MouseMessage.Up && e.Button == MouseButton.Left && _swallowPickerUp)
        {
            _swallowPickerUp = false;
            return true;
        }

        switch (e.Message)
        {
            case MouseMessage.Down: return OnMouseDown(e.Button);
            case MouseMessage.Up: return OnMouseUp(e.Button);
            case MouseMessage.Wheel:
            case MouseMessage.HWheel: return OnWheel(e);
            default: return false;
        }
    }

    private bool OnMouseDown(MouseButton button)
    {
        var name = ButtonName(button);
        _chord.Remove(name);
        _chord.Add(name);
        _armed = true;
        var mods = CurrentModifiers();
        var hotkey = Hotkey.Format(mods, string.Join(Hotkey.Separator, _chord));

        if (_capture is { } capture)
        {
            // Plain left/right clicks keep working so the user can interact with the editor.
            if (_chord.Count == 1 && mods == Modifiers.None && button is MouseButton.Left or MouseButton.Right) return false;
            _captureMouse = true;
            capture(hotkey, false);
            _blockedButtons.Add(button);
            return true;
        }

        var index = _index;
        if (index.VoiceHotkey.Length > 0 && hotkey.Equals(index.VoiceHotkey, StringComparison.OrdinalIgnoreCase))
        {
            _armed = false; // nothing fires on release
            _voiceButton = button;
            _voiceButtonHeld = true;
            _blockedButtons.Add(button);
            Post(() => VoiceRequested?.Invoke(() => _voiceButtonHeld));
            return true;
        }
        var block = false;
        ForegroundWindow? fg = null;
        if (button is MouseButton.X1 or MouseButton.X2 && index.BlockSideButtons)
        {
            fg = ForegroundWindow.Capture();
            block = index.BlocksSideButtonsIn(fg.ProcessName);
        }
        if (index.Contains(hotkey))
        {
            fg ??= ForegroundWindow.Capture();
            if (index.Resolve(hotkey, fg.ProcessName, out var b).Count > 0) block |= b;
        }
        if (block) _blockedButtons.Add(button);
        return block;
    }

    private bool OnMouseUp(MouseButton button)
    {
        var name = ButtonName(button);
        if (_voiceButton == button)
        {
            _voiceButton = null;
            _voiceButtonHeld = false;
        }
        var fire = _armed && _chord.Count > 0 ? Hotkey.Format(CurrentModifiers(), string.Join(Hotkey.Separator, _chord)) : null;
        _armed = false;
        _chord.Remove(name);
        var wasBlocked = _blockedButtons.Remove(button);

        if (_captureMouse && _capture is { } capture)
        {
            if (fire is not null) capture(fire, true);
            _captureMouse = _chord.Count > 0;
            return true;
        }
        _captureMouse = false;

        if (fire is not null) Dispatch(fire);
        return wasBlocked;
    }

    private bool OnWheel(MouseInput e)
    {
        var name = (e.Message == MouseMessage.Wheel ? "Wheel " : "HWheel ") + (e.WheelDelta > 0 ? "Up" : "Down");
        var mods = CurrentModifiers();
        var main = _chord.Count == 0 ? name : string.Join(Hotkey.Separator, _chord) + Hotkey.Separator + name;
        var hotkey = Hotkey.Format(mods, main);

        if (_capture is { } capture)
        {
            if (mods == Modifiers.None && _chord.Count == 0) return false;
            _armed = false;
            capture(hotkey, true);
            return true;
        }

        var index = _index;
        if (index.VoiceHotkey.Length > 0 && hotkey.Equals(index.VoiceHotkey, StringComparison.OrdinalIgnoreCase))
        {
            _armed = false;
            Post(() => VoiceRequested?.Invoke(static () => false));
            return true;
        }
        if (!index.Contains(hotkey)) return false;
        var fg = ForegroundWindow.Capture();
        var commands = index.Resolve(hotkey, fg.ProcessName, out var block);
        if (commands.Count == 0) return false;
        _armed = false; // the held buttons were used as a modifier: don't fire them on release
        Post(() => Triggered?.Invoke(new HotkeyTrigger(hotkey, commands, false, fg)));
        return block;
    }

    private void Dispatch(string hotkey)
    {
        var index = _index;
        if (!index.Contains(hotkey)) return;
        var fg = ForegroundWindow.Capture();
        var commands = index.Resolve(hotkey, fg.ProcessName, out _);
        if (commands.Count > 0) Post(() => Triggered?.Invoke(new HotkeyTrigger(hotkey, commands, false, fg)));
    }

    private static string ButtonName(MouseButton b) => b switch
    {
        MouseButton.Left => "Mouse 1",
        MouseButton.Right => "Mouse 2",
        MouseButton.Middle => "Mouse 3",
        MouseButton.X1 => "Mouse 4",
        MouseButton.X2 => "Mouse 5",
        _ => "Mouse ?",
    };

    private static void Post(Action action) => ThreadPool.UnsafeQueueUserWorkItem(static a =>
    {
        try { a(); }
        catch (Exception ex) { Log.Error("Error procesando un hotkey", ex); }
    }, action, preferLocal: false);

    public void Dispose() => _hook.Dispose();
}

/// <summary>Shared timestamp so the router knows modifier key-ups may have been injected by us.</summary>
internal static class InputSimulatorState
{
    public static long LastModifierRelease;
}

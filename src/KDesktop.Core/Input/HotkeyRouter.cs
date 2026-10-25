using KDesktop.Core.Model;
using KDesktop.Core.Native;

namespace KDesktop.Core.Input;

public enum BuiltInHotkey { ShowWindow, Exit, Launcher, Pause }

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
    /// The voice hotkey was pressed. The first argument tells whether it is still held (hold to talk); for the wheel it
    /// is always false (a tap). The second is the assistant whose key to talk it is (5.0.113); null for K-Desktop's voice key.
    /// </summary>
    public event Action<Func<bool>, string?>? VoiceRequested;

    /// <summary>The «Desactivar en» hotkey was pressed in the window given (Settings › Desactivar en).</summary>
    public event Action<ForegroundWindow>? DisabledInToggleRequested;

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

    internal bool OnKey(KeyboardInput e)
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

        if (GameKey(e)) return true;

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
        if (index.IsShowWindowHotkey(hotkey, FrontProcess))
        {
            if (!repeat) Post(() => BuiltIn?.Invoke(BuiltInHotkey.ShowWindow));
            return index.ShowWindowBlock && Block(vk, mods);
        }
        if (index.IsExitHotkey(hotkey, FrontProcess))
        {
            if (!repeat) Post(() => BuiltIn?.Invoke(BuiltInHotkey.Exit));
            return index.ExitBlock && Block(vk, mods);
        }
        if (index.IsPauseHotkey(hotkey, FrontProcess))
        {
            if (!repeat) Post(() => BuiltIn?.Invoke(BuiltInHotkey.Pause));
            return index.PauseBlock && Block(vk, mods);
        }

        if (index.IsTalkKey(hotkey, FrontProcess, out var keyToAssistant))
        {
            var to = keyToAssistant;
            if (!repeat) Post(() => VoiceRequested?.Invoke(() => IsKeyHeld(vk), to));
            return index.BlocksTalk(to) && Block(vk, mods);
        }

        if (index.IsLauncherHotkey(hotkey, FrontProcess))
        {
            if (!repeat) Post(() => BuiltIn?.Invoke(BuiltInHotkey.Launcher));
            return index.LauncherBlock && Block(vk, mods);
        }

        if (index.IsDisabledInHotkey(hotkey))
        {
            if (!repeat)
            {
                var window = ForegroundWindow.Capture();
                Post(() => DisabledInToggleRequested?.Invoke(window));
            }
            return index.DisabledInBlock && Block(vk, mods);
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
        if (e.Message is MouseMessage.Wheel or MouseMessage.HWheel) WheelObserver?.Invoke(e);
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
        if (index.IsPauseHotkey(hotkey, FrontProcess))
        {
            if (!index.PauseBlock) return false; // the application gets it too; it still fires on release
            _blockedButtons.Add(button); // it fires on release (Dispatch)
            return true;
        }
        if (index.IsTalkKey(hotkey, FrontProcess, out var buttonToAssistant))
        {
            _armed = false; // nothing fires on release
            _voiceButton = button;
            _voiceButtonHeld = true;
            var to = buttonToAssistant;
            var blockButton = index.BlocksTalk(to);
            if (blockButton) _blockedButtons.Add(button);
            Post(() => VoiceRequested?.Invoke(() => _voiceButtonHeld, to));
            return blockButton;
        }
        if (index.IsDisabledInHotkey(hotkey))
        {
            if (!index.DisabledInBlock) return false; // the application gets it too; it still fires on release
            _blockedButtons.Add(button); // it fires on release (Dispatch)
            return true;
        }
        if (index.IsLauncherHotkey(hotkey, FrontProcess))
        {
            if (!index.LauncherBlock) return false; // the application gets it too; it still fires on release
            _blockedButtons.Add(button);
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
        if (index.IsPauseHotkey(hotkey, FrontProcess))
        {
            _armed = false;
            Post(() => BuiltIn?.Invoke(BuiltInHotkey.Pause));
            return index.PauseBlock;
        }
        if (index.IsTalkKey(hotkey, FrontProcess, out var wheelToAssistant))
        {
            _armed = false;
            var to = wheelToAssistant;
            Post(() => VoiceRequested?.Invoke(static () => false, to));
            return index.BlocksTalk(to);
        }
        if (index.IsLauncherHotkey(hotkey, FrontProcess))
        {
            _armed = false;
            Post(() => BuiltIn?.Invoke(BuiltInHotkey.Launcher));
            return index.LauncherBlock;
        }
        if (index.IsDisabledInHotkey(hotkey))
        {
            _armed = false;
            var window = ForegroundWindow.Capture();
            Post(() => DisabledInToggleRequested?.Invoke(window));
            return index.DisabledInBlock;
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
        if (index.IsPauseHotkey(hotkey, FrontProcess))
        {
            Post(() => BuiltIn?.Invoke(BuiltInHotkey.Pause));
            return;
        }
        if (index.IsLauncherHotkey(hotkey, FrontProcess))
        {
            Post(() => BuiltIn?.Invoke(BuiltInHotkey.Launcher));
            return;
        }
        if (index.IsDisabledInHotkey(hotkey))
        {
            var window = ForegroundWindow.Capture();
            Post(() => DisabledInToggleRequested?.Invoke(window));
            return;
        }
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

    /// <summary>The program in front, asked only when a hotkey that respects «Desactivar en» is pressed.</summary>
    private static string FrontProcess() => ForegroundWindow.Capture().ProcessName;

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

using System.Runtime.InteropServices;
using KDesktop.Core.Native;

namespace KDesktop.Core.Input;

/// <summary>Synthesises keyboard and mouse input with SendInput.</summary>
public static class InputSimulator
{
    /// <summary>Marker placed in dwExtraInfo of every event we inject.</summary>
    public static readonly IntPtr Marker = new(0x4B44_0005);

    private static readonly int InputSize = Marshal.SizeOf<Win32.INPUT>();
    private static readonly int[] ModifierVks =
        [KeyNames.VK_LCONTROL, KeyNames.VK_RCONTROL, KeyNames.VK_LSHIFT, KeyNames.VK_RSHIFT, KeyNames.VK_LMENU, KeyNames.VK_RMENU, KeyNames.VK_LWIN, KeyNames.VK_RWIN];

    // ── keyboard ─────────────────────────────────────────────────────────

    public static void KeyDown(int vk, bool extended = false) => Send(Key(vk, false, extended));
    public static void KeyUp(int vk, bool extended = false) => Send(Key(vk, true, extended));
    public static void KeyPress(int vk, bool extended = false) => Send(Key(vk, false, extended), Key(vk, true, extended));

    /// <summary>
    /// Sends an unassigned virtual key. Used after swallowing a key while ALT/WIN is held so that
    /// releasing ALT/WIN does not open the menu bar / Start menu.
    /// </summary>
    public static void SendMenuMask() => KeyPress(0xE8);

    /// <summary>Releases modifiers that are physically held (e.g. the ones of the hotkey that triggered us).</summary>
    public static void ReleaseModifiers()
    {
        var ups = ModifierVks.Where(Win32.IsKeyDown).Select(vk => Key(vk, true)).ToArray();
        if (ups.Length == 0) return;
        InputSimulatorState.LastModifierRelease = Environment.TickCount64;
        Send(ups);
    }

    /// <summary>Sends a combination in hotkey format: "CTRL + SHIFT + N", "F6", "Mouse 1", "CTRL + Wheel Up".</summary>
    public static bool SendCombo(string combo)
    {
        var (mods, main) = Hotkey.Parse(combo);
        if (main.Length == 0) return false;

        var down = new List<Win32.INPUT>();
        var up = new List<Win32.INPUT>();
        void Mod(Modifiers flag, int vk)
        {
            if (!mods.HasFlag(flag)) return;
            down.Add(Key(vk, false));
            up.Insert(0, Key(vk, true));
        }
        Mod(Modifiers.Win, KeyNames.VK_LWIN);
        Mod(Modifiers.Ctrl, KeyNames.VK_LCONTROL);
        Mod(Modifiers.Alt, KeyNames.VK_LMENU);
        Mod(Modifiers.Shift, KeyNames.VK_LSHIFT);

        var mainInputs = MainInputs(main);
        if (mainInputs is null) return false;

        ReleaseModifiers();
        Send([.. down, .. mainInputs, .. up]);
        if (mods != Modifiers.None) InputSimulatorState.LastModifierRelease = Environment.TickCount64;
        return true;
    }

    /// <summary>Types arbitrary Unicode text, independent of the keyboard layout.</summary>
    public static void SendText(string text, bool releaseModifiers = true)
    {
        if (releaseModifiers) ReleaseModifiers();
        var inputs = new List<Win32.INPUT>(text.Length * 2);
        foreach (var ch in text.Replace("\r\n", "\n"))
        {
            if (ch == '\n')
            {
                inputs.Add(Key(KeyNames.VK_RETURN, false));
                inputs.Add(Key(KeyNames.VK_RETURN, true));
                continue;
            }
            if (ch == '\t')
            {
                inputs.Add(Key(KeyNames.VK_TAB, false));
                inputs.Add(Key(KeyNames.VK_TAB, true));
                continue;
            }
            inputs.Add(Unicode(ch, false));
            inputs.Add(Unicode(ch, true));
        }
        // Chunk to keep each SendInput call reasonably small.
        foreach (var chunk in inputs.Chunk(64)) Send(chunk);
    }

    // ── mouse ────────────────────────────────────────────────────────────

    public static void MouseClick(MouseButton button)
    {
        var (downFlag, upFlag, data) = ButtonFlags(button);
        if (downFlag == 0) return;
        Send(Mouse(downFlag, data), Mouse(upFlag, data));
    }

    public static void MouseDown(MouseButton button)
    {
        var (downFlag, _, data) = ButtonFlags(button);
        if (downFlag != 0) Send(Mouse(downFlag, data));
    }

    public static void MouseUp(MouseButton button)
    {
        var (_, upFlag, data) = ButtonFlags(button);
        if (upFlag != 0) Send(Mouse(upFlag, data));
    }

    public static void Wheel(int delta, bool horizontal = false) =>
        Send(Mouse(horizontal ? Win32.MOUSEEVENTF_HWHEEL : Win32.MOUSEEVENTF_WHEEL, delta));

    public static void MoveTo(int x, int y) => Win32.SetCursorPos(x, y);

    public static void MoveBy(int dx, int dy)
    {
        if (Win32.GetCursorPos(out var p)) Win32.SetCursorPos(p.X + dx, p.Y + dy);
    }

    public static (int X, int Y) CursorPosition() => Win32.GetCursorPos(out var p) ? (p.X, p.Y) : (0, 0);

    // ── helpers ──────────────────────────────────────────────────────────

    private static Win32.INPUT[]? MainInputs(string main)
    {
        var canonical = KeyNames.Canonical(main);
        switch (canonical)
        {
            case "Wheel Up": return [Mouse(Win32.MOUSEEVENTF_WHEEL, 120)];
            case "Wheel Down": return [Mouse(Win32.MOUSEEVENTF_WHEEL, -120)];
            case "HWheel Up": return [Mouse(Win32.MOUSEEVENTF_HWHEEL, 120)];
            case "HWheel Down": return [Mouse(Win32.MOUSEEVENTF_HWHEEL, -120)];
        }
        if (canonical.StartsWith("Mouse ", StringComparison.Ordinal))
        {
            var button = canonical[^1] switch
            {
                '1' => MouseButton.Left, '2' => MouseButton.Right, '3' => MouseButton.Middle,
                '4' => MouseButton.X1, '5' => MouseButton.X2, _ => MouseButton.None,
            };
            var (d, u, data) = ButtonFlags(button);
            return d == 0 ? null : [Mouse(d, data), Mouse(u, data)];
        }
        var vk = KeyNames.ToVk(canonical);
        if (vk == 0) return null;
        return [Key(vk, false, canonical == "NumPadEnter"), Key(vk, true, canonical == "NumPadEnter")];
    }

    private static (uint Down, uint Up, int Data) ButtonFlags(MouseButton button) => button switch
    {
        MouseButton.Left => (Win32.MOUSEEVENTF_LEFTDOWN, Win32.MOUSEEVENTF_LEFTUP, 0),
        MouseButton.Right => (Win32.MOUSEEVENTF_RIGHTDOWN, Win32.MOUSEEVENTF_RIGHTUP, 0),
        MouseButton.Middle => (Win32.MOUSEEVENTF_MIDDLEDOWN, Win32.MOUSEEVENTF_MIDDLEUP, 0),
        MouseButton.X1 => (Win32.MOUSEEVENTF_XDOWN, Win32.MOUSEEVENTF_XUP, 1),
        MouseButton.X2 => (Win32.MOUSEEVENTF_XDOWN, Win32.MOUSEEVENTF_XUP, 2),
        _ => (0u, 0u, 0),
    };

    private static Win32.INPUT Key(int vk, bool up, bool forceExtended = false)
    {
        var flags = up ? Win32.KEYEVENTF_KEYUP : 0;
        if (forceExtended || KeyNames.IsExtendedVk(vk)) flags |= Win32.KEYEVENTF_EXTENDEDKEY;
        return new Win32.INPUT
        {
            type = Win32.INPUT_KEYBOARD,
            U = new Win32.InputUnion
            {
                ki = new Win32.KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = (ushort)Win32.MapVirtualKey((uint)vk, 0),
                    dwFlags = flags,
                    dwExtraInfo = Marker,
                },
            },
        };
    }

    private static Win32.INPUT Unicode(char ch, bool up) => new()
    {
        type = Win32.INPUT_KEYBOARD,
        U = new Win32.InputUnion
        {
            ki = new Win32.KEYBDINPUT
            {
                wVk = 0,
                wScan = ch,
                dwFlags = Win32.KEYEVENTF_UNICODE | (up ? Win32.KEYEVENTF_KEYUP : 0),
                dwExtraInfo = Marker,
            },
        },
    };

    private static Win32.INPUT Mouse(uint flags, int data) => new()
    {
        type = Win32.INPUT_MOUSE,
        U = new Win32.InputUnion { mi = new Win32.MOUSEINPUT { dwFlags = flags, mouseData = data, dwExtraInfo = Marker } },
    };

    private static void Send(params Win32.INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        if (Win32.SendInput((uint)inputs.Length, inputs, InputSize) != inputs.Length)
            Log.Warn($"SendInput no envió todos los eventos (error {Marshal.GetLastWin32Error()})");
    }
}

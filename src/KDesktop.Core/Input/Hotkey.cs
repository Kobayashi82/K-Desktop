using System.Globalization;

namespace KDesktop.Core.Input;

[Flags]
public enum Modifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// Hotkeys are stored as text, compatible with K-Desktop 4:
/// <c>"CTRL + ALT + SHIFT + WIN + Key"</c>, <c>"Mouse 5"</c>, <c>"Mouse 4 + Mouse 1"</c>, <c>"CTRL + Wheel Up"</c>.
/// </summary>
public static class Hotkey
{
    public const string Separator = " + ";

    public static readonly IEqualityComparer<string> Comparer = StringComparer.OrdinalIgnoreCase;

    public static string Format(Modifiers mods, string main)
    {
        var prefix = string.Empty;
        if (mods.HasFlag(Modifiers.Ctrl)) prefix += "CTRL + ";
        if (mods.HasFlag(Modifiers.Alt)) prefix += "ALT + ";
        if (mods.HasFlag(Modifiers.Shift)) prefix += "SHIFT + ";
        if (mods.HasFlag(Modifiers.Win)) prefix += "WIN + ";
        return prefix + main;
    }

    /// <summary>Splits a hotkey into its modifiers and main part (key or mouse chord).</summary>
    public static (Modifiers Mods, string Main) Parse(string hotkey)
    {
        var mods = Modifiers.None;
        var parts = new List<string>();
        var tokens = Split(hotkey);
        foreach (var raw in tokens)
        {
            var t = raw.Trim();
            switch (t.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": mods |= Modifiers.Ctrl; break;
                case "ALT": mods |= Modifiers.Alt; break;
                case "SHIFT": mods |= Modifiers.Shift; break;
                case "WIN": mods |= Modifiers.Win; break;
                default: parts.Add(KeyNames.Canonical(t)); break;
            }
        }
        return (mods, string.Join(Separator, parts));
    }

    /// <summary>Canonical textual form (modifier order fixed, key names normalised).</summary>
    public static string Normalize(string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey)) return string.Empty;
        var (mods, main) = Parse(hotkey);
        return main.Length == 0 ? string.Empty : Format(mods, main);
    }

    /// <summary>A screen corner (E4): <c>Corner TopLeft</c>, <c>Corner BottomRight</c>…</summary>
    public const string CornerPrefix = "Corner ";

    /// <summary>A mouse gesture (E4): <c>Gesture C</c>, <c>Gesture Left</c> or the name of one drawn by the user.</summary>
    public const string GesturePrefix = "Gesture ";

    public static bool IsCorner(string hotkey) => hotkey.StartsWith(CornerPrefix, StringComparison.OrdinalIgnoreCase);

    public static bool IsGesture(string hotkey) => hotkey.StartsWith(GesturePrefix, StringComparison.OrdinalIgnoreCase);

    public static string Gesture(string id) => GesturePrefix + id;

    /// <summary>
    /// How a hotkey is shown: as stored, but a corner or a gesture in the language of the interface («Esquina ↖», «Gesto C»).
    /// </summary>
    public static string Display(string hotkey)
    {
        if (IsCorner(hotkey)) return Localization.Loc.T("hotkey.corner." + hotkey[CornerPrefix.Length..].Trim());
        if (IsGesture(hotkey)) return Localization.Loc.T("hotkey.gesture", MouseGestures.Symbol(hotkey[GesturePrefix.Length..].Trim()));
        return hotkey;
    }

    public static bool IsMouse(string hotkey)
    {
        var (_, main) = Parse(hotkey);
        return main.StartsWith("Mouse ", StringComparison.OrdinalIgnoreCase) ||
               main.Contains("Wheel", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Splits on " + " but keeps a literal "+" key ("CTRL + +").</summary>
    private static List<string> Split(string hotkey)
    {
        var result = new List<string>();
        var rest = hotkey.Trim();
        while (rest.Length > 0)
        {
            var idx = rest.IndexOf(Separator, StringComparison.Ordinal);
            if (idx < 0) { result.Add(rest); break; }
            if (idx == 0) { result.Add("+"); rest = rest[1..].TrimStart(); continue; }
            result.Add(rest[..idx]);
            rest = rest[(idx + Separator.Length)..];
            if (rest == "+") { result.Add("+"); break; }
        }
        return result.Where(t => t.Length > 0).ToList();
    }
}

/// <summary>Virtual-key ↔ name table (names match K-Desktop 4 / WinForms Keys, Spanish layout for OEM keys).</summary>
public static class KeyNames
{
    public const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04, VK_XBUTTON1 = 0x05, VK_XBUTTON2 = 0x06;
    public const int VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    public const int VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    public const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
    public const int VK_CAPITAL = 0x14;

    private static readonly Dictionary<int, string> VkToName = new();
    private static readonly Dictionary<string, int> NameToVk = new(StringComparer.OrdinalIgnoreCase);

    static KeyNames()
    {
        void Add(int vk, string name, params string[] aliases)
        {
            VkToName.TryAdd(vk, name);
            NameToVk.TryAdd(name, vk);
            foreach (var a in aliases) NameToVk.TryAdd(a, vk);
        }

        for (var c = 'A'; c <= 'Z'; c++) Add(c, c.ToString());
        for (var d = 0; d <= 9; d++) Add('0' + d, d.ToString(CultureInfo.InvariantCulture), "D" + d);
        for (var f = 1; f <= 24; f++) Add(0x6F + f, "F" + f);
        for (var n = 0; n <= 9; n++) Add(0x60 + n, "NumPad" + n);

        Add(VK_BACK, "Backspace", "Back");
        Add(VK_TAB, "Tab");
        Add(0x0C, "NumPadClear", "Clear");
        Add(VK_RETURN, "Enter", "Return");
        Add(0x13, "Pause");
        Add(VK_CAPITAL, "CapsLock", "Capital");
        Add(VK_ESCAPE, "Escape", "Esc");
        Add(VK_SPACE, "Space");
        Add(0x21, "PageUp", "Prior");
        Add(0x22, "PageDown", "Next", "PgDn");
        Add(0x23, "End");
        Add(0x24, "Home");
        Add(0x25, "Left");
        Add(0x26, "Up");
        Add(0x27, "Right");
        Add(0x28, "Down");
        Add(0x29, "Select");
        Add(0x2A, "Print");
        Add(0x2B, "Execute");
        Add(0x2C, "PrintScreen", "Snapshot");
        Add(0x2D, "Insert");
        Add(0x2E, "Delete", "Supr");
        Add(0x2F, "Help");
        Add(VK_LWIN, "LWin");
        Add(VK_RWIN, "RWin");
        Add(0x5D, "AppsKey", "Apps");
        Add(0x5F, "Sleep");
        Add(0x6A, "NumPadMult", "Multiply");
        Add(0x6B, "NumPadAdd", "Add");
        Add(0x6C, "Separator");
        Add(0x6D, "NumPadSub", "Subtract");
        Add(0x6E, "NumPadDot", "Decimal");
        Add(0x6F, "NumPadDiv", "Divide");
        Add(0x90, "NumLock");
        Add(0x91, "Scroll", "ScrollLock");
        Add(VK_LSHIFT, "LShift", "LShiftKey");
        Add(VK_RSHIFT, "RShift", "RShiftKey");
        Add(VK_LCONTROL, "LCtrl", "LControlKey");
        Add(VK_RCONTROL, "RCtrl", "RControlKey");
        Add(VK_LMENU, "LAlt", "LMenu");
        Add(VK_RMENU, "RAlt", "RMenu");
        Add(0xA6, "BrowserBack");
        Add(0xA7, "BrowserForward");
        Add(0xA8, "BrowserRefresh");
        Add(0xA9, "BrowserStop");
        Add(0xAA, "BrowserSearch");
        Add(0xAB, "BrowserFavorites");
        Add(0xAC, "BrowserHome");
        Add(0xAD, "VolumeMute");
        Add(0xAE, "VolumeDown");
        Add(0xAF, "VolumeUp");
        Add(0xB0, "MediaNextTrack");
        Add(0xB1, "MediaPreviousTrack");
        Add(0xB2, "MediaStop");
        Add(0xB3, "MediaPlayPause");
        Add(0xB4, "LaunchMail");
        Add(0xB5, "SelectMedia");
        Add(0xB6, "LaunchApplication1");
        Add(0xB7, "LaunchApplication2");
        // OEM keys, named after what they print on a Spanish keyboard (same as K-Desktop 4).
        Add(0xBA, "`", "Oem1");
        Add(0xBB, "+", "OemPlus");
        Add(0xBC, ",", "OemComma");
        Add(0xBD, "-", "OemMinus");
        Add(0xBE, ".", "OemPeriod");
        Add(0xBF, "ç", "OemQuestion", "Oem2");
        Add(0xC0, "Ñ", "OemTilde", "Oem3");
        Add(0xDB, "'", "OemOpenBrackets", "Oem4");
        Add(0xDC, "\\", "Oem5", "OemPipe");
        Add(0xDD, "¡", "Oem6", "OemCloseBrackets");
        Add(0xDE, "´", "Oem7", "OemQuotes");
        Add(0xDF, "Oem8");
        Add(0xE2, "<", "OemBackslash", "Oem102");
        Add(0xFA, "Play");
        Add(0xFB, "Zoom");
        // Synthetic name used when the Enter key comes from the numeric pad.
        NameToVk.TryAdd("NumPadEnter", VK_RETURN);
    }

    public static string FromVk(int vk, bool extended = false)
    {
        if (vk == VK_RETURN && extended) return "NumPadEnter";
        return VkToName.TryGetValue(vk, out var name) ? name : $"VK{vk:X2}";
    }

    public static int ToVk(string name)
    {
        if (NameToVk.TryGetValue(name.Trim(), out var vk)) return vk;
        var n = name.Trim();
        if (n.StartsWith("VK", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(n[2..], System.Globalization.NumberStyles.HexNumber, null, out vk)) return vk;
        return 0;
    }

    /// <summary>Normalises a key/button token ("lcontrolkey" → "LCtrl", "mouse4" → "Mouse 4").</summary>
    public static string Canonical(string token)
    {
        var t = token.Trim();
        var compact = t.Replace(" ", string.Empty);
        if (compact.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase) && compact.Length == 6 && char.IsDigit(compact[5]))
            return "Mouse " + compact[5];
        foreach (var w in WheelNames)
            if (compact.Equals(w.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase)) return w;
        if (t.Equals("NumPadEnter", StringComparison.OrdinalIgnoreCase)) return "NumPadEnter";
        var vk = ToVk(t);
        return vk != 0 ? FromVk(vk) : t;
    }

    public static readonly string[] WheelNames = ["Wheel Up", "Wheel Down", "HWheel Up", "HWheel Down"];

    public static bool IsModifierVk(int vk) => vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LSHIFT or VK_RSHIFT
        or VK_LCONTROL or VK_RCONTROL or VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN;

    public static bool IsExtendedVk(int vk) => vk is >= 0x21 and <= 0x2E or VK_RCONTROL or VK_RMENU or VK_LWIN or VK_RWIN
        or 0x5D or 0x6F or 0x90 or >= 0xA6 and <= 0xB7;
}

using System.Runtime.InteropServices;

namespace KDesktop.Core.Platform;

/// <summary>A Windows power plan («Equilibrado», «Alto rendimiento»…).</summary>
public sealed record PowerPlan(Guid Id, string Name, bool IsActive)
{
    /// <summary>The id as the actions keep it.</summary>
    public string Key => Id.ToString();
}

/// <summary>Lists the power plans and switches the active one (what the control panel's «Opciones de energía» does).</summary>
public static class PowerPlans
{
    private const uint AccessScheme = 16;

    public static IReadOnlyList<PowerPlan> All()
    {
        var result = new List<PowerPlan>();
        var active = Active();
        for (uint index = 0; ; index++)
        {
            var buffer = new byte[16];
            var size = (uint)buffer.Length;
            if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, AccessScheme, index, buffer, ref size) != 0) break;
            var id = new Guid(buffer);
            result.Add(new PowerPlan(id, Name(id), id == active));
        }
        return result;
    }

    public static Guid? Active()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0 || pointer == IntPtr.Zero) return null;
        try { return Marshal.PtrToStructure<Guid>(pointer); }
        finally { LocalFree(pointer); }
    }

    public static bool SetActive(Guid id)
    {
        var result = PowerSetActiveScheme(IntPtr.Zero, ref id);
        if (result != 0) Log.Warn($"No se pudo activar el plan de energía {id} (error {result})");
        return result == 0;
    }

    public static string Name(Guid id)
    {
        uint size = 0;
        if (PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref size) != 0 || size == 0) return id.ToString();
        var buffer = new byte[size];
        if (PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buffer, ref size) != 0) return id.ToString();
        return System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    /// <summary>The plan after the active one (in Windows' order), round and round.</summary>
    public static PowerPlan? Next()
    {
        var plans = All();
        if (plans.Count == 0) return null;
        var current = plans.ToList().FindIndex(p => p.IsActive);
        return plans[(current + 1) % plans.Count];
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid, uint accessFlags, uint index, byte[] buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroupGuid, IntPtr settingGuid, byte[]? buffer, ref uint bufferSize);

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}

/// <summary>Light or dark mode of Windows (Configuración › Personalización › Colores).</summary>
public static class WindowsTheme
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Whether the apps are in light mode now.</summary>
    public static bool AppsLight()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
        return key?.GetValue("AppsUseLightTheme") is not int v || v != 0;
    }

    /// <summary>Sets light (true) or dark mode for the apps and/or the taskbar and Start, and tells every window.</summary>
    public static void Set(bool light, bool apps, bool system)
    {
        using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key))
        {
            if (apps) key.SetValue("AppsUseLightTheme", light ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
            if (system) key.SetValue("SystemUsesLightTheme", light ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);
        }
        // What the Settings app sends: open windows (Explorer, the taskbar…) repaint with the new colours.
        foreach (var area in new[] { "ImmersiveColorSet", "WindowsThemeElement" })
            SendMessageTimeout((IntPtr)0xFFFF, 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, area, 0x0002 /* SMTO_ABORTIFHUNG */, 1000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}

using System.Runtime.InteropServices;
using KDesktop.Core.Model;

namespace KDesktop.Core.Platform;

/// <summary>A monitor: its GDI name («\\.\DISPLAY1»), its own name («DELL U2723QE») and whether it is the main one.</summary>
public sealed record MonitorInfo(string DeviceName, string Name, bool IsPrimary)
{
    public string Display => IsPrimary ? $"{Name} ★" : Name;

    public override string ToString() => Display;
}

/// <summary>A resolution and refresh rate a monitor supports.</summary>
public sealed record DisplayModeInfo(int Width, int Height, int Frequency)
{
    public string Key => DisplayAction.KeyOf(Width, Height, Frequency);
    public string Name => ToString();

    public override string ToString() => DisplayAction.DescribeMode(Width, Height, Frequency);
}

/// <summary>Screens: turn them off, project (Win + P), brightness, resolution and refresh rate.</summary>
public static class DisplayTools
{
    /// <summary>Turns the screens off (they come back with the mouse or a key), like closing a laptop's lid does.</summary>
    public static void MonitorOff() =>
        // Posted, not sent: a hung window would block a broadcast SendMessage.
        PostMessage((IntPtr)0xFFFF, 0x0112 /* WM_SYSCOMMAND */, (IntPtr)0xF170 /* SC_MONITORPOWER */, 2);

    /// <summary>What Win + P does: only this screen, duplicate, extend or only the second screen.</summary>
    public static bool Project(ProjectionMode mode)
    {
        var topology = mode switch
        {
            ProjectionMode.Internal => 0x1u,
            ProjectionMode.Clone => 0x2u,
            ProjectionMode.External => 0x8u,
            _ => 0x4u,
        };
        var result = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, 0x80 /* SDC_APPLY */ | topology);
        if (result != 0) Log.Warn($"No se pudo cambiar la proyección a {mode} (error {result})");
        return result == 0;
    }

    // ── monitors and modes ───────────────────────────────────────────────

    /// <summary>The monitors that are on, with their own names when Windows knows them.</summary>
    public static IReadOnlyList<MonitorInfo> Monitors()
    {
        var names = FriendlyNames();
        var result = new List<MonitorInfo>();
        for (uint i = 0; ; i++)
        {
            var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref device, 0)) break;
            if ((device.StateFlags & 0x1 /* ATTACHED_TO_DESKTOP */) == 0) continue;
            var name = names.GetValueOrDefault(device.DeviceName);
            if (string.IsNullOrWhiteSpace(name))
            {
                var monitor = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                name = EnumDisplayDevices(device.DeviceName, 0, ref monitor, 0) ? monitor.DeviceString : device.DeviceString;
            }
            var number = device.DeviceName.Replace(@"\\.\DISPLAY", "", StringComparison.OrdinalIgnoreCase);
            result.Add(new MonitorInfo(device.DeviceName, $"{number}. {name}", (device.StateFlags & 0x4 /* PRIMARY */) != 0));
        }
        return result;
    }

    /// <summary>The modes a monitor supports at the current colour depth, best first.</summary>
    public static IReadOnlyList<DisplayModeInfo> Modes(string deviceName)
    {
        var result = new HashSet<DisplayModeInfo>();
        var current = Current(deviceName);
        for (var i = 0; ; i++)
        {
            var mode = NewDevMode();
            if (!EnumDisplaySettings(deviceName, i, ref mode)) break;
            if (current is { } c && mode.dmBitsPerPel != c.dmBitsPerPel) continue;
            if (mode.dmPelsWidth < 640) continue;
            result.Add(new DisplayModeInfo(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency));
        }
        return result.OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.Frequency).ToList();
    }

    /// <summary>Where a monitor is on the desktop, in pixels (null when it is not on).</summary>
    public static (int X, int Y, int Width, int Height)? Bounds(string deviceName) =>
        Current(deviceName) is { } mode ? (mode.dmPositionX, mode.dmPositionY, mode.dmPelsWidth, mode.dmPelsHeight) : null;

    public static DisplayModeInfo? CurrentMode(string deviceName) =>
        Current(deviceName) is { } mode ? new DisplayModeInfo(mode.dmPelsWidth, mode.dmPelsHeight, mode.dmDisplayFrequency) : null;

    /// <summary>
    /// Changes the resolution and/or the refresh rate (0 keeps that part). Returns the mode it is in afterwards, or null
    /// when Windows refused it.
    /// </summary>
    public static DisplayModeInfo? SetMode(string deviceName, int width, int height, int frequency)
    {
        if (Current(deviceName) is not { } mode) return null;
        if (width > 0 && height > 0)
        {
            mode.dmPelsWidth = width;
            mode.dmPelsHeight = height;
        }
        if (frequency > 0) mode.dmDisplayFrequency = frequency;
        mode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
        var result = ChangeDisplaySettingsEx(deviceName, ref mode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        if (result != 0)
        {
            Log.Warn($"Windows no aceptó {DisplayAction.DescribeMode(width, height, frequency)} en {deviceName} (código {result})");
            return null;
        }
        return CurrentMode(deviceName);
    }

    private static DEVMODE? Current(string deviceName)
    {
        var mode = NewDevMode();
        return EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref mode) ? mode : null;
    }

    private static DEVMODE NewDevMode() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>() };

    /// <summary>GDI name → the monitor's own name (EDID), from the display configuration.</summary>
    private static Dictionary<string, string> FriendlyNames()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0) return result;
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return result;
            for (var i = 0; i < pathCount; i++)
            {
                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                source.header.type = 1; // GET_SOURCE_NAME
                source.header.size = Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                source.header.adapterId = paths[i].sourceInfo.adapterId;
                source.header.id = paths[i].sourceInfo.id;
                if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;

                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                target.header.type = 2; // GET_TARGET_NAME
                target.header.size = Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                target.header.adapterId = paths[i].targetInfo.adapterId;
                target.header.id = paths[i].targetInfo.id;
                if (DisplayConfigGetDeviceInfo(ref target) != 0) continue;
                if (!string.IsNullOrWhiteSpace(target.monitorFriendlyDeviceName))
                    result.TryAdd(source.viewGdiDeviceName, target.monitorFriendlyDeviceName);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("No se pudieron leer los nombres de los monitores: " + ex.Message);
        }
        return result;
    }

    // ── brightness ───────────────────────────────────────────────────────

    /// <summary>
    /// Changes the brightness of every screen that lets it: external monitors through DDC/CI, a laptop's own screen
    /// through Windows. Returns the new level of the first one, or null when none could.
    /// </summary>
    public static int? ChangeBrightness(LevelChange change, int value)
    {
        int? first = null;
        foreach (var monitor in PhysicalMonitors())
        {
            using (monitor)
            {
                if (!GetMonitorBrightness(monitor.Handle, out var min, out var current, out var max) || max <= min) continue;
                var percent = (int)Math.Round((current - min) * 100.0 / (max - min));
                var target = Target(percent);
                var raw = (uint)Math.Round(min + (max - min) * target / 100.0);
                if (SetMonitorBrightness(monitor.Handle, raw)) first ??= target;
            }
        }
        if (LaptopBrightness() is { } laptop)
        {
            var target = Target(laptop);
            if (SetLaptopBrightness(target)) first ??= target;
        }
        return first;

        int Target(int current) => Math.Clamp(change switch
        {
            LevelChange.Up => current + value,
            LevelChange.Down => current - value,
            _ => value,
        }, 0, 100);
    }

    private sealed class PhysicalMonitor(IntPtr handle) : IDisposable
    {
        public IntPtr Handle { get; } = handle;
        public void Dispose() => DestroyPhysicalMonitor(Handle);
    }

    private static List<PhysicalMonitor> PhysicalMonitors()
    {
        var result = new List<PhysicalMonitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count) || count == 0) return true;
            var monitors = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, monitors)) return true;
            result.AddRange(monitors.Select(m => new PhysicalMonitor(m.hPhysicalMonitor)));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>The built-in screen's brightness (laptops), through WMI; null on a desktop.</summary>
    private static int? LaptopBrightness()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (var item in searcher.Get())
                using (item)
                    return Convert.ToInt32(item["CurrentBrightness"], System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or COMException or UnauthorizedAccessException) { }
        return null;
    }

    private static bool SetLaptopBrightness(int percent)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            var done = false;
            foreach (System.Management.ManagementObject item in searcher.Get())
                using (item)
                {
                    item.InvokeMethod("WmiSetBrightness", [1u, (byte)percent]);
                    done = true;
                }
            return done;
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or COMException or UnauthorizedAccessException)
        {
            Log.Warn("No se pudo cambiar el brillo de la pantalla: " + ex.Message);
            return false;
        }
    }

    // ── P/Invoke ─────────────────────────────────────────────────────────

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const uint CDS_UPDATEREGISTRY = 0x01;
    private const int DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x2;

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, int lParam) => PostMessage(hWnd, msg, wParam, (IntPtr)lParam);

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(uint numPathArrayElements, IntPtr pathArray, uint numModeInfoArrayElements, IntPtr modeInfoArray, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out int numPathArrayElements, out int numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref int numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref int numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("dxva2.dll")]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll")]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll")]
    private static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

    [DllImport("dxva2.dll")]
    private static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint minimum, out uint current, out uint maximum);

    [DllImport("dxva2.dll")]
    private static extern bool SetMonitorBrightness(IntPtr hMonitor, uint brightness);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public uint refreshRateNumerator;
        public uint refreshRateDenominator;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    /// <summary>64 bytes: the largest member of the union is the target mode.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public int type;
        public int size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }
}

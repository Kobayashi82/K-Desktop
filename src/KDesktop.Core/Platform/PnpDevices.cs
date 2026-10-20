using System.Runtime.InteropServices;
using System.Text;

namespace KDesktop.Core.Platform;

/// <summary>A device Windows has now (Device Manager): its instance id and the name it shows.</summary>
public sealed record PnpDevice(string Id, string Name);

/// <summary>
/// The devices present right now, as the Device Manager lists them (SetupAPI). Only the ones a person plugs or pairs:
/// USB, Bluetooth, HID (controllers, keyboards, mice…) and the like; the PC's own buses and software devices are left
/// out so «any device» doesn't fire with every driver that comes and goes. A paired Bluetooth device is in Windows
/// even when it is off: it counts only while it is connected (and its service nodes, always there, never count).
/// </summary>
public static class PnpDevices
{
    /// <summary>Enumerators of the PC itself (instance id prefix): never a device someone connects.</summary>
    private static readonly HashSet<string> Internal = new(StringComparer.OrdinalIgnoreCase)
    {
        "ROOT", "ACPI", "ACPI_HAL", "PCI", "HTREE", "UEFI", "SWD", "SW", "STORAGE", "SCSI", "HDAUDIO", "INTELAUDIO",
        "DISPLAY", "MONITOR", "VMBUS", "COMPOSITEBATTERY", "BTH", "UMB", "TS_USB_HUB_ENUMERATOR", "WPDBUSENUMROOT",
    };

    /// <param name="pairedToo">Also the paired Bluetooth devices that are off (to choose them in the editor).</param>
    public static IReadOnlyList<PnpDevice> Present(bool pairedToo = false)
    {
        var result = new List<PnpDevice>();
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;
        try
        {
            var info = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            var buffer = new char[512];
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref info); i++)
            {
                if (!SetupDiGetDeviceInstanceIdW(set, ref info, buffer, buffer.Length, out _)) continue;
                var id = new string(buffer, 0, Math.Max(0, Array.IndexOf(buffer, '\0')));
                var slash = id.IndexOf('\\');
                if (slash <= 0 || id[0] == '{' || Internal.Contains(id[..slash])) continue; // {GUID}\…: virtual adapters
                var bus = id[..slash].ToUpperInvariant();
                if (bus == "BTHLEDEVICE") continue; // the services of a BLE device
                if (bus is "BTHENUM" or "BTHLE")
                {
                    if (!id.Contains(@"\DEV_", StringComparison.OrdinalIgnoreCase)) continue; // the services of a paired device
                    if (!pairedToo && !IsConnected(set, ref info)) continue;
                }
                var name = Property(set, ref info, SPDRP_FRIENDLYNAME) ?? Property(set, ref info, SPDRP_DEVICEDESC);
                if (!string.IsNullOrWhiteSpace(name)) result.Add(new PnpDevice(id, name.Trim()));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    /// <summary>The Bluetooth «connected» flag of a device node ({83DA6326-…} 15).</summary>
    private static bool IsConnected(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        var key = new DEVPROPKEY { fmtid = new Guid("83DA6326-97A6-4088-9453-A1923F573B29"), pid = 15 };
        var value = new byte[4];
        return SetupDiGetDevicePropertyW(set, ref info, ref key, out var type, value, (uint)value.Length, out _, 0) &&
               type == DEVPROP_TYPE_BOOLEAN && value[0] != 0;
    }

    private static string? Property(IntPtr set, ref SP_DEVINFO_DATA info, uint property)
    {
        var bytes = new byte[1024];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref info, property, out _, bytes, (uint)bytes.Length, out var size)) return null;
        var text = Encoding.Unicode.GetString(bytes, 0, (int)Math.Min(size, (uint)bytes.Length));
        var end = text.IndexOf('\0');
        return end >= 0 ? text[..end] : text;
    }

    private const uint DIGCF_PRESENT = 0x2, DIGCF_ALLCLASSES = 0x4;
    private const uint SPDRP_DEVICEDESC = 0x0, SPDRP_FRIENDLYNAME = 0xC;
    private const uint DEVPROP_TYPE_BOOLEAN = 0x11;

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA info, ref DEVPROPKEY key, out uint type,
        byte[] buffer, uint size, out uint required, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr parent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA info);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA info, [Out] char[] id, int size, out int required);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA info, uint property, out uint type,
        byte[] buffer, uint size, out uint required);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}

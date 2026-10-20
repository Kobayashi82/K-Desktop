using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace KDesktop.Core.Assistant.Packages;

/// <summary>
/// Opening a <c>.kdassistant</c> with a double click: <c>KDesktop.exe --install-assistant "file"</c>, which the
/// running K-Desktop receives and answers with the package's card. Registered for the current user (HKCU) while the
/// assistant module is on, and only by the normal data folder (a test instance leaves the user's files alone).
/// </summary>
public static partial class PackageAssociation
{
    public const string Argument = "--install-assistant";

    private const string ProgId = "KDesktop.Assistant";
    private const string ExtensionKey = @"Software\Classes\" + AssistantPackages.Extension;
    private const string ProgIdKey = @"Software\Classes\" + ProgId;

    /// <summary>Registers the extension (or brings it up to date if K-Desktop moved), or removes it.</summary>
    /// <param name="typeName">What Explorer shows as the file's type.</param>
    public static void Apply(bool on, string typeName)
    {
        if (AppPaths.InstanceSuffix.Length > 0) return;
        try
        {
            if (on ? Register(typeName) : Unregister()) SHChangeNotify(SHCNE_ASSOCCHANGED, 0, 0, 0);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            Log.Warn($"Asistentes: no se ha podido {(on ? "asociar" : "desasociar")} {AssistantPackages.Extension} ({ex.Message})");
        }
    }

    private static bool Register(string typeName)
    {
        var exe = AppPaths.ExecutablePath;
        var command = $"\"{exe}\" {Argument} \"%1\"";
        using (var current = Registry.CurrentUser.OpenSubKey(ProgIdKey + @"\shell\open\command"))
        using (var ext = Registry.CurrentUser.OpenSubKey(ExtensionKey))
            if (current?.GetValue(null) as string == command && ext?.GetValue(null) as string == ProgId) return false;

        using (var key = Registry.CurrentUser.CreateSubKey(ExtensionKey)) key.SetValue(null, ProgId);
        using (var key = Registry.CurrentUser.CreateSubKey(ProgIdKey)) key.SetValue(null, typeName);
        using (var key = Registry.CurrentUser.CreateSubKey(ProgIdKey + @"\DefaultIcon")) key.SetValue(null, $"\"{exe}\",0");
        using (var key = Registry.CurrentUser.CreateSubKey(ProgIdKey + @"\shell\open\command")) key.SetValue(null, command);
        Log.Info($"Asistentes: {AssistantPackages.Extension} se abre con K-Desktop");
        return true;
    }

    private static bool Unregister()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(ProgIdKey))
            if (key is null) return false;
        Registry.CurrentUser.DeleteSubKeyTree(ProgIdKey, throwOnMissingSubKey: false);
        bool ours;
        using (var ext = Registry.CurrentUser.OpenSubKey(ExtensionKey)) ours = ext?.GetValue(null) as string == ProgId;
        if (ours) Registry.CurrentUser.DeleteSubKeyTree(ExtensionKey, throwOnMissingSubKey: false);
        Log.Info($"Asistentes: {AssistantPackages.Extension} ya no se abre con K-Desktop");
        return true;
    }

    private const int SHCNE_ASSOCCHANGED = 0x08000000;

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}

using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace KDesktop.Core.Platform;

/// <summary>
/// A stable id of this PC for the phone app (it keeps a list of PCs): the same while Windows is the same, whatever the
/// PC is called, and nothing to store. Made from Windows' own machine id, hashed so the phone never sees that one.
/// </summary>
public static class PcIdentity
{
    private static readonly Lazy<string> _id = new(Make);

    public static string Id => _id.Value;

    private static string Make()
    {
        string? guid = null;
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            guid = key?.GetValue("MachineGuid") as string;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            Log.Warn("No se pudo leer el identificador de Windows: " + ex.Message);
        }
        var seed = "K-Desktop|" + (guid ?? Environment.MachineName);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..20];
    }
}

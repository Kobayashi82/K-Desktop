using System.Security.Cryptography;
using System.Text;

namespace KDesktop.Core.Platform;

/// <summary>
/// Values kept encrypted in config.json with DPAPI (current Windows user): a token in a header of «Petición web».
/// Only this user on this PC can read them back, so a copy restored on another PC loses them (they must be typed
/// again there).
/// </summary>
public static class Secrets
{
    private const string Prefix = "dpapi:";
    private static readonly byte[] Entropy = "K-Desktop 5"u8.ToArray();

    public static string Protect(string value) =>
        Prefix + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));

    /// <summary>The plain value, or null when it can't be read (another user or PC, damaged text).</summary>
    public static string? Unprotect(string stored)
    {
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored[Prefix.Length..]), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}

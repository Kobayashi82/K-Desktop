using System.Text.Json;

namespace KDesktop.Core.Remote.Web;

/// <summary>
/// The Android app this K-Desktop carries (built with it, see KDesktop.Core.csproj): the session tells the app its
/// version, and an older app downloads it from api/android and installs it. Null when the build had none.
/// </summary>
public static class AndroidApp
{
    public sealed record Package(string Version, int Code, byte[] Bytes);

    private static readonly Lazy<Package?> Embedded = new(Load);

    public static Package? Current => Embedded.Value;

    private static Package? Load()
    {
        try
        {
            var assembly = typeof(AndroidApp).Assembly;
            using var info = assembly.GetManifestResourceStream("Android/KDesktop.apk.json");
            using var apk = assembly.GetManifestResourceStream("Android/KDesktop.apk");
            if (info is null || apk is null) return null;
            using var json = JsonDocument.Parse(info);
            var bytes = new byte[apk.Length];
            apk.ReadExactly(bytes);
            return new Package(json.RootElement.GetProperty("version").GetString() ?? "", json.RootElement.GetProperty("code").GetInt32(), bytes);
        }
        catch (Exception ex)
        {
            Log.Warn("Web: la app de Android incluida no se puede leer: " + ex.Message);
            return null;
        }
    }
}

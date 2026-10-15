using System.Reflection;

namespace KDesktop.Core;

/// <summary>
/// Files built into KDesktop.Core.dll (see the csproj): "Languages/&lt;code&gt;.json" and "Sounds/&lt;name&gt;.wav".
/// File names are matched ignoring case, like on disk.
/// </summary>
internal static class EmbeddedAssets
{
    private static readonly Assembly Assembly = typeof(EmbeddedAssets).Assembly;

    private static readonly Lazy<string[]> Names = new(Assembly.GetManifestResourceNames);

    /// <summary>File names (with extension) embedded in <paramref name="folder"/>.</summary>
    public static IEnumerable<string> List(string folder) =>
        Names.Value.Where(n => n.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)).Select(n => n[(folder.Length + 1)..]);

    /// <param name="pinned">Allocates the array on the pinned heap, so native code can keep its address.</param>
    public static byte[]? ReadBytes(string folder, string file, bool pinned = false)
    {
        if (Open(folder, file) is not { } stream) return null;
        using (stream)
        {
            var bytes = GC.AllocateUninitializedArray<byte>((int)stream.Length, pinned);
            stream.ReadExactly(bytes);
            return bytes;
        }
    }

    public static string? ReadText(string folder, string file)
    {
        if (Open(folder, file) is not { } stream) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Stream? Open(string folder, string file)
    {
        var name = Names.Value.FirstOrDefault(n => n.Equals($"{folder}/{file}", StringComparison.OrdinalIgnoreCase));
        return name is null ? null : Assembly.GetManifestResourceStream(name);
    }
}

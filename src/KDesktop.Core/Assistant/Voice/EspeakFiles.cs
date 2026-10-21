using System.IO.Compression;
using KDesktop.Core.Voice;

namespace KDesktop.Core.Assistant.Voice;

/// <summary>
/// espeak-ng, which turns text into phonemes for Kokoro (English, French, Italian, Portuguese) and for every Piper voice.
/// It is GPL and so is not part of K-Desktop: it is downloaded apart like a model (the Windows package of
/// espeakng-loader 0.2.4 on PyPI, pinned by its hash) and unpacked into <c>Models\espeak-ng\</c>; the assistant process
/// only loads it from there. Shared by both voices: it goes when neither of them is left on disk.
/// </summary>
public static class EspeakFiles
{
    private const string PackageUrl =
        "https://files.pythonhosted.org/packages/9d/ed/a3d872fbad4f3a3f3db0e8c31768ab14e77cd77306de16b8b20b1e1df7ea/espeakng_loader-0.2.4-py3-none-win_amd64.whl";

    private const long PackageSize = 9_437_292;

    private const string PackageSha256 = "41f1e08ac9deda2efd1ea9de0b81dab9f5ae3c4b24284f76533d0a7b1dd7abd7";

    /// <summary>Where espeak-ng is unpacked (its DLL and <c>espeak-ng-data</c>).</summary>
    public static string Folder => Path.Combine(AppPaths.ModelsDir, "espeak-ng");

    private static string Package => Path.Combine(Folder, "espeakng_loader.whl");

    /// <summary>What to download to have it (unpacked afterwards with <see cref="Unpack"/>).</summary>
    public static ModelFile Download => new(PackageUrl, Package, PackageSize, PackageSha256);

    /// <summary>espeak-ng is unpacked.</summary>
    public static bool Installed =>
        File.Exists(Path.Combine(Folder, "espeak-ng.dll")) && File.Exists(Path.Combine(Folder, "espeak-ng-data", "phontab"));

    /// <summary>
    /// After a download: unpacks the package (its DLL and its data, nothing else) and deletes it. False when there was
    /// nothing to unpack or it failed (the next download gets it again). Both voices' downloads call it: one at a time.
    /// </summary>
    public static bool Unpack()
    {
        lock (Gate)
        {
            if (!File.Exists(Package)) return false;
            const string prefix = "espeakng_loader/";
            try
            {
                using (var zip = ZipFile.OpenRead(Package))
                {
                    var root = Path.GetFullPath(Folder) + Path.DirectorySeparatorChar;
                    foreach (var entry in zip.Entries)
                    {
                        if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) || entry.Name.Length == 0) continue;
                        var relative = entry.FullName[prefix.Length..];
                        if (relative != "espeak-ng.dll" && !relative.StartsWith("espeak-ng-data/", StringComparison.Ordinal)) continue;
                        var target = Path.GetFullPath(Path.Combine(Folder, relative));
                        // Never outside its folder.
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        entry.ExtractToFile(target, overwrite: true);
                    }
                }
                File.Delete(Package);
                return Installed;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                Log.Warn($"No se ha podido descomprimir espeak-ng: {ex.Message}");
                return false;
            }
        }
    }

    private static readonly Lock Gate = new();

    /// <summary>Size of espeak-ng on disk.</summary>
    public static long Size() => Directory.Exists(Folder) ? new DirectoryInfo(Folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

    /// <summary>
    /// Removes espeak-ng if no voice that needs it is left on disk (Kokoro in a language other than Spanish, or any Piper
    /// voice). The assistant process must have let it go first.
    /// </summary>
    public static void DeleteIfUnused()
    {
        if (PiperModels.InstalledSize() > 0 || KokoroModels.NeedsEspeakOnDisk()) return;
        try
        {
            if (Directory.Exists(Folder)) Directory.Delete(Folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"No se ha podido borrar {Folder}: {ex.Message}");
        }
    }
}

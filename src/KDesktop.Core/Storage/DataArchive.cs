using System.IO.Compression;
using KDesktop.Core.Model;

namespace KDesktop.Core.Storage;

/// <summary>
/// Full backup of the data folder as a .zip: configuration, achievements and statistics, own icons, sounds and
/// languages. Leaves out what is rebuilt or downloaded again (logs, automatic backups, favicons, voice models) and the
/// paired web devices (their keys are secrets and belong to this PC).
/// </summary>
public static class DataArchive
{
    /// <summary>Folders of the data dir that go into the archive.</summary>
    private static readonly string[] Folders = ["Icons", "Sounds", "Languages"];

    private static readonly string[] Files = ["config.json", "stats.json"];

    /// <summary>Writes the archive. Save the configuration and the statistics first.</summary>
    public static void Export(string zipPath)
    {
        var tmp = zipPath + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var name in Files)
            {
                var path = Path.Combine(AppPaths.DataDir, name);
                if (File.Exists(path)) zip.CreateEntryFromFile(path, name, CompressionLevel.Optimal);
            }
            foreach (var folder in Folders)
            {
                var dir = Path.Combine(AppPaths.DataDir, folder);
                if (!Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    zip.CreateEntryFromFile(file, folder + "/" + Path.GetRelativePath(dir, file).Replace('\\', '/'), CompressionLevel.Optimal);
            }
        }
        File.Move(tmp, zipPath, overwrite: true);
    }

    /// <summary>What an archive holds, read and checked before anything is written.</summary>
    public sealed class Contents
    {
        public required AppConfig Config { get; init; }

        /// <summary>Contents of stats.json (achievements and statistics), null when the archive has none.</summary>
        public string? StatsJson { get; init; }

        public required string ZipPath { get; init; }
    }

    /// <summary>Opens and checks an archive (it must hold a valid config.json). Throws <see cref="InvalidDataException"/> otherwise.</summary>
    public static Contents Read(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry("config.json") ?? throw new InvalidDataException("config.json missing");
        var config = Json.Deserialize<AppConfig>(ReadText(entry)) ?? throw new InvalidDataException("config.json empty");
        return new Contents
        {
            Config = config,
            StatsJson = zip.GetEntry("stats.json") is { } stats ? ReadText(stats) : null,
            ZipPath = zipPath,
        };
    }

    /// <summary>
    /// Copies the folders of the archive into the data dir (files with the same name are replaced, the others are kept).
    /// The configuration and the statistics are <see cref="Contents.Config"/> and <see cref="Contents.StatsJson"/>:
    /// the caller swaps them in, so nothing still in memory overwrites them afterwards.
    /// </summary>
    public static void ExtractFiles(Contents contents)
    {
        using var zip = ZipFile.OpenRead(contents.ZipPath);
        foreach (var entry in zip.Entries)
        {
            if (Files.Contains(entry.FullName, StringComparer.OrdinalIgnoreCase) || Target(entry) is not { } target) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    /// <summary>Where an entry goes, or null for anything that is not ours (or tries to leave the data folder).</summary>
    private static string? Target(ZipArchiveEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Name)) return null; // folder entry
        var name = entry.FullName.Replace('\\', '/');
        if (!Folders.Any(f => name.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase))) return null;
        var root = Path.GetFullPath(AppPaths.DataDir) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(root, name));
        return target.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? target : null;
    }
}

using System.IO.Compression;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Model;

namespace KDesktop.Core.Storage;

/// <summary>
/// Full backup of the data folder as a .zip: configuration, achievements and statistics, own icons, sounds,
/// languages, wake words, and the assistants' data and settings with the list of those installed (not their packages:
/// those are downloaded again). Leaves out what is rebuilt or downloaded again (logs, automatic backups, favicons, voice models) and the
/// paired web devices (their keys are secrets and belong to this PC).
/// </summary>
public static class DataArchive
{
    /// <summary>Folders of the data dir that go into the archive.</summary>
    private static readonly string[] Folders = ["Icons", "Sounds", "Languages", "Voice", "Assistants/Data", "Assistants/Settings", "Assistant"];

    private static readonly string[] Files = ["config.json", "stats.json"];

    /// <summary>The assistants installed when the archive was made (only a list).</summary>
    private const string AssistantsList = "assistants.json";

    /// <summary>An assistant that was installed when the archive was made, and where to get it again.</summary>
    public sealed record ArchivedAssistant(string Id, string Name, string Version, string? Updates);

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
            var assistants = AssistantPackages.Installed()
                .Select(a => new ArchivedAssistant(a.Id, a.Manifest.Name, a.Version, a.Manifest.Updates)).ToList();
            if (assistants.Count > 0)
            {
                using var writer = new StreamWriter(zip.CreateEntry(AssistantsList, CompressionLevel.Optimal).Open());
                writer.Write(Json.Serialize(assistants));
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

        /// <summary>The assistants that were installed (empty in older archives).</summary>
        public IReadOnlyList<ArchivedAssistant> Assistants { get; init; } = [];

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
            Assistants = zip.GetEntry(AssistantsList) is { } list ? ReadAssistants(list) : [],
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

    /// <summary>Only informative: a broken list is never a reason to turn the archive down.</summary>
    private static List<ArchivedAssistant> ReadAssistants(ZipArchiveEntry entry)
    {
        try { return Json.Deserialize<List<ArchivedAssistant>>(ReadText(entry)) ?? []; }
        catch (System.Text.Json.JsonException) { return []; }
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

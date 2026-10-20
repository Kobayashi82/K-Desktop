using System.Globalization;
using KDesktop.Core.Model;

namespace KDesktop.Core.Storage;

/// <summary>
/// Automatic backups: the same full .zip as «Exportar copia» (configuration, achievements, icons, sounds, languages,
/// wake words), every few days, keeping the last ones. Checked at start and every hour.
/// </summary>
public static class AutoBackup
{
    private const string Prefix = "K-Desktop auto ";
    private const string Format = "yyyy-MM-dd HHmm";

    public static string FolderOf(AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.AutoBackupFolder) ? AppPaths.BackupDir : Environment.ExpandEnvironmentVariables(settings.AutoBackupFolder.Trim());

    /// <summary>The automatic backups in <paramref name="folder"/>, newest first, with their dates.</summary>
    public static IReadOnlyList<(string Path, DateTime Time)> List(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder, Prefix + "*.zip")
            .Select(p => (Path: p, Ok: DateTime.TryParseExact(Path.GetFileNameWithoutExtension(p)[Prefix.Length..], Format,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var t), Time: t))
            .Where(x => x.Ok).Select(x => (x.Path, x.Time)).OrderByDescending(x => x.Time).ToList();
    }

    /// <summary>Whether one is due at <paramref name="now"/>.</summary>
    public static bool IsDue(AppSettings settings, DateTime now)
    {
        if (!settings.AutoBackup) return false;
        var list = List(FolderOf(settings));
        var last = list.Count > 0 ? list[0] : default;
        // A little early is fine: the hourly check would otherwise drift a day by an hour.
        return last.Path is null || now - last.Time >= TimeSpan.FromDays(Math.Max(1, settings.AutoBackupDays)) - TimeSpan.FromHours(1);
    }

    /// <summary>
    /// Makes a backup now (<paramref name="save"/> writes what is pending first) and deletes the oldest beyond
    /// «keep». Returns its path.
    /// </summary>
    public static string Create(AppSettings settings, Action save, DateTime? now = null)
    {
        var folder = FolderOf(settings);
        Directory.CreateDirectory(folder);
        save();
        var path = Path.Combine(folder, Prefix + (now ?? DateTime.Now).ToString(Format, CultureInfo.InvariantCulture) + ".zip");
        DataArchive.Export(path);
        Prune(folder, Math.Max(1, settings.AutoBackupKeep));
        Log.Info($"Copia automática: {path}");
        return path;
    }

    public static void Prune(string folder, int keep)
    {
        foreach (var (old, _) in List(folder).Skip(keep))
        {
            try { File.Delete(old); }
            catch (Exception ex) { Log.Warn($"No se pudo borrar la copia antigua {old}: {ex.Message}"); }
        }
    }
}

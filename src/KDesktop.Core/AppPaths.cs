namespace KDesktop.Core;

/// <summary>Well-known locations used by K-Desktop.</summary>
public static class AppPaths
{
    public const string AppName = "K-Desktop";
    public const string VersionName = "K-Desktop 5.0";

    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "K-Desktop 5");

    public static string ConfigFile => Path.Combine(DataDir, "config.json");
    public static string BackupDir => Path.Combine(DataDir, "Backups");
    public static string IconsDir => Path.Combine(DataDir, "Icons");
    public static string LogDir => Path.Combine(DataDir, "Logs");
    public static string TempDir => Path.Combine(Path.GetTempPath(), "K-Desktop");

    /// <summary>Folder that holds the bundled .wav sounds (next to the executable).</summary>
    public static string SoundsDir => Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds");

    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "KDesktop.exe");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(BackupDir);
        Directory.CreateDirectory(IconsDir);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(TempDir);
    }

    /// <summary>Resolves an icon reference stored in the config (file name inside <see cref="IconsDir"/> or absolute path).</summary>
    public static string? ResolveIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        var path = Path.IsPathRooted(icon) ? icon : Path.Combine(IconsDir, icon);
        return File.Exists(path) ? path : null;
    }
}

namespace KDesktop.Core;

/// <summary>Well-known locations used by K-Desktop.</summary>
public static class AppPaths
{
    public const string AppName = "K-Desktop";
    public const string VersionName = "K-Desktop 5.0";

    private static readonly string DefaultDataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "K-Desktop 5");

    public static string DataDir { get; private set; } = DefaultDataDir;

    /// <summary>
    /// Empty for the normal data folder. With <c>--data</c> it identifies the alternative folder, so that instance
    /// gets its own single-instance mutex and never talks to (or blocks) the normal one.
    /// </summary>
    public static string InstanceSuffix { get; private set; } = string.Empty;

    /// <summary>Uses another data folder (<c>--data "C:\folder"</c>: tests, portable copies…). Call before anything else.</summary>
    public static void UseDataDir(string dir)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(dir.Trim().Trim('"')));
        if (full.Equals(DefaultDataDir, StringComparison.OrdinalIgnoreCase)) return;
        DataDir = full;
        InstanceSuffix = "." + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes(full.ToLowerInvariant())))[..8];
    }

    public static string ConfigFile => Path.Combine(DataDir, "config.json");
    public static string StatsFile => Path.Combine(DataDir, "stats.json");
    public static string BackupDir => Path.Combine(DataDir, "Backups");
    public static string IconsDir => Path.Combine(DataDir, "Icons");
    public static string LogDir => Path.Combine(DataDir, "Logs");
    public static string FaviconsDir => Path.Combine(DataDir, "Favicons");
    public static string TempDir => Path.Combine(Path.GetTempPath(), "K-Desktop");

    /// <summary>User sounds (.wav): added to the built-in ones, replacing those with the same name.</summary>
    public static string SoundsDir => Path.Combine(DataDir, "Sounds");

    /// <summary>User languages (&lt;code&gt;.json): new languages, or keys that replace the built-in texts.</summary>
    public static string LanguagesDir => Path.Combine(DataDir, "Languages");

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

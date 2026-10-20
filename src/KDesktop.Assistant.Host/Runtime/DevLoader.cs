using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Assistant.Packages;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// <c>--assistant-dev &lt;folder&gt;</c>: loads an assistant from its build without installing it, and loads it again
/// each time it is rebuilt. The folder is the package folder the build makes (assistant.json + lib/) or the project
/// folder (its newest <c>bin\…\package</c> is used). A copy is loaded, so the build can overwrite the DLLs.
/// </summary>
internal sealed class DevLoader(string folder, AssistantRuntime runtime, Action<Action> post) : IDisposable
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(800);

    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private string? _source;
    private int _copies;
    private bool _loading;
    /// <summary>The id it loaded last (the others on the desktop, if any, are not its own).</summary>
    private string? _loaded;

    /// <summary>Finds the package folder of a build: the folder itself, or the newest bin\…\package inside it.</summary>
    public static string? FindPackage(string folder)
    {
        if (File.Exists(Path.Combine(folder, AssistantManifest.FileName)) && Directory.Exists(Path.Combine(folder, "lib"))) return folder;
        var bin = Path.Combine(folder, "bin");
        if (!Directory.Exists(bin)) return null;
        return Directory.EnumerateFiles(bin, AssistantManifest.FileName, SearchOption.AllDirectories)
            .Where(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(f)), "package", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(Path.GetDirectoryName)
            .FirstOrDefault();
    }

    /// <summary>On the engine thread: the first load, then watching for rebuilds.</summary>
    public void Start()
    {
        _source = FindPackage(folder);
        if (_source is null)
        {
            Log.Error($"Asistente (desarrollo): no hay un paquete compilado en {folder} (falta assistant.json con lib/, o bin\\…\\package)");
            return;
        }
        Log.Info($"Asistente (desarrollo): paquete en {_source}");
        CleanOldCopies();
        _ = LoadAsync(EntranceKind.Startup);

        _debounce = new Timer(_ => post(() => _ = LoadAsync(EntranceKind.Reloaded)));
        _watcher = new FileSystemWatcher(_source) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        _watcher.Changed += (_, _) => Soon();
        _watcher.Created += (_, _) => Soon();
        _watcher.Renamed += (_, _) => Soon();
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }

    private void Soon() => _debounce?.Change(Quiet, Timeout.InfiniteTimeSpan);

    private async Task LoadAsync(EntranceKind entrance)
    {
        if (_loading || _source is null) return;
        _loading = true;
        try
        {
            if (_loaded is { } previous) await runtime.UnloadAsync(previous, ExitKind.Reloading).ConfigureAwait(true);
            var copy = Path.Combine(CopiesRoot, $"{Environment.ProcessId}-{++_copies}");
            CopyFolder(_source, copy);
            var id = _loaded = await runtime.LoadPackageAsync(copy, null, entrance).ConfigureAwait(true);
            Log.Info($"Asistente (desarrollo): «{id}» {(entrance == EntranceKind.Reloaded ? "recargado" : "cargado")}");
        }
        catch (Exception ex)
        {
            Log.Error("Asistente (desarrollo): no se ha podido cargar", ex);
        }
        finally { _loading = false; }
    }

    private static string CopiesRoot => Path.Combine(AppPaths.TempDir, "assistant-dev");

    private static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>Copies of earlier sessions (their process is gone): best effort.</summary>
    private static void CleanOldCopies()
    {
        if (!Directory.Exists(CopiesRoot)) return;
        foreach (var dir in Directory.EnumerateDirectories(CopiesRoot))
        {
            if (Path.GetFileName(dir).Split('-')[0] == Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)) continue;
            try { Directory.Delete(dir, recursive: true); } catch { /* still in use: next time */ }
        }
    }
}

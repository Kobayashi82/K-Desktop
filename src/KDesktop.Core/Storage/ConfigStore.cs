using System.Collections.Specialized;
using KDesktop.Core.Model;

namespace KDesktop.Core.Storage;

/// <summary>
/// Owns the in-memory <see cref="AppConfig"/>, watches it for changes and persists it to disk
/// (debounced, atomic write, rolling backups).
/// </summary>
public sealed class ConfigStore : IDisposable
{
    private const int MaxBackups = 15;
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(600);

    private readonly object _saveLock = new();
    private readonly Timer _saveTimer;
    private bool _dirty;
    private int _suspend;

    public AppConfig Config { get; private set; } = new();

    /// <summary>Raised (on any thread) after anything in the configuration changes.</summary>
    public event EventHandler? Changed;

    public ConfigStore() => _saveTimer = new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);

    /// <summary>Loads config.json. Returns false when the file did not exist.</summary>
    public bool Load()
    {
        AppPaths.EnsureCreated();
        var exists = File.Exists(AppPaths.ConfigFile);
        AppConfig config = new();
        if (exists)
        {
            try
            {
                config = Json.Deserialize<AppConfig>(File.ReadAllText(AppPaths.ConfigFile)) ?? new AppConfig();
            }
            catch (Exception ex)
            {
                Log.Error("No se pudo leer config.json; se conserva una copia y se empieza de cero", ex);
                TryCopy(AppPaths.ConfigFile, Path.Combine(AppPaths.BackupDir, $"config.corrupt.{DateTime.Now:yyyyMMdd-HHmmss}.json"));
                config = new AppConfig();
            }
        }
        Replace(config, save: false);
        return exists;
    }

    /// <summary>Swaps the whole configuration (after an import, for example).</summary>
    public void Replace(AppConfig config, bool save = true)
    {
        var upgraded = config.Upgrade();
        Config = config;
        Attach();
        RaiseChanged();
        if (save) SaveNow();
        else if (upgraded) MarkDirty();
    }

    /// <summary>Runs <paramref name="action"/> without triggering a save/change for every intermediate step.</summary>
    public void Batch(Action action)
    {
        Interlocked.Increment(ref _suspend);
        try { action(); }
        finally
        {
            if (Interlocked.Decrement(ref _suspend) == 0) MarkDirty();
        }
    }

    public void MarkDirty()
    {
        if (Volatile.Read(ref _suspend) > 0) return;
        _dirty = true;
        _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        RaiseChanged();
    }

    public void SaveNow()
    {
        lock (_saveLock)
        {
            try
            {
                AppPaths.EnsureCreated();
                var json = Json.Serialize(Config);
                var tmp = AppPaths.ConfigFile + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(AppPaths.ConfigFile))
                {
                    RotateBackup();
                    File.Replace(tmp, AppPaths.ConfigFile, null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tmp, AppPaths.ConfigFile);
                }
                _dirty = false;
            }
            catch (Exception ex)
            {
                Log.Error("Error guardando la configuración", ex);
            }
        }
    }

    /// <summary>Flushes any pending save (call on exit).</summary>
    public void Flush()
    {
        _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        if (_dirty) SaveNow();
    }

    /// <summary>Keeps one backup per day, plus the last <see cref="MaxBackups"/> days.</summary>
    private static void RotateBackup()
    {
        var today = Path.Combine(AppPaths.BackupDir, $"config.{DateTime.Now:yyyy-MM-dd}.json");
        if (File.Exists(today)) return;
        TryCopy(AppPaths.ConfigFile, today);
        foreach (var old in Directory.GetFiles(AppPaths.BackupDir, "config.????-??-??.json").OrderDescending().Skip(MaxBackups))
        {
            try { File.Delete(old); } catch { /* ignore */ }
        }
    }

    private static void TryCopy(string from, string to)
    {
        try { File.Copy(from, to, true); } catch { /* ignore */ }
    }

    private void RaiseChanged()
    {
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Log.Error("Error en un manejador de cambios de configuración", ex); }
    }

    // ── change tracking ──────────────────────────────────────────────────

    private void Attach()
    {
        var c = Config;
        c.Settings.Modified += (_, _) => MarkDirty();
        foreach (var cmd in c.Commands) cmd.Modified += OnItemModified;
        foreach (var m in c.QuickMenus) m.Modified += OnItemModified;
        foreach (var m in c.Macros) m.Modified += OnItemModified;
        foreach (var k in c.KBoards) k.Modified += OnItemModified;
        c.Commands.CollectionChanged += OnCollectionChanged;
        c.QuickMenus.CollectionChanged += OnCollectionChanged;
        c.Macros.CollectionChanged += OnCollectionChanged;
        c.KBoards.CollectionChanged += OnCollectionChanged;
    }

    private void OnItemModified(object? sender, EventArgs e) => MarkDirty();

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (var item in e.NewItems)
            {
                if (item is Command cmd) cmd.Modified += OnItemModified;
                if (item is QuickMenu m) m.Modified += OnItemModified;
                if (item is Macro macro) macro.Modified += OnItemModified;
                if (item is KBoardProfile kb) kb.Modified += OnItemModified;
            }
        if (e.OldItems is not null)
            foreach (var item in e.OldItems)
            {
                if (item is Command cmd) cmd.Modified -= OnItemModified;
                if (item is QuickMenu m) m.Modified -= OnItemModified;
                if (item is Macro macro) macro.Modified -= OnItemModified;
                if (item is KBoardProfile kb) kb.Modified -= OnItemModified;
            }
        MarkDirty();
    }

    public void Dispose()
    {
        Flush();
        _saveTimer.Dispose();
    }
}

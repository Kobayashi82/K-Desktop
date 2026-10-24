using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Assistant.Packages;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// The code of one assistant, in an <see cref="AssemblyLoadContext"/> of its own that can be unloaded (to switch
/// assistants or reload one in development). The SDK always comes from the host (never from the package), so the
/// IAssistant types are the same on both sides; the rest is resolved with the entry's .deps.json.
/// </summary>
internal sealed class AssistantLoadContext(string entry) : AssemblyLoadContext("assistant:" + Path.GetFileNameWithoutExtension(entry), isCollectible: true)
{
    private static readonly string SdkName = typeof(IAssistant).Assembly.GetName().Name!;

    private readonly AssemblyDependencyResolver _resolver = new(entry);

    protected override Assembly? Load(AssemblyName name)
    {
        if (string.Equals(name.Name, SdkName, StringComparison.OrdinalIgnoreCase)) return null; // the host's
        return _resolver.ResolveAssemblyToPath(name) is { } path ? LoadFromAssemblyPath(path) : null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName) =>
        _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path ? LoadUnmanagedDllFromPath(path) : 0;
}

/// <summary>
/// An assistant ready to attach: its instance, its manifest, its folders, its settings and its load context. The
/// context has to be held for as long as the assistant runs: a collectible one that nothing references is finalized,
/// and its finalizer starts unloading it — the code already loaded goes on, but every assembly it loads later fails
/// (5.0.60: the Lander's Vortice, after a GC, once it had been updated).
/// </summary>
internal sealed record LoadedPackage(
    IAssistant Assistant,
    AssistantManifest Manifest,
    string Folder,
    string DataFolder,
    JsonElement Settings,
    AssemblyLoadContext? Context);

/// <summary>Loads and unloads assistant packages (installed, or a build folder in development).</summary>
internal static class PackageLoader
{
    /// <summary>
    /// Loads the package in <paramref name="folder"/> (assistant.json and lib/). Throws with a message for K-Desktop's
    /// panel when it cannot (bad manifest, another major version of the SDK, the type is not an IAssistant…).
    /// </summary>
    public static LoadedPackage Load(string folder, string? dataFolder = null, bool defaultSettings = false)
    {
        var manifest = AssistantManifest.Load(folder);
        var host = typeof(IAssistant).Assembly.GetName().Version ?? new Version(1, 0);
        if (!manifest.RunsOn(host))
            throw new InvalidOperationException($"«{manifest.Id}» necesita el SDK {manifest.Sdk}; este K-Desktop tiene el {host.Major}.{host.Minor}");

        if (manifest.IsCharacter) return LoadCharacter(folder, dataFolder, manifest, defaultSettings);

        var (dll, typeName) = manifest.EntryParts;
        var entry = Path.GetFullPath(Path.Combine(folder, dll.Replace('/', Path.DirectorySeparatorChar)));
        if (!entry.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase) || !File.Exists(entry))
            throw new FileNotFoundException($"No está {dll} en el paquete");

        var context = new AssistantLoadContext(entry);
        try
        {
            var assembly = context.LoadFromAssemblyPath(entry);
            var type = assembly.GetType(typeName, throwOnError: false)
                       ?? throw new TypeLoadException($"No está el tipo {typeName} en {dll}");
            if (Activator.CreateInstance(type) is not IAssistant assistant)
                throw new InvalidCastException($"{typeName} no implementa IAssistant del SDK {host.Major}");

            var data = dataFolder ?? AssistantPaths.Data(manifest.Id);
            Directory.CreateDirectory(data);
            var settings = JsonSerializer.SerializeToElement(AssistantSettingsValues.Read(folder, manifest, defaultSettings));
            Log.Info($"Asistente: paquete «{manifest.Id}» {manifest.Version} cargado desde {folder}");
            return new LoadedPackage(assistant, manifest, folder, data, settings, context);
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    /// <summary>An assistant without code (phase 12): its character.json, moved by the host; nothing of the package is loaded as code.</summary>
    private static LoadedPackage LoadCharacter(string folder, string? dataFolder, AssistantManifest manifest, bool defaultSettings)
    {
        var file = Path.GetFullPath(Path.Combine(folder, manifest.Entry));
        if (!file.StartsWith(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
            throw new FileNotFoundException($"No está {manifest.Entry} en el paquete");
        var character = CharacterFile.Parse(File.ReadAllText(file));
        var data = dataFolder ?? AssistantPaths.Data(manifest.Id);
        Directory.CreateDirectory(data);
        var settings = JsonSerializer.SerializeToElement(AssistantSettingsValues.Read(folder, manifest, defaultSettings));
        Log.Info($"Asistente: personaje «{manifest.Id}» {manifest.Version} (sin código) cargado desde {folder}");
        return new LoadedPackage(new Characters.CharacterAssistant(character), manifest, folder, data, settings, null);
    }

    /// <summary>
    /// Unloads a package's context and checks that it went. Call it once nothing holds the assistant any more (the
    /// instance disposed and every reference to it dropped): that is why it takes only the weak reference.
    /// </summary>
    public static void Unload(WeakReference reference, string id)
    {
        StartUnloading(reference);
        for (var i = 0; i < 8 && reference.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        // WPF keeps some types in static tables: an assistant that registers its own may stay loaded. Not fatal;
        // switching restarts the process if it piles up (pruebas-de-concepto.md, c).
        if (reference.IsAlive) Log.Warn($"Asistente: el código de «{id}» no se ha podido descargar del todo");
    }

    /// <summary>Apart, so no local of the caller keeps the context alive while it is collected.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void StartUnloading(WeakReference reference) => (reference.Target as AssistantLoadContext)?.Unload();
}

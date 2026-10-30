using System.Runtime.CompilerServices;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 1.6: Dot's package loads in its own context, with the host's SDK, and unloads.</summary>
public class PackageLoaderTests
{
    private static string DotPackage =>
        DevLoader.FindPackage(Path.Combine(Contracts.Root, "tests", "DotAssistant"))
        ?? throw new InvalidOperationException("Compila tests/DotAssistant primero");

    [Fact]
    public void DotLoadsWithTheHostsSdkAndUnloads()
    {
        var (weak, sdkIsShared) = LoadAndDrop(DotPackage);
        Assert.True(sdkIsShared);
        PackageLoader.Unload(weak, "dot");
        Assert.False(weak.IsAlive, "El contexto de Dot sigue cargado");
    }

    /// <summary>
    /// While the package is held its context can still load assemblies, however many collections pass: a collectible
    /// context that only a weak reference pointed to was finalized, and its finalizer started unloading it (the Lander
    /// lost its Vortice after being updated).
    /// </summary>
    [Fact]
    public void AHeldPackageKeepsLoadingAfterCollections()
    {
        var package = Load(DotPackage);
        for (var i = 0; i < 4; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        var entry = package.Assistant.GetType().Assembly;
        Assert.Same(entry, package.Context!.LoadFromAssemblyPath(entry.Location));
        PackageLoader.Unload(Drop(ref package), "dot");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Drop(ref LoadedPackage package)
    {
        var weak = new WeakReference(package.Context);
        package = null!;
        return weak;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference, bool) LoadAndDrop(string folder)
    {
        var package = Load(folder);
        Assert.Equal("dot", package.Manifest.Id);
        Assert.Equal(28, package.Settings.GetProperty("size").GetInt32());
        var shared = package.Assistant.GetType().GetInterfaces().Single(i => i.Name == nameof(IAssistant)) == typeof(IAssistant);
        return (new WeakReference(package.Context), shared);
    }

    private static LoadedPackage Load(string folder) =>
        PackageLoader.Load(folder, Path.Combine(Path.GetTempPath(), "kdesktop-tests", "dot-" + Guid.NewGuid().ToString("N")[..6]));
}

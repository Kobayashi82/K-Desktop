using System.Runtime.CompilerServices;
using KDesktop.Assistant.Host.Runtime;
using KDesktop.Assistant.Sdk;

namespace KDesktop.Assistant.Tests;

/// <summary>Stage 1.6: Dot's package loads in its own context, with the host's SDK, and unloads.</summary>
public class PackageLoaderTests
{
    private static string DotPackage =>
        DevLoader.FindPackage(Path.Combine(Contracts.DocsDir, "..", "..", "samples", "DotAssistant"))
        ?? throw new InvalidOperationException("Compila samples/DotAssistant primero");

    [Fact]
    public void DotLoadsWithTheHostsSdkAndUnloads()
    {
        var (weak, sdkIsShared) = LoadAndDrop(DotPackage);
        Assert.True(sdkIsShared);
        PackageLoader.Unload(weak, "dot");
        Assert.False(weak.IsAlive, "El contexto de Dot sigue cargado");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference, bool) LoadAndDrop(string folder)
    {
        var package = PackageLoader.Load(folder, Path.Combine(Path.GetTempPath(), "kdesktop-tests", "dot-" + Guid.NewGuid().ToString("N")[..6]));
        Assert.Equal("dot", package.Manifest.Id);
        Assert.Equal(28, package.Settings.GetProperty("size").GetInt32());
        var shared = package.Assistant.GetType().GetInterfaces().Single(i => i.Name == nameof(IAssistant)) == typeof(IAssistant);
        return (package.Context, shared);
    }
}

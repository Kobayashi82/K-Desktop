using System.Runtime.CompilerServices;
using KDesktop.Core;

namespace KDesktop.Assistant.Tests;

internal static class TestSetup
{
    /// <summary>Never touch the real data folder: the log and anything saved go to a temporary one.</summary>
    [ModuleInitializer]
    internal static void Init() =>
        AppPaths.UseDataDir(Path.Combine(Path.GetTempPath(), "kdesktop-tests", Guid.NewGuid().ToString("N")[..8]));
}

using System.Runtime.CompilerServices;

namespace KDesktop.Core.Tests;

internal static class TestSetup
{
    /// <summary>Never touch the real data folder: the log and anything saved go to a temporary one.</summary>
    [ModuleInitializer]
    internal static void Init()
    {
        // Never switch off, restart, sleep or lock the PC the tests run on (nor the KDesktop.exe they start).
        Environment.SetEnvironmentVariable("KDESKTOP_NO_POWER", "1");
        Platform.PowerTools.Disabled = true;
        AppPaths.UseDataDir(Path.Combine(Path.GetTempPath(), "kdesktop-tests", Guid.NewGuid().ToString("N")[..8]));
        Localization.Loc.Instance.Load("es");
    }
}

using System.Runtime.CompilerServices;
using KDesktop.Core;

namespace KDesktop.Assistant.Tests;

internal static class TestSetup
{
    /// <summary>
    /// Never touch the real data folder: the log and anything saved go to a temporary one. Texts in Spanish, like the
    /// Core tests (the phrases and rules of the conversation are checked in it).
    /// </summary>
    [ModuleInitializer]
    internal static void Init()
    {
        // Never switch off, restart, sleep or lock the PC the tests run on: the KDesktop.exe they start inherit it.
        Environment.SetEnvironmentVariable("KDESKTOP_NO_POWER", "1");
        Core.Platform.PowerTools.Disabled = true;
        AppPaths.UseDataDir(Path.Combine(Path.GetTempPath(), "kdesktop-tests", Guid.NewGuid().ToString("N")[..8]));
        Core.Localization.Loc.Instance.Load("es");
    }
}

using KDesktop.Core.Native;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Tests;

/// <summary>The P/Invoke wrappers that fill a character buffer (title, class, process path, device id).</summary>
public class Win32Tests
{
    [Fact]
    public void ReadsTheShellWindow()
    {
        var shell = Win32.GetShellWindow();
        Assert.NotEqual(IntPtr.Zero, shell);
        Assert.Equal("Progman", Win32.GetClassName(shell));
        Assert.Equal("Program Manager", Win32.GetWindowTitle(shell));
    }

    [Fact]
    public void ReadsTheProcessPath() =>
        Assert.Equal(Environment.ProcessPath, Win32.GetProcessPath((uint)Environment.ProcessId), StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void DeviceIdsAreWhole() =>
        Assert.All(PnpDevices.Present(pairedToo: true), d => Assert.Contains('\\', d.Id));
}

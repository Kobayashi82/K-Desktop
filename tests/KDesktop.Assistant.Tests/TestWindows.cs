using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace KDesktop.Assistant.Tests;

/// <summary>
/// Plain windows of another process for the tests that act on windows (K-Desktop never touches its own process's): a
/// PowerShell form that does not take the focus, optionally run as administrator.
/// </summary>
internal static partial class TestWindows
{
    /// <summary>
    /// Runs <paramref name="steps"/> with the window's handle, then closes it. One run as administrator cannot be closed
    /// from here: it closes itself after <paramref name="lifetimeSeconds"/>. When it cannot be elevated, null reaches
    /// <paramref name="steps"/>'s caller as 0.
    /// </summary>
    public static async Task RunAsync(string title, Func<long, Task> steps, bool elevated = false, int lifetimeSeconds = 60)
    {
        var script = $$"""
            Add-Type -AssemblyName System.Windows.Forms
            Add-Type -ReferencedAssemblies System.Windows.Forms -TypeDefinition 'public class QuietForm : System.Windows.Forms.Form { protected override bool ShowWithoutActivation { get { return true; } } }'
            $f = New-Object QuietForm
            $f.Text = '{{title}}'
            $f.StartPosition = 'Manual'
            $f.SetBounds(300, 300, 420, 300)
            $f.ShowInTaskbar = $false
            $t = New-Object System.Windows.Forms.Timer
            $t.Interval = {{lifetimeSeconds * 1000}}
            $t.Add_Tick({ $f.Close() })
            $t.Start()
            [System.Windows.Forms.Application]::Run($f)
            """;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var start = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded)
        {
            UseShellExecute = elevated, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
        };
        if (elevated) start.Verb = "runas";
        Process? process;
        try { process = Process.Start(start); }
        catch (System.ComponentModel.Win32Exception)
        {
            // Elevation refused: nothing to test here.
            await steps(0);
            return;
        }
        try
        {
            var handle = IntPtr.Zero;
            for (var i = 0; i < 150 && handle == IntPtr.Zero; i++)
            {
                await Task.Delay(100);
                handle = FindWindow(null, title);
            }
            Assert.NotEqual(IntPtr.Zero, handle);
            await Task.Delay(200);
            await steps(handle.ToInt64());
        }
        finally
        {
            try { process?.Kill(); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            process?.Dispose();
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr FindWindow(string? className, string? windowName);
}

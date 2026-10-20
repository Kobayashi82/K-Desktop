using System.Diagnostics;
using System.Runtime.InteropServices;
using KDesktop.Core;
using Microsoft.Win32;

namespace KDesktop.Assistant.Host.World;

/// <summary>
/// Keeps the assistant's windows on the active virtual desktop (from Kobii). They are tool windows and Windows 11
/// shows them on every desktop without assigning them to any (checked, also when moving to an empty one); this is
/// the safety net in case some version left them on the previous one: it moves them to the new one
/// (IVirtualDesktopManager, the documented API of Windows 10/11; it only moves our own windows). To know the active one
/// it uses the window in front, if it is on it; if not (an empty desktop, where the front is the shell's, which is on
/// all of them), the one the Explorer points to in the registry.
/// </summary>
internal sealed class VirtualDesktopKeeper : IDisposable
{
    private static readonly Guid ManagerClsid = new("AA509086-5CA9-4C25-8F95-589D3C07B48A");

    private readonly IVirtualDesktopManager? _manager;

    public VirtualDesktopKeeper()
    {
        try
        {
            if (Type.GetTypeFromCLSID(ManagerClsid) is { } type)
            {
                _manager = Activator.CreateInstance(type) as IVirtualDesktopManager;
            }
        }
        catch (COMException ex)
        {
            Log.Warn($"Asistente: escritorios virtuales no disponibles ({ex.Message})");
        }
    }

    /// <summary>
    /// If any of <paramref name="windows"/> is not on the active desktop, takes them there (to the one of
    /// <paramref name="reference"/>, normally the window in front, or the one the Explorer says). True if it moved any.
    /// </summary>
    public bool Follow(IReadOnlyCollection<nint> windows, nint reference)
    {
        if (_manager is null)
        {
            return false;
        }

        try
        {
            var away = windows.Where(IsElsewhere).ToList();
            if (away.Count == 0)
            {
                return false;
            }

            foreach (var desktop in Candidates(reference))
            {
                var target = desktop;
                foreach (var hwnd in away)
                {
                    _manager.MoveWindowToDesktop(hwnd, ref target);
                }

                if (!IsElsewhere(away[0]))
                {
                    return true;
                }
            }

            Log.Warn("Asistente: no se han podido llevar sus ventanas al escritorio virtual activo");
            return false;
        }
        catch (COMException)
        {
            // Windows that no longer exist or desktops just closed: it will try again.
            return false;
        }
    }

    public void Dispose()
    {
        if (_manager is not null)
        {
            Marshal.ReleaseComObject(_manager);
        }
    }

    /// <summary>Is the window on another virtual desktop? (false if unknown).</summary>
    private bool IsElsewhere(nint hwnd) => _manager!.IsWindowOnCurrentVirtualDesktop(hwnd, out var onCurrent) == 0 && onCurrent == 0;

    /// <summary>The desktops that may be the active one, most reliable first.</summary>
    private IEnumerable<Guid> Candidates(nint reference)
    {
        // The window in front, if it is on the active desktop (windows pinned to all give another id: so, if moving
        // there does not work, the next one is tried).
        if (reference != 0 && _manager!.IsWindowOnCurrentVirtualDesktop(reference, out var onCurrent) == 0 && onCurrent != 0
            && _manager.GetWindowDesktopId(reference, out var desktop) == 0 && desktop != Guid.Empty)
        {
            yield return desktop;
        }

        if (RegistryDesktop() is { } current)
        {
            yield return current;
        }
    }

    /// <summary>The active desktop according to the Explorer (Windows 11 and, on Windows 10, the session's).</summary>
    private static Guid? RegistryDesktop()
    {
        const string explorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
        string[] paths = [$@"{explorer}\VirtualDesktops", $@"{explorer}\SessionInfo\{Process.GetCurrentProcess().SessionId}\VirtualDesktops"];
        foreach (var path in paths)
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            if (key?.GetValue("CurrentVirtualDesktop") is byte[] { Length: 16 } bytes && new Guid(bytes) is var desktop && desktop != Guid.Empty)
            {
                return desktop;
            }
        }

        return null;
    }

    [ComImport]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, out int onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(nint topLevelWindow, ref Guid desktopId);
    }
}

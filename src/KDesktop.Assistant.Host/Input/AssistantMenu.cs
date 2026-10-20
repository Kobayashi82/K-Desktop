using System.Windows;
using System.Windows.Interop;
using KDesktop.Core;
using KDesktop.Core.Localization;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.Input;

/// <summary>What the right-click menu offers, gathered on the engine thread.</summary>
/// <param name="Id">The assistant clicked (there may be several on the desktop).</param>
/// <param name="Name">Its name.</param>
/// <param name="Extras">Its extras that are not hidden, in K-Desktop's language.</param>
/// <param name="At">Where it was clicked, in screen pixels.</param>
internal sealed record MenuRequest(string Id, string Name, IReadOnlyList<MenuExtra> Extras, Point At);

/// <summary>An extra in the menu: greyed out when the assistant says it cannot be done now.</summary>
internal sealed record MenuExtra(string Id, string Name, bool Enabled);

internal enum MenuChoice
{
    None,
    Panel,
    Settings,
    Extra,
    Hide,
}

/// <summary>
/// The menu of the right button on the assistant when K-Desktop cannot draw it (not connected, or in development):
/// Windows' own popup menu, so the keyboard, clicking outside and the monitor's scale work as anywhere else, in
/// K-Desktop's theme. It runs on the UI thread: the assistant keeps moving on the engine thread while it is open.
/// </summary>
internal static class AssistantMenu
{
    private const uint PanelId = 1;
    private const uint SettingsId = 2;
    private const uint HideId = 3;
    private const uint FirstExtraId = 100;

    /// <summary>The hidden window that owns the menu (the menu needs one in the foreground to close when clicking outside).</summary>
    private static HwndSource? _owner;

    /// <summary>Shows the menu and returns what was chosen. UI thread.</summary>
    /// <param name="linked">Connected with K-Desktop: without it, what K-Desktop does is greyed out.</param>
    public static (MenuChoice Choice, string? Extra) Show(MenuRequest request, bool dark, bool linked)
    {
        _owner ??= new HwndSource(new HwndSourceParameters("KDesktopAssistantMenu")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)WS_POPUP),
            ExtendedWindowStyle = (int)WS_EX_TOOLWINDOW,
        });
        UseTheme(dark);

        var menu = CreatePopupMenu();
        try
        {
            var name = Escape(request.Name);
            var needsLink = linked ? MF_STRING : MF_STRING | MF_GRAYED;
            AppendMenu(menu, needsLink, PanelId, Loc.T("assistantMenu.panel"));
            AppendMenu(menu, needsLink, SettingsId, Loc.T("assistantMenu.settings", name));
            if (request.Extras.Count > 0)
            {
                AppendMenu(menu, MF_SEPARATOR, 0, null);
                for (var i = 0; i < request.Extras.Count; i++)
                    AppendMenu(menu, request.Extras[i].Enabled ? MF_STRING : MF_STRING | MF_GRAYED, FirstExtraId + (uint)i, Escape(request.Extras[i].Name));
            }
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, needsLink, HideId, Loc.T("assistantMenu.hide", name));

            // The window the user was in gets the focus back afterwards, unless K-Desktop comes forward.
            var previous = GetForegroundWindow();
            var owner = _owner.Handle;
            SetForegroundWindow(owner);
            var chosen = (uint)TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY,
                (int)Math.Round(request.At.X), (int)Math.Round(request.At.Y), owner, 0);
            PostMessage(owner, WM_NULL, 0, 0); // so a second right-click opens it again at once
            if (chosen is not (PanelId or SettingsId) && previous != 0 && IsWindow(previous)) SetForegroundWindow(previous);

            return chosen switch
            {
                PanelId => (MenuChoice.Panel, null),
                SettingsId => (MenuChoice.Settings, null),
                HideId => (MenuChoice.Hide, null),
                >= FirstExtraId when chosen - FirstExtraId < (uint)request.Extras.Count => (MenuChoice.Extra, request.Extras[(int)(chosen - FirstExtraId)].Id),
                _ => (MenuChoice.None, null),
            };
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    /// <summary>«&amp;» marks the access key in a menu: a name that has one shows it as it is.</summary>
    private static string Escape(string text) => text.Replace("&", "&&", StringComparison.Ordinal);

    private static void UseTheme(bool dark)
    {
        try
        {
            SetPreferredAppMode(dark ? AppModeForceDark : AppModeForceLight);
            FlushMenuThemes();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            Log.Warn($"Asistente: el menú no puede seguir el tema ({ex.Message})");
        }
    }
}

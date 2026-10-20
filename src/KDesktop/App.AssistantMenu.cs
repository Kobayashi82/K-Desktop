using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using KDesktop.Assistant.Sdk;
using KDesktop.Core;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Native;
using KDesktop.Services;
using Wpf.Ui.Controls;
using Image = System.Windows.Controls.Image;
using MenuItem = System.Windows.Controls.MenuItem;

namespace KDesktop;

/// <summary>
/// The right-click menu of an assistant (stage 2.3), drawn by K-Desktop like the rest of its menus: its extras with their
/// icons and hotkeys (those it hides are left out, those it greys out are disabled), its panel, its settings and hiding
/// it. Also what each assistant says of its extras (<c>extraState</c>), which its panel buttons follow, and the hotkeys
/// of the commands that run them (<c>extraKeys</c>). Everything here runs on the UI thread.
/// </summary>
public partial class App
{
    /// <summary>The extras each assistant greyed out or hid since it was last loaded (shown ones are not kept).</summary>
    private readonly Dictionary<string, Dictionary<string, ExtraState>> _extraStates = new(StringComparer.Ordinal);

    /// <summary>The <c>extraKeys</c> last sent to each loaded assistant, to send only changes.</summary>
    private readonly Dictionary<string, string> _sentExtraKeys = new(StringComparer.Ordinal);

    private ContextMenu? _assistantMenu;

    /// <summary>How one extra of an assistant shows right now (its menu and its panel buttons).</summary>
    public ExtraState ExtraStateOf(string assistant, string extra) =>
        _extraStates.TryGetValue(assistant, out var states) ? states.GetValueOrDefault(extra, ExtraState.Shown) : ExtraState.Shown;

    private void OnAssistantExtraState(ExtraStateMessage message)
    {
        var state = message.State switch
        {
            "disabled" => ExtraState.Disabled,
            "hidden" => ExtraState.Hidden,
            _ => ExtraState.Shown,
        };
        if (!_extraStates.TryGetValue(message.Assistant, out var states)) _extraStates[message.Assistant] = states = new(StringComparer.Ordinal);
        if (state == ExtraState.Shown) states.Remove(message.Extra);
        else states[message.Extra] = state;
        AssistantPanelChanged?.Invoke(message.Assistant);
    }

    /// <summary>An assistant is being loaded: all its extras are shown again and it gets the hotkeys afresh.</summary>
    private void ForgetAssistantExtras(string id)
    {
        if (_extraStates.Remove(id)) AssistantPanelChanged?.Invoke(id);
        _sentExtraKeys.Remove(id);
    }

    /// <summary>The process stopped or started again: no assistant has said anything of its extras.</summary>
    private void ClearAssistantExtras()
    {
        var ids = _extraStates.Keys.ToList();
        _extraStates.Clear();
        _sentExtraKeys.Clear();
        foreach (var id in ids) AssistantPanelChanged?.Invoke(id);
    }

    /// <summary>
    /// Sends each loaded assistant the hotkeys of the commands that run its extras, when they changed (after a load and
    /// whenever the commands change).
    /// </summary>
    private void SendAssistantExtraKeys()
    {
        if (_assistant is not { IsRunning: true } supervisor) return;
        foreach (var id in Config.Settings.AssistantsOnDesktop)
        {
            if (!supervisor.IsLoaded(id) || AssistantPackages.Find(id) is not { } installed) continue;
            var keys = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var extra in installed.Manifest.Extras)
            {
                var hotkey = AssistantExtras.CommandsFor(Config, id, extra.Id)
                    .Where(c => c.Enabled)
                    .Select(c => c.FirstHotkey)
                    .FirstOrDefault(h => h.Length > 0);
                if (hotkey is not null) keys[extra.Id] = hotkey;
            }
            var signature = string.Join("|", keys.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + k.Value));
            if (_sentExtraKeys.TryGetValue(id, out var sent) && sent == signature) continue;
            _sentExtraKeys[id] = signature;
            supervisor.Send(new ExtraKeysMessage(id, keys));
        }
    }

    /// <summary>
    /// <c>menu</c>: the user right-clicked an assistant. It opens at the mouse (where the click was) and takes the
    /// foreground, so clicking elsewhere or Escape closes it; the window the user was in gets it back afterwards, unless
    /// a page of K-Desktop comes forward.
    /// </summary>
    private void OnAssistantMenu(MenuMessage message)
    {
        var id = message.Assistant;
        if (!IsOnDesktop(id) || AssistantPackages.Find(id) is not { } installed) return;
        _assistantMenu?.IsOpen = false;

        var name = installed.Manifest.Name;
        var language = Loc.Instance.Language;
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        var bringsPage = false;
        var extras = installed.Manifest.Extras.Where(e => ExtraStateOf(id, e.Id) != ExtraState.Hidden).ToList();
        foreach (var extra in extras)
        {
            var extraId = extra.Id;
            var item = new MenuItem
            {
                Header = extra.Name.In(language),
                Icon = IconProvider.ForExtra(id, extraId) is { } image ? new Image { Source = image, Width = 16, Height = 16 } : null,
                InputGestureText = AssistantExtras.CommandsFor(Config, id, extraId).Where(c => c.Enabled).Select(c => c.FirstHotkey).FirstOrDefault(h => h.Length > 0) ?? string.Empty,
                IsEnabled = ExtraStateOf(id, extraId) != ExtraState.Disabled,
            };
            item.Click += (_, _) => RunAssistantExtra(id, extraId);
            menu.Items.Add(item);
        }
        if (extras.Count > 0) menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Loc.T("assistantMenu.panel"), SymbolRegular.Bot24, () =>
        {
            bringsPage = true;
            ShowMainWindow(w => w.Navigate("assistant/" + id));
        }));
        menu.Items.Add(Entry(Loc.T("assistantMenu.settings", name), SymbolRegular.Settings24, () =>
        {
            bringsPage = true;
            ShowMainWindow(w => w.Navigate("settings/assistants/" + id));
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Loc.T("assistantMenu.hide", name), SymbolRegular.EyeOff24, () =>
        {
            HideAssistant(id);
            Ui.ShowOsd(Loc.T("assistant.hidden", name), Core.Engine.OsdKind.Info, TimeSpan.FromSeconds(5));
        }));

        var previous = UiNative.GetForegroundWindow();
        menu.Closed += (_, _) =>
        {
            if (_assistantMenu == menu) _assistantMenu = null;
            // After the click has been handled: a page that comes forward keeps the foreground, and so does a window of
            // another program the user clicked to close the menu.
            Dispatcher.BeginInvoke(() =>
            {
                if (bringsPage || previous == IntPtr.Zero || !UiNative.IsWindow(previous)) return;
                UiNative.GetWindowThreadProcessId(UiNative.GetForegroundWindow(), out var pid);
                if (pid == 0 || pid == Environment.ProcessId) UiNative.SetForegroundWindow(previous);
            }, System.Windows.Threading.DispatcherPriority.Background);
        };
        _assistantMenu = menu;
        menu.IsOpen = true;

        // Without the foreground the popup does not see the clicks outside it (it would stay open): the assistant's
        // process allowed it (AllowSetForegroundWindow), and the menu is ours to take it.
        if (PresentationSource.FromVisual(menu) is HwndSource source && !UiNative.SetForegroundWindow(source.Handle))
            Core.Platform.WindowTools.ForceForeground(source.Handle);
        Log.Info($"Asistente: menú de «{id}» ({extras.Count} extras)");

        static MenuItem Entry(string header, SymbolRegular symbol, Action click)
        {
            var item = new MenuItem { Header = header, Icon = new SymbolIcon { Symbol = symbol } };
            item.Click += (_, _) => click();
            return item;
        }
    }
}

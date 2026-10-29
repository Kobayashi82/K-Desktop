using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Assistant;

/// <summary>
/// «Asistente: chat» (D3, 5.0.371): the conversation of Panel → Asistente in a window of its own, apart from the main one.
/// The icon on top changes who it talks to (the ones not on the desktop dimmed: choosing one brings it out); the pin keeps
/// it on top. Esc closes it. Where it was, the pin and the assistant are kept in the settings (closed, nothing stays).
/// </summary>
public partial class AssistantChatWindow : FluentWindow
{
    private readonly App _app = App.Current;

    /// <summary>Who it talks to now.</summary>
    public string Assistant { get; private set; } = AssistantTargeting.KDesktop;

    public AssistantChatWindow(string assistant)
    {
        InitializeComponent();
        Chat.Detach();
        var settings = _app.Config.Settings;
        WindowBounds.Apply(this, settings.AssistantChatWindowBounds);
        Pin(settings.AssistantChatWindowPinned);
        Talk(assistant);
        Loaded += (_, _) =>
        {
            _app.AssistantChanged += Refresh;
            Chat.FocusInput();
        };
        Closing += (_, _) => settings.AssistantChatWindowBounds = WindowBounds.Save(this);
        Closed += (_, _) => _app.AssistantChanged -= Refresh;
    }

    /// <summary>Talks to <paramref name="id"/> (remembered for the next time).</summary>
    public void Talk(string id)
    {
        Assistant = id;
        _app.Config.Settings.AssistantChatWindowAssistant = id;
        Refresh();
    }

    /// <summary>The name, the icon and whether it can be written to (on the desktop) as they are now.</summary>
    private void Refresh()
    {
        var name = App.AssistantName(Assistant);
        Title = Loc.T("assistantChatWindow.title", name);
        Bar.Title = Title;
        WhoName.Text = name;
        WhoIcon.Source = AssistantLook.IconOf(Assistant);
        Chat.Show(Assistant, name, _app.IsOnDesktop(Assistant));
    }

    /// <summary>The assistants to talk to: K-Desktop and the installed ones; the ones not on the desktop, dimmed.</summary>
    private void OnWho(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = WhoButton, Placement = PlacementMode.Bottom };
        foreach (var choice in Lookups.AssistantList)
        {
            var id = choice.Id;
            var there = _app.IsOnDesktop(id);
            var header = new StackPanel();
            header.Children.Add(new TextBlock { Text = choice.Name, FontWeight = id == Assistant ? FontWeights.SemiBold : FontWeights.Normal });
            if (!there) header.Children.Add(new TextBlock { Text = Loc.T("assistantChatWindow.bringOut"), Style = (Style)FindResource("Caption") });
            var item = new MenuItem
            {
                Header = header,
                Icon = new System.Windows.Controls.Image { Source = choice.Icon, Width = 18, Height = 18 },
                Opacity = there ? 1 : 0.55,
            };
            System.Windows.Automation.AutomationProperties.SetName(item, choice.Name);
            item.Click += (_, _) =>
            {
                if (!_app.IsOnDesktop(id)) _app.ShowAssistant(id);
                Talk(id);
                Chat.FocusInput();
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void OnPin(object sender, RoutedEventArgs e) => Pin(!Topmost);

    private void Pin(bool on)
    {
        Topmost = on;
        _app.Config.Settings.AssistantChatWindowPinned = on;
        PinIcon.Symbol = on ? SymbolRegular.PinOff24 : SymbolRegular.Pin24;
        var tip = Loc.T(on ? "assistantChatWindow.unpin" : "assistantChatWindow.pin");
        PinButton.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(PinButton, tip);
    }

    private void OnClear(object sender, RoutedEventArgs e) => _app.ClearAssistantChat(Assistant);

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}

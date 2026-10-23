using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using KDesktop.Core.Localization;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Assistant;

/// <summary>
/// Panel → Assistant → the conversation (stage 3.6): every line with the assistant shown (K-Desktop too, 5.0.112; one
/// that is not on the desktop only shows what was said), whatever way it was said (voice, this box, the box over the character, a command), the answer growing while it
/// is written, «…» while it thinks, and a box to write to it with a stop button while it works. The rows are made in
/// code (an ItemsControl hides its content from UI Automation).
/// </summary>
public partial class AssistantChatView : UserControl
{
    private readonly App _app = App.Current;
    private string _id = Core.Assistant.AssistantTargeting.KDesktop;
    private string? _name;
    private bool _active;
    private bool _thinking;

    public AssistantChatView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _app.AssistantChatChanged += OnChatChanged;
            _app.AssistantChanged += OnStateChanged;
            Fill();
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantChatChanged -= OnChatChanged;
            _app.AssistantChanged -= OnStateChanged;
        };
    }

    /// <summary>The conversation with this assistant; it is written to only while it is <paramref name="active"/> (on the desktop).</summary>
    public void Show(string id, string name, bool active)
    {
        if (_id == id && _name == name && _active == active) return;
        _id = id;
        _name = name;
        _active = active;
        Input.PlaceholderText = Loc.T("assistantWrite.placeholder", name);
        AutomationProperties.SetName(Input, Input.PlaceholderText);
        HintText.Text = !active ? Loc.T(Core.Assistant.AssistantTargeting.IsSystem(id) ? "assistantChat.hintSystemInactive" : "assistantChat.hintInactive", name)
            : Core.Assistant.AssistantTargeting.IsSystem(id) ? Loc.T("assistantChat.hintSystem")
            : Loc.T("assistantChat.hint", name);
        Fill();
    }

    private void OnChatChanged(string key)
    {
        if (key == _id) Fill();
    }

    private void OnStateChanged()
    {
        if (_app.IsAssistantThinking(_id) != _thinking) Fill();
    }

    private void Fill()
    {
        var chat = _app.AssistantChat(_id);
        _thinking = _app.IsAssistantThinking(_id);
        var atEnd = Scroller.VerticalOffset >= Scroller.ScrollableHeight - 4;
        Rows.Children.Clear();
        foreach (var item in chat) Rows.Children.Add(Row(item.IsUser, item.Text, item.Final, item.Written is { Length: > 0 } ? item : null));
        // Thinking, and nothing of the answer yet.
        if (_thinking && chat.Count > 0 && chat[^1].IsUser) Rows.Children.Add(Row(false, "…", false));
        if (chat.Count == 0)
            Rows.Children.Add(new TextBlock { Style = (Style)FindResource("Caption"), Margin = new Thickness(0, 2, 0, 2), Text = Loc.T("assistantChat.empty") });
        ClearButton.Visibility = chat.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var running = _app.Assistant is { IsRunning: true };
        StopButton.Visibility = _thinking ? Visibility.Visible : Visibility.Collapsed;
        SendButton.Visibility = _thinking ? Visibility.Collapsed : Visibility.Visible;
        Input.IsEnabled = running && _active;
        if (atEnd) Dispatcher.BeginInvoke(Scroller.ScrollToEnd, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// One line: the user's on the right in the accent, the assistant's on the left. A long answer (stage 14.2): its summary,
    /// then the whole text, and «Leer entero» for its reading sheet.
    /// </summary>
    private Border Row(bool user, string text, bool final, ChatItem? written = null)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorPrimaryBrush"),
            Opacity = final ? 1 : 0.75,
        };
        UIElement content = block;
        if (written is { Written: { } markdown })
        {
            var whole = new StackPanel();
            block.Margin = new Thickness(0, 0, 0, 8);
            whole.Children.Add(block);
            var body = MarkdownView.Panel(markdown);
            body.Opacity = final ? 1 : 0.75;
            whole.Children.Add(new Border
            {
                BorderBrush = (System.Windows.Media.Brush)FindResource("Kd.MenuSeparator"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 8, 0, 0),
                // The start of it: the whole text is on its reading sheet («Leer entero»).
                MaxHeight = 280,
                ClipToBounds = true,
                Child = body,
            });
            if (final)
            {
                var open = new Wpf.Ui.Controls.Button
                {
                    Content = Loc.T("assistantChat.openLong"),
                    Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.DocumentText24 },
                    Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
                    Padding = new Thickness(10, 4, 10, 4),
                    Margin = new Thickness(0, 6, 0, 0),
                };
                var turn = written.Turn;
                open.Click += (_, _) => _app.ShowReadingSheet(_id, turn, markdown);
                whole.Children.Add(open);
            }
            content = whole;
        }
        var row = new Border
        {
            Child = content,
            CornerRadius = user ? new CornerRadius(12, 12, 3, 12) : new CornerRadius(12, 12, 12, 3),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = user ? new Thickness(60, 3, 0, 3) : new Thickness(0, 3, 60, 3),
            HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Background = (System.Windows.Media.Brush)FindResource(user ? "Kd.AccentSoft" : "Kd.ChipBackground"),
        };
        AutomationProperties.SetName(row, (user ? Loc.T("assistantChat.you") : _name) + ": " + text);
        return row;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e) => SendButton.IsEnabled = Input.Text.Trim().Length > 0;

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Send();
    }

    private void OnSend(object sender, RoutedEventArgs e) => Send();

    private void Send()
    {
        var text = Input.Text.Trim();
        if (text.Length == 0 || !_app.SendToAssistant(_id, text, "panel")) return;
        Input.Text = string.Empty;
        Dispatcher.BeginInvoke(Scroller.ScrollToEnd, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnStop(object sender, RoutedEventArgs e) => _app.StopAssistant(_id);

    private void OnClear(object sender, RoutedEventArgs e) => _app.ClearAssistantChat(_id);
}

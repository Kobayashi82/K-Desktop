using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using KDesktop.Core.Localization;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Assistant;

/// <summary>
/// Panel → Assistant → the conversation (stage 3.6): every line with the assistant shown (null: the module without a
/// body), whatever way it was said (voice, this box, the box over the character, a command), the answer growing while it
/// is written, «…» while it thinks, and a box to write to it with a stop button while it works. The rows are made in
/// code (an ItemsControl hides its content from UI Automation).
/// </summary>
public partial class AssistantChatView : UserControl
{
    private readonly App _app = App.Current;
    private string? _id;
    private string? _name;
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

    /// <summary>The conversation with this assistant (null: the module without a body).</summary>
    public void Show(string? id, string name)
    {
        if (_id == id && _name == name) return;
        _id = id;
        _name = name;
        Input.PlaceholderText = Loc.T("assistantWrite.placeholder", name);
        AutomationProperties.SetName(Input, Input.PlaceholderText);
        HintText.Text = id is null ? Loc.T("assistantChat.hintNobody") : Loc.T("assistantChat.hint", name);
        Fill();
    }

    private void OnChatChanged(string key)
    {
        if (key == (_id ?? string.Empty)) Fill();
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
        foreach (var item in chat) Rows.Children.Add(Row(item.IsUser, item.Text, item.Final));
        // Thinking, and nothing of the answer yet.
        if (_thinking && chat.Count > 0 && chat[^1].IsUser) Rows.Children.Add(Row(false, "…", false));
        if (chat.Count == 0)
            Rows.Children.Add(new TextBlock { Style = (Style)FindResource("Caption"), Margin = new Thickness(0, 2, 0, 2), Text = Loc.T("assistantChat.empty") });
        ClearButton.Visibility = chat.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var running = _app.Assistant is { IsRunning: true };
        StopButton.Visibility = _thinking ? Visibility.Visible : Visibility.Collapsed;
        SendButton.Visibility = _thinking ? Visibility.Collapsed : Visibility.Visible;
        Input.IsEnabled = running;
        if (atEnd) Dispatcher.BeginInvoke(Scroller.ScrollToEnd, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>One line: the user's on the right in the accent, the assistant's on the left.</summary>
    private Border Row(bool user, string text, bool final)
    {
        var block = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorPrimaryBrush"),
            Opacity = final ? 1 : 0.75,
        };
        var row = new Border
        {
            Child = block,
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

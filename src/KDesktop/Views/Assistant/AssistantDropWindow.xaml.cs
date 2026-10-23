using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Native;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Assistant;

/// <summary>
/// «¿Qué hago con «factura.pdf»?» (stage 14.1): files were dropped on an assistant and it caught them. A button for each
/// likely thing by the kind of file (summarize or read a document, describe a picture, see what a folder has, open it, its
/// folder, look for similar ones, send it), the box to say it otherwise and, while it is open, the microphone a few seconds.
/// What is chosen goes to the assistant as an order with the files, which stay as its context a few minutes («mándalo»,
/// «bórralo» later work too). Above the assistant, like the box to write to it.
/// </summary>
public partial class AssistantDropWindow : Window
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".docm", ".pptx", ".txt", ".md", ".markdown", ".rtf", ".odt", ".html", ".htm", ".csv", ".json", ".xml", ".log", ".ini",
        ".yaml", ".yml", ".cs", ".js", ".ts", ".py", ".ps1", ".bat", ".cmd", ".java", ".cpp", ".c", ".h", ".css", ".sql", ".srt",
    };

    private static readonly HashSet<string> Pictures = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic", ".heif" };

    private readonly Action<string, string?> _send;
    private readonly IReadOnlyList<string> _files;
    private readonly UiNative.POINT _at;
    private bool _closing;

    /// <summary>Closed (chosen, written, said or left): the microphone stops.</summary>
    public CancellationTokenSource Over { get; } = new();

    /// <param name="send">The order and, without the AI, what to do (<see cref="HeardMessage.ActOpen"/>…).</param>
    /// <param name="canSend">The assistant has some tool that sends things (Telegram, mail…).</param>
    internal AssistantDropWindow(string assistant, ImageSource? icon, IReadOnlyList<string> files, UiNative.POINT at, bool canSend, Action<string, string?> send)
    {
        InitializeComponent();
        _send = send;
        _files = files;
        _at = at;
        AssistantIcon.Source = icon;
        Fallback.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;
        var one = files.Count == 1;
        Question.Text = one ? Loc.T("assistantDrop.titleOne", NameOf(files[0])) : Loc.T("assistantDrop.titleMany", files.Count);
        var placeholder = Loc.T(one ? "assistantDrop.placeholderOne" : "assistantDrop.placeholderMany", assistant);
        Input.PlaceholderText = placeholder;
        AutomationProperties.SetName(Input, placeholder);
        AddActions(canSend);
        Loaded += (_, _) => Appear();
        Deactivated += (_, _) => Leave();
        Closed += (_, _) => Over.Cancel();
    }

    private static string NameOf(string path) => Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path;

    /// <summary>The files by name, for the order: «factura.pdf», or «a.pdf», «b.pdf».</summary>
    private string Names => string.Join(", ", _files.Select(f => "«" + NameOf(f) + "»"));

    private void AddActions(bool canSend)
    {
        var folders = _files.All(Directory.Exists);
        var documents = _files.All(f => Documents.Contains(Path.GetExtension(f)));
        var pictures = _files.All(f => Pictures.Contains(Path.GetExtension(f)));
        var one = _files.Count == 1;
        if (folders) Add("inside", "FolderOpen24");
        else if (documents) { Add("summarize", "TextBulletListSquare24"); if (one) Add("readAloud", "Speaker224"); }
        else if (pictures) { Add("describe", "ImageSearch24"); Add("readText", "ScanText24"); }
        if (one) Add("open", "Open24", HeardMessage.ActOpen);
        if (!folders) Add("folder", "FolderArrowRight24", HeardMessage.ActFolder);
        if (one && !folders) Add("similar", "DocumentSearch24");
        if (canSend) Add("send", "Send24");
    }

    private void Add(string what, string symbol, string? act = null)
    {
        var first = Actions.Children.Count == 0;
        var button = new Wpf.Ui.Controls.Button
        {
            Content = Loc.T("assistantDrop." + what),
            Icon = new SymbolIcon { Symbol = Enum.TryParse<SymbolRegular>(symbol, out var s) ? s : SymbolRegular.Document24 },
            Appearance = first ? ControlAppearance.Primary : ControlAppearance.Secondary,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(10, 5, 10, 5),
        };
        button.Click += (_, _) => Choose(Loc.T("assistantDrop.order." + what, Names), act);
        Actions.Children.Add(button);
    }

    /// <summary>The microphone is listening for what to do (the voice models are there).</summary>
    public void ShowListening() => ListeningRow.Visibility = Visibility.Visible;

    /// <summary>What was said aloud (or written): the order, with the files.</summary>
    public void Choose(string order, string? act = null)
    {
        if (_closing || order.Trim().Length == 0) return;
        _send(order.Trim(), act);
        Leave();
    }

    private void Appear()
    {
        var hwnd = UiNative.Handle(this);
        var work = UiNative.WorkAreaAt(_at);
        if (UiNative.GetWindowRect(hwnd, out var r))
        {
            var scale = UiNative.GetDpiForWindow(hwnd) / 96.0;
            var margin = (int)(Root.Margin.Top * scale);
            var x = _at.X - r.Width / 2;
            var y = _at.Y - r.Height + margin - (int)(40 * scale);
            if (y < work.Top) y = _at.Y - margin + (int)(24 * scale);
            x = Math.Clamp(x, work.Left - margin, Math.Max(work.Left - margin, work.Right - r.Width + margin));
            y = Math.Clamp(y, work.Top - margin, Math.Max(work.Top - margin, work.Bottom - r.Height + margin));
            UiNative.MoveWindow(hwnd, x, y);
        }
        if (!UiNative.SetForegroundWindow(hwnd)) Core.Platform.WindowTools.ForceForeground(hwnd);
        Activate();
        Input.Focus();
        Keyboard.Focus(Input);
        var d = TimeSpan.FromMilliseconds(200);
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, d));
        RootShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, d) { EasingFunction = EaseOut });
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e) => SendButton.IsEnabled = Input.Text.Trim().Length > 0;

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                Choose(Input.Text);
                break;
            case Key.Escape:
                e.Handled = true;
                Leave();
                break;
        }
    }

    private void OnSend(object sender, RoutedEventArgs e) => Choose(Input.Text);

    /// <summary>Closes it, with a short fade.</summary>
    public void Leave()
    {
        if (_closing) return;
        _closing = true;
        Over.Cancel();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) => Close();
        Root.BeginAnimation(OpacityProperty, fade);
    }
}

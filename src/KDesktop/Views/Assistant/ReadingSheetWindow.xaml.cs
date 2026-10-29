using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KDesktop.Core.Localization;
using KDesktop.Native;

namespace KDesktop.Views.Assistant;

/// <summary>
/// The reading sheet (stage 14.2): the whole text of an assistant's long answer, in Markdown, next to the assistant (to its
/// side that has room) or, without one to be next to, at the bottom right of the monitor with the mouse. It grows while the
/// answer is written, keeping where the reader is. «Copiar» puts the text on the clipboard, «Guardar como nota» keeps it in
/// the notes, «Léemela» has the assistant read it aloud (and becomes «Parar»). It never takes the focus by itself.
/// </summary>
public partial class ReadingSheetWindow : Window
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly Action<string> _saveNote;
    private readonly Action<string> _readAloud;
    private readonly Action _stopReading;
    private readonly Action _followChat;
    private readonly Func<bool> _speaking;
    private readonly DispatcherTimer _reading = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private UiNative.POINT? _at;
    private string _markdown = string.Empty;
    private bool _closing;
    private bool _shown;
    private bool _placed;
    private DateTime _readSince;

    /// <summary>The answer it shows (its turn).</summary>
    public string Turn { get; private set; } = string.Empty;

    /// <param name="speaking">Whether the assistant is saying something now (the reading aloud is over when it is not).</param>
    /// <param name="followChat">«Seguir en el chat» (D3): the chat window with this assistant.</param>
    internal ReadingSheetWindow(string assistant, ImageSource? icon, Action<string> saveNote, Action<string> readAloud, Action stopReading, Func<bool> speaking,
        Action followChat)
    {
        InitializeComponent();
        _saveNote = saveNote;
        _readAloud = readAloud;
        _stopReading = stopReading;
        _followChat = followChat;
        _speaking = speaking;
        AssistantIcon.Source = icon;
        Fallback.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;
        SheetTitle.Text = Loc.T("readingSheet.title", assistant);
        _reading.Tick += (_, _) =>
        {
            // A moment to start, then over when it says nothing.
            if (DateTime.Now - _readSince < TimeSpan.FromSeconds(2) || _speaking()) return;
            Reading(false);
        };
        Closed += (_, _) => _reading.Stop();
        // It grows while the answer is written: kept in its place, never out of the screen.
        SizeChanged += (_, _) =>
        {
            if (_placed) Place();
        };
    }

    /// <summary>
    /// The text of an answer (<paramref name="turn"/>), more of it while it is written (<paramref name="final"/> false), next to
    /// <paramref name="at"/> (screen pixels; null: the corner of the monitor with the mouse). Another answer starts at its top.
    /// </summary>
    internal void Show(string turn, string markdown, bool final, UiNative.POINT? at)
    {
        var fresh = turn != Turn;
        Turn = turn;
        if (at is not null) _at = at;
        Writing.Visibility = final ? Visibility.Collapsed : Visibility.Visible;
        NoteButton.IsEnabled = CopyButton.IsEnabled = ReadButton.IsEnabled = final;
        if (fresh) Done.Text = string.Empty;
        if (markdown != _markdown || fresh)
        {
            _markdown = markdown;
            var offset = fresh ? 0 : ScrollOffset();
            Viewer.Document = MarkdownView.Document(markdown);
            System.Windows.Automation.AutomationProperties.SetName(Viewer, MarkdownView.Plain(markdown));
            if (offset > 0) Dispatcher.BeginInvoke(() => Scroller()?.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
        }
        if (!_shown)
        {
            _shown = true;
            Loaded += (_, _) => Appear();
            Show();
        }
        else if (fresh) Dispatcher.BeginInvoke(Place, DispatcherPriority.Loaded);
    }

    private double ScrollOffset() => Scroller()?.VerticalOffset ?? 0;

    private System.Windows.Controls.ScrollViewer? Scroller() => FindChild<System.Windows.Controls.ScrollViewer>(Viewer);

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (FindChild<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    private void Appear()
    {
        Place();
        var d = TimeSpan.FromMilliseconds(220);
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, d));
        RootShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(10, 0, d) { EasingFunction = EaseOut });
    }

    /// <summary>
    /// Beside the assistant, on the side of its monitor with more room, its bottom a little below the assistant's head (it
    /// grows upwards); without one, at the bottom right of the monitor with the mouse. Never out of the work area.
    /// </summary>
    private void Place()
    {
        _placed = true;
        var hwnd = UiNative.Handle(this);
        var point = _at ?? UiNative.CursorPosition();
        var work = UiNative.WorkAreaAt(point);
        var scale = UiNative.GetDpiForWindow(hwnd) / 96.0;
        // As tall as most of the work area allows; the text scrolls inside.
        var most = Math.Max(160, work.Height / scale * 0.62);
        if (Math.Abs(Viewer.MaxHeight - most) > 0.5)
        {
            Viewer.MaxHeight = most;
            UpdateLayout();
        }
        // Its size as laid out now (the window itself may not have been resized yet).
        var r = new UiNative.RECT { Right = (int)Math.Round(ActualWidth * scale), Bottom = (int)Math.Round(ActualHeight * scale) };
        if (r.Width <= 0) return;
        var margin = (int)(Root.Margin.Top * scale);
        int x, y;
        if (_at is { } at)
        {
            var gap = (int)(56 * scale);
            x = at.X - work.Left > work.Right - at.X ? at.X - gap - r.Width + margin : at.X + gap - margin;
            y = at.Y + (int)(40 * scale) - r.Height + margin;
        }
        else
        {
            x = work.Right - r.Width + margin - (int)(12 * scale);
            y = work.Bottom - r.Height + margin - (int)(12 * scale);
        }
        x = Math.Clamp(x, work.Left - margin, Math.Max(work.Left - margin, work.Right - r.Width + margin));
        y = Math.Clamp(y, work.Top - margin, Math.Max(work.Top - margin, work.Bottom - r.Height + margin));
        UiNative.MoveWindow(hwnd, x, y);
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            var data = new DataObject();
            data.SetText(MarkdownView.Plain(_markdown), TextDataFormat.UnicodeText);
            Clipboard.SetDataObject(data, true);
            Done.Text = Loc.T("readingSheet.copied");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // The clipboard is busy with another program: nothing said.
        }
    }

    private void OnChat(object sender, RoutedEventArgs e) => _followChat();

    private void OnNote(object sender, RoutedEventArgs e)
    {
        _saveNote(_markdown);
        NoteButton.IsEnabled = false;
        Done.Text = Loc.T("readingSheet.saved");
    }

    private void OnRead(object sender, RoutedEventArgs e)
    {
        if (_reading.IsEnabled)
        {
            _stopReading();
            Reading(false);
            return;
        }
        _readAloud(_markdown);
        Reading(true);
    }

    private void Reading(bool on)
    {
        if (on)
        {
            _readSince = DateTime.Now;
            _reading.Start();
        }
        else _reading.Stop();
        ReadButton.Content = Loc.T(on ? "readingSheet.stop" : "readingSheet.readAloud");
        ReadButton.Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = on ? Wpf.Ui.Controls.SymbolRegular.Stop24 : Wpf.Ui.Controls.SymbolRegular.Speaker224 };
    }

    private void OnClose(object sender, RoutedEventArgs e) => Leave();

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Leave();
    }

    /// <summary>Closes it, with a short fade (reading it aloud stops).</summary>
    public void Leave()
    {
        if (_closing) return;
        _closing = true;
        if (_reading.IsEnabled) _stopReading();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) => Close();
        Root.BeginAnimation(OpacityProperty, fade);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Native;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Osd;

/// <summary>
/// Floating, click-through notification pill. One instance is reused for every message: an icon in the colour of its
/// kind, the text, a bar for levels (volume, brightness), a line that empties while it lasts, and up to two earlier
/// messages still on screen stacked beside it.
/// </summary>
public partial class OsdWindow : Window
{
    private static readonly Duration ShowDuration = new(TimeSpan.FromMilliseconds(240));
    private static readonly Duration HideDuration = new(TimeSpan.FromMilliseconds(180));
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction EaseIn = new CubicEase { EasingMode = EasingMode.EaseIn };
    private const int MaxHistory = 2;

    private readonly DispatcherTimer _hideTimer = new();
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private DateTime? _countdownTo;
    private bool _visible;

    /// <summary>The message on show while it can be stacked when another comes (a timed one; not voice or a countdown).</summary>
    private Shown? _current;

    private sealed record Shown(string Text, OsdKind Kind, string? Icon, bool IsLevel, DateTime Until);

    public OsdPosition Position { get; set; } = OsdPosition.BottomCenter;
    public ScreenChoice Screen { get; set; } = ScreenChoice.Mouse;

    public OsdSize Size
    {
        set
        {
            var scale = value switch { OsdSize.Small => 0.85, OsdSize.Large => 1.22, _ => 1.0 };
            SizeScale.ScaleX = SizeScale.ScaleY = scale;
        }
    }

    /// <summary>Incremented every time a new message replaces the current one.</summary>
    public int Generation { get; private set; }

    public OsdWindow()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) => HideMessage();
        _countdownTimer.Tick += (_, _) => UpdateCountdown();
        SourceInitialized += (_, _) =>
        {
            var hwnd = UiNative.Handle(this);
            UiNative.AddExStyle(hwnd, toolWindow: true, noActivate: true, clickThrough: true);
        };
        SizeChanged += (_, _) => Reposition();
    }

    public int ShowMessage(string text, OsdKind kind, TimeSpan? duration, DateTime? countdownTo = null, OsdLook? look = null)
    {
        Generation++;
        var level = look?.Level;

        // The one on show goes up the stack if it still had time left, unless this is the same message again or one
        // level following another (holding the volume key).
        if (_visible && _current is { } previous && previous.Until > DateTime.Now
            && previous.Text != text && !(previous.IsLevel && level is not null))
            PushHistory(previous);
        _current = duration is { } d && countdownTo is null ? new Shown(text, kind, look?.Icon, level is not null, DateTime.Now + d) : null;

        Meter.Visibility = Visibility.Collapsed;
        Badge.Visibility = Visibility.Visible;
        var brush = KindBrush(kind);
        BadgeIcon.Symbol = SymbolOf(look?.Icon, kind);
        BadgeIcon.Foreground = brush;
        Badge.Background = Tint(brush, 0.16);

        MessageText.Text = text;
        MessageText.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;

        LevelTrack.Visibility = level is null ? Visibility.Collapsed : Visibility.Visible;
        if (level is { } l)
        {
            LevelFill.Background = brush;
            var width = LevelTrack.Width * Math.Clamp(l, 0, 1);
            if (_visible && LevelFill.ActualWidth > 0)
                LevelFill.BeginAnimation(WidthProperty, new DoubleAnimation(width, new Duration(TimeSpan.FromMilliseconds(140))) { EasingFunction = EaseOut });
            else
            {
                LevelFill.BeginAnimation(WidthProperty, null);
                LevelFill.Width = width;
            }
        }

        _countdownTo = countdownTo;
        CountdownText.Visibility = countdownTo is null ? Visibility.Collapsed : Visibility.Visible;
        if (countdownTo is not null)
        {
            UpdateCountdown();
            _countdownTimer.Start();
        }
        else _countdownTimer.Stop();

        _hideTimer.Stop();
        TimeLine.Visibility = duration is null ? Visibility.Collapsed : Visibility.Visible;
        if (duration is { } shown)
        {
            _hideTimer.Interval = shown;
            _hideTimer.Start();
            TimeScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, new Duration(shown)));
        }

        if (!IsVisible) Show();
        Reposition();
        if (!_visible)
        {
            _visible = true;
            Animate(1, 0, 1, ShowDuration, EaseOut);
        }
        else if (level is null)
        {
            // Already visible: small "pulse" so the change is noticed (not for a level that is only moving).
            var pulse = new DoubleAnimation(1.04, 1, new Duration(TimeSpan.FromMilliseconds(200))) { EasingFunction = EaseOut };
            Scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            Scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }
        return Generation;
    }

    /// <summary>Voice: a message that stays, with the microphone meter instead of the icon.</summary>
    public int ShowVoice(string text)
    {
        var generation = ShowMessage(text, OsdKind.Info, null);
        Badge.Visibility = Visibility.Collapsed;
        Meter.Visibility = Visibility.Visible;
        _level = 0;
        return generation;
    }

    private static readonly double[] BarShape = [0.55, 0.85, 1, 0.8, 0.6];
    private double _level;

    /// <summary>Moves the meter: rises at once, falls slowly; each bar a little different so it looks alive.</summary>
    public void SetLevel(float level)
    {
        if (Meter.Visibility != Visibility.Visible) return;
        // Speech peaks are small numbers: a square root spreads them over the meter.
        var target = Math.Min(1, Math.Sqrt(level) * 1.4);
        _level = target > _level ? target : _level * 0.8 + target * 0.2;
        var i = 0;
        foreach (var bar in Meter.Children.OfType<System.Windows.Shapes.Rectangle>())
        {
            var jitter = 0.85 + Random.Shared.NextDouble() * 0.3;
            ((ScaleTransform)bar.RenderTransform).ScaleY = Math.Clamp(0.18 + _level * BarShape[i++] * jitter, 0.18, 1);
        }
    }

    public void HideMessage(int? generation = null)
    {
        if (generation is { } g && g != Generation) return;
        _hideTimer.Stop();
        _countdownTimer.Stop();
        _current = null;
        if (!_visible) return;
        _visible = false;
        Animate(0, 10, 0.97, HideDuration, EaseIn);
        // The stacked ones leave with it.
        foreach (var old in History.Children.OfType<FrameworkElement>().ToList()) RemoveHistory(old, fast: true);
    }

    // ── earlier messages ─────────────────────────────────────────────────

    /// <summary>A smaller copy of <paramref name="shown"/> beside the pill, until its own time runs out.</summary>
    private void PushHistory(Shown shown)
    {
        var brush = KindBrush(shown.Kind);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new SymbolIcon { Symbol = SymbolOf(shown.Icon, shown.Kind), FontSize = 13, Foreground = brush, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock
        {
            Text = shown.Text, FontSize = 13, FontWeight = FontWeights.SemiBold, MaxWidth = 560,
            FontFamily = MessageText.FontFamily, Foreground = (Brush)FindResource("Kd.OsdTextSecondary"),
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        });
        var pill = new Border
        {
            Child = row, CornerRadius = new CornerRadius(11), Padding = new Thickness(12, 6, 14, 6), Margin = new Thickness(0, 3, 0, 3),
            Background = (Brush)FindResource("Kd.OsdBackground"), BorderBrush = (Brush)FindResource("Kd.OsdBorder"), BorderThickness = new Thickness(1),
            HorizontalAlignment = Pill.HorizontalAlignment, Opacity = 0,
        };
        // Newest next to the pill: last in the panel when it is above it, first when below.
        if (Grid.GetRow(History) == 0) History.Children.Add(pill);
        else History.Children.Insert(0, pill);
        while (History.Children.Count > MaxHistory)
            RemoveHistory((FrameworkElement)History.Children[Grid.GetRow(History) == 0 ? 0 : History.Children.Count - 1], fast: true, now: true);
        pill.BeginAnimation(OpacityProperty, new DoubleAnimation(0.85, ShowDuration) { EasingFunction = EaseOut });

        var left = shown.Until - DateTime.Now;
        var timer = new DispatcherTimer { Interval = left < TimeSpan.FromSeconds(1.2) ? TimeSpan.FromSeconds(1.2) : left };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RemoveHistory(pill, fast: false);
        };
        timer.Start();
    }

    private void RemoveHistory(FrameworkElement pill, bool fast, bool now = false)
    {
        if (now)
        {
            History.Children.Remove(pill);
            return;
        }
        var fade = new DoubleAnimation(0, fast ? HideDuration : new Duration(TimeSpan.FromMilliseconds(260))) { EasingFunction = EaseIn };
        fade.Completed += (_, _) => History.Children.Remove(pill);
        pill.BeginAnimation(OpacityProperty, fade);
    }

    // ── looks ────────────────────────────────────────────────────────────

    private Brush KindBrush(OsdKind kind) => (Brush)FindResource(kind switch
    {
        OsdKind.Success => "Kd.Success",
        OsdKind.Warning => "Kd.Warning",
        _ => "Kd.AccentText",
    });

    private static Brush Tint(Brush brush, double opacity)
    {
        var tint = brush.CloneCurrentValue();
        tint.Opacity = opacity;
        tint.Freeze();
        return tint;
    }

    /// <summary>The icon asked for, or the one of the kind (also when the name is not a known symbol).</summary>
    private static SymbolRegular SymbolOf(string? icon, OsdKind kind) =>
        icon is not null && Enum.TryParse<SymbolRegular>(icon, out var symbol) ? symbol : kind switch
        {
            OsdKind.Success => SymbolRegular.CheckmarkCircle24,
            OsdKind.Warning => SymbolRegular.Warning24,
            _ => SymbolRegular.Info24,
        };

    private void UpdateCountdown()
    {
        if (_countdownTo is not { } to) return;
        var left = to - DateTime.Now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        CountdownText.Text = left.TotalSeconds >= 60 ? $"{(int)left.TotalMinutes}:{left.Seconds:00}" : $"{Math.Ceiling(left.TotalSeconds):0}";
    }

    private void Animate(double opacity, double y, double scale, Duration duration, IEasingFunction ease)
    {
        // Top positions come down from the edge; the others come up.
        var fromEdge = Position is OsdPosition.TopCenter or OsdPosition.TopLeft or OsdPosition.TopRight ? -y : y;
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, duration) { EasingFunction = ease });
        Shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromEdge, duration) { EasingFunction = ease });
        Scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
        Scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
    }

    /// <summary>Places the window on the chosen monitor, and the pill inside it at the side of the position.</summary>
    private void Reposition()
    {
        var left = Position is OsdPosition.TopLeft or OsdPosition.BottomLeft;
        var right = Position is OsdPosition.TopRight or OsdPosition.BottomRight;
        var top = Position is OsdPosition.TopCenter or OsdPosition.TopLeft or OsdPosition.TopRight;
        Root.HorizontalAlignment = left ? HorizontalAlignment.Left : right ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        Root.VerticalAlignment = top ? VerticalAlignment.Top : Position == OsdPosition.Center ? VerticalAlignment.Center : VerticalAlignment.Bottom;
        Pill.HorizontalAlignment = Root.HorizontalAlignment;
        // Earlier messages away from the edge: below the pill at the top, above it everywhere else.
        Grid.SetRow(History, top ? 1 : 0);
        Grid.SetRow(Pill, top ? 0 : 1);

        if (!IsLoaded && !IsVisible) return;
        var hwnd = UiNative.Handle(this);
        var work = UiNative.MonitorFor(Screen).Work;
        if (!UiNative.GetWindowRect(hwnd, out var r)) return;
        int w = r.Width, h = r.Height;
        var dpi = UiNative.GetDpiForWindow(hwnd) / 96.0;
        var gap = (int)(40 * dpi);
        var x = left ? work.Left + gap / 2 : right ? work.Right - w - gap / 2 : work.Left + (work.Width - w) / 2;
        var y = top ? work.Top + gap : Position == OsdPosition.Center ? work.Top + (work.Height - h) / 2 : work.Bottom - h - gap;
        UiNative.MoveWindow(hwnd, x, y);
    }
}

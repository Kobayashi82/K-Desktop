using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Native;

namespace KDesktop.Osd;

/// <summary>Floating, click-through notification pill. One instance is reused for every message.</summary>
public partial class OsdWindow : Window
{
    private static readonly Duration ShowDuration = new(TimeSpan.FromMilliseconds(240));
    private static readonly Duration HideDuration = new(TimeSpan.FromMilliseconds(180));
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction EaseIn = new CubicEase { EasingMode = EasingMode.EaseIn };

    private readonly DispatcherTimer _hideTimer = new();
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private DateTime? _countdownTo;
    private bool _visible;

    public OsdPosition Position { get; set; } = OsdPosition.BottomCenter;

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

    public int ShowMessage(string text, OsdKind kind, TimeSpan? duration, DateTime? countdownTo = null)
    {
        Generation++;
        MessageText.Text = text;
        MessageText.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        Dot.Fill = (Brush)FindResource(kind switch
        {
            OsdKind.Success => "Kd.Success",
            OsdKind.Warning => "Kd.Warning",
            _ => "Kd.Accent",
        });

        _countdownTo = countdownTo;
        CountdownText.Visibility = countdownTo is null ? Visibility.Collapsed : Visibility.Visible;
        if (countdownTo is not null)
        {
            UpdateCountdown();
            _countdownTimer.Start();
        }
        else _countdownTimer.Stop();

        _hideTimer.Stop();
        if (duration is { } d)
        {
            _hideTimer.Interval = d;
            _hideTimer.Start();
        }

        if (!IsVisible) Show();
        Reposition();
        if (!_visible)
        {
            _visible = true;
            Animate(1, 0, 1, ShowDuration, EaseOut);
        }
        else
        {
            // Already visible: small "pulse" so the change is noticed.
            var pulse = new DoubleAnimation(1.04, 1, new Duration(TimeSpan.FromMilliseconds(200))) { EasingFunction = EaseOut };
            Scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            Scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }
        return Generation;
    }

    public void HideMessage(int? generation = null)
    {
        if (generation is { } g && g != Generation) return;
        _hideTimer.Stop();
        _countdownTimer.Stop();
        if (!_visible) return;
        _visible = false;
        Animate(0, 10, 0.97, HideDuration, EaseIn);
    }

    private void UpdateCountdown()
    {
        if (_countdownTo is not { } to) return;
        var left = to - DateTime.Now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        CountdownText.Text = left.TotalSeconds >= 60 ? $"{(int)left.TotalMinutes}:{left.Seconds:00}" : $"{Math.Ceiling(left.TotalSeconds):0}";
    }

    private void Animate(double opacity, double y, double scale, Duration duration, IEasingFunction ease)
    {
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, duration) { EasingFunction = ease });
        Shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, duration) { EasingFunction = ease });
        Scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
        Scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
    }

    /// <summary>Places the window on the monitor that has the mouse cursor.</summary>
    private void Reposition()
    {
        if (!IsLoaded && !IsVisible) return;
        var hwnd = UiNative.Handle(this);
        var cursor = UiNative.CursorPosition();
        var work = UiNative.WorkAreaAt(cursor);
        if (!UiNative.GetWindowRect(hwnd, out var r)) return;
        int w = r.Width, h = r.Height;
        var dpi = UiNative.GetDpiForWindow(hwnd) / 96.0;
        var gap = (int)(40 * dpi);
        var (x, y) = Position switch
        {
            OsdPosition.TopCenter => (work.Left + (work.Width - w) / 2, work.Top + gap),
            OsdPosition.Center => (work.Left + (work.Width - w) / 2, work.Top + (work.Height - h) / 2),
            OsdPosition.TopRight => (work.Right - w - gap / 2, work.Top + gap),
            OsdPosition.BottomRight => (work.Right - w - gap / 2, work.Bottom - h - gap),
            _ => (work.Left + (work.Width - w) / 2, work.Bottom - h - gap),
        };
        UiNative.MoveWindow(hwnd, x, y);
    }
}

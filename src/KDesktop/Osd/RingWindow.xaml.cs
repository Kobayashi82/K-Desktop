using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Native;
using Wpf.Ui.Controls;

namespace KDesktop.Osd;

/// <summary>
/// Countdowns and the progress of what can be stopped, as a ring a little below the middle of the screen (Options ›
/// «Cuentas atrás en un círculo»): the seconds left or the % done in the middle, the message below. Click-through and
/// never focused, like the OSD. One instance shows the last one asked for; <see cref="Generation"/> tells them apart.
/// </summary>
public partial class RingWindow : Window
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction EaseIn = new CubicEase { EasingMode = EasingMode.EaseIn };

    /// <summary>How much of the ring is drawn (0–1).</summary>
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(RingWindow), new PropertyMetadata(0.0, (d, _) => ((RingWindow)d).DrawArc()));

    public double Fraction
    {
        get => (double)GetValue(FractionProperty);
        set => SetValue(FractionProperty, value);
    }

    private DateTime _from, _to;
    private bool _counting, _visible;

    public ScreenChoice Screen { get; set; } = ScreenChoice.Mouse;

    public OsdSize Size
    {
        set
        {
            var scale = value switch { OsdSize.Small => 0.85, OsdSize.Large => 1.22, _ => 1.0 };
            SizeScale.ScaleX = SizeScale.ScaleY = scale;
        }
    }

    /// <summary>Incremented every time a new countdown or progress replaces the current one.</summary>
    public int Generation { get; private set; }

    public RingWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => UiNative.AddExStyle(UiNative.Handle(this), toolWindow: true, noActivate: true, clickThrough: true);
    }

    public int ShowCountdown(string text, string? icon, DateTime to)
    {
        Begin(text, icon ?? "Timer24");
        _from = DateTime.Now;
        _to = to;
        BeginAnimation(FractionProperty, null);
        Tick(null, EventArgs.Empty);
        if (!_counting)
        {
            _counting = true;
            CompositionTarget.Rendering += Tick;
        }
        return Generation;
    }

    public int ShowProgress(string text, string? icon, double level)
    {
        Begin(text, icon ?? "ArrowSync24");
        StopCounting();
        BeginAnimation(FractionProperty, null);
        Fraction = 0;
        SetProgress(Generation, text, level);
        return Generation;
    }

    /// <summary>A new step of the progress <paramref name="generation"/> (only while it is the one shown).</summary>
    public void SetProgress(int generation, string text, double level)
    {
        if (generation != Generation) return;
        level = Math.Clamp(level, 0, 1);
        SetMessage(text);
        Number.Text = (level).ToString("P0", Loc.Instance.Culture);
        BeginAnimation(FractionProperty, new DoubleAnimation(level, new Duration(TimeSpan.FromMilliseconds(220))) { EasingFunction = EaseOut });
    }

    public void HideRing(int? generation = null)
    {
        if (generation is { } g && g != Generation) return;
        StopCounting();
        if (!_visible) return;
        _visible = false;
        Animate(0, 0.94, new Duration(TimeSpan.FromMilliseconds(180)), EaseIn);
    }

    private void Begin(string text, string icon)
    {
        Generation++;
        RingIcon.Symbol = Enum.TryParse<SymbolRegular>(icon, out var symbol) ? symbol : SymbolRegular.Timer24;
        SetMessage(text);
        if (!IsVisible) Show();
        Reposition();
        if (_visible) return;
        _visible = true;
        Scale.ScaleX = Scale.ScaleY = 0.9;
        Animate(1, 1, new Duration(TimeSpan.FromMilliseconds(260)), EaseOut);
    }

    private void SetMessage(string text)
    {
        MessageText.Text = text;
        Pill.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void StopCounting()
    {
        if (!_counting) return;
        _counting = false;
        CompositionTarget.Rendering -= Tick;
    }

    /// <summary>Every frame of a countdown: the ring empties smoothly, the number changes once a second.</summary>
    private void Tick(object? sender, EventArgs e)
    {
        var total = (_to - _from).TotalSeconds;
        var left = Math.Max(0, (_to - DateTime.Now).TotalSeconds);
        Fraction = total <= 0 ? 0 : left / total;
        var seconds = (int)Math.Ceiling(left);
        var shown = seconds >= 60
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}")
            : seconds.ToString(CultureInfo.InvariantCulture);
        if (Number.Text != shown) Number.Text = shown;
    }

    private void DrawArc()
    {
        var f = Math.Clamp(Fraction, 0, 1);
        // On the track: the disc less its margin (14) and half the stroke (4).
        var size = Disc.Width;
        var r = size / 2 - 18;
        var c = size / 2;
        if (f <= 0.0005)
        {
            Arc.Data = null;
            return;
        }
        if (f >= 0.9995)
        {
            Arc.Data = new EllipseGeometry(new Point(c, c), r, r);
            return;
        }
        var angle = f * 2 * Math.PI;
        var start = new Point(c, c - r);
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, isFilled: false, isClosed: false);
            ctx.ArcTo(end, new Size(r, r), 0, isLargeArc: f > 0.5, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }
        geometry.Freeze();
        Arc.Data = geometry;
    }

    private void Animate(double opacity, double scale, Duration duration, IEasingFunction ease)
    {
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, duration) { EasingFunction = ease });
        Scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
        Scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
    }

    /// <summary>The ring's centre in the middle of the chosen monitor, a little below (58 % of its height).</summary>
    private void Reposition()
    {
        var hwnd = UiNative.Handle(this);
        if (hwnd == IntPtr.Zero) return;
        UpdateLayout();
        var work = UiNative.MonitorFor(Screen).Work;
        if (!UiNative.GetWindowRect(hwnd, out var r)) return;
        var dpi = UiNative.GetDpiForWindow(hwnd) / 96.0;
        // Where the ring's centre is in the window (it does not move: the window never changes size).
        var centre = Disc.TranslatePoint(new Point(Disc.ActualWidth / 2, Disc.ActualHeight / 2), this);
        var x = work.Left + work.Width / 2 - (int)Math.Round(centre.X * dpi);
        var y = work.Top + (int)(work.Height * 0.58) - (int)Math.Round(centre.Y * dpi);
        UiNative.MoveWindow(hwnd, x, Math.Max(work.Top, Math.Min(y, work.Bottom - r.Height)));
    }
}

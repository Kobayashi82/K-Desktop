using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KDesktop.Core.Achievements;
using KDesktop.Core.Localization;
using KDesktop.Native;
using Wpf.Ui.Controls;

namespace KDesktop.Osd;

/// <summary>
/// "Achievement unlocked" card: slides in at a corner of the screen (Options › OSD › Notificaciones), the badge pops
/// with a burst and a shine sweeps over it. Hovering pauses it, clicking opens the achievements page. Never takes the focus.
/// </summary>
public partial class AchievementToast : Window
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction EaseIn = new CubicEase { EasingMode = EasingMode.EaseIn };

    private readonly TaskCompletionSource _closed = new();
    private AnimationClock? _timeClock;
    private bool _hiding;

    /// <summary>Raised when the user clicks the card.</summary>
    public event Action? Clicked;

    public Core.Model.ScreenChoice Screen { get; init; }
    public Core.Model.NotifyCorner Corner { get; init; }

    private bool OnLeft => Corner is Core.Model.NotifyCorner.BottomLeft or Core.Model.NotifyCorner.TopLeft;

    /// <summary>It slides in from its side of the screen: from the right, or from the left in a left corner.</summary>
    private double Side => OnLeft ? -1 : 1;

    public AchievementToast()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => UiNative.AddExStyle(UiNative.Handle(this), toolWindow: true, noActivate: true, clickThrough: false);
        Card.SizeChanged += (_, e) => CardClip.Rect = new Rect(e.NewSize);
        MouseEnter += (_, _) =>
        {
            _timeClock?.Controller?.Pause();
            CloseButton.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        };
        MouseLeave += (_, _) =>
        {
            _timeClock?.Controller?.Resume();
            CloseButton.BeginAnimation(OpacityProperty, new DoubleAnimation(0.45, TimeSpan.FromMilliseconds(200)));
        };
        MouseLeftButtonUp += OnClick;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide(fast: true);

    /// <summary>Shows the card and completes when it has disappeared.</summary>
    public Task ShowAsync(AchievementDef def, TimeSpan duration)
    {
        Kicker.Text = Loc.T("toast.unlocked").ToUpper(Loc.Instance.Culture);
        NameText.Text = def.Name;
        DescText.Text = def.Description;
        if (Enum.TryParse<SymbolRegular>(def.Symbol, out var symbol)) BadgeIcon.Symbol = symbol;
        if (def.Id == AchievementCatalog.Master) BadgeIcon.Symbol = SymbolRegular.Crown20;
        return Present(duration);
    }

    /// <summary>Options › Notificaciones › «Probar»: the same card, saying what it is instead of an achievement.</summary>
    public Task ShowSampleAsync(TimeSpan duration)
    {
        Kicker.Text = Loc.T("settings.notifySampleKicker").ToUpper(Loc.Instance.Culture);
        NameText.Text = Loc.T("settings.notifySampleTitle");
        DescText.Text = Loc.T("settings.notifySampleText");
        BadgeIcon.Symbol = SymbolRegular.Alert24;
        return Present(duration);
    }

    /// <summary>«Mostrar notificación»: your title, text, icon and colour.</summary>
    public Task ShowCustomAsync(Core.Engine.Notification notification, TimeSpan duration)
    {
        Kicker.Visibility = Visibility.Collapsed;
        NameText.Text = notification.Title;
        DescText.Text = notification.Text;
        DescText.Visibility = string.IsNullOrWhiteSpace(notification.Text) ? Visibility.Collapsed : Visibility.Visible;
        BadgeIcon.Symbol = SymbolRegular.Alert24;
        if (!string.IsNullOrWhiteSpace(notification.Icon) && Services.IconProvider.FromConfigIcon(notification.Icon, adaptToTheme: false) is { } image)
        {
            BadgeImage.Source = image;
            BadgeImage.Visibility = Visibility.Visible;
            BadgeIcon.Visibility = Visibility.Collapsed;
        }
        Paint(notification.Color);
        return Present(duration);
    }

    /// <summary>Light, middle and dark tone of the badge, and the colour of the symbol on it.</summary>
    private static readonly Dictionary<Core.Model.NotifyColor, (uint Light, uint Mid, uint Dark, uint Ink)> Palette = new()
    {
        [Core.Model.NotifyColor.Gold] = (0xFFF6D365, 0xFFE0A815, 0xFFB7790B, 0xFF3A2600),
        [Core.Model.NotifyColor.Blue] = (0xFF9CCBFF, 0xFF3B8BEB, 0xFF1E5BB8, 0xFF061C3A),
        [Core.Model.NotifyColor.Green] = (0xFFA3E99A, 0xFF3DBB5A, 0xFF23843A, 0xFF0B2E13),
        [Core.Model.NotifyColor.Red] = (0xFFFFA59A, 0xFFE5534B, 0xFFB3261E, 0xFF3A0806),
        [Core.Model.NotifyColor.Purple] = (0xFFD9B3FF, 0xFF9B5DE5, 0xFF6A2FB8, 0xFF1F0839),
    };

    /// <summary>Gives the card a colour of its own (the achievements keep their gold).</summary>
    private void Paint(Core.Model.NotifyColor color)
    {
        if (!Palette.TryGetValue(color, out var p)) return;
        static Color C(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        var mid = C(p.Mid);
        BadgeLight.Color = C(p.Light);
        BadgeMid.Color = mid;
        BadgeDark.Color = C(p.Dark);
        BadgeIcon.Foreground = new SolidColorBrush(C(p.Ink));
        GlowStop.Color = Color.FromArgb(0x55, mid.R, mid.G, mid.B);
        GlowEnd.Color = Color.FromArgb(0, mid.R, mid.G, mid.B);
        var brush = new SolidColorBrush(mid);
        Burst.Stroke = brush;
        TimeBar.Fill = brush;
    }

    private Task Present(TimeSpan duration)
    {
        Show();
        Reposition();
        PlayIntro(duration);
        return _closed.Task;
    }

    private void PlayIntro(TimeSpan duration)
    {
        var slide = new Duration(TimeSpan.FromMilliseconds(420));
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(260))));
        RootShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(70 * Side, 0, slide) { EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } });
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, slide) { EasingFunction = EaseOut });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, slide) { EasingFunction = EaseOut });

        // Badge: pops with a little overshoot and straightens.
        var pop = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(650)))
        {
            BeginTime = TimeSpan.FromMilliseconds(160),
            EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut },
        };
        BadgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        BadgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        BadgeRotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(-25, 0, new Duration(TimeSpan.FromMilliseconds(600))) { BeginTime = TimeSpan.FromMilliseconds(160), EasingFunction = EaseOut });

        // Burst ring.
        var burstTime = TimeSpan.FromMilliseconds(330);
        var burstDuration = new Duration(TimeSpan.FromMilliseconds(700));
        var grow = new DoubleAnimation(0.6, 1.9, burstDuration) { BeginTime = burstTime, EasingFunction = EaseOut };
        BurstScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        BurstScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        var fade = new DoubleAnimationUsingKeyFrames { BeginTime = burstTime };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(burstDuration.TimeSpan), EaseOut));
        Burst.BeginAnimation(OpacityProperty, fade);

        // Shine sweep across the card.
        ShineShift.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(-140, 440, new Duration(TimeSpan.FromMilliseconds(900))) { BeginTime = TimeSpan.FromMilliseconds(520), EasingFunction = EaseIn });

        // Remaining time; when it runs out the card leaves.
        var time = new DoubleAnimation(1, 0, new Duration(duration)) { BeginTime = TimeSpan.FromMilliseconds(400) };
        _timeClock = time.CreateClock();
        _timeClock.Completed += (_, _) => Hide(fast: false);
        TimeScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, _timeClock);
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        Clicked?.Invoke();
        Hide(fast: true);
    }

    public void Hide(bool fast)
    {
        if (_hiding) return;
        _hiding = true;
        var d = new Duration(TimeSpan.FromMilliseconds(fast ? 160 : 280));
        var fadeOut = new DoubleAnimation(0, d) { EasingFunction = EaseIn };
        fadeOut.Completed += (_, _) =>
        {
            Close();
            _closed.TrySetResult();
        };
        RootShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(50 * Side, d) { EasingFunction = EaseIn });
        Root.BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>The chosen corner of the chosen monitor, inside the work area (never over the taskbar).</summary>
    private void Reposition()
    {
        var hwnd = UiNative.Handle(this);
        var work = UiNative.MonitorFor(Screen).Work;
        if (!UiNative.GetWindowRect(hwnd, out var r)) return;
        var dpi = UiNative.GetDpiForWindow(hwnd) / 96.0;
        var gap = (int)(4 * dpi);
        var top = Corner is Core.Model.NotifyCorner.TopLeft or Core.Model.NotifyCorner.TopRight;
        UiNative.MoveWindow(hwnd, OnLeft ? work.Left + gap : work.Right - r.Width - gap, top ? work.Top + gap : work.Bottom - r.Height - gap);
    }
}

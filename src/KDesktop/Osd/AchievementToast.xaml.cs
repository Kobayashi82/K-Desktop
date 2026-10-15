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
/// "Achievement unlocked" card: slides in at the bottom-right corner, the badge pops with a burst and a shine
/// sweeps over it. Hovering pauses it, clicking opens the achievements page. Never takes the focus.
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

    public AchievementToast()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => UiNative.AddExStyle(UiNative.Handle(this), toolWindow: true, noActivate: true, clickThrough: false);
        Card.SizeChanged += (_, e) => CardClip.Rect = new Rect(e.NewSize);
        MouseEnter += (_, _) => _timeClock?.Controller?.Pause();
        MouseLeave += (_, _) => _timeClock?.Controller?.Resume();
        MouseLeftButtonUp += OnClick;
    }

    /// <summary>Shows the card and completes when it has disappeared.</summary>
    public Task ShowAsync(AchievementDef def, TimeSpan duration)
    {
        Kicker.Text = Loc.T("toast.unlocked").ToUpper(Loc.Instance.Culture);
        NameText.Text = def.Name;
        DescText.Text = def.Description;
        if (Enum.TryParse<SymbolRegular>(def.Symbol, out var symbol)) BadgeIcon.Symbol = symbol;
        if (def.Id == AchievementCatalog.Master) BadgeIcon.Symbol = SymbolRegular.Crown24;

        Show();
        Reposition();
        PlayIntro(duration);
        return _closed.Task;
    }

    private void PlayIntro(TimeSpan duration)
    {
        var slide = new Duration(TimeSpan.FromMilliseconds(420));
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(260))));
        RootShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(70, 0, slide) { EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut } });
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
        RootShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(50, d) { EasingFunction = EaseIn });
        Root.BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>Bottom-right corner of the monitor that has the mouse, above the taskbar.</summary>
    private void Reposition()
    {
        var hwnd = UiNative.Handle(this);
        var work = UiNative.WorkAreaAt(UiNative.CursorPosition());
        if (!UiNative.GetWindowRect(hwnd, out var r)) return;
        var dpi = UiNative.GetDpiForWindow(hwnd) / 96.0;
        var gap = (int)(4 * dpi);
        UiNative.MoveWindow(hwnd, work.Right - r.Width - gap, work.Bottom - r.Height - gap);
    }
}

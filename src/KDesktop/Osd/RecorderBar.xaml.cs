using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using KDesktop.Core.Localization;
using KDesktop.Native;
using Wpf.Ui.Controls;

namespace KDesktop.Osd;

/// <summary>
/// Floating bar shown while a macro is recorded: 3-2-1 countdown, then a pulsing dot, elapsed time and number of
/// steps, with pause and stop. It never takes the focus, so the application being recorded keeps it.
/// </summary>
public partial class RecorderBar : Window
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Stopwatch _elapsed = new();
    private Func<int>? _count;
    private Func<string>? _detail;
    private bool _paused;

    public event Action? StopClicked;
    public event Action<bool>? PauseToggled;

    public RecorderBar()
    {
        InitializeComponent();
        StopText.Text = Loc.T("macro.stop");
        PauseButton.ToolTip = Loc.T("macro.pause");
        System.Windows.Automation.AutomationProperties.SetName(PauseButton, Loc.T("macro.pause"));
        System.Windows.Automation.AutomationProperties.SetName(StopButton, Loc.T("macro.stop"));
        SourceInitialized += (_, _) => UiNative.AddExStyle(UiNative.Handle(this), toolWindow: true, noActivate: true, clickThrough: false);
        SizeChanged += (_, _) => Reposition();
        _clock.Tick += (_, _) => UpdateInfo();
    }

    /// <summary>Appears and counts down; completes when recording should start (false if stopped meanwhile).</summary>
    public async Task<bool> CountdownAsync(int seconds)
    {
        var stopped = false;
        void OnStop() => stopped = true;
        StopClicked += OnStop;
        StateText.Text = Loc.T("macro.getReady");
        InfoText.Text = Loc.T("macro.stopHint");
        Show();
        Reposition();
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        RootShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-24, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = EaseOut });

        for (var i = seconds; i > 0 && !stopped; i--)
        {
            CountdownText.Text = i.ToString(Loc.Instance.Culture);
            var pop = new DoubleAnimation(1.6, 1, TimeSpan.FromMilliseconds(350)) { EasingFunction = EaseOut };
            CountdownScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            CountdownScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            CountdownText.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(250)));
            await Task.Delay(1000);
        }
        StopClicked -= OnStop;
        return !stopped;
    }

    /// <summary>
    /// «Grabar vídeo» (F2): straight to "recording", with <paramref name="detail"/> after the time (the size), and left out
    /// of every capture and recording of the screen (it never shows in the video).
    /// </summary>
    public void StartVideo(Func<string> detail)
    {
        _detail = detail;
        SourceInitialized += (_, _) => UiNative.SetWindowDisplayAffinity(UiNative.Handle(this), UiNative.WDA_EXCLUDEFROMCAPTURE);
        Show();
        Reposition();
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        RootShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-24, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = EaseOut });
        StartRecording(() => 0);
    }

    /// <summary>Switches to "recording".</summary>
    public void StartRecording(Func<int> count)
    {
        _count = count;
        CountdownPanel.Visibility = Visibility.Collapsed;
        DotPanel.Visibility = Visibility.Visible;
        PauseButton.IsEnabled = true;
        _elapsed.Restart();
        _clock.Start();
        Pulse(true);
        UpdateInfo();
    }

    private void Pulse(bool on)
    {
        if (!on)
        {
            HaloScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            HaloScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            Halo.BeginAnimation(OpacityProperty, null);
            Halo.Opacity = 0;
            return;
        }
        var grow = new DoubleAnimation(0.6, 1.25, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = EaseOut };
        HaloScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        HaloScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        Halo.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    private void UpdateInfo()
    {
        var t = _elapsed.Elapsed;
        StateText.Text = Loc.T(_paused ? "macro.paused" : "macro.recording");
        InfoText.Text = _detail is not null
            ? $"{(int)t.TotalMinutes:00}:{t.Seconds:00}  ·  " + _detail()
            : $"{(int)t.TotalMinutes:00}:{t.Seconds:00}  ·  " + Loc.T("macro.stepCount", _count?.Invoke() ?? 0) + "  ·  " + Loc.T("macro.stopKeys");
    }

    private void OnPause(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        if (_paused) _elapsed.Stop(); else _elapsed.Start();
        PauseIcon.Symbol = _paused ? SymbolRegular.Play24 : SymbolRegular.Pause24;
        PauseButton.ToolTip = Loc.T(_paused ? "macro.resume" : "macro.pause");
        System.Windows.Automation.AutomationProperties.SetName(PauseButton, (string)PauseButton.ToolTip);
        Dot.Fill = _paused ? (Brush)FindResource("Kd.Warning") : new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
        Pulse(!_paused);
        UpdateInfo();
        PauseToggled?.Invoke(_paused);
    }

    private void OnStop(object sender, RoutedEventArgs e) => StopClicked?.Invoke();

    public void CloseAnimated()
    {
        _clock.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) => Close();
        RootShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-16, TimeSpan.FromMilliseconds(180)));
        Root.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Top centre of the monitor that has the mouse.</summary>
    private void Reposition()
    {
        if (!IsVisible) return;
        var hwnd = UiNative.Handle(this);
        var work = UiNative.WorkAreaAt(UiNative.CursorPosition());
        if (!UiNative.GetWindowRect(hwnd, out var r)) return;
        UiNative.MoveWindow(hwnd, work.Left + (work.Width - r.Width) / 2, work.Top);
    }
}

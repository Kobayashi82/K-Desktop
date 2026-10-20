using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KDesktop.Native;

namespace KDesktop.Views.Assistant;

/// <summary>
/// «¿Puedo cerrar Word?» (stage 3.3, plan §8): an assistant asks before using a tool. Bottom right of the monitor with the
/// mouse, without taking the focus; it gives one answer — <c>yes</c>, <c>no</c>, <c>always</c> or <c>timeout</c> (no
/// answer in time is a no) — and closes.
/// </summary>
public partial class AskWindow : Window
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    private readonly TaskCompletionSource<string> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan _time;

    /// <param name="who">The assistant's name (or K-Desktop's, without a body).</param>
    /// <param name="canTrust">Whether «yes, and don't ask again» is offered.</param>
    public AskWindow(string who, ImageSource? icon, string question, bool canTrust, TimeSpan time)
    {
        InitializeComponent();
        _time = time;
        WhoText.Text = who;
        QuestionText.Text = question;
        AssistantIcon.Source = icon;
        Fallback.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;
        AlwaysButton.Visibility = canTrust ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => Appear();
    }

    /// <summary>The answer, when it is given (or the time runs out).</summary>
    public Task<string> Answer => _answer.Task;

    /// <summary>Closes it from outside (the process went away): it counts as no answer.</summary>
    public void Abandon() => Give("timeout");

    private void Appear()
    {
        var hwnd = UiNative.Handle(this);
        var work = UiNative.WorkAreaAt(UiNative.CursorPosition());
        if (UiNative.GetWindowRect(hwnd, out var r))
        {
            var gap = (int)(4 * UiNative.GetDpiForWindow(hwnd) / 96.0);
            UiNative.MoveWindow(hwnd, work.Right - r.Width - gap, work.Bottom - r.Height - gap);
        }
        var d = TimeSpan.FromMilliseconds(260);
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, d));
        RootShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, new DoubleAnimation(60, 0, d) { EasingFunction = EaseOut });
        var countdown = new DoubleAnimation(1, 0, _time);
        countdown.Completed += (_, _) => Give("timeout");
        TimeBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, countdown);
    }

    private void OnYes(object sender, RoutedEventArgs e) => Give("yes");

    private void OnNo(object sender, RoutedEventArgs e) => Give("no");

    private void OnAlways(object sender, RoutedEventArgs e) => Give("always");

    private void Give(string choice)
    {
        if (!_answer.TrySetResult(choice)) return;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => Close();
        Root.BeginAnimation(OpacityProperty, fade);
    }
}

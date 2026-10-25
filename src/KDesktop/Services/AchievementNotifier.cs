using KDesktop.Core.Achievements;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Native;
using KDesktop.Osd;

namespace KDesktop.Services;

/// <summary>
/// Shows the notification cards one after another (UI thread only): achievements, «Mostrar notificación» and the sample
/// of Options, on the monitor and in the corner of Options › OSD › Notificaciones (or the action's own), for as long as
/// it says; while something covers that monitor (a film, a game) they show, wait or are dropped.
/// </summary>
public sealed class AchievementNotifier(Func<AppSettings> settings, Action<string> openPage)
{
    private static readonly TimeSpan FullscreenRecheck = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Hurried = TimeSpan.FromSeconds(3.2);

    /// <summary>One card: an achievement, a notification of an action, or (both null) the sample of «Probar».</summary>
    private sealed record Card(AchievementDef? Achievement, Notification? Custom, TaskCompletionSource? Done)
    {
        public bool IsSample => Achievement is null && Custom is null;
    }

    private readonly Queue<Card> _queue = new();
    private bool _showing;

    public void Show(AchievementDef def)
    {
        if (!settings().AchievementNotifications) return;
        Enqueue(new Card(def, null, null));
    }

    /// <summary>Options › «Probar»: a sample card now, whatever the switches and full screen say.</summary>
    public void Preview() => Enqueue(new Card(null, null, null));

    /// <summary>«Mostrar notificación»: completes when the card is gone, or was dropped (full screen and «No mostrarlas»).</summary>
    public Task ShowCustom(Notification notification)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new Card(null, notification, done));
        return done.Task;
    }

    private void Enqueue(Card card)
    {
        _queue.Enqueue(card);
        if (!_showing) _ = PumpAsync();
    }

    private async Task PumpAsync()
    {
        _showing = true;
        try
        {
            while (_queue.TryPeek(out var next))
            {
                if (!next.IsSample && Covered())
                {
                    if (settings().NotifyFullscreen == NotifyFullscreen.Hide)
                    {
                        _queue.Dequeue().Done?.TrySetResult();
                        continue;
                    }
                    // Wait: they stay queued until the film or the game is over.
                    await Task.Delay(FullscreenRecheck);
                    continue;
                }
                _queue.Dequeue();
                try { await ShowAsync(next); }
                finally { next.Done?.TrySetResult(); }
                await Task.Delay(150);
            }
        }
        catch (Exception ex)
        {
            Core.Log.Error("Error mostrando una notificación", ex);
        }
        finally
        {
            _showing = false;
        }
    }

    private Task ShowAsync(Card card)
    {
        var s = settings();
        var custom = card.Custom;
        var corner = custom?.Corner ?? s.NotifyCorner;
        var toast = new AchievementToast { Screen = s.NotifyScreen, Corner = corner };
        var duration = custom?.Duration ?? TimeSpan.FromSeconds(Math.Max(1, s.NotifySeconds));
        // Shorter when several are waiting.
        if (_queue.Count > 0 && duration > Hurried) duration = Hurried;

        if (custom is not null)
        {
            Sounds.Play(custom.Sound);
            if (custom.Clicked is { } clicked) toast.Clicked += clicked;
            return toast.ShowCustomAsync(custom, duration);
        }
        if (s.AchievementSound) Sounds.Play("Logro");
        if (card.Achievement is not { } def) return toast.ShowSampleAsync(duration);
        toast.Clicked += () =>
        {
            // The rest are dropped (whoever waits for one is let go).
            while (_queue.TryDequeue(out var dropped)) dropped.Done?.TrySetResult();
            openPage("achievements");
        };
        return toast.ShowAsync(def, duration);
    }

    /// <summary>Whether something covers the monitor the cards go to, and that should hold them back.</summary>
    private bool Covered()
    {
        var s = settings();
        return s.NotifyFullscreen != NotifyFullscreen.Show && UiNative.FullscreenOn(UiNative.MonitorFor(s.NotifyScreen).Monitor);
    }
}

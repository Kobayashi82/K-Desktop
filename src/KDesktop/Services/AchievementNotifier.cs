using KDesktop.Core.Achievements;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Native;
using KDesktop.Osd;

namespace KDesktop.Services;

/// <summary>
/// Shows the "achievement unlocked" cards one after another (UI thread only), on the monitor and in the corner of
/// Options › OSD › Notificaciones, for as long as it says; while something covers that monitor (a film, a game) they
/// show, wait or are dropped.
/// </summary>
public sealed class AchievementNotifier(Func<AppSettings> settings, Action<string> openPage)
{
    private static readonly TimeSpan FullscreenRecheck = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Hurried = TimeSpan.FromSeconds(3.2);

    /// <summary>An achievement, or (null) the sample card of «Probar».</summary>
    private readonly Queue<AchievementDef?> _queue = new();
    private bool _showing;

    public void Show(AchievementDef def)
    {
        if (!settings().AchievementNotifications) return;
        Enqueue(def);
    }

    /// <summary>Options › «Probar»: a sample card now, whatever the switches and full screen say.</summary>
    public void Preview() => Enqueue(null);

    private void Enqueue(AchievementDef? def)
    {
        _queue.Enqueue(def);
        if (!_showing) _ = PumpAsync();
    }

    private async Task PumpAsync()
    {
        _showing = true;
        try
        {
            while (_queue.TryPeek(out var next))
            {
                if (next is not null && Covered())
                {
                    if (settings().NotifyFullscreen == NotifyFullscreen.Hide)
                    {
                        _queue.Dequeue();
                        continue;
                    }
                    // Wait: they stay queued until the film or the game is over.
                    await Task.Delay(FullscreenRecheck);
                    continue;
                }
                _queue.Dequeue();
                var s = settings();
                if (s.AchievementSound) Sounds.Play("Logro");
                var toast = new AchievementToast { Screen = s.NotifyScreen, Corner = s.NotifyCorner };
                // Shorter when several are waiting.
                var duration = TimeSpan.FromSeconds(Math.Max(1, s.NotifySeconds));
                if (_queue.Count > 0 && duration > Hurried) duration = Hurried;
                if (next is null) await toast.ShowSampleAsync(duration);
                else
                {
                    toast.Clicked += () =>
                    {
                        _queue.Clear();
                        openPage("achievements");
                    };
                    await toast.ShowAsync(next, duration);
                }
                await Task.Delay(150);
            }
        }
        catch (Exception ex)
        {
            Core.Log.Error("Error mostrando un logro", ex);
        }
        finally
        {
            _showing = false;
        }
    }

    /// <summary>Whether something covers the monitor the cards go to, and that should hold them back.</summary>
    private bool Covered()
    {
        var s = settings();
        return s.NotifyFullscreen != NotifyFullscreen.Show && UiNative.FullscreenOn(UiNative.MonitorFor(s.NotifyScreen).Monitor);
    }
}

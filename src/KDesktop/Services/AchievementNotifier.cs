using KDesktop.Core.Achievements;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Native;
using KDesktop.Osd;

namespace KDesktop.Services;

/// <summary>
/// Shows the "achievement unlocked" cards one after another (UI thread only), on the monitor and in the corner of
/// Options › OSD › Notificaciones; while something covers that monitor (a film, a game) they show, wait or are dropped.
/// </summary>
public sealed class AchievementNotifier(Func<AppSettings> settings, Action<string> openPage)
{
    private static readonly TimeSpan FullscreenRecheck = TimeSpan.FromSeconds(2);

    private readonly Queue<(AchievementDef Def, bool Preview)> _queue = new();
    private bool _showing;

    public void Show(AchievementDef def)
    {
        if (!settings().AchievementNotifications) return;
        Enqueue(def, preview: false);
    }

    /// <summary>Options › «Probar»: a card now, whatever the switches and full screen say.</summary>
    public void Preview(AchievementDef def) => Enqueue(def, preview: true);

    private void Enqueue(AchievementDef def, bool preview)
    {
        _queue.Enqueue((def, preview));
        if (!_showing) _ = PumpAsync();
    }

    private async Task PumpAsync()
    {
        _showing = true;
        try
        {
            while (_queue.TryPeek(out var next))
            {
                if (!next.Preview && Covered())
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
                if (s.AchievementSound || next.Preview) Sounds.Play("Logro");
                var toast = new AchievementToast { Screen = s.NotifyScreen, Corner = s.NotifyCorner };
                toast.Clicked += () =>
                {
                    _queue.Clear();
                    openPage("achievements");
                };
                // Shorter when several are waiting.
                var duration = TimeSpan.FromSeconds(_queue.Count > 0 ? 3.2 : 5.5);
                await toast.ShowAsync(next.Def, duration);
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

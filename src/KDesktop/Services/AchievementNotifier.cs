using KDesktop.Core.Achievements;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Osd;

namespace KDesktop.Services;

/// <summary>Shows the "achievement unlocked" cards one after another (UI thread only).</summary>
public sealed class AchievementNotifier(Func<AppSettings> settings, Action<string> openPage)
{
    private readonly Queue<AchievementDef> _queue = new();
    private bool _showing;

    public void Show(AchievementDef def)
    {
        if (!settings().AchievementNotifications) return;
        _queue.Enqueue(def);
        if (!_showing) _ = PumpAsync();
    }

    private async Task PumpAsync()
    {
        _showing = true;
        try
        {
            while (_queue.TryDequeue(out var def))
            {
                if (settings().AchievementSound) Sounds.Play("Logro");
                var toast = new AchievementToast();
                toast.Clicked += () =>
                {
                    _queue.Clear();
                    openPage("achievements");
                };
                // Shorter when several are waiting.
                var duration = TimeSpan.FromSeconds(_queue.Count > 0 ? 3.2 : 5.5);
                await toast.ShowAsync(def, duration);
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
}

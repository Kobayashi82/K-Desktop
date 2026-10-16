namespace KDesktop.Core.Engine.Events;

/// <summary>
/// «No repetir antes de» and the protection against loops (an event runs a command that sets off another event…): an
/// event that fires more than <see cref="MaxPerMinute"/> times in a minute stops for <see cref="Pause"/>.
/// </summary>
public sealed class EventGuard
{
    public const int MaxPerMinute = 10;
    public static readonly TimeSpan Pause = TimeSpan.FromMinutes(10);

    public enum Verdict { Run, Cooldown, Suspended, Loop }

    private readonly Dictionary<string, Queue<DateTime>> _recent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _suspendedUntil = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    /// <summary>Decides whether the event may fire at <paramref name="now"/> (UTC) and, if so, counts it.</summary>
    public Verdict TryFire(string id, int cooldownSeconds, DateTime now)
    {
        lock (_lock)
        {
            if (_suspendedUntil.TryGetValue(id, out var until))
            {
                if (now < until) return Verdict.Suspended;
                _suspendedUntil.Remove(id);
                _recent.Remove(id);
            }
            if (!_recent.TryGetValue(id, out var times)) _recent[id] = times = new Queue<DateTime>();
            while (times.Count > 0 && now - times.Peek() > TimeSpan.FromMinutes(1)) times.Dequeue();
            if (cooldownSeconds > 0 && times.Count > 0 && now - times.Last() < TimeSpan.FromSeconds(cooldownSeconds)) return Verdict.Cooldown;
            if (times.Count >= MaxPerMinute)
            {
                _suspendedUntil[id] = now + Pause;
                return Verdict.Loop;
            }
            times.Enqueue(now);
            return Verdict.Run;
        }
    }

    public bool IsSuspended(string id, DateTime now)
    {
        lock (_lock) return _suspendedUntil.TryGetValue(id, out var until) && now < until;
    }

    /// <summary>Forgets an event (deleted, or switched off and on again by hand).</summary>
    public void Reset(string id)
    {
        lock (_lock)
        {
            _recent.Remove(id);
            _suspendedUntil.Remove(id);
        }
    }
}

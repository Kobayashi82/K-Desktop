using System.Diagnostics;

namespace KDesktop.Assistant.Sdk;

/// <summary>
/// A set of variants to choose from at random (SDK 1.10): several entrances, reactions, idle animations or lines, so
/// the assistant does not do the same thing every time. Each variant has a weight (how often it comes up compared with
/// the others) and a cooldown (seconds it rests after being picked). It never picks the same one twice in a row while
/// there is another to choose. Give it a seed in tests to always get the same sequence.
/// </summary>
/// <example>
/// <code>
/// private readonly Variety&lt;Clip&gt; _happy = new Variety&lt;Clip&gt;()
///     .Add(Clip.Jump)
///     .Add(Clip.Spin, weight: 0.5)
///     .Add(Clip.Backflip, weight: 0.2, cooldown: 120);   // rare, and never twice in two minutes
/// // in React(Happy):
/// Play(_happy.Pick());
/// // only the ones that fit where it stands:
/// Play(_happy.Pick(clip => clip.FitsOnTaskbar || !onTaskbar));
/// </code>
/// </example>
/// <remarks>Safe to use from several threads (the engine thread and a gesture).</remarks>
public sealed class Variety<T>
{
    private readonly List<Entry> _entries = [];
    private readonly Random _random;
    private readonly Func<double> _clock;
    private readonly Lock _lock = new();
    private int _last = -1;

    /// <param name="seed">Same seed, same sequence (for tests). Null: different every time.</param>
    /// <param name="clock">
    /// Seconds on a monotonic clock, for the cooldowns. Null: its own (any clock works, as long as it is always the
    /// same one).
    /// </param>
    public Variety(int? seed = null, Func<double>? clock = null)
    {
        _random = seed is { } value ? new Random(value) : new Random();
        _clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
    }

    /// <summary>How many variants it has.</summary>
    public int Count
    {
        get { lock (_lock) return _entries.Count; }
    }

    /// <summary>The variants, in the order they were added.</summary>
    public IReadOnlyList<T> Items
    {
        get { lock (_lock) return [.. _entries.Select(e => e.Value)]; }
    }

    /// <summary>Never the same one twice in a row while there is another to choose (on by default).</summary>
    public bool NoRepeat { get; set; } = true;

    /// <summary>The last one picked (or marked with <see cref="Use"/>); false if none yet.</summary>
    public bool TryGetLast(out T value)
    {
        lock (_lock)
        {
            value = _last >= 0 ? _entries[_last].Value : default!;
            return _last >= 0;
        }
    }

    /// <summary>Adds a variant.</summary>
    /// <param name="weight">How often it comes up compared with the others (2 = twice as often as 1). 0 = never.</param>
    /// <param name="cooldown">Seconds it rests after being picked: no other pick returns it before (0 = no rest).</param>
    /// <returns>Itself, to chain the calls.</returns>
    public Variety<T> Add(T value, double weight = 1, double cooldown = 0)
    {
        if (!double.IsFinite(weight) || weight < 0) throw new ArgumentOutOfRangeException(nameof(weight), "0 or more.");
        if (!double.IsFinite(cooldown) || cooldown < 0) throw new ArgumentOutOfRangeException(nameof(cooldown), "0 or more.");
        lock (_lock) _entries.Add(new Entry(value, weight, cooldown));
        return this;
    }

    /// <summary>
    /// Picks one at random by weight, among those that are not resting and are not the last one. If all are resting, the
    /// one that will be ready soonest: something always comes out (use <see cref="TryPick"/> to do nothing instead).
    /// </summary>
    /// <param name="allowed">Only these (the ones that fit now). Always respected.</param>
    /// <exception cref="InvalidOperationException">None allowed with a weight above 0.</exception>
    public T Pick(Func<T, bool>? allowed = null)
    {
        lock (_lock)
        {
            var now = _clock();
            var index = Choose(now, allowed, rested: true);
            if (index < 0) index = Soonest(now, allowed);
            if (index < 0) throw new InvalidOperationException("There is no variant to pick (none allowed with a weight above 0).");
            return Mark(index, now);
        }
    }

    /// <summary>Like <see cref="Pick"/>, but returns false (and picks nothing) when all the allowed ones are resting.</summary>
    public bool TryPick(out T value, Func<T, bool>? allowed = null)
    {
        lock (_lock)
        {
            var now = _clock();
            var index = Choose(now, allowed, rested: true);
            value = index >= 0 ? Mark(index, now) : default!;
            return index >= 0;
        }
    }

    /// <summary>
    /// Marks a variant as just done although it was not picked here (it happened another way): it becomes the last one
    /// and starts resting. Nothing happens if it is not in the set.
    /// </summary>
    public void Use(T value)
    {
        lock (_lock)
        {
            var index = IndexOf(value);
            if (index >= 0) Mark(index, _clock());
        }
    }

    /// <summary>Seconds left before a variant can come up again (0 = ready, or not in the set).</summary>
    public double CooldownLeft(T value)
    {
        lock (_lock)
        {
            var index = IndexOf(value);
            return index >= 0 ? Math.Max(0, _entries[index].ReadyAt - _clock()) : 0;
        }
    }

    /// <summary>Forgets the last one and every cooldown.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _last = -1;
            foreach (var entry in _entries) entry.ReadyAt = double.NegativeInfinity;
        }
    }

    private int Choose(double now, Func<T, bool>? allowed, bool rested)
    {
        var total = 0.0;
        var others = 0.0;
        for (var i = 0; i < _entries.Count; i++)
        {
            if (!Candidate(i, now, allowed, rested)) continue;
            total += _entries[i].Weight;
            if (i != _last) others += _entries[i].Weight;
        }
        if (total <= 0) return -1;
        // The last one only when it is the only one left.
        var skipLast = NoRepeat && others > 0;
        var roll = _random.NextDouble() * (skipLast ? others : total);
        var chosen = -1;
        for (var i = 0; i < _entries.Count; i++)
        {
            if (!Candidate(i, now, allowed, rested) || (skipLast && i == _last)) continue;
            chosen = i;
            roll -= _entries[i].Weight;
            if (roll < 0) break;
        }

        return chosen;
    }

    private int Soonest(double now, Func<T, bool>? allowed)
    {
        var best = -1;
        for (var i = 0; i < _entries.Count; i++)
        {
            if (!Candidate(i, now, allowed, rested: false)) continue;
            if (best < 0 || _entries[i].ReadyAt < _entries[best].ReadyAt || (best == _last && NoRepeat && _entries[i].ReadyAt <= _entries[best].ReadyAt))
            {
                best = i;
            }
        }

        return best;
    }

    private bool Candidate(int index, double now, Func<T, bool>? allowed, bool rested)
    {
        var entry = _entries[index];
        return entry.Weight > 0 && (!rested || entry.ReadyAt <= now) && (allowed is null || allowed(entry.Value));
    }

    private T Mark(int index, double now)
    {
        var entry = _entries[index];
        entry.ReadyAt = now + entry.Cooldown;
        _last = index;
        return entry.Value;
    }

    private int IndexOf(T value)
    {
        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < _entries.Count; i++)
        {
            if (comparer.Equals(_entries[i].Value, value)) return i;
        }

        return -1;
    }

    private sealed class Entry(T value, double weight, double cooldown)
    {
        public T Value { get; } = value;

        public double Weight { get; } = weight;

        public double Cooldown { get; } = cooldown;

        public double ReadyAt { get; set; } = double.NegativeInfinity;
    }
}

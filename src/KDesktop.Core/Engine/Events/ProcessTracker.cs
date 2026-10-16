namespace KDesktop.Core.Engine.Events;

/// <summary>
/// Which watched programs are open, from successive lists of running processes. A group of names («steam, epic»)
/// opens when the first of its processes appears and closes when the last one is gone (several windows, helpers…).
/// </summary>
public sealed class ProcessTracker
{
    private readonly Dictionary<string, string[]> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _open = new(StringComparer.Ordinal);

    /// <summary>Names to look for in the process lists (normalised: «steam»).</summary>
    public IReadOnlySet<string> Names { get; private set; } = new HashSet<string>();

    /// <summary>
    /// Sets the groups (id → names). Groups that stay keep their state; new ones take it from <paramref name="running"/>
    /// without firing (a program already open is not «opening»).
    /// </summary>
    public void SetGroups(IReadOnlyDictionary<string, string[]> groups, IReadOnlyCollection<string> running)
    {
        foreach (var id in _groups.Keys.Where(id => !groups.TryGetValue(id, out var names) || !names.SequenceEqual(_groups[id])).ToList())
        {
            _groups.Remove(id);
            _open.Remove(id);
        }
        foreach (var (id, names) in groups)
        {
            if (_groups.ContainsKey(id)) continue;
            _groups[id] = names;
            _open[id] = names.Any(running.Contains);
        }
        Names = _groups.Values.SelectMany(n => n).ToHashSet(StringComparer.Ordinal);
    }

    public bool IsOpen(string id) => _open.GetValueOrDefault(id);

    /// <summary>Compares with the processes running now and returns the groups that opened or closed.</summary>
    public List<(string Id, bool Opened)> Update(IReadOnlyCollection<string> running)
    {
        var changes = new List<(string, bool)>();
        foreach (var (id, names) in _groups)
        {
            var open = names.Any(running.Contains);
            if (open == _open[id]) continue;
            _open[id] = open;
            changes.Add((id, open));
        }
        return changes;
    }
}

using KDesktop.Core.Engine;
using KDesktop.Core.Localization;

namespace KDesktop.Views;

/// <summary>
/// Names are unique within each kind: with two commands «Prueba», «ejecuta prueba» could not tell them apart. A name
/// typed that another one already has goes back to the one it had (or gets a number), and the OSD says so.
/// </summary>
internal static class UniqueNames
{
    public static bool Taken<T>(IEnumerable<T> items, T self, Func<T, string> nameOf, string name) where T : class =>
        items.Any(i => !ReferenceEquals(i, self) && nameOf(i).Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary><paramref name="baseName"/>, or with a number after it if another one has it already.</summary>
    public static string Free(IEnumerable<string> taken, string baseName)
    {
        var names = taken.Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = baseName;
        for (var i = 2; names.Contains(name); i++) name = $"{baseName} {i}";
        return name;
    }

    /// <summary>The name to keep once edited: <paramref name="name"/> if it is free; if not, <paramref name="before"/> when that one is, or <paramref name="unique"/>.</summary>
    public static string Settle<T>(IEnumerable<T> items, T self, Func<T, string> nameOf, string name, string? before, Func<string, string> unique) where T : class
    {
        if (!Taken(items, self, nameOf, name)) return name;
        var kept = before is { Length: > 0 } && !Taken(items, self, nameOf, before) ? before : unique(name);
        App.Current.Ui.ShowNotice(Loc.T("common.nameTaken", name.Trim(), kept), OsdKind.Warning);
        return kept;
    }
}

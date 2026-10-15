using System.Collections.ObjectModel;

namespace KDesktop.Core.Model;

/// <summary>Tree operations on QuickMenu items (used by the visual editor).</summary>
public static class QuickMenuOps
{
    /// <summary>Collection that contains <paramref name="item"/> and its owner (null = menu root).</summary>
    public static (ObservableCollection<QuickMenuItem> List, QuickMenuItem? Owner)? FindParent(QuickMenu menu, QuickMenuItem item)
    {
        if (menu.Items.Contains(item)) return (menu.Items, null);
        foreach (var candidate in menu.Descendants())
            if (candidate.Children.Contains(item)) return (candidate.Children, candidate);
        return null;
    }

    public static bool IsAncestorOf(QuickMenuItem ancestor, QuickMenuItem item) =>
        ancestor.Children.Any(c => ReferenceEquals(c, item) || IsAncestorOf(c, item));

    /// <summary>Moves <paramref name="item"/> to <paramref name="target"/> at <paramref name="index"/>. Returns false if not allowed.</summary>
    public static bool Move(QuickMenu menu, QuickMenuItem item, ObservableCollection<QuickMenuItem> target, int index)
    {
        if (WouldCreateCycle(menu, item, target)) return false;
        if (FindParent(menu, item) is not { } source) return false;

        var from = source.List.IndexOf(item);
        if (ReferenceEquals(source.List, target))
        {
            if (index > from) index--;
            index = Math.Clamp(index, 0, target.Count - 1);
            if (index != from) target.Move(from, index);
            return true;
        }
        source.List.RemoveAt(from);
        target.Insert(Math.Clamp(index, 0, target.Count), item);
        return true;
    }

    /// <summary>Item whose <see cref="QuickMenuItem.Children"/> is <paramref name="list"/> (null for the root).</summary>
    public static QuickMenuItem? OwnerOf(QuickMenu menu, ObservableCollection<QuickMenuItem> list) =>
        ReferenceEquals(list, menu.Items) ? null : menu.Descendants().FirstOrDefault(i => ReferenceEquals(i.Children, list));

    /// <summary>True when dropping <paramref name="item"/> into <paramref name="list"/> would put it inside itself.</summary>
    public static bool WouldCreateCycle(QuickMenu menu, QuickMenuItem item, ObservableCollection<QuickMenuItem> list)
    {
        var owner = OwnerOf(menu, list);
        return owner is not null && (ReferenceEquals(owner, item) || IsAncestorOf(item, owner));
    }

    public static QuickMenuItem NewCommandItem(Command command) =>
        new() { Kind = QuickMenuItemKind.Command, CommandId = command.Id };

    public static QuickMenuItem NewSeparator() => new() { Kind = QuickMenuItemKind.Separator };
}

/// <summary>Creates ready-to-use commands from a dropped/typed file, folder or URL ("comando rápido").</summary>
public static class CommandFactory
{
    public static Command? FromTarget(string target, string? name = null)
    {
        target = target.Trim().Trim('"');
        if (target.Length == 0) return null;

        CommandAction action;
        string defaultName;
        if (Directory.Exists(target))
        {
            action = new OpenFolderAction { Path = target };
            defaultName = Path.GetFileName(target.TrimEnd('\\')) is { Length: > 0 } n ? n : target;
        }
        else if (File.Exists(target) && Path.GetExtension(target).Equals(".url", StringComparison.OrdinalIgnoreCase) && ReadUrlFile(target) is { } url)
        {
            action = new OpenUrlAction { Url = url };
            defaultName = Path.GetFileNameWithoutExtension(target);
        }
        else if (File.Exists(target) || target.Contains('\\'))
        {
            action = new RunAppAction { Path = target };
            defaultName = Path.GetFileNameWithoutExtension(target);
        }
        else if (target.Contains("://", StringComparison.Ordinal) || target.StartsWith("www.", StringComparison.OrdinalIgnoreCase) || target.Contains('.'))
        {
            action = new OpenUrlAction { Url = target };
            defaultName = UrlName(target);
        }
        else
        {
            action = new RunAppAction { Path = target };
            defaultName = target;
        }

        var cmd = new Command { Name = string.IsNullOrWhiteSpace(name) ? defaultName : name.Trim(), BlockKey = true };
        cmd.Actions.Add(action);
        return cmd;
    }

    private static string? ReadUrlFile(string file)
    {
        try
        {
            return File.ReadLines(file).FirstOrDefault(l => l.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..].Trim();
        }
        catch { return null; }
    }

    private static string UrlName(string url)
    {
        var s = url.Contains("://", StringComparison.Ordinal) ? url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..] : url;
        s = s.Split('/', '?', '#')[0];
        if (s.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        return s.Length > 0 ? s : url;
    }
}

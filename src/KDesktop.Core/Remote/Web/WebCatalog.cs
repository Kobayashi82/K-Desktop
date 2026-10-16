using System.Collections.Concurrent;
using KDesktop.Core.Model;

namespace KDesktop.Core.Remote.Web;

/// <summary>A command as the web app sees it. <c>IconLight</c>: the icon for light backgrounds when it differs.</summary>
public sealed record WebCommandInfo(string Id, string Name, string? Icon, long Runs, string? IconLight = null);

/// <summary>A QuickMenu item; only commands available on the web (and the containers that keep some) are included.</summary>
public sealed record WebMenuNode(string Id, string Kind, string? Label, string? CommandId, string? Icon, IReadOnlyList<WebMenuNode>? Children, string? IconLight = null);

public sealed record WebMenuInfo(string Id, string Name, IReadOnlyList<WebMenuNode> Items);

public sealed record WebCatalogInfo(string Machine, IReadOnlyList<WebCommandInfo> Commands, IReadOnlyList<WebMenuInfo> Menus);

/// <summary>Builds the catalog (commands + QuickMenus) for the web app and keeps the PNG of every icon it refers to.</summary>
internal sealed class WebCatalog(IWebHost host)
{
    private const int MaxIcons = 4000;
    private readonly ConcurrentDictionary<string, byte[]> _icons = new(StringComparer.OrdinalIgnoreCase);

    public byte[]? IconPng(string hash) => _icons.TryGetValue(hash, out var png) ? png : null;

    /// <summary>Commands the web may show and run.</summary>
    public static bool Available(Command c) => c.Enabled && c.Web;

    public WebCatalogInfo Build()
    {
        var runs = host.Runs();
        return host.Invoke(() =>
        {
            var config = host.Engine.Config;
            var showHidden = config.Settings.ShowHiddenCommands;
            var commands = config.Commands
                .Where(c => Available(c) && (showHidden || !c.Hidden))
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(c => new WebCommandInfo(c.Id, c.Name, Keep(host.Icon(c)), runs.GetValueOrDefault(c.Id), KeepLight(host.Icon(c), host.Icon(c, light: true))))
                .ToList();

            var menus = config.QuickMenus
                .Select(m => new WebMenuInfo(m.Id, m.Name, Nodes(m.Items, config)))
                .Where(m => m.Items.Any(i => i.Kind != "separator"))
                .ToList();
            return new WebCatalogInfo(Environment.MachineName, commands, menus);
        });
    }

    private List<WebMenuNode> Nodes(IEnumerable<QuickMenuItem> items, AppConfig config)
    {
        var nodes = new List<WebMenuNode>();
        foreach (var item in items)
        {
            if (item.Kind == QuickMenuItemKind.Separator)
            {
                // No separators at the start, at the end or two in a row.
                if (nodes.Count > 0 && nodes[^1].Kind != "separator") nodes.Add(new WebMenuNode(item.Id, "separator", null, null, null, null));
                continue;
            }
            var command = item.Kind == QuickMenuItemKind.Command ? config.FindCommand(item.CommandId) : null;
            var runnable = command is not null && Available(command);
            var children = item.Children.Count > 0 ? Nodes(item.Children, config) : null;
            var hasChildren = children?.Any(c => c.Kind != "separator") == true;
            if (!runnable && !hasChildren && item.Kind != QuickMenuItemKind.Title) continue;

            var kind = item.Kind switch
            {
                QuickMenuItemKind.Title => "title",
                QuickMenuItemKind.Command when runnable => "command",
                _ => "submenu",
            };
            var icon = host.Icon(command, item);
            nodes.Add(new WebMenuNode(item.Id, kind, item.Label ?? command?.Name, runnable ? command!.Id : null,
                Keep(icon), hasChildren ? children : null, KeepLight(icon, host.Icon(command, item, light: true))));
        }
        if (nodes.Count > 0 && nodes[^1].Kind == "separator") nodes.RemoveAt(nodes.Count - 1);
        // Titles left without anything under them.
        for (var i = nodes.Count - 1; i >= 0; i--)
            if (nodes[i].Kind == "title" && nodes[i].Children is null && (i == nodes.Count - 1 || nodes[i + 1].Kind is "title" or "separator"))
                nodes.RemoveAt(i);
        return nodes;
    }

    /// <summary>The light-background icon, only when it is not the same picture.</summary>
    private string? KeepLight(WebIcon? normal, WebIcon? light) => light is null || light.Hash == normal?.Hash ? null : Keep(light);

    private string? Keep(WebIcon? icon)
    {
        if (icon is null) return null;
        if (_icons.Count > MaxIcons) _icons.Clear();
        _icons.TryAdd(icon.Hash, icon.Png);
        return icon.Hash;
    }
}

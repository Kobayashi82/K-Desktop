using System.IO.Compression;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Core.Storage;

/// <summary>What can be exported on its own (E6): also layouts (which travel too with the commands that put them) and texts.</summary>
public enum TransferKind { Command, Macro, KBoard, Event, QuickMenu, Arrangement, Snippet }

/// <summary>
/// One item and everything it needs, as it travels in a .kdx: the commands an event runs, the macros, menus, K-Board
/// profiles, layouts and events a command uses, the commands of a menu or of a K-Board profile… and so on, all the way.
/// </summary>
public sealed class TransferBundle
{
    public const int CurrentFormat = 1;

    public int Format { get; set; } = CurrentFormat;
    public TransferKind Kind { get; set; }
    public string RootId { get; set; } = string.Empty;

    /// <summary>The K-Desktop that made it (only informative).</summary>
    public string? Version { get; set; }

    public List<Command> Commands { get; set; } = [];
    public List<Macro> Macros { get; set; } = [];
    public List<KBoardProfile> KBoards { get; set; } = [];
    public List<EventRule> Events { get; set; } = [];
    public List<QuickMenu> QuickMenus { get; set; } = [];
    public List<WindowArrangement> Arrangements { get; set; } = [];
    public List<TextSnippet> Snippets { get; set; } = [];

    /// <summary>The gestures drawn by the user that the commands use as hotkeys.</summary>
    public List<CustomGesture> Gestures { get; set; } = [];
}

/// <summary>
/// Exports one item with what it needs into a .kdx (a zip: <c>item.json</c> plus the own icons and sounds it uses), and
/// imports one without stepping on anything: what is already there and the same is used as it is, what is there but
/// different is asked about (<see cref="ClashChoice"/>), names already taken get a number, and hotkeys already in use
/// are left out.
/// </summary>
public static class ItemTransfer
{
    public const string Extension = ".kdx";
    private const string ItemEntry = "item.json";
    private const string IconsFolder = "Icons/";
    private const string SoundsFolder = "Sounds/";

    /// <summary>What to do with an item that is already here (same one) but different.</summary>
    public enum ClashChoice
    {
        /// <summary>Add the imported one as a copy, with a new id and name.</summary>
        Copy,

        /// <summary>Keep the one here; what the import brings uses it.</summary>
        KeepMine,
    }

    /// <summary>An item of the import that is already here (same id) and is not the same.</summary>
    public sealed record Clash(TransferKind Kind, string Name);

    /// <summary>A .kdx read and checked.</summary>
    public sealed class Incoming
    {
        public required TransferBundle Bundle { get; init; }
        public required string Path { get; init; }

        /// <summary>The name of the item exported.</summary>
        public string Name => Root(Bundle) is { } root ? NameOf(root) : string.Empty;
    }

    /// <summary>What an import added (or used), to show it.</summary>
    public sealed record Result(TransferKind Kind, string RootId, string RootName, int Added, int HotkeysDropped);

    // ── export ─────────────────────────────────────────────────────────────

    /// <summary>The item and everything it uses. Throws when the item is not in <paramref name="config"/>.</summary>
    public static TransferBundle Collect(AppConfig config, TransferKind kind, string id)
    {
        var bundle = new TransferBundle { Kind = kind, RootId = id, Version = AppPaths.FullVersion };
        var seen = new HashSet<(TransferKind, string)>();
        var pending = new Queue<(TransferKind Kind, string Id)>();
        pending.Enqueue((kind, id));
        while (pending.TryDequeue(out var next))
        {
            if (!seen.Add(next) || Existing(config, next.Kind, next.Id) is not { } found) continue;
            var copy = System.Text.Json.JsonSerializer.Deserialize(Json.Serialize(found), found.GetType(), Json.Options)!;
            switch (copy)
            {
                case Command cmd:
                    bundle.Commands.Add(cmd);
                    foreach (var gesture in cmd.Hotkeys.Where(Input.Hotkey.IsGesture).Select(h => h[Input.Hotkey.GesturePrefix.Length..].Trim()))
                        if (config.Settings.CustomGestures.FirstOrDefault(g => g.Name.Equals(gesture, StringComparison.OrdinalIgnoreCase)) is { } custom &&
                            bundle.Gestures.All(g => g.Name != custom.Name))
                            bundle.Gestures.Add(Json.Clone(custom));
                    break;
                case Macro macro: bundle.Macros.Add(macro); break;
                case KBoardProfile profile: bundle.KBoards.Add(profile); break;
                case EventRule rule: bundle.Events.Add(rule); break;
                case QuickMenu menu: bundle.QuickMenus.Add(menu); break;
                case WindowArrangement arrangement: bundle.Arrangements.Add(arrangement); break;
                case TextSnippet snippet: bundle.Snippets.Add(snippet); break;
            }
            foreach (var reference in References(copy)) pending.Enqueue(reference);
        }
        if (Root(bundle) is null) throw new ArgumentException($"No existe {kind} {id}", nameof(id));
        return bundle;
    }

    /// <summary>Writes the .kdx of the item. Throws on a disk error.</summary>
    public static void Export(AppConfig config, TransferKind kind, string id, string path)
    {
        var bundle = Collect(config, kind, id);
        var tmp = path + ".tmp";
        if (File.Exists(tmp)) File.Delete(tmp);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry(ItemEntry, CompressionLevel.Optimal).Open()))
                writer.Write(Json.Serialize(bundle));
            foreach (var icon in Icons(bundle).Select(i => i.Value).Distinct(StringComparer.OrdinalIgnoreCase))
                if (OwnIcon(icon) is { } file) zip.CreateEntryFromFile(file, IconsFolder + icon.Replace('\\', '/'), CompressionLevel.Optimal);
            foreach (var sound in Sounds(bundle).Select(s => s.Value).Distinct(StringComparer.OrdinalIgnoreCase))
                if (OwnSound(sound) is { } file) zip.CreateEntryFromFile(file, SoundsFolder + Path.GetFileName(file), CompressionLevel.Optimal);
        }
        File.Move(tmp, path, overwrite: true);
    }

    // ── import ─────────────────────────────────────────────────────────────

    /// <summary>Opens and checks a .kdx. Throws <see cref="InvalidDataException"/> when it is not one.</summary>
    public static Incoming Read(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.GetEntry(ItemEntry) ?? throw new InvalidDataException(ItemEntry + " missing");
            using var reader = new StreamReader(entry.Open());
            var bundle = Json.Deserialize<TransferBundle>(reader.ReadToEnd()) ?? throw new InvalidDataException(ItemEntry + " empty");
            if (bundle.Format > TransferBundle.CurrentFormat) throw new InvalidDataException($"Formato {bundle.Format}, de un K-Desktop más nuevo");
            if (Root(bundle) is null) throw new InvalidDataException("Sin el elemento exportado");
            return new Incoming { Bundle = bundle, Path = path };
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    /// <summary>What the import brings that is already here but not the same (to ask before importing).</summary>
    public static IReadOnlyList<Clash> Clashes(AppConfig config, TransferBundle bundle) =>
        Items(bundle).Where(i => Existing(config, i.Kind, IdOf(i.Item)) is { } mine && !Same(mine, i.Item))
            .Select(i => new Clash(i.Kind, NameOf(i.Item))).ToList();

    /// <summary>
    /// Adds what <paramref name="incoming"/> brings to <paramref name="config"/>: new ids for the copies (and every
    /// reference between them rewired), free names, its own icons and sounds into the data folder (renamed when one with
    /// that name and other content is there), hotkeys already in use left out.
    /// </summary>
    public static Result Apply(AppConfig config, Incoming incoming, ClashChoice choice)
    {
        // A fresh copy: an import never shares objects with the bundle it came from.
        var bundle = Json.Clone(incoming.Bundle);
        var items = Items(bundle).ToList();
        var mine = items.ToDictionary(i => (i.Kind, IdOf(i.Item)), i => Existing(config, i.Kind, IdOf(i.Item)));
        // What is added: what is not here, and the ones here but different when they are copied; and then, so that the copies
        // are used, whatever uses a copy is copied too (all the way up).
        var copied = new HashSet<(TransferKind, string)>();
        if (choice == ClashChoice.Copy)
        {
            foreach (var (kind, item) in items)
                if (mine[(kind, IdOf(item))] is { } here && !Same(here, item)) copied.Add((kind, IdOf(item)));
            for (var grown = true; grown;)
            {
                grown = false;
                foreach (var (kind, item) in items)
                    if (mine[(kind, IdOf(item))] is not null && !copied.Contains((kind, IdOf(item))) && References(item).Any(copied.Contains))
                        grown = copied.Add((kind, IdOf(item)));
            }
        }
        var ids = new Dictionary<(TransferKind, string), string>();
        var added = new List<(TransferKind Kind, object Item)>();
        foreach (var (kind, item) in items)
        {
            var key = (kind, IdOf(item));
            if (mine[key] is not null && !copied.Contains(key)) continue;
            if (mine[key] is not null)
            {
                var fresh = Ids.New();
                ids[key] = fresh;
                SetId(item, fresh);
            }
            added.Add((kind, item));
        }

        string Map(TransferKind kind, string id) => ids.GetValueOrDefault((kind, id), id);
        foreach (var (_, item) in added) Rewire(item, Map);

        var files = Files(incoming.Path);
        foreach (var (_, item) in added) Settle(item, files);

        // Free names, kind by kind.
        foreach (var (kind, item) in added)
            SetName(item, UniqueName(Names(config, kind).Concat(added.Where(a => a.Kind == kind && a.Item != item).Select(a => NameOf(a.Item))), NameOf(item),
                kind == TransferKind.Snippet ? string.Empty : " "));

        var dropped = 0;
        foreach (var (_, item) in added)
        {
            switch (item)
            {
                case Command cmd:
                    foreach (var hotkey in cmd.Hotkeys.ToList().Where(h => config.HotkeyUsers(h).Count > 0))
                    {
                        cmd.Hotkeys.Remove(hotkey);
                        dropped++;
                    }
                    config.Commands.Add(cmd);
                    break;
                case Macro macro: config.Macros.Add(macro); break;
                case KBoardProfile profile: config.KBoards.Add(profile); break;
                case EventRule rule: config.Events.Add(rule); break;
                case QuickMenu menu: config.QuickMenus.Add(menu); break;
                case WindowArrangement arrangement: config.Arrangements.Add(arrangement); break;
                case TextSnippet snippet: config.Snippets.Add(snippet); break;
            }
        }
        foreach (var gesture in bundle.Gestures)
            if (!config.Settings.CustomGestures.Any(g => g.Name.Equals(gesture.Name, StringComparison.OrdinalIgnoreCase)))
                config.Settings.CustomGestures.Add(gesture);
        config.RefreshReferenceNames();

        var root = Map(bundle.Kind, bundle.RootId);
        Log.Info($"Importado «{incoming.Name}» ({bundle.Kind}) de {Path.GetFileName(incoming.Path)}: {added.Count} cosas nuevas, {dropped} atajos ya usados quitados");
        return new Result(bundle.Kind, root, Existing(config, bundle.Kind, root) is { } kept ? NameOf(kept) : incoming.Name, added.Count, dropped);
    }

    // ── pieces ─────────────────────────────────────────────────────────────

    private static object? Root(TransferBundle bundle) => bundle.Kind switch
    {
        TransferKind.Command => bundle.Commands.FirstOrDefault(c => c.Id == bundle.RootId),
        TransferKind.Macro => bundle.Macros.FirstOrDefault(m => m.Id == bundle.RootId),
        TransferKind.KBoard => bundle.KBoards.FirstOrDefault(k => k.Id == bundle.RootId),
        TransferKind.Event => bundle.Events.FirstOrDefault(e => e.Id == bundle.RootId),
        TransferKind.QuickMenu => bundle.QuickMenus.FirstOrDefault(q => q.Id == bundle.RootId),
        TransferKind.Arrangement => bundle.Arrangements.FirstOrDefault(a => a.Id == bundle.RootId),
        TransferKind.Snippet => bundle.Snippets.FirstOrDefault(s => s.Id == bundle.RootId),
        _ => null,
    };

    /// <summary>Every item of the bundle, with its kind.</summary>
    private static IEnumerable<(TransferKind Kind, object Item)> Items(TransferBundle bundle) =>
        bundle.Commands.Select(c => (TransferKind.Command, (object)c))
            .Concat(bundle.Macros.Select(m => (TransferKind.Macro, (object)m)))
            .Concat(bundle.KBoards.Select(k => (TransferKind.KBoard, (object)k)))
            .Concat(bundle.Events.Select(e => (TransferKind.Event, (object)e)))
            .Concat(bundle.QuickMenus.Select(q => (TransferKind.QuickMenu, (object)q)))
            .Concat(bundle.Arrangements.Select(a => (TransferKind.Arrangement, (object)a)))
            .Concat(bundle.Snippets.Select(s => (TransferKind.Snippet, (object)s)));

    private static object? Existing(AppConfig config, TransferKind kind, string itemId) => (kind, itemId) switch
    {
        (TransferKind.Command, var id) => config.Commands.FirstOrDefault(c => c.Id == id),
        (TransferKind.Macro, var id) => config.Macros.FirstOrDefault(m => m.Id == id),
        (TransferKind.KBoard, var id) => config.KBoards.FirstOrDefault(k => k.Id == id),
        (TransferKind.Event, var id) => config.Events.FirstOrDefault(e => e.Id == id),
        (TransferKind.QuickMenu, var id) => config.QuickMenus.FirstOrDefault(q => q.Id == id),
        (TransferKind.Arrangement, var id) => config.Arrangements.FirstOrDefault(a => a.Id == id),
        (TransferKind.Snippet, var id) => config.Snippets.FirstOrDefault(s => s.Id == id),
        _ => null,
    };

    private static IEnumerable<string> Names(AppConfig config, TransferKind kind) => kind switch
    {
        TransferKind.Command => config.Commands.Select(c => c.Name),
        TransferKind.Macro => config.Macros.Select(m => m.Name),
        TransferKind.KBoard => config.KBoards.Select(k => k.Name),
        TransferKind.Event => config.Events.Select(e => e.Name),
        TransferKind.QuickMenu => config.QuickMenus.Select(q => q.Name),
        TransferKind.Snippet => config.Snippets.Select(s => s.Abbreviation),
        _ => config.Arrangements.Select(a => a.Name),
    };

    /// <summary>The same as here, ignoring what only says when it was made.</summary>
    private static bool Same(object mine, object theirs) => Comparable(mine) == Comparable(theirs);

    private static string Comparable(object item)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(Json.Serialize(item))!.AsObject();
        node.Remove("created");
        return node.ToJsonString();
    }

    /// <summary><paramref name="name"/>, or with a number after it if another one has it already (as «Duplicar»; an abbreviation, with no space).</summary>
    public static string UniqueName(IEnumerable<string> taken, string name, string separator = " ")
    {
        var names = taken.Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var free = name;
        for (var i = 2; names.Contains(free); i++) free = name + separator + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return free;
    }

    private static string IdOf(object item) => item switch
    {
        Command c => c.Id,
        Macro m => m.Id,
        KBoardProfile k => k.Id,
        EventRule e => e.Id,
        QuickMenu q => q.Id,
        WindowArrangement a => a.Id,
        TextSnippet s => s.Id,
        _ => string.Empty,
    };

    private static void SetId(object item, string id)
    {
        switch (item)
        {
            case Command c: c.Id = id; break;
            case Macro m: m.Id = id; break;
            case KBoardProfile k: k.Id = id; break;
            case EventRule e: e.Id = id; break;
            case QuickMenu q: q.Id = id; break;
            case WindowArrangement a: a.Id = id; break;
            case TextSnippet s: s.Id = id; break;
        }
    }

    private static string NameOf(object item) => item switch
    {
        Command c => c.Name,
        Macro m => m.Name,
        KBoardProfile k => k.Name,
        EventRule e => e.Name,
        QuickMenu q => q.Name,
        WindowArrangement a => a.Name,
        TextSnippet s => s.Abbreviation,
        _ => string.Empty,
    };

    private static void SetName(object item, string name)
    {
        switch (item)
        {
            case Command c: c.Name = name; break;
            case Macro m: m.Name = name; break;
            case KBoardProfile k: k.Name = name; break;
            case EventRule e: e.Name = name; break;
            case QuickMenu q: q.Name = name; break;
            case WindowArrangement a: a.Name = name; break;
            case TextSnippet s: s.Abbreviation = name; break;
        }
    }

    /// <summary>What an item uses: the commands, macros, menus, profiles, events and layouts it points to.</summary>
    private static IEnumerable<(TransferKind Kind, string Id)> References(object item)
    {
        IEnumerable<(TransferKind, string)> Raw()
        {
            switch (item)
            {
                case Command cmd:
                    foreach (var action in cmd.Actions)
                        switch (action)
                        {
                            case OpenQuickMenuAction q: yield return (TransferKind.QuickMenu, q.MenuId); break;
                            case CommandControlAction { Target: CommandControlTarget.Event } c: yield return (TransferKind.Event, c.EventId); break;
                            case CommandControlAction c: yield return (TransferKind.Command, c.CommandId); break;
                            case ConditionAction { Else: ConditionElse.RunCommand } i: yield return (TransferKind.Command, i.ElseCommandId); break;
                            case PlayMacroAction p: yield return (TransferKind.Macro, p.MacroId); break;
                            case KBoardAction k: yield return (TransferKind.KBoard, k.ProfileId); break;
                            case ArrangementAction a: yield return (TransferKind.Arrangement, a.ArrangementId); break;
                        }
                    break;
                case KBoardProfile profile:
                    foreach (var remap in profile.Remaps.Where(r => r.Kind == RemapKind.Command)) yield return (TransferKind.Command, remap.CommandId ?? string.Empty);
                    break;
                case EventRule rule:
                    foreach (var c in rule.Commands.Concat(rule.EndCommands).Concat(rule.Ask?.Commands ?? [])) yield return (TransferKind.Command, c);
                    break;
                case QuickMenu menu:
                    foreach (var entry in menu.Descendants().Where(i => i.Kind == QuickMenuItemKind.Command)) yield return (TransferKind.Command, entry.CommandId ?? string.Empty);
                    break;
            }
        }
        return Raw().Where(r => r.Item2.Length > 0);
    }

    /// <summary>Points the references of an added item to the ids the import gave (the copies').</summary>
    private static void Rewire(object item, Func<TransferKind, string, string> map)
    {
        static void List(IList<string> ids, Func<string, string> to)
        {
            for (var i = 0; i < ids.Count; i++) ids[i] = to(ids[i]);
        }

        string Cmd(string id) => map(TransferKind.Command, id);
        switch (item)
        {
            case Command cmd:
                foreach (var action in cmd.Actions)
                    switch (action)
                    {
                        case OpenQuickMenuAction q: q.MenuId = map(TransferKind.QuickMenu, q.MenuId); break;
                        case CommandControlAction c:
                            c.CommandId = Cmd(c.CommandId);
                            c.EventId = map(TransferKind.Event, c.EventId);
                            break;
                        case ConditionAction i: i.ElseCommandId = Cmd(i.ElseCommandId); break;
                        case PlayMacroAction p: p.MacroId = map(TransferKind.Macro, p.MacroId); break;
                        case KBoardAction k: k.ProfileId = map(TransferKind.KBoard, k.ProfileId); break;
                        case ArrangementAction a: a.ArrangementId = map(TransferKind.Arrangement, a.ArrangementId); break;
                    }
                break;
            case KBoardProfile profile:
                foreach (var remap in profile.Remaps.Where(r => r.Kind == RemapKind.Command && r.CommandId is not null)) remap.CommandId = Cmd(remap.CommandId!);
                break;
            case EventRule rule:
                List(rule.Commands, Cmd);
                List(rule.EndCommands, Cmd);
                List(rule.DisabledCommands, Cmd);
                List(rule.DisabledEndCommands, Cmd);
                if (rule.Ask?.Commands is { } asked) rule.Ask.Commands = [.. asked.Select(Cmd)];
                break;
            case QuickMenu menu:
                foreach (var entry in menu.Descendants().Where(i => i.CommandId is not null)) entry.CommandId = Cmd(entry.CommandId!);
                break;
        }
    }

    // ── own icons and sounds ───────────────────────────────────────────────

    /// <summary>An icon reference and how to change it.</summary>
    private sealed record Reference(string Value, Action<string> Set);

    private static IEnumerable<Reference> Icons(object item)
    {
        switch (item)
        {
            case TransferBundle bundle:
                foreach (var r in Items(bundle).SelectMany(i => Icons(i.Item))) yield return r;
                break;
            case Command cmd:
                if (cmd.Icon is { } icon) yield return new(icon, v => cmd.Icon = v);
                foreach (var notify in cmd.Actions.OfType<ShowNotificationAction>())
                    if (notify.Icon is { } n) yield return new(n, v => notify.Icon = v);
                break;
            case EventRule rule when rule.Icon is { } eventIcon:
                yield return new(eventIcon, v => rule.Icon = v);
                break;
            case QuickMenu menu:
                foreach (var entry in menu.Descendants())
                    if (entry.Icon is { } i) yield return new(i, v => entry.Icon = v);
                break;
        }
    }

    private static IEnumerable<Reference> Sounds(object item)
    {
        switch (item)
        {
            case TransferBundle bundle:
                foreach (var r in Items(bundle).SelectMany(i => Sounds(i.Item))) yield return r;
                break;
            case Command cmd:
                if (cmd.Sound is { } sound) yield return new(sound, v => cmd.Sound = v);
                foreach (var action in cmd.Actions)
                    switch (action)
                    {
                        case PlaySoundAction { Sound.Length: > 0 } play: yield return new(play.Sound, v => play.Sound = v); break;
                        case AudioOutputAction { Sound: { } s } output: yield return new(s, v => output.Sound = v); break;
                        case ShowNotificationAction { Sound: { } s } notify: yield return new(s, v => notify.Sound = v); break;
                    }
                break;
        }
    }

    /// <summary>The file of an icon of the user's Icons folder (not a built-in one, nor a path elsewhere).</summary>
    private static string? OwnIcon(string icon)
    {
        if (Path.IsPathRooted(icon) || icon.Contains("..", StringComparison.Ordinal) || icon.Contains(':', StringComparison.Ordinal)) return null;
        var path = Path.Combine(AppPaths.IconsDir, icon);
        return File.Exists(path) ? path : null;
    }

    /// <summary>The file of a sound of the user's Sounds folder (a name without extension).</summary>
    private static string? OwnSound(string sound)
    {
        if (Path.IsPathRooted(sound) || sound.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        return SoundLibrary.Extensions.Select(e => Path.Combine(AppPaths.SoundsDir, sound + e)).FirstOrDefault(File.Exists);
    }

    /// <summary>The icons and sounds inside a .kdx: entry name → bytes.</summary>
    private static Dictionary<string, byte[]> Files(string path)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var zip = ZipFile.OpenRead(path);
        foreach (var entry in zip.Entries.Where(e => e.Name.Length > 0 && (e.FullName.StartsWith(IconsFolder, StringComparison.OrdinalIgnoreCase) ||
                                                                            e.FullName.StartsWith(SoundsFolder, StringComparison.OrdinalIgnoreCase))))
        {
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            files[entry.FullName.Replace('\\', '/')] = memory.ToArray();
        }
        return files;
    }

    /// <summary>The own icons and sounds of an added item, into the data folder (another name if one there is different).</summary>
    private static void Settle(object item, Dictionary<string, byte[]> files)
    {
        foreach (var icon in Icons(item))
        {
            if (Path.IsPathRooted(icon.Value) || icon.Value.Contains("..", StringComparison.Ordinal)) continue;
            if (!files.TryGetValue(IconsFolder + icon.Value.Replace('\\', '/'), out var bytes)) continue;
            var name = Place(AppPaths.IconsDir, icon.Value, bytes);
            if (name != icon.Value) icon.Set(name);
        }
        foreach (var sound in Sounds(item))
        {
            if (sound.Value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
            var entry = files.Keys.FirstOrDefault(k => k.StartsWith(SoundsFolder, StringComparison.OrdinalIgnoreCase) &&
                                                       Path.GetFileNameWithoutExtension(k).Equals(sound.Value, StringComparison.OrdinalIgnoreCase));
            if (entry is null) continue;
            var placed = Place(AppPaths.SoundsDir, Path.GetFileName(entry), files[entry]);
            var name = Path.GetFileNameWithoutExtension(placed);
            if (name != sound.Value) sound.Set(name);
        }
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> as <paramref name="name"/> in <paramref name="folder"/>, or finds it there already;
    /// when that name holds something else, «name 2.ext»… Returns the name used.
    /// </summary>
    private static string Place(string folder, string name, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(name) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        for (var i = 1; ; i++)
        {
            var candidate = i == 1 ? name : Path.Combine(dir, $"{stem} {i.ToString(System.Globalization.CultureInfo.InvariantCulture)}{ext}");
            var path = Path.GetFullPath(Path.Combine(folder, candidate));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return name;
            if (File.Exists(path))
            {
                if (File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return candidate;
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return candidate;
        }
    }
}

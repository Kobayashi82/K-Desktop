using KDesktop.Core.Localization;

namespace KDesktop.Services;

/// <summary>
/// "Did you know…" tips: shortcuts of Windows, the File Explorer, browsers, text editing and K-Desktop itself.
/// Texts live in the language files: <c>tip.{id}</c> (what it does) and <c>tip.{id}.keys</c> (the keys; alternatives
/// separated by " | ").
/// </summary>
public static class Tips
{
    public sealed record Tip(string Id, string Category)
    {
        public string Text => Loc.T($"tip.{Id}");

        /// <summary>Alternative shortcuts, each one as "Ctrl + Shift + T".</summary>
        public IReadOnlyList<string> Keys => Loc.T($"tip.{Id}.keys").Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static IReadOnlyList<string> Categories { get; } = ["windows", "explorer", "browser", "text", "kdesktop"];

    /// <summary>K-Desktop icon of each category.</summary>
    public static KdIconSpec Icon(string category) => category switch
    {
        "windows" => new("Window24", "blue"),
        "explorer" => new("Folder24", "yellow"),
        "browser" => new("Globe24", "sky"),
        "text" => new("TextFont24", "orange"),
        _ => new("Flash24", "gold"),
    };

    public static IReadOnlyList<Tip> All { get; } = Build();

    private static List<Tip> Build()
    {
        var list = new List<Tip>();
        void Add(string category, string prefix, params string[] ids)
        {
            foreach (var id in ids) list.Add(new Tip($"{prefix}.{id}", category));
        }

        Add("windows", "w",
            "desktop", "explorer", "settings", "lock", "run", "search", "quicklink", "clipboard", "emoji", "snip", "printscreen",
            "taskview", "newdesktop", "switchdesktop", "closedesktop", "snaplayouts", "maximize", "minimize", "snapside", "monitor",
            "stretch", "minall", "restoreall", "shake", "taskbarN", "taskbarNew", "taskbarAdmin", "taskbarCycle", "tray",
            "notifications", "quicksettings", "widgets", "project", "cast", "gamebar", "record", "dictation", "language",
            "magnifier", "narrator", "peek", "taskmgr", "secure", "alttab", "altf4", "graphics", "hdr", "mute");
        Add("explorer", "e",
            "address", "search", "newwindow", "newtab", "closetab", "switchtab", "newfolder", "rename", "properties", "preview",
            "details", "up", "back", "copypath", "permanent", "views", "zoom", "morecontext", "expand", "refresh", "fullscreen",
            "taskbarShift", "taskbarAdmin", "taskbarMiddle", "taskbarCtrl");
        Add("browser", "b",
            "reopen", "newtab", "close", "newwindow", "private", "tabnum", "lasttab", "nexttab", "prevtab", "address",
            "addressnewtab", "dotcom", "find", "refresh", "hardrefresh", "back", "home", "bookmark", "bookmarksbar", "history",
            "downloads", "clear", "devtools", "source", "print", "save", "zoom", "zoomreset", "fullscreen", "space", "topbottom",
            "middleclick", "ctrlclick", "ctrlshiftclick", "shiftclick", "alttext", "taskmgr", "pasteplain", "escape");
        Add("text", "t",
            "word", "selword", "delword", "line", "selline", "doc", "undo", "clipboard", "insert", "clicks", "paragraph", "altcode");
        Add("kdesktop", "k",
            "qmleft", "qmright", "qmmiddle", "qmkeys", "qmtoggle", "esc", "shiftesc", "show", "chord", "kboard", "disabledin");
        return list;
    }
}

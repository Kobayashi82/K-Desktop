using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using KDesktop.Core;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Native;
using KDesktop.Services;
using MouseButton = System.Windows.Input.MouseButton;

namespace KDesktop.QuickMenus;

/// <summary>
/// Shows QuickMenus: cascading windows, hover/keyboard navigation and execution.
/// Left click runs and closes, right click runs and keeps the menu open, middle click edits.
/// </summary>
public sealed class QuickMenuController
{
    private readonly Func<AppConfig> _config;
    private readonly CommandEngine _engine;
    private readonly List<QuickMenuWindow> _levels = [];
    private readonly Stack<QuickMenuWindow> _pool = new();
    private readonly DispatcherTimer _hoverTimer = new();
    private QuickMenuWindow? _root;
    private (QuickMenuWindow Window, QuickMenuEntry Entry)? _pendingHover;
    private ForegroundWindow _previous = ForegroundWindow.None;
    private QuickMenu? _menu;
    private bool _closing;

    /// <summary>
    /// After a right click (run and keep the menu open), what was started may take the focus: until then the menu
    /// takes it back instead of closing.
    /// </summary>
    private DateTime _keepOpenUntil;

    private static readonly TimeSpan KeepOpenAfterRun = TimeSpan.FromSeconds(4);

    /// <summary>(commandId, menuId) — commandId is null to edit the whole menu.</summary>
    public event Action<string?, string?>? EditRequested;

    /// <summary>Raised when a menu is opened (statistics).</summary>
    public event Action? Opened;

    public bool IsOpen => _levels.Count > 0;

    public QuickMenuController(Func<AppConfig> config, CommandEngine engine)
    {
        _config = config;
        _engine = engine;
        _hoverTimer.Tick += OnHoverTimer;
        ThemeService.Changed += () =>
        {
            _root?.ApplyTheme();
            foreach (var w in _pool.Concat(_levels)) w.ApplyTheme();
        };
    }

    /// <summary>Creates and renders the root window once so the first real opening is instant.</summary>
    public void Prewarm()
    {
        _root = CreateWindow(isSubmenu: false);
        _root.Left = -32000;
        _root.Top = -32000;
        _root.Show();
        _root.HideNow();
        var sub = CreateWindow(isSubmenu: true);
        sub.Left = -32000;
        sub.Top = -32000;
        sub.Show();
        sub.HideNow();
        _pool.Push(sub);
    }

    public void Show(string menuIdOrName, ForegroundWindow previous)
    {
        var config = _config();
        var menu = config.FindMenu(menuIdOrName);
        if (menu is null)
        {
            Log.Warn($"QuickMenu no encontrado: {menuIdOrName}");
            return;
        }

        // Its hotkey again: over the open menu, it closes; anywhere else, it opens again where the mouse is. Either way
        // the window to go back to is still the one it was opened over (the menu itself is in front now).
        if (IsOpen)
        {
            if (ReferenceEquals(menu, _menu) && PointerOverMenu())
            {
                CloseAll(restoreFocus: true);
                return;
            }
            previous = _previous;
            CloseAll(restoreFocus: false);
        }

        _previous = previous;
        _menu = menu;
        _root ??= CreateWindow(isSubmenu: false);
        var root = _root;
        root.Level = 0;
        root.ParentEntry = null;
        root.ApplySize(menu.Size);
        root.ApplyTransparency(menu.Transparency);
        root.SetEntries(Build(menu.Items, config));
        _levels.Add(root);

        var cursor = UiNative.CursorPosition();
        Place(root, (w, h, work) =>
        {
            var x = cursor.X + w > work.Right ? cursor.X - w : cursor.X;
            var y = cursor.Y + h > work.Bottom ? cursor.Y - h : cursor.Y;
            return (Math.Max(work.Left, x), Math.Max(work.Top, y));
        }, cursor);

        root.Activate();
        var focused = WindowTools.ForceForeground(root.Hwnd);
        Keyboard.Focus(root);
        Log.Info($"QuickMenu «{menu.Name}» abierto (sobre {previous.ProcessName}{(focused ? "" : "; no consiguió el foco")})");
        Opened?.Invoke();
    }

    /// <summary>Whether the mouse is on one of the open levels.</summary>
    private bool PointerOverMenu()
    {
        var p = UiNative.CursorPosition();
        return _levels.Any(l => UiNative.GetWindowRect(l.Hwnd, out var r) && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom);
    }

    public void CloseAll(bool restoreFocus = true)
    {
        if (!IsOpen || _closing) return;
        _closing = true;
        _keepOpenUntil = default;
        try
        {
            _hoverTimer.Stop();
            _pendingHover = null;
            for (var i = _levels.Count - 1; i >= 0; i--) Release(_levels[i]);
            _levels.Clear();
            if (restoreFocus && _previous.Handle != IntPtr.Zero) WindowTools.ForceForeground(_previous.Handle);
        }
        finally
        {
            _closing = false;
        }
    }

    // ── building ─────────────────────────────────────────────────────────

    private static List<QuickMenuEntry> Build(IEnumerable<QuickMenuItem> items, AppConfig config)
    {
        var list = new List<QuickMenuEntry>();
        foreach (var item in items)
        {
            // Hide commands marked as hidden only if they are also disabled; skip doubled separators.
            if (item.Kind == QuickMenuItemKind.Separator && (list.Count == 0 || list[^1].IsSeparator)) continue;
            list.Add(new QuickMenuEntry(item, config));
        }
        while (list.Count > 0 && list[^1].IsSeparator) list.RemoveAt(list.Count - 1);
        return list;
    }

    private QuickMenuWindow CreateWindow(bool isSubmenu)
    {
        var w = new QuickMenuWindow(isSubmenu);
        w.EntryHovered += OnEntryHovered;
        w.EntryClicked += OnEntryClicked;
        w.PointerEntered += OnPointerEntered;
        if (!isSubmenu)
        {
            w.PreviewKeyDown += OnKeyDown;
            w.Deactivated += (_, _) =>
            {
                if (!IsOpen || _closing) return;
                // The focus went to one of its own submenus: the menu is still in use.
                var foreground = ForegroundWindow.Capture();
                if (_levels.Any(l => l.Hwnd == foreground.Handle)) return;
                if (DateTime.UtcNow < _keepOpenUntil)
                {
                    TakeFocusBack(w);
                    return;
                }
                Log.Info($"QuickMenu: se cierra al perder el foco (activa: {foreground.ProcessName})");
                CloseAll(restoreFocus: false);
            };
        }
        return w;
    }

    /// <summary>A program started with a right click took the focus: the menu stays open and takes it back.</summary>
    private void TakeFocusBack(QuickMenuWindow root)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!IsOpen || !ReferenceEquals(root, _root)) return;
            root.Activate();
            WindowTools.ForceForeground(root.Hwnd);
            Keyboard.Focus(root);
        };
        timer.Start();
    }

    private QuickMenuWindow RentSubmenu() => _pool.Count > 0 ? _pool.Pop() : CreateWindow(isSubmenu: true);

    private void Release(QuickMenuWindow w)
    {
        if (w.ParentEntry is { } parent) parent.IsExpanded = false;
        foreach (var e in w.Entries) e.IsHighlighted = false;
        w.ParentEntry = null;
        if (w.IsSubmenu)
        {
            w.HideAnimated();
            _pool.Push(w);
        }
        else
        {
            w.HideNow();
        }
    }

    /// <summary>Shows a window off-screen, measures it and moves it to its final position (physical pixels).</summary>
    private static void Place(QuickMenuWindow w, Func<int, int, UiNative.RECT, (int X, int Y)> position, UiNative.POINT reference)
    {
        var work = UiNative.WorkAreaAt(reference);
        if (!w.IsVisible)
        {
            w.Left = -32000;
            w.Top = -32000;
            w.Show();
        }
        var scale = UiNative.GetDpiForWindow(w.Hwnd) / 96.0;
        w.SetMaxHeight(work.Height / scale - 24);
        w.UpdateLayout();
        if (!UiNative.GetWindowRect(w.Hwnd, out var r)) return;
        var (x, y) = position(r.Width, r.Height, work);
        UiNative.MoveWindow(w.Hwnd, x, y);
        w.PlayShow();
    }

    private void OpenSubmenu(QuickMenuWindow parent, QuickMenuEntry entry, bool highlightFirst)
    {
        CloseFrom(parent.Level + 1);
        if (!entry.HasChildren || parent.EntryScreenRect(entry) is not { } row) return;

        var sub = RentSubmenu();
        sub.Level = parent.Level + 1;
        sub.ParentEntry = entry;
        sub.ApplySize(_menu?.Size ?? 1);
        sub.ApplyTransparency(_menu?.Transparency ?? Core.Model.QuickMenu.DefaultTransparency);
        sub.SetEntries(Build(entry.Children, _config()));
        entry.IsExpanded = true;
        _levels.Add(sub);

        UiNative.GetWindowRect(parent.Hwnd, out var p);
        var anchor = new UiNative.POINT { X = (int)row.Right, Y = (int)row.Top };
        Place(sub, (w, h, work) =>
        {
            var overlap = 2;
            var x = p.Right - overlap;
            if (x + w > work.Right) x = p.Left - w + overlap;
            var y = (int)row.Top - 6;
            if (y + h > work.Bottom) y = work.Bottom - h;
            return (Math.Max(work.Left, x), Math.Max(work.Top, y));
        }, anchor);

        if (highlightFirst && sub.Entries.FirstOrDefault(e => e.IsSelectable) is { } first) Highlight(sub, first);
    }

    private void CloseFrom(int level)
    {
        for (var i = _levels.Count - 1; i >= level && i > 0; i--)
        {
            Release(_levels[i]);
            _levels.RemoveAt(i);
        }
    }

    // ── mouse ────────────────────────────────────────────────────────────

    private void OnEntryHovered(QuickMenuWindow window, QuickMenuEntry entry)
    {
        if (entry.IsSeparator) return;
        var current = window.Entries.FirstOrDefault(e => e.IsHighlighted);
        if (ReferenceEquals(current, entry) && _pendingHover is null) return;

        Highlight(window, entry);
        var childOpen = _levels.Count > window.Level + 1 && ReferenceEquals(_levels[window.Level + 1].ParentEntry, entry);
        if (childOpen)
        {
            _hoverTimer.Stop();
            _pendingHover = null;
            return;
        }
        _pendingHover = (window, entry);
        var submenuOpen = _levels.Count > window.Level + 1;
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(submenuOpen ? 240 : entry.HasChildren ? 110 : 60);
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    private void OnHoverTimer(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        if (_pendingHover is not { } pending) return;
        var (window, entry) = pending;
        _pendingHover = null;
        if (!window.IsVisible || !_levels.Contains(window)) return;
        if (entry.HasChildren) OpenSubmenu(window, entry, highlightFirst: false);
        else CloseFrom(window.Level + 1);
    }

    private void OnPointerEntered(QuickMenuWindow window)
    {
        // Reached the submenu that was opening/open: keep it.
        if (_pendingHover is { } p && p.Window.Level < window.Level)
        {
            _hoverTimer.Stop();
            _pendingHover = null;
            if (window.ParentEntry is { } parentEntry) Highlight(p.Window, parentEntry);
        }
    }

    private void OnEntryClicked(QuickMenuWindow window, QuickMenuEntry entry, MouseButton button)
    {
        if (entry.IsSeparator) return;
        Log.Info($"QuickMenu: clic {button} en «{entry.Label}» (nivel {window.Level}{(entry.IsRunnable ? "" : ", no ejecutable")})");
        switch (button)
        {
            case MouseButton.Left:
                if (entry.IsRunnable) Execute(entry, close: true);
                else if (entry.HasChildren)
                {
                    _hoverTimer.Stop();
                    _pendingHover = null;
                    OpenSubmenu(window, entry, highlightFirst: false);
                }
                break;
            case MouseButton.Right:
                if (entry.IsRunnable) Execute(entry, close: false);
                break;
            case MouseButton.Middle:
                var wholeMenu = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || entry.Command is null;
                var menuId = _menu?.Id;
                CloseAll(restoreFocus: false);
                EditRequested?.Invoke(wholeMenu ? null : entry.Command!.Id, menuId);
                break;
        }
    }

    // ── keyboard ─────────────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsOpen) return;
        e.Handled = true;
        var level = _levels[^1];
        // If the deepest level was opened by hover and has no selection yet, keyboard acts on its parent.
        if (level.Level > 0 && level.Entries.All(x => !x.IsHighlighted) && e.Key is not (Key.Down or Key.Up or Key.Right))
            level = _levels[^2];
        var current = level.Entries.FirstOrDefault(x => x.IsHighlighted);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        switch (key)
        {
            case Key.Down: Move(level, current, +1); break;
            case Key.Up: Move(level, current, -1); break;
            case Key.Home: Move(level, null, +1); break;
            case Key.End: Move(level, null, -1); break;
            case Key.Right:
                if (current is { HasChildren: true }) OpenSubmenu(level, current, highlightFirst: true);
                break;
            case Key.Left:
                if (level.Level > 0) CloseFrom(level.Level);
                break;
            case Key.Escape:
                if (_levels.Count > 1) CloseFrom(_levels.Count - 1);
                else CloseAll();
                break;
            case Key.Enter:
            case Key.Space:
                if (current is null) break;
                if (current.IsRunnable) Execute(current, close: !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                else if (current.HasChildren) OpenSubmenu(level, current, highlightFirst: true);
                break;
            default:
                var ch = KeyToChar(key);
                if (ch is null) { e.Handled = false; break; }
                JumpTo(level, current, ch.Value);
                break;
        }
    }

    private void Move(QuickMenuWindow level, QuickMenuEntry? current, int delta)
    {
        var items = level.Entries.Where(x => x.IsSelectable || x.IsTitle && x.HasChildren).ToList();
        if (items.Count == 0) return;
        var index = current is null ? (delta > 0 ? -1 : items.Count) : items.IndexOf(current);
        var next = items[((index + delta) % items.Count + items.Count) % items.Count];
        Highlight(level, next);
        CloseFrom(level.Level + 1);
    }

    private void JumpTo(QuickMenuWindow level, QuickMenuEntry? current, char ch)
    {
        var items = level.Entries.Where(x => x.IsSelectable).ToList();
        if (items.Count == 0) return;
        var start = current is null ? 0 : items.IndexOf(current) + 1;
        for (var i = 0; i < items.Count; i++)
        {
            var candidate = items[(start + i) % items.Count];
            if (candidate.Label.StartsWith(ch.ToString(), StringComparison.CurrentCultureIgnoreCase))
            {
                Highlight(level, candidate);
                CloseFrom(level.Level + 1);
                return;
            }
        }
    }

    private static char? KeyToChar(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (char)('a' + (key - Key.A)),
        >= Key.D0 and <= Key.D9 => (char)('0' + (key - Key.D0)),
        >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (key - Key.NumPad0)),
        _ => null,
    };

    private static void Highlight(QuickMenuWindow window, QuickMenuEntry entry)
    {
        foreach (var e in window.Entries) e.IsHighlighted = ReferenceEquals(e, entry);
        window.BringIntoView(entry);
    }

    // ── execution ────────────────────────────────────────────────────────

    private void Execute(QuickMenuEntry entry, bool close)
    {
        if (entry.Command is not { } command) return;
        Log.Info($"QuickMenu → {command.Name}");
        var previous = _previous;
        if (close)
        {
            CloseAll(restoreFocus: true);
            _ = Task.Run(async () =>
            {
                await Task.Delay(30);
                await _engine.RunAsync(command, TriggerSource.QuickMenu, previous);
            });
        }
        else
        {
            _keepOpenUntil = DateTime.UtcNow + KeepOpenAfterRun;
            entry.IsFlashing = true;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            timer.Tick += (_, _) => { timer.Stop(); entry.IsFlashing = false; };
            timer.Start();
            _ = Task.Run(() => _engine.RunAsync(command, TriggerSource.QuickMenu, previous));
        }
    }
}

using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KDesktop.Core;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Native;
using KDesktop.Services;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace KDesktop.Launcher;

/// <summary>
/// The clipboard history (Settings › Portapapeles): watches what is copied and keeps it in <see cref="ClipboardHistory"/>,
/// and shows it next to the mouse, as the launcher, to paste it again (as it was, without its format, or changed like
/// «Portapapeles» does). Nothing is kept from the programs excluded, from what a program marks as private (password
/// managers, K-Desktop's own pastes), while paused, or with K-Desktop paused.
/// </summary>
public sealed class ClipboardController : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const int MaxResults = 60;
    private const int ThumbSize = 56;

    private readonly Func<AppConfig> _config;
    private readonly ClipboardHistory _history = new(ClipboardHistory.DefaultFolder);
    private HwndSource? _listener;
    private readonly DispatcherTimer _read = new() { Interval = TimeSpan.FromMilliseconds(80) };

    private ClipboardWindow? _window;
    private ForegroundWindow _previous = ForegroundWindow.None;
    private List<ClipboardItem> _items = [];
    private int _selected = -1;
    private bool _menuOpen;

    public ClipboardController(Func<AppConfig> config)
    {
        _config = config;
        ThemeService.Changed += () => _window?.ApplyTheme();
        _read.Tick += (_, _) =>
        {
            _read.Stop();
            Capture();
        };
        _history.Changed += () => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (IsOpen) Search();
        });
    }

    public ClipboardHistory History => _history;

    public bool IsOpen => _window?.IsVisible == true;

    private AppSettings Settings => _config().Settings;

    /// <summary>Reads the history and starts listening to the clipboard (UI thread).</summary>
    public void Start()
    {
        ApplyLimits();
        _history.Load();
        var parameters = new HwndSourceParameters("K-Desktop clipboard") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */, Width = 0, Height = 0 };
        _listener = new HwndSource(parameters);
        _listener.AddHook(WndProc);
        if (!AddClipboardFormatListener(_listener.Handle)) Log.Warn($"Historial del portapapeles: no se pudo escuchar el portapapeles (error {Marshal.GetLastWin32Error()})");
    }

    public void Dispose()
    {
        if (_listener is null) return;
        RemoveClipboardFormatListener(_listener.Handle);
        _listener.Dispose();
        _listener = null;
    }

    /// <summary>The limits of Settings › Portapapeles (call after they change).</summary>
    public void ApplyLimits()
    {
        _history.MaxItems = Math.Clamp(Settings.ClipboardMaxItems, 10, 5000);
        _history.MaxDays = Math.Max(0, Settings.ClipboardMaxDays);
        _history.Trim();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // A program may still be writing other formats: read a moment later, once.
        if (msg == WM_CLIPBOARDUPDATE)
        {
            _read.Stop();
            _read.Start();
        }
        return IntPtr.Zero;
    }

    // ── capture ──────────────────────────────────────────────────────────

    private void Capture()
    {
        var s = Settings;
        if (!s.ClipboardHistory || s.ClipboardPaused || s.Paused) return;
        var source = OwnerProcess();
        if (source is not null && s.ClipboardExcluded.Any(p => ProcessNames.Normalize(p) == source)) return;
        try
        {
            if (ReadData() is not { } data) return;
            // What a program marks as private: password managers, K-Desktop's own pastes of passing texts.
            if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || data.GetDataPresent("Clipboard Viewer Ignore")) return;
            if (data.GetDataPresent("CanIncludeInClipboardHistory") && data.GetData("CanIncludeInClipboardHistory") is MemoryStream allow &&
                allow.Length >= 4 && BitConverter.ToInt32(allow.ToArray(), 0) == 0) return;

            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            {
                _history.Add(new ClipCapture(ClipKind.Files, string.Empty, Files: files, Source: source));
                return;
            }
            if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text && text.Trim().Length > 0)
            {
                var rtf = data.GetDataPresent(DataFormats.Rtf) ? data.GetData(DataFormats.Rtf) as string : null;
                var html = data.GetDataPresent(DataFormats.Html) ? data.GetData(DataFormats.Html) as string : null;
                _history.Add(new ClipCapture(ClipKind.Text, text, rtf, html, Source: source));
                return;
            }
            if (!s.ClipboardImages) return;
            var png = data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream pngStream ? pngStream.ToArray() : null;
            var dib = png is null && data.GetDataPresent(DataFormats.Dib) && data.GetData(DataFormats.Dib) is MemoryStream dibStream ? dibStream.ToArray() : null;
            if (png is null && dib is null) return;
            // Turning a big picture into PNG takes a moment: not on the UI thread.
            _ = Task.Run(() =>
            {
                try
                {
                    var (bytes, width, height) = ToPng(png ?? ClipboardSnapshot.BmpFromDib(dib!));
                    _history.Add(new ClipCapture(ClipKind.Image, string.Empty, Png: bytes, Width: width, Height: height, Source: source));
                }
                catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException or IOException)
                {
                    Log.Info($"Historial del portapapeles: una imagen que no se pudo leer ({ex.Message})");
                }
            });
        }
        catch (Exception ex) when (ex is COMException or ExternalException or OutOfMemoryException)
        {
            Log.Info($"Historial del portapapeles: no se pudo leer lo copiado ({ex.Message})");
        }
    }

    /// <summary>The clipboard, asked again a few times while another program has it open.</summary>
    private static IDataObject? ReadData()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try { return Clipboard.GetDataObject(); }
            catch (COMException) { Thread.Sleep(30); }
        }
        return null;
    }

    private static string? OwnerProcess()
    {
        var owner = GetClipboardOwner();
        if (owner == IntPtr.Zero) return null;
        GetWindowThreadProcessId(owner, out var pid);
        var name = ProcessNames.Get(pid);
        return name.Length > 0 ? name : null;
    }

    private static (byte[] Png, int Width, int Height) ToPng(byte[] image)
    {
        using var input = new MemoryStream(image);
        var frame = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        if (image.Length > 8 && image[0] == 0x89 && image[1] == 0x50) return (image, frame.PixelWidth, frame.PixelHeight); // PNG already
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(frame);
        using var output = new MemoryStream();
        encoder.Save(output);
        return (output.ToArray(), frame.PixelWidth, frame.PixelHeight);
    }

    // ── window ───────────────────────────────────────────────────────────

    /// <summary>Creates the window once so the first opening is instant.</summary>
    public void Prewarm()
    {
        var w = Window;
        w.Left = -32000;
        w.Top = -32000;
        w.Show();
        w.HideNow();
    }

    private ClipboardWindow Window
    {
        get
        {
            if (_window is not null) return _window;
            var w = new ClipboardWindow();
            w.QueryChanged += Search;
            w.MoveRequested += Move;
            w.PasteRequested += plain => Paste(Selected, plain ? ClipboardMode.PlainText : null);
            w.PasteNumber += n => Paste(n < _items.Count ? _items[n] : null, null);
            w.PinRequested += () => TogglePin(Selected);
            w.DeleteRequested += () => Delete(Selected);
            w.CloseRequested += () => Close(restoreFocus: true);
            w.ItemHovered += item => Select(_items.IndexOf(item), scroll: false);
            w.ItemClicked += item => Paste(item, null);
            w.PinClicked += TogglePin;
            w.ItemMenu += OpenMenu;
            w.Deactivated += (_, _) => { if (IsOpen && !_menuOpen) Close(restoreFocus: false); };
            return _window = w;
        }
    }

    private ClipboardItem? Selected => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    /// <summary>Opens it next to the mouse over <paramref name="previous"/>, where it pastes; the same hotkey again closes it.</summary>
    public void Show(ForegroundWindow previous)
    {
        if (IsOpen)
        {
            Close(restoreFocus: true);
            return;
        }
        if (!Settings.ClipboardHistory)
        {
            App.Current.Ui.ShowNotice(Loc.T("clipboard.off"), OsdKind.Info);
            return;
        }
        _previous = previous;
        var w = Window;
        w.Reset();
        Search();

        // Next to the mouse, inside its screen.
        var cursor = UiNative.CursorPosition();
        var work = UiNative.WorkAreaAt(cursor);
        w.Left = -32000;
        w.Top = -32000;
        w.Show();
        w.UpdateLayout();
        var scale = UiNative.GetDpiForWindow(w.Hwnd) / 96.0;
        var width = (int)Math.Round(w.ActualWidth * scale);
        var height = (int)Math.Round(w.ActualHeight * scale);
        var x = Math.Clamp(cursor.X + 12, work.Left, Math.Max(work.Left, work.Right - width));
        var y = Math.Clamp(cursor.Y + 12, work.Top, Math.Max(work.Top, work.Bottom - height));
        UiNative.MoveWindow(w.Hwnd, x, y);
        w.PlayShow();
        w.Activate();
        WindowTools.ForceForeground(w.Hwnd);
        w.FocusSearch();
    }

    public void Close(bool restoreFocus)
    {
        if (!IsOpen) return;
        _window!.HideNow();
        if (restoreFocus && _previous.Handle != IntPtr.Zero) WindowTools.ForceForeground(_previous.Handle);
    }

    private void Search()
    {
        if (_window is not { } w) return;
        var filter = w.Filter;
        var query = w.Query.Trim();
        var entries = _history.Entries.Where(e => filter switch
        {
            "text" => e.Kind == ClipKind.Text,
            "images" => e.Kind == ClipKind.Image,
            "files" => e.Kind == ClipKind.Files,
            "pinned" => e.Pinned,
            _ => true,
        });
        if (query.Length > 0)
            entries = entries.Where(e => e.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                         (e.Source?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        var previous = Selected?.Entry.Id;
        _items = entries.Take(MaxResults).Select((e, i) => Item(e, i)).ToList();
        w.SetResults(_items, Loc.T(_history.Entries.Count == 0 ? "clipboard.empty" : "clipboard.noResults"));
        var keep = previous is null ? -1 : _items.FindIndex(i => i.Entry.Id == previous);
        _selected = -1;
        Select(keep >= 0 ? keep : _items.Count > 0 ? 0 : -1, scroll: true);
        if (keep < 0) w.ScrollToTop();
    }

    private ClipboardItem Item(ClipboardEntry e, int index)
    {
        var badge = index < 9 ? "Ctrl+" + (index + 1).ToString(CultureInfo.InvariantCulture) : string.Empty;
        var when = e.Copied.Date == DateTime.Today ? e.Copied.ToString("t", Loc.Instance.Culture) : e.Copied.ToString("g", Loc.Instance.Culture);
        var from = e.Source is { Length: > 0 } source ? source + " · " + when : when;
        switch (e.Kind)
        {
            case ClipKind.Image:
                return new ClipboardItem(e, Loc.T("clipboard.image", e.Width, e.Height), from, badge, () => Picture(e.Id, ThumbSize), () => Picture(e.Id, 360));
            case ClipKind.Files:
                var title = e.Files.Count == 1 ? Path.GetFileName(e.Files[0]) : Loc.T("clipboard.files", e.Files.Count, string.Join(", ", e.Files.Select(Path.GetFileName)));
                var folder = Path.GetDirectoryName(e.Files[0]) ?? string.Empty;
                return new ClipboardItem(e, title, folder + " · " + when, badge,
                    () => IconProvider.Kd(e.Files.Count == 1 ? "Document24" : "DocumentMultiple24", "blue"), null);
            default:
                var line = e.Text.ReplaceLineEndings(" ").Trim();
                if (line.Length > 300) line = line[..300];
                return new ClipboardItem(e, line, from, badge, () => IconProvider.Kd("TextT24", e.HasFormat ? "purple" : "teal"), null);
        }
    }

    /// <summary>A picture of the history, decoded small (it is only shown).</summary>
    private ImageSource? Picture(string id, int size)
    {
        if (_history.ImageBytes(id) is not { } bytes) return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = new MemoryStream(bytes);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = size;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void Move(int delta)
    {
        if (_items.Count == 0) return;
        Select(Math.Clamp((_selected < 0 ? -1 : _selected) + delta, 0, _items.Count - 1), scroll: true);
    }

    private void Select(int index, bool scroll)
    {
        if (_selected >= 0 && _selected < _items.Count) _items[_selected].IsSelected = false;
        _selected = index;
        if (index < 0 || index >= _items.Count) return;
        _items[index].IsSelected = true;
        if (scroll) _window?.BringIntoView(_items[index]);
    }

    // ── what it does with an entry ───────────────────────────────────────

    private void OpenMenu(ClipboardItem item, UIElement target)
    {
        Select(_items.IndexOf(item), scroll: false);
        var e = item.Entry;
        var menu = new ContextMenu { PlacementTarget = target };
        MenuItem Entry(string text, SymbolRegular symbol, Action click)
        {
            var mi = new MenuItem { Header = text, Icon = new SymbolIcon { Symbol = symbol } };
            mi.Click += (_, _) => click();
            return mi;
        }
        menu.Items.Add(Entry(Loc.T("clipboard.paste"), SymbolRegular.ClipboardPaste24, () => Paste(item, null)));
        if (e.Kind == ClipKind.Text)
        {
            if (e.HasFormat) menu.Items.Add(Entry(Loc.T("clipboard.pastePlain"), SymbolRegular.ClearFormatting24, () => Paste(item, ClipboardMode.PlainText)));
            var change = new MenuItem { Header = Loc.T("clipboard.pasteChanged"), Icon = new SymbolIcon { Symbol = SymbolRegular.TextCaseTitle24 } };
            foreach (var mode in new[] { ClipboardMode.Upper, ClipboardMode.Lower, ClipboardMode.Capitalize, ClipboardMode.Trim, ClipboardMode.CleanUrl })
            {
                var m = mode;
                var mi = new MenuItem { Header = Loc.T($"enum.clipboard.{m}") };
                mi.Click += (_, _) => Paste(item, m);
                change.Items.Add(mi);
            }
            menu.Items.Add(change);
        }
        menu.Items.Add(Entry(Loc.T("clipboard.copy"), SymbolRegular.Copy24, () =>
        {
            Close(restoreFocus: true);
            Put(e, null);
        }));
        if (e.Kind == ClipKind.Image)
            menu.Items.Add(Entry(Loc.T("clipboard.openImage"), SymbolRegular.Image24, () => OpenImage(e)));
        if (e.Kind == ClipKind.Files)
            menu.Items.Add(Entry(Loc.T("clipboard.openFolder"), SymbolRegular.Folder24, () => OpenFolder(e)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry(Loc.T(e.Pinned ? "clipboard.unpin" : "clipboard.pin"), e.Pinned ? SymbolRegular.PinOff24 : SymbolRegular.Pin24, () => TogglePin(item)));
        menu.Items.Add(Entry(Loc.T("common.delete"), SymbolRegular.Delete24, () => Delete(item)));
        _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (IsOpen) _window!.FocusSearch();
        };
        menu.IsOpen = true;
    }

    private void TogglePin(ClipboardItem? item)
    {
        if (item is not null) _history.SetPinned(item.Entry.Id, !item.Entry.Pinned);
    }

    private void Delete(ClipboardItem? item)
    {
        if (item is not null) _history.Remove(item.Entry.Id);
    }

    /// <summary>Puts the entry back on the clipboard and pastes it in the window it was opened over.</summary>
    /// <param name="change">Without its format (<see cref="ClipboardMode.PlainText"/>) or changed as «Portapapeles» does; null: as it was.</param>
    private void Paste(ClipboardItem? item, ClipboardMode? change)
    {
        if (item is null) return;
        Close(restoreFocus: true);
        PasteEntry(item.Entry, change);
    }

    /// <summary>Puts <paramref name="entry"/> on the clipboard and pastes it in the window in front (the launcher gives it back first).</summary>
    public void PasteEntry(ClipboardEntry entry, ClipboardMode? change = null)
    {
        if (!Put(entry, change)) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(80).ConfigureAwait(false);
            InputSimulator.SendCombo("CTRL + V");
        });
    }

    /// <summary>
    /// The entries whose text (or the program they came from) has every word of <paramref name="query"/>, newest first:
    /// the launcher's results, as rows of this history (title, when and where from, icon).
    /// </summary>
    public IReadOnlyList<ClipboardItem> Find(string query, int count)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0 || !Settings.ClipboardHistory) return [];
        return _history.Entries
            .Where(e => words.All(w => e.Text.Contains(w, StringComparison.CurrentCultureIgnoreCase) || (e.Source?.Contains(w, StringComparison.OrdinalIgnoreCase) ?? false)))
            .Take(count)
            .Select(e => Item(e, int.MaxValue))
            .ToList();
    }

    /// <summary>Leaves the entry on the clipboard (the history sees it and takes it to the top).</summary>
    private bool Put(ClipboardEntry e, ClipboardMode? change)
    {
        var data = new DataObject();
        switch (e.Kind)
        {
            case ClipKind.Files:
                var list = new System.Collections.Specialized.StringCollection();
                list.AddRange([.. e.Files.Where(f => File.Exists(f) || Directory.Exists(f))]);
                if (list.Count == 0)
                {
                    App.Current.Ui.ShowNotice(Loc.T("clipboard.filesGone"), OsdKind.Warning);
                    return false;
                }
                data.SetFileDropList(list);
                break;
            case ClipKind.Image:
                if (_history.ImageBytes(e.Id) is not { } png) return false;
                data.SetData("PNG", new MemoryStream(png));
                if (Picture(e.Id, 0) is BitmapSource bitmap) data.SetImage(bitmap);
                break;
            default:
                var text = change is { } mode && mode != ClipboardMode.PlainText ? ClipboardText.Transform(mode, e.Text, Loc.Instance.Culture) ?? e.Text : e.Text;
                data.SetText(text, TextDataFormat.UnicodeText);
                if (change is null)
                {
                    if (e.Rtf is not null) data.SetData(DataFormats.Rtf, e.Rtf);
                    if (e.Html is not null) data.SetData(DataFormats.Html, e.Html);
                }
                break;
        }
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (COMException) { Thread.Sleep(30); }
        }
        Log.Warn("Historial del portapapeles: no se pudo poner en el portapapeles (otro programa lo tiene abierto)");
        return false;
    }

    private void OpenImage(ClipboardEntry e)
    {
        if (_history.ImageBytes(e.Id) is not { } png) return;
        Close(restoreFocus: false);
        // A copy in the temporary folder, as read_clipboard does; it is for looking at it.
        var path = Path.Combine(AppPaths.TempDir, "Clipboard", e.Id + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, png);
        ProcessLauncher.Launch(new LaunchSpec(path), asAdmin: false);
    }

    private void OpenFolder(ClipboardEntry e)
    {
        Close(restoreFocus: false);
        var first = e.Files.FirstOrDefault(f => File.Exists(f) || Directory.Exists(f));
        if (first is null)
        {
            App.Current.Ui.ShowNotice(Loc.T("clipboard.filesGone"), OsdKind.Warning);
            return;
        }
        ProcessLauncher.Launch(new LaunchSpec("explorer.exe", "/select,\"" + first + "\""), asAdmin: false);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}

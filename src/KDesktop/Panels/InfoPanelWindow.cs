using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Native;

namespace KDesktop.Panels;

/// <summary>
/// A panel of «Paneles» on the desktop: its figures in columns, over the desktop and under the other windows (or over
/// everything with «Siempre encima»). It never takes the focus nor shows in Alt+Tab; it is dragged with the mouse and
/// remembers where, and the right button opens its menu.
/// </summary>
internal sealed class InfoPanelWindow : Window
{
    private const int WM_WINDOWPOSCHANGING = 0x0046, WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
    private const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10, SWP_NOZORDER = 0x4;
    private static readonly IntPtr HWND_BOTTOM = new(1), HWND_TOP = IntPtr.Zero, HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2);

    /// <summary>The cell of a figure: its value, and its bar or graph if it has one.</summary>
    private sealed record Cell(InfoPanelItem Item, TextBlock Value, PanelGauge? Gauge);

    private readonly PanelSampler _sampler;
    private readonly Border _back;
    private readonly Grid _grid = new();
    private readonly List<Cell> _cells = [];
    private readonly Action<InfoPanel> _edit;
    private IntPtr _hwnd;
    private long _shownAt;
    private bool _over, _saving, _covered;

    public InfoPanel Panel { get; }

    public InfoPanelWindow(InfoPanel panel, PanelSampler sampler, Action<InfoPanel> edit)
    {
        Panel = panel;
        _sampler = sampler;
        _edit = edit;
        Title = "K-Desktop · " + panel.Name;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        // The background is a layer of its own, so its opacity doesn't fade the text.
        _back = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        _back.SetResourceReference(Border.BorderBrushProperty, "Kd.MenuBorder");
        // Almost invisible but there, so the whole panel takes the mouse to drag it (also with no background, 0 %).
        var grab = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        grab.Freeze();
        Content = new Grid { Background = grab, Children = { _back, new Border { Padding = new Thickness(12, 7, 12, 7), Child = _grid } } };

        MouseLeftButtonDown += OnDrag;
        MouseRightButtonUp += OnMenu;
        SourceInitialized += OnSourceInitialized;
        ContentRendered += (_, _) => Place();
        Panel.PropertyChanged += OnPanelChanged;
        Build();
    }

    // ── window ───────────────────────────────────────────────────────────

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        UiNative.AddExStyle(_hwnd, toolWindow: true, noActivate: true);
        UiNative.SetClickThrough(_hwnd, Panel.Locked && Panel.ClickThrough);
        HwndSource.FromHwnd(_hwnd)?.AddHook(Hook);
        Topmost = Panel.Topmost;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            // Under the other windows: whatever puts it above them, it goes to its place (save over the desktop, see Over).
            case WM_WINDOWPOSCHANGING when !Panel.Topmost && !_over:
                var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                if ((pos.flags & SWP_NOZORDER) == 0)
                {
                    if (Anchor() is { } after) pos.hwndInsertAfter = after;
                    else pos.flags |= SWP_NOZORDER;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
                break;
            case WM_MOUSEACTIVATE:
                handled = true;
                return MA_NOACTIVATE;
        }
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>
    /// The window in front changed. «Mostrar el escritorio» (Win + D) puts the desktop over everything: then the panel
    /// goes over it, and back to the bottom as soon as a window is in front again.
    /// </summary>
    public void OnForeground(IntPtr foreground)
    {
        if (_hwnd == IntPtr.Zero) return;
        if (Panel.Topmost)
        {
            KeepPlace();
            return;
        }
        _over = UiNative.IsDesktop(foreground);
        // HWND_BOTTOM: the hook takes it to its place (over the taskbar it is on, if any).
        SetWindowPos(_hwnd, _over ? HWND_TOP : HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private const uint GW_HWNDNEXT = 2, GW_HWNDPREV = 3;
    private const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20;

    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, char[] name, int max);

    private static string ClassOf(IntPtr hwnd)
    {
        var name = new char[32];
        var length = GetClassName(hwnd, name, name.Length);
        return length > 0 ? new string(name, 0, length) : string.Empty;
    }

    private static bool IsTaskbar(IntPtr hwnd) => ClassOf(hwnd) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    /// <summary>
    /// A window that can cover it: shown (not one Windows keeps cloaked, as the closed Settings), takes the mouse (not a
    /// see-through overlay) and over part of it.
    /// </summary>
    private static bool Covers(IntPtr hwnd, UiNative.RECT me) =>
        IsWindowVisible(hwnd) && (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TRANSPARENT) == 0 &&
        !(DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out var cloaked, sizeof(int)) == 0 && cloaked != 0) &&
        UiNative.GetWindowRect(hwnd, out var r) && r.Left < me.Right && r.Right > me.Left && r.Top < me.Bottom && r.Bottom > me.Top;

    /// <summary>
    /// Where it goes under the other windows (the window to put it after): just over the taskbar it is on, so the taskbar
    /// never covers it; elsewhere, at the bottom (over the desktop). Null: it is there already.
    /// </summary>
    private IntPtr? Anchor()
    {
        if (!UiNative.GetWindowRect(_hwnd, out var me)) return HWND_BOTTOM;
        // From the top down, the first taskbar under it: it goes right above that one.
        for (var w = GetTopWindow(IntPtr.Zero); w != IntPtr.Zero; w = GetWindow(w, GW_HWNDNEXT))
        {
            if (w == _hwnd || !IsTaskbar(w) || !Covers(w, me)) continue;
            var above = GetWindow(w, GW_HWNDPREV);
            return above == _hwnd ? null : above == IntPtr.Zero ? HWND_TOP : above;
        }
        return HWND_BOTTOM;
    }

    /// <summary>
    /// Checked at each refresh, as the other windows move about: «Siempre encima», nothing over it (a window that went
    /// topmost later); else, no taskbar over it (clicking it brings it up) and no window under it (it never stays above them).
    /// </summary>
    private void KeepPlace()
    {
        if (_hwnd == IntPtr.Zero || _over || !IsVisible || !UiNative.GetWindowRect(_hwnd, out var me)) return;
        if (Panel.Topmost)
        {
            for (var w = GetWindow(_hwnd, GW_HWNDPREV); w != IntPtr.Zero; w = GetWindow(w, GW_HWNDPREV))
                if (Covers(w, me))
                {
                    SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    return;
                }
            return;
        }
        var wrong = false;
        for (var w = GetWindow(_hwnd, GW_HWNDPREV); w != IntPtr.Zero && !wrong; w = GetWindow(w, GW_HWNDPREV))
            wrong = IsTaskbar(w) && Covers(w, me);
        // Under it, down to the taskbar it is on (what is under that one is under the panel anyway).
        for (var w = GetWindow(_hwnd, GW_HWNDNEXT); w != IntPtr.Zero && !wrong && !(IsTaskbar(w) && Covers(w, me)); w = GetWindow(w, GW_HWNDNEXT))
            wrong = !UiNative.IsDesktop(w) && Covers(w, me);
        if (wrong) SetWindowPos(_hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>A «Siempre encima» one hides while something fills its monitor (a game, a film); checked at each refresh.</summary>
    private void CheckCovered()
    {
        var covered = Panel.Topmost && _hwnd != IntPtr.Zero && UiNative.FullscreenOn(UiNative.MonitorOf(_hwnd).Monitor);
        if (covered == _covered) return;
        _covered = covered;
        Visibility = covered ? Visibility.Hidden : Visibility.Visible;
    }

    // ── place ────────────────────────────────────────────────────────────

    /// <summary>Where it was left (its monitor, or the main one if that one is not connected); new: top right of the main one.</summary>
    public void Place()
    {
        if (_hwnd == IntPtr.Zero) return;
        UiNative.GetWindowRect(_hwnd, out var me);
        var (monitor, work) = UiNative.MonitorFor(ScreenChoice.Primary);
        int x, y;
        if (Panel.IsPlaced && Panel.Monitor is { } device && DisplayTools.Bounds(device) is { } bounds)
        {
            x = bounds.X + (int)Math.Round(Panel.X!.Value * bounds.Width);
            y = bounds.Y + (int)Math.Round(Panel.Y!.Value * bounds.Height);
            monitor = new UiNative.RECT { Left = bounds.X, Top = bounds.Y, Right = bounds.X + bounds.Width, Bottom = bounds.Y + bounds.Height };
        }
        else if (Panel.IsPlaced)
        {
            x = monitor.Left + (int)Math.Round(Panel.X!.Value * monitor.Width);
            y = monitor.Top + (int)Math.Round(Panel.Y!.Value * monitor.Height);
        }
        else
        {
            var margin = (int)(24 * UiNative.GetDpiForWindow(_hwnd) / 96.0);
            x = work.Right - me.Width - margin;
            y = work.Top + margin;
        }
        // Always whole on its monitor (another resolution, a smaller monitor).
        x = Math.Clamp(x, monitor.Left, Math.Max(monitor.Left, monitor.Right - me.Width));
        y = Math.Clamp(y, monitor.Top, Math.Max(monitor.Top, monitor.Bottom - me.Height));
        SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        if (!Panel.Topmost) OnForeground(UiNative.GetForegroundWindow());
    }

    /// <summary>Remembers where it is now, in fractions of its monitor (so it stays in its corner with another resolution).</summary>
    private void SavePlace()
    {
        if (_hwnd == IntPtr.Zero || !UiNative.GetWindowRect(_hwnd, out var me)) return;
        var (device, monitor, _) = UiNative.MonitorOf(_hwnd);
        if (monitor.Width <= 0 || monitor.Height <= 0) return;
        _saving = true;
        try
        {
            Panel.Monitor = device;
            Panel.X = Math.Round((me.Left - monitor.Left) / (double)monitor.Width, 4);
            Panel.Y = Math.Round((me.Top - monitor.Top) / (double)monitor.Height, 4);
        }
        finally { _saving = false; }
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (Panel.Locked || e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); }
        catch (InvalidOperationException) { return; }
        SavePlace();
    }

    // ── menu ─────────────────────────────────────────────────────────────

    /// <summary>The right-click menu, as the one of the tray icon: lock, over everything, the background, edit, hide.</summary>
    private void OnMenu(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var menu = new ContextMenu { PlacementTarget = this, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        menu.Items.Add(Check(Loc.T("panels.locked"), Panel.Locked, on => Panel.Locked = on));
        menu.Items.Add(Check(Loc.T("panels.topmost"), Panel.Topmost, on => Panel.Topmost = on));
        var opacity = new MenuItem { Header = Loc.T("panels.backgroundOpacity") };
        foreach (var percent in (int[])[100, 80, 60, 40, 20, 0])
            opacity.Items.Add(Check(percent.ToString(Loc.Instance.Culture) + " %", Panel.BackgroundOpacity == percent, _ => Panel.BackgroundOpacity = percent));
        menu.Items.Add(opacity);
        menu.Items.Add(new Separator());
        var edit = new MenuItem { Header = Loc.T("panels.menuEdit") };
        edit.Click += (_, _) => _edit(Panel);
        menu.Items.Add(edit);
        var hide = new MenuItem { Header = Loc.T("panels.menuHide") };
        hide.Click += (_, _) => Panel.Enabled = false;
        menu.Items.Add(hide);
        menu.IsOpen = true;
    }

    private static MenuItem Check(string header, bool on, Action<bool> set)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = on };
        item.Click += (_, _) => set(item.IsChecked);
        return item;
    }

    // ── content ──────────────────────────────────────────────────────────

    private void OnPanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(InfoPanel.Name):
                Title = "K-Desktop · " + Panel.Name;
                break;
            case nameof(InfoPanel.Notes) or nameof(InfoPanel.Enabled) or nameof(InfoPanel.Interval):
                break;
            case nameof(InfoPanel.Monitor) or nameof(InfoPanel.X) or nameof(InfoPanel.Y):
                if (!_saving) Place();
                break;
            case nameof(InfoPanel.Locked) or nameof(InfoPanel.ClickThrough):
                if (_hwnd != IntPtr.Zero) UiNative.SetClickThrough(_hwnd, Panel.Locked && Panel.ClickThrough);
                break;
            case nameof(InfoPanel.Topmost):
                Topmost = Panel.Topmost;
                if (!Panel.Topmost)
                {
                    SetWindowPos(_hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    OnForeground(UiNative.GetForegroundWindow());
                }
                else SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                CheckCovered();
                break;
            default:
                Build();
                break;
        }
    }

    /// <summary>
    /// Lays out the figures: in columns of <see cref="InfoPanel.Rows"/>, each one its name and its value (right-aligned,
    /// as wide as its widest figure so nothing jumps), with its bar or its graph under it.
    /// </summary>
    private void Build()
    {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _grid.ColumnDefinitions.Clear();
        _cells.Clear();
        ApplyBackground();
        var rows = Math.Clamp(Panel.Rows, 1, 12);
        var items = Panel.Items.ToList();
        var columns = Math.Max(1, (items.Count + rows - 1) / rows);
        rows = Math.Min(rows, Math.Max(1, items.Count));
        for (var r = 0; r < rows; r++) _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var c = 0; c < columns; c++)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        var size = Panel.FontSize;
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var (row, column) = (i % rows, i / rows * 2);
            if (item.Kind == PanelItemKind.Spacer) continue; // its cell stays empty
            var gap = column > 0 ? size * 1.4 : 0;
            // Under the figure: a thin bar, or a strip with the graph.
            var strip = item.Look switch
            {
                PanelItemLook.Bar => 2,
                PanelItemLook.Graph => Math.Round(size * 1.3),
                _ => 0,
            };
            var below = strip > 0 ? strip + 3 : 0;
            var spacing = row < rows - 1 ? size * 0.25 : 0;
            PanelGauge? gauge = null;
            if (strip > 0)
            {
                gauge = new PanelGauge
                {
                    Look = item.Look,
                    Margin = new Thickness(gap, 0, 0, spacing),
                    Height = strip,
                    VerticalAlignment = VerticalAlignment.Bottom,
                };
                Paint(gauge, PanelGauge.BrushProperty, Panel.LabelColor, "Kd.AccentText");
                Place(gauge, row, column, 2);
            }
            var label = new TextBlock
            {
                Text = item.ShownLabel,
                FontSize = size,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(gap, 0, size * 0.5, spacing + below),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Paint(label, TextBlock.ForegroundProperty, Panel.LabelColor, "Kd.AccentText");
            Place(label, row, column, 1);
            var value = new TextBlock
            {
                FontSize = size,
                TextAlignment = TextAlignment.Right,
                MinWidth = Widest(item.Kind, typeface, size),
                Margin = new Thickness(0, 0, 0, spacing + below),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Paint(value, TextBlock.ForegroundProperty, Panel.ValueColor, "TextFillColorPrimaryBrush");
            Place(value, row, column + 1, 1);
            _cells.Add(new Cell(item, value, gauge));
        }
        if (items.All(i => i.Kind == PanelItemKind.Spacer))
        {
            var empty = new TextBlock { Text = Loc.T("panels.noItems"), FontSize = size };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            _grid.Children.Add(empty);
        }
        Refresh(force: true);
    }

    private void Place(UIElement element, int row, int column, int span)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, span);
        _grid.Children.Add(element);
    }

    /// <summary>Its own colour when the panel has one («#RRGGBB»), else the theme's <paramref name="resource"/>.</summary>
    private static void Paint(FrameworkElement element, DependencyProperty property, string? color, string resource)
    {
        if (Parse(color) is { } own)
        {
            var brush = new SolidColorBrush(own);
            brush.Freeze();
            element.SetValue(property, brush);
        }
        else element.SetResourceReference(property, resource);
    }

    private static Color? Parse(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        try { return (Color)ColorConverter.ConvertFromString(color); }
        catch (FormatException) { return null; }
    }

    private void ApplyBackground()
    {
        _back.Opacity = Math.Clamp(Panel.BackgroundOpacity, 0, 100) / 100.0;
        if (Parse(Panel.Background) is { } own)
        {
            var brush = new SolidColorBrush(own);
            brush.Freeze();
            _back.Background = brush;
        }
        else _back.SetResourceReference(Border.BackgroundProperty, "Kd.MenuTintOpaque");
    }

    /// <summary>The width of the widest figure of a kind («888.8 MB/s», «100%», «100°»), so the text never jumps.</summary>
    private double Widest(PanelItemKind kind, Typeface typeface, double size)
    {
        var sample = kind switch
        {
            PanelItemKind.NetUp or PanelItemKind.NetDown => PanelSampler.Format(kind, 888.8 * 1024 * 1024, Loc.Instance.Culture),
            _ => PanelSampler.Format(kind, 100, Loc.Instance.Culture),
        };
        var text = new FormattedText(sample, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return Math.Ceiling(text.WidthIncludingTrailingWhitespace);
    }

    /// <summary>The figures again (after each reading; each panel at its own pace).</summary>
    public void Refresh(bool force = false)
    {
        var now = Environment.TickCount64;
        if (!force && now - _shownAt < Panel.Interval - 50) return;
        _shownAt = now;
        var culture = Loc.Instance.Culture;
        foreach (var cell in _cells)
        {
            var value = _sampler.Value(cell.Item.Kind);
            var text = PanelSampler.Format(cell.Item.Kind, value, culture);
            if (cell.Value.Text != text) cell.Value.Text = text;
            if (cell.Gauge is null) continue;
            // The top of a speed is the highest of the last minute, for the bar too (its own figure would always fill it).
            var history = _sampler.History(cell.Item.Kind);
            var top = PanelSampler.GraphTop(cell.Item.Kind, history);
            cell.Gauge.Show(cell.Item.Look == PanelItemLook.Bar ? value is { } v ? [v] : [] : history, top);
        }
        CheckCovered();
        KeepPlace();
    }

    protected override void OnClosed(EventArgs e)
    {
        Panel.PropertyChanged -= OnPanelChanged;
        base.OnClosed(e);
    }
}

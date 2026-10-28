using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;

namespace KDesktop.Views;

/// <summary>
/// Clickable picture of a full keyboard (ISO, navigation block and numeric pad) and a mouse. The keys that print
/// something are named and labelled after the keyboard layout in use in Windows (an English, a French… keyboard shows its
/// own keys where they are): each one is a physical position, its scan code.
/// Keys remapped in the current modifier layer are highlighted and show what they do; keys remapped only in other
/// layers get a small dot. Clicking a key raises <see cref="KeyClicked"/> with its name ("J", "LCtrl", "Mouse 4"…).
/// <see cref="Compact"/> puts the navigation block, the numeric pad and the mouse under the keyboard (about half as wide:
/// bigger keys in a narrow window). Ctrl, Alt, Shift and Win carry a corner that turns their layer on or off
/// (<see cref="LayerToggled"/>), golden while it is on.
/// </summary>
public sealed class KBoardSurface : Viewbox
{
    private const double U = 44;      // one key unit, including the gap
    private const double Gap = 4;

    private sealed record KeyDef(string Name, double X, double Y, double W = 1, double H = 1, string? Caption = null, bool IsoEnter = false);

    private sealed class KeyVisual
    {
        public required KeyDef Def { get; init; }
        public required Shape Shape { get; init; }
        public required TextBlock Caption { get; init; }
        public required TextBlock Target { get; init; }
        public required Ellipse Dot { get; init; }
        public required ScaleTransform Scale { get; init; }

        /// <summary>Area where the texts go (canvas coordinates) and its fixed caption position, if any.</summary>
        public required Rect TextArea { get; init; }
        public double? FixedTop { get; init; }
        public double BaseSize { get; init; }
    }

    private readonly Canvas _canvas = new();
    private readonly Dictionary<string, KeyVisual> _keys = new(StringComparer.OrdinalIgnoreCase);
    private KBoardProfile? _profile;
    private Modifiers _layer;
    private string? _selected;

    public event Action<string>? KeyClicked;

    /// <summary>The corner of a modifier key was clicked: its layer on or off.</summary>
    public event Action<Modifiers>? LayerToggled;

    private readonly List<(Modifiers Mod, Path Corner)> _corners = [];

    public KBoardSurface()
    {
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.Both;
        Child = _canvas;
        Build();
        // Another keyboard layout chosen in Windows: its keys
        InputLanguageManager.Current.InputLanguageChanged += (_, _) => CheckLayout();
    }

    /// <summary>Draws the keys again if the keyboard layout in use changed (the page calls it when it is shown).</summary>
    public void CheckLayout()
    {
        if (Native.UiNative.GetKeyboardLayout(0) != _hkl) Rebuild();
    }

    private void Rebuild()
    {
        _canvas.Children.Clear();
        _keys.Clear();
        _corners.Clear();
        Build();
        Refresh();
        PaintCorners();
    }

    public KBoardProfile? Profile
    {
        get => _profile;
        set { _profile = value; Refresh(); }
    }

    public Modifiers Layer
    {
        get => _layer;
        set { _layer = value; Refresh(); PaintCorners(); }
    }

    /// <summary>Name of the selected key (without modifiers).</summary>
    public string? Selected
    {
        get => _selected;
        set
        {
            // The old key is repainted once it is no longer the selected one (before, it kept its outline).
            var previous = _selected;
            _selected = value;
            if (previous is not null && _keys.TryGetValue(previous, out var old)) Paint(old);
            if (value is not null && _keys.TryGetValue(value, out var now))
            {
                Paint(now);
                Pulse(now);
            }
        }
    }

    /// <summary>The numeric pad and the mouse under the keyboard instead of beside it.</summary>
    public bool Compact
    {
        get => _compact;
        set
        {
            if (_compact == value) return;
            _compact = value;
            Rebuild();
        }
    }

    private bool _compact;

    public bool Contains(string name) => _keys.ContainsKey(name);

    /// <summary>Repaints every key from the profile.</summary>
    public void Refresh()
    {
        foreach (var k in _keys.Values) Paint(k);
    }

    // ── layout ───────────────────────────────────────────────────────────

    private void Build()
    {
        _hkl = Native.UiNative.GetKeyboardLayout(0);
        var defs = new List<KeyDef>();
        void Row(double y, double x, params (string Name, double W)[] keys)
        {
            foreach (var (name, w) in keys)
            {
                // A key that prints something: the key and the label of that place in the layout in use
                var (key, label) = Physical(name);
                defs.Add(new KeyDef(key, x, y, w, Caption: label));
                x += w;
            }
        }
        (string, double) K(string name, double w = 1) => (name, w);

        // Function row
        Row(0, 0, K("Escape"));
        Row(0, 2, K("F1"), K("F2"), K("F3"), K("F4"));
        Row(0, 6.5, K("F5"), K("F6"), K("F7"), K("F8"));
        Row(0, 11, K("F9"), K("F10"), K("F11"), K("F12"));

        // Main block (Spanish ISO)
        const double r1 = 1.4;
        Row(r1, 0, K("\\"), K("1"), K("2"), K("3"), K("4"), K("5"), K("6"), K("7"), K("8"), K("9"), K("0"), K("'"), K("¡"), K("Backspace", 2));
        Row(r1 + 1, 0, K("Tab", 1.5), K("Q"), K("W"), K("E"), K("R"), K("T"), K("Y"), K("U"), K("I"), K("O"), K("P"), K("`"), K("+"));
        defs.Add(new KeyDef("Enter", 13.5, r1 + 1, 1.5, 2, IsoEnter: true));
        Row(r1 + 2, 0, K("CapsLock", 1.75), K("A"), K("S"), K("D"), K("F"), K("G"), K("H"), K("J"), K("K"), K("L"), K("Ñ"), K("´"), K("ç"));
        Row(r1 + 3, 0, K("LShift", 1.25), K("<"), K("Z"), K("X"), K("C"), K("V"), K("B"), K("N"), K("M"), K(","), K("."), K("-"), K("RShift", 2.75));
        Row(r1 + 4, 0, K("LCtrl", 1.25), K("LWin", 1.25), K("LAlt", 1.25), K("Space", 6.25), K("RAlt", 1.25), K("RWin", 1.25), K("AppsKey", 1.25), K("RCtrl", 1.25));

        // Navigation block, numeric pad and mouse: beside the keyboard, or (compact) all three under it, centred
        var py = _compact ? r1 + 5.5 : r1;
        var nx = _compact ? 1.6 : 15.4;
        var px = _compact ? 5.2 : 18.8;
        var mx = _compact ? 10.0 : 23.4;
        var top = _compact ? py : 0;
        var rows = _compact ? py + 1 : r1;
        Row(top, nx, K("PrintScreen"), K("Scroll"), K("Pause"));
        Row(rows, nx, K("Insert"), K("Home"), K("PageUp"));
        Row(rows + 1, nx, K("Delete"), K("End"), K("PageDown"));
        Row(_compact ? py + 3 : r1 + 3, nx + 1, K("Up"));
        Row(_compact ? py + 4 : r1 + 4, nx, K("Left"), K("Down"), K("Right"));

        Row(py, px, K("NumLock"), K("NumPadDiv"), K("NumPadMult"), K("NumPadSub"));
        Row(py + 1, px, K("NumPad7"), K("NumPad8"), K("NumPad9"));
        defs.Add(new KeyDef("NumPadAdd", px + 3, py + 1, 1, 2));
        Row(py + 2, px, K("NumPad4"), K("NumPad5"), K("NumPad6"));
        Row(py + 3, px, K("NumPad1"), K("NumPad2"), K("NumPad3"));
        defs.Add(new KeyDef("NumPadEnter", px + 3, py + 3, 1, 2));
        Row(py + 4, px, K("NumPad0", 2), K("NumPadDot"));

        foreach (var d in defs) AddKey(d);

        _canvas.MinHeight = 0;
        // In one row the mouse starts at the top, a little shorter, so its last wheel chips end where the bottom row does
        if (_compact) BuildMouse(mx * U, py * U, 5);
        else BuildMouse(mx * U, 0, ((r1 + 5) * U - Gap - MouseChips + Gap) / U);

        _canvas.Width = (_compact ? 15 : 27.2) * U;
        _canvas.Height = (py + 5) * U;
        PaintCorners();
    }

    private void AddKey(KeyDef d)
    {
        var w = d.W * U - Gap;
        var h = d.H * U - Gap;
        Geometry geometry = d.IsoEnter
            ? IsoEnterGeometry(w, h)
            : new RectangleGeometry(new Rect(0, 0, w, h), 6, 6);
        AddVisual(d, geometry, d.X * U, d.Y * U, w, d.IsoEnter ? U - Gap : h);
    }

    /// <summary>The "L" shaped Enter of ISO keyboards: wide on the top row, narrower on the second.</summary>
    private static Geometry IsoEnterGeometry(double w, double h)
    {
        var inset = 0.25 * U;
        var top = U - Gap;
        var fig = new PathFigure { StartPoint = new Point(6, 0), IsClosed = true };
        void L(double x, double y) => fig.Segments.Add(new LineSegment(new Point(x, y), true));
        void A(double x, double y) => fig.Segments.Add(new ArcSegment(new Point(x, y), new Size(6, 6), 0, false, SweepDirection.Clockwise, true));
        L(w - 6, 0); A(w, 6);
        L(w, h - 6); A(w - 6, h);
        L(inset + 6, h); A(inset, h - 6);
        L(inset, top + 6);
        fig.Segments.Add(new ArcSegment(new Point(inset - 6, top), new Size(6, 6), 0, false, SweepDirection.Counterclockwise, true));
        L(6, top); A(0, top - 6);
        L(0, 6); A(6, 0);
        var g = new PathGeometry([fig]);
        g.Freeze();
        return g;
    }

    // ── keyboard layout ──────────────────────────────────────────────────

    private IntPtr _hkl;

    /// <summary>
    /// The scan code (the physical place) of each key that prints something, by its name on a Spanish keyboard (the
    /// names the drawing is written with).
    /// </summary>
    private static readonly Dictionary<string, uint> Scans = new(StringComparer.Ordinal)
    {
        ["\\"] = 0x29, ["1"] = 0x02, ["2"] = 0x03, ["3"] = 0x04, ["4"] = 0x05, ["5"] = 0x06, ["6"] = 0x07, ["7"] = 0x08,
        ["8"] = 0x09, ["9"] = 0x0A, ["0"] = 0x0B, ["'"] = 0x0C, ["¡"] = 0x0D,
        ["Q"] = 0x10, ["W"] = 0x11, ["E"] = 0x12, ["R"] = 0x13, ["T"] = 0x14, ["Y"] = 0x15, ["U"] = 0x16, ["I"] = 0x17,
        ["O"] = 0x18, ["P"] = 0x19, ["`"] = 0x1A, ["+"] = 0x1B,
        ["A"] = 0x1E, ["S"] = 0x1F, ["D"] = 0x20, ["F"] = 0x21, ["G"] = 0x22, ["H"] = 0x23, ["J"] = 0x24, ["K"] = 0x25,
        ["L"] = 0x26, ["Ñ"] = 0x27, ["´"] = 0x28, ["ç"] = 0x2B,
        ["<"] = 0x56, ["Z"] = 0x2C, ["X"] = 0x2D, ["C"] = 0x2E, ["V"] = 0x2F, ["B"] = 0x30, ["N"] = 0x31, ["M"] = 0x32,
        [","] = 0x33, ["."] = 0x34, ["-"] = 0x35,
    };

    /// <summary>What each key name prints in the layout in use («Ñ» is «;» on an English keyboard): labels and tooltips.</summary>
    private static readonly Dictionary<string, string> Printed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The key at the place of <paramref name="name"/> in the layout in use, and what it prints (null: as it is).</summary>
    private (string Key, string? Label) Physical(string name)
    {
        if (!Scans.TryGetValue(name, out var scan)) return (name, null);
        var vk = Native.UiNative.MapVirtualKeyEx(scan, Native.UiNative.MAPVK_VSC_TO_VK_EX, _hkl);
        if (vk == 0) return (name, null);
        var key = KeyNames.FromVk((int)vk);
        var buffer = new char[8];
        // Flag 4: without touching the state of the keyboard (a dead key stays untouched for what is typed)
        var n = Native.UiNative.ToUnicodeEx(vk, scan, new byte[256], buffer, buffer.Length, 4, _hkl);
        if (n == 0 || buffer[0] < ' ') return (key, null);
        var label = buffer[0].ToString().ToUpper(Loc.Instance.Culture);
        Printed[key] = label;
        return (key, label);
    }

    /// <summary>What the wheel chips under the mouse take below its body (the gap, two rows of chips).</summary>
    private const double MouseChips = 10 + 34 + 28;

    private void BuildMouse(double x, double y, double units)
    {
        // Body 3.4 units wide, <units> high.
        var w = 3.4 * U - Gap;
        var h = units * U - Gap;
        var mid = w / 2;
        var split = h * 0.42;

        var body = new Path
        {
            Data = Svg($"M 0,{split} C 0,{h * 0.1} {w * 0.12},0 {mid},0 C {w * 0.88},0 {w},{h * 0.1} {w},{split} L {w},{h * 0.72} C {w},{h * 0.95} {w * 0.8},{h} {mid},{h} C {w * 0.2},{h} 0,{h * 0.95} 0,{h * 0.72} Z"),
            StrokeThickness = 1.5,
        };
        body.SetResourceReference(Shape.FillProperty, "Kd.Subtle");
        body.SetResourceReference(Shape.StrokeProperty, "CardStrokeColorDefaultBrush");
        Canvas.SetLeft(body, x);
        Canvas.SetTop(body, y);
        _canvas.Children.Add(body);

        var left = Svg($"M 0,{split} C 0,{h * 0.1} {w * 0.12},0 {mid - 3},0 L {mid - 3},{split} Z");
        var right = Svg($"M {mid + 3},0 C {w * 0.88},0 {w},{h * 0.1} {w},{split} L {mid + 3},{split} Z");
        AddVisual(new KeyDef("Mouse 1", 0, 0), left, x, y, mid - 3, split, captionTop: split * 0.45);
        AddVisual(new KeyDef("Mouse 2", 0, 0), right, x + mid + 3, y, mid - 3, split, offsetX: -(mid + 3), captionTop: split * 0.45);

        // Wheel (button 3) and the wheel directions
        var wheelW = 18.0;
        var wheelH = split * 0.42;
        AddVisual(new KeyDef("Mouse 3", 0, 0, Caption: ""), new RectangleGeometry(new Rect(0, 0, wheelW, wheelH), 9, 9),
            x + mid - wheelW / 2, y + split * 0.3, wheelW, wheelH, small: true);

        // Side buttons (left side, as on most mice): forward (5) above back (4).
        var sideW = 12.0;
        var sideH = 34.0;
        AddVisual(new KeyDef("Mouse 5", 0, 0, Caption: "5"), new RectangleGeometry(new Rect(0, 0, sideW, sideH), 5, 5),
            x - sideW - 3, y + split + 10, sideW, sideH, small: true);
        AddVisual(new KeyDef("Mouse 4", 0, 0, Caption: "4"), new RectangleGeometry(new Rect(0, 0, sideW, sideH), 5, 5),
            x - sideW - 3, y + split + 14 + sideH, sideW, sideH, small: true);

        // Wheel directions as chips under the mouse.
        var chipY = y + h + 10;
        string[] wheels = ["Wheel Up", "Wheel Down", "HWheel Down", "HWheel Up"];
        var chipW = (w + 14) / 2 - 4;
        for (var i = 0; i < wheels.Length; i++)
        {
            var cx = x - 7 + i % 2 * (chipW + 8);
            var cy = chipY + i / 2 * 34;
            AddVisual(new KeyDef(wheels[i], 0, 0), new RectangleGeometry(new Rect(0, 0, chipW, 28), 6, 6), cx, cy, chipW, 28, small: true);
        }
        _canvas.MinHeight = chipY + 70;
    }

    private void AddVisual(KeyDef d, Geometry geometry, double x, double y, double w, double h,
        double offsetX = 0, double? captionTop = null, bool small = false)
    {
        if (offsetX != 0)
        {
            geometry = geometry.Clone();
            geometry.Transform = new TranslateTransform(offsetX, 0);
        }
        var bounds = geometry.Bounds;
        var scale = new ScaleTransform(1, 1, bounds.Width / 2, bounds.Height / 2);
        var shape = new Path { Data = geometry, StrokeThickness = 1, Cursor = Cursors.Hand, RenderTransform = scale };
        var caption = new TextBlock
        {
            Text = d.Name is "LWin" or "RWin" ? string.Empty : d.Caption ?? Caption(d.Name),
            FontSize = small ? 11 : CaptionSize(d),
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            IsHitTestVisible = false,
            Width = Math.Max(10, w - 4),
        };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        if (d.Name is "LWin" or "RWin") caption.Inlines.Add(new System.Windows.Documents.InlineUIContainer(WindowsLogo()) { BaselineAlignment = BaselineAlignment.Center });
        var target = new TextBlock
        {
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
            Width = Math.Max(10, w - 4),
        };
        target.SetResourceReference(TextBlock.ForegroundProperty, "Kd.AccentText");
        var dot = new Ellipse { Width = 6, Height = 6, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        dot.SetResourceReference(Shape.FillProperty, "Kd.Accent");

        Canvas.SetLeft(shape, x);
        Canvas.SetTop(shape, y);
        _canvas.Children.Add(shape);

        Canvas.SetLeft(caption, x + 2);
        Canvas.SetLeft(target, x + 2);
        Canvas.SetLeft(dot, x + w - 10);
        Canvas.SetTop(dot, y + 4);
        _canvas.Children.Add(caption);
        _canvas.Children.Add(target);
        _canvas.Children.Add(dot);

        var visual = new KeyVisual
        {
            Def = d, Shape = shape, Caption = caption, Target = target, Dot = dot, Scale = scale,
            TextArea = new Rect(x, y, w, h), FixedTop = captionTop is { } t ? y + t : null, BaseSize = caption.FontSize,
        };
        _keys[d.Name] = visual;
        shape.MouseEnter += (_, _) => Paint(visual, hover: true);
        shape.MouseLeave += (_, _) => Paint(visual);
        shape.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            KeyClicked?.Invoke(d.Name);
        };
        Paint(visual);
        if (LayerOf(d.Name) is { } mod) AddCorner(mod, x, y, w, h);
    }

    /// <summary>The layer a modifier key turns on with its corner (both Ctrl, both Alt…).</summary>
    private static Modifiers? LayerOf(string key) => key switch
    {
        "LCtrl" or "RCtrl" => Modifiers.Ctrl,
        "LAlt" => Modifiers.Alt,
        // AltGr is Ctrl + Alt for Windows (AltGr + E, the €, arrives as Ctrl + Alt + E)
        "RAlt" => Modifiers.Ctrl | Modifiers.Alt,
        "LShift" or "RShift" => Modifiers.Shift,
        "LWin" or "RWin" => Modifiers.Win,
        _ => null,
    };

    /// <summary>A small triangle in the bottom-right corner of a modifier key: its own button for the layer.</summary>
    private void AddCorner(Modifiers mod, double x, double y, double w, double h)
    {
        const double size = 10, inset = 2.5;
        var corner = new Path
        {
            Data = Svg($"M {w - inset},{h - inset - size} L {w - inset},{h - inset - 3} Q {w - inset},{h - inset} {w - inset - 3},{h - inset} L {w - inset - size},{h - inset} Z"),
            Cursor = Cursors.Hand,
            // A bigger invisible target than the triangle drawn (it is small)
            Stroke = Brushes.Transparent,
            StrokeThickness = 6,
        };
        var name = mod == (Modifiers.Ctrl | Modifiers.Alt) ? "AltGr (Ctrl + Alt)" : mod.ToString();
        corner.ToolTip = Loc.T("kboard.layerCorner", name);
        System.Windows.Automation.AutomationProperties.SetName(corner, Loc.T("kboard.layerCorner", name));
        Canvas.SetLeft(corner, x);
        Canvas.SetTop(corner, y);
        corner.MouseEnter += (_, _) => PaintCorners();
        corner.MouseLeave += (_, _) => PaintCorners();
        corner.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            LayerToggled?.Invoke(mod);
        };
        _canvas.Children.Add(corner);
        _corners.Add((mod, corner));
    }

    /// <summary>Golden while its layer is on; grey (lighter under the mouse) while off.</summary>
    private void PaintCorners()
    {
        foreach (var (mod, corner) in _corners)
        {
            var on = (_layer & mod) == mod;
            corner.SetResourceReference(Shape.FillProperty, on ? "Kd.Accent" : corner.IsMouseOver ? "Kd.AccentText" : "TextFillColorTertiaryBrush");
            corner.Opacity = on || corner.IsMouseOver ? 1 : 0.55;
        }
    }

    /// <summary>The Windows logo (four panes), for the Win keys.</summary>
    private static Path WindowsLogo()
    {
        const double s = 6.2, g = 1.2;
        var logo = new Path
        {
            Data = Svg($"M 0,0 H {s} V {s} H 0 Z M {s + g},0 H {2 * s + g} V {s} H {s + g} Z M 0,{s + g} H {s} V {2 * s + g} H 0 Z M {s + g},{s + g} H {2 * s + g} V {2 * s + g} H {s + g} Z"),
            Margin = new Thickness(0, 2, 0, 2),
        };
        logo.SetResourceReference(Shape.FillProperty, "TextFillColorPrimaryBrush");
        return logo;
    }

    /// <summary>Path data with invariant numbers (a Spanish culture would write "0,5" and break the syntax).</summary>
    private static Geometry Svg(FormattableString data) => Geometry.Parse(FormattableString.Invariant(data));

    private static double CaptionSize(KeyDef d) => d.Name.Length <= 2 || d.Name.StartsWith("NumPad", StringComparison.Ordinal) && d.Name.Length == 7 || d.Name is "Up" or "Down" or "Left" or "Right" ? 17 : 12;

    // ── painting ─────────────────────────────────────────────────────────

    private void Paint(KeyVisual k, bool hover = false)
    {
        var from = Hotkey.Format(_layer, k.Def.Name);
        var remap = _profile?.Find(from);
        var selected = string.Equals(_selected, k.Def.Name, StringComparison.OrdinalIgnoreCase);
        // Every remap of this key, whatever its modifiers (all of them go in its tooltip)
        var all = _profile?.Remaps.Where(r => Hotkey.Parse(r.From).Main.Equals(k.Def.Name, StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
        var elsewhere = remap is null && all.Count > 0;

        string fill, stroke;
        if (remap is { Enabled: true })
        {
            fill = remap.Kind == RemapKind.Disable ? "Kd.KeyDisabled" : "Kd.KeyMapped";
            stroke = remap.Kind == RemapKind.Disable ? "Kd.Danger" : "Kd.Accent";
        }
        else
        {
            fill = hover ? "ControlFillColorSecondaryBrush" : "ControlFillColorDefaultBrush";
            stroke = "ControlStrokeColorDefaultBrush";
        }
        if (hover && remap is { Enabled: true }) fill = "Kd.Selected";
        k.Shape.SetResourceReference(Shape.FillProperty, fill);
        k.Shape.SetResourceReference(Shape.StrokeProperty, selected ? "Kd.Accent" : stroke);
        k.Shape.StrokeThickness = selected ? 2.5 : 1;
        k.Shape.Opacity = remap is { Enabled: false } ? 0.6 : 1;

        // Too narrow for any of it (the side buttons of the mouse, the wheel): only its colour, and the tooltip
        k.Target.Text = remap is null || k.Target.Width < 24 ? string.Empty : ShortTarget(remap);
        k.Dot.Visibility = elsewhere ? Visibility.Visible : Visibility.Collapsed;
        k.Shape.ToolTip = KeyTip(k.Def.Name, all);
        Layout(k);
    }

    /// <summary>Centres the name (and the new function under it) inside the key; long names use two lines.</summary>
    private static void Layout(KeyVisual k)
    {
        var area = k.TextArea;
        var mapped = k.Target.Text.Length > 0;
        // Long names on narrow keys and remapped keys get a smaller name, so everything fits.
        var size = k.BaseSize;
        if (mapped) size = Math.Min(size, area.Height < 50 ? 12 : 14);
        if (k.Caption.Text.Contains(' ') && area.Width < 1.6 * U) size = Math.Min(size, 10);
        k.Caption.FontSize = size;
        k.Caption.Measure(new Size(k.Caption.Width, double.PositiveInfinity));
        var captionHeight = k.Caption.DesiredSize.Height;
        var targetHeight = 0.0;
        if (mapped)
        {
            // Smaller before cutting it; cut, it ends in «…» (whole in the key's tooltip and in the panel a click opens)
            k.Target.FontSize = 10.5;
            for (; k.Target.FontSize > 8.5 && TextWidth(k.Target) > k.Target.Width; k.Target.FontSize -= 0.5) { }
            k.Target.Measure(new Size(k.Target.Width, double.PositiveInfinity));
            targetHeight = k.Target.DesiredSize.Height;
        }
        var top = k.FixedTop ?? area.Y + Math.Max(1, (area.Height - captionHeight - targetHeight) / 2);
        Canvas.SetTop(k.Caption, top);
        Canvas.SetTop(k.Target, top + captionHeight - 1);
    }

    /// <summary>How wide a one-line text is with its font, uncut.</summary>
    private static double TextWidth(TextBlock t) =>
        new FormattedText(t.Text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch), t.FontSize, Brushes.Black, 1).WidthIncludingTrailingWhitespace;

    /// <summary>The key's name and every remap it has (alone and with modifiers), one per line.</summary>
    private static string KeyTip(string name, IReadOnlyList<KeyRemap> remaps)
    {
        // A key of the layout says only what it prints (its inner name is the one of a Spanish keyboard)
        var title = Printed.ContainsKey(name) ? Caption(name) : Caption(name) is var c && c.Length > 0 && c != name ? $"{c}  ({name})" : name;
        var lines = remaps.OrderBy(r => r.From.Length).Select(r =>
            $"{r.From}  →  {(r.Kind == RemapKind.Keys ? r.To : r.TargetText)}{(r.Enabled ? "" : "  (" + Loc.T("common.disabled") + ")")}");
        return string.Join("\n", lines.Prepend(title));
    }

    private static void Pulse(KeyVisual k)
    {
        var anim = new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut } };
        k.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        k.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    /// <summary>What a remapped key shows under its name.</summary>
    public static string ShortTarget(KeyRemap r) => r.Kind switch
    {
        RemapKind.Disable => "⦸",
        RemapKind.Text => "“" + r.To + "”",
        RemapKind.Command => "⚡ " + (r.CommandName ?? ""),
        _ => ShortCombo(r.To),
    };

    /// <summary>"CTRL + SHIFT + Left" → "Ctrl+Shift+←".</summary>
    public static string ShortCombo(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) return "?";
        var (mods, main) = Hotkey.Parse(combo);
        var parts = new List<string>();
        if (mods.HasFlag(Modifiers.Ctrl)) parts.Add("Ctrl");
        if (mods.HasFlag(Modifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(Modifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(Modifiers.Win)) parts.Add("Win");
        if (main.Length > 0) parts.Add(Caption(main));
        return string.Join("+", parts);
    }

    /// <summary>Text printed on a key (translated where it makes sense).</summary>
    public static string Caption(string name) => Printed.TryGetValue(name, out var printed) ? printed : name switch
    {
        "\\" => "º",
        "Up" => "↑",
        "Down" => "↓",
        "Left" => "←",
        "Right" => "→",
        "LCtrl" or "RCtrl" => "Ctrl",
        "LAlt" => "Alt",
        "LWin" or "RWin" => "Win",
        "Tab" => "Tab ⇥",
        "NumPadDiv" => "/",
        "NumPadMult" => "*",
        "NumPadSub" => "−",
        "NumPadAdd" => "+",
        "NumPadDot" => ".",
        "Mouse 1" or "Mouse 2" or "Mouse 3" => Loc.T($"kb.cap.{name.Replace(" ", "")}"),
        "Wheel Up" => "↑ " + Loc.T("kb.cap.wheel"),
        "Wheel Down" => "↓ " + Loc.T("kb.cap.wheel"),
        "HWheel Up" => Loc.T("kb.cap.wheel") + " →",
        "HWheel Down" => "← " + Loc.T("kb.cap.wheel"),
        _ when name.StartsWith("NumPad", StringComparison.Ordinal) && name.Length == 7 => name[^1..],
        _ => Loc.T("kb.cap." + name) is var t && t != "kb.cap." + name ? t : name,
    };
}

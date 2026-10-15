using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace KDesktop.Services;

public enum KdIconStyle { Tile, Glyph }

/// <summary>A K-Desktop 5 icon: a Fluent symbol, a colour of the palette and a style.</summary>
public sealed record KdIconSpec(string Symbol, string Color, KdIconStyle Style = KdIconStyle.Tile)
{
    public const string Prefix = "kd:";

    /// <summary>Value stored in the configuration: "kd:Folder24:yellow" or "kd:Folder24:yellow:glyph".</summary>
    public override string ToString() => $"{Prefix}{Symbol}:{Color}" + (Style == KdIconStyle.Glyph ? ":glyph" : "");

    public static bool TryParse(string? value, out KdIconSpec spec)
    {
        spec = null!;
        if (value is null || !value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var parts = value[Prefix.Length..].Split(':');
        if (parts.Length < 1 || parts[0].Length == 0) return false;
        spec = new KdIconSpec(parts[0], parts.Length > 1 ? parts[1] : "blue",
            parts.Length > 2 && parts[2].Equals("glyph", StringComparison.OrdinalIgnoreCase) ? KdIconStyle.Glyph : KdIconStyle.Tile);
        return true;
    }
}

/// <summary>
/// The K-Desktop 5 icon collection. Icons are vectors (a rounded tile with a gradient and a white Fluent symbol, or the
/// symbol alone painted with the gradient), so they are sharp at any size and any colour of the palette can be used.
/// </summary>
public static class KdIcons
{
    public sealed record Entry(string Symbol, string Color, string Category)
    {
        public KdIconSpec Spec => new(Symbol, Color);
        public string Name => Humanize(Symbol);
    }

    /// <summary>Palette: key → gradient (top, bottom).</summary>
    public static IReadOnlyList<(string Key, Color Top, Color Bottom)> Palette { get; } =
    [
        ("gold", Hex("#FFD45A"), Hex("#E09A0B")),
        ("yellow", Hex("#FFE066"), Hex("#F2B807")),
        ("orange", Hex("#FFB061"), Hex("#F06A12")),
        ("red", Hex("#FF7A7A"), Hex("#D9363E")),
        ("pink", Hex("#FF8CC6"), Hex("#DB3F86")),
        ("purple", Hex("#BC8CFF"), Hex("#7B3FE4")),
        ("indigo", Hex("#8C93FF"), Hex("#4A4FD9")),
        ("blue", Hex("#5AA9FF"), Hex("#1E62DA")),
        ("sky", Hex("#55D2FF"), Hex("#0C8FD6")),
        ("teal", Hex("#3EDCC1"), Hex("#0C9A87")),
        ("green", Hex("#6BDE79"), Hex("#209E3E")),
        ("lime", Hex("#C3EB5C"), Hex("#7CB518")),
        ("brown", Hex("#D2A375"), Hex("#8F5C34")),
        ("gray", Hex("#A9B2BD"), Hex("#606A77")),
        ("dark", Hex("#5B6573"), Hex("#1E2530")),
    ];

    public static IReadOnlyList<string> Categories { get; } = ["apps", "files", "system", "media", "web", "office", "fun", "symbols"];

    public static IReadOnlyList<Entry> All { get; } = Build();

    private static List<Entry> Build()
    {
        var list = new List<Entry>();
        void Add(string category, string color, params string[] symbols)
        {
            foreach (var s in symbols) list.Add(new Entry(s, color, category));
        }

        Add("apps", "blue", "Apps24", "AppsList24", "Window24", "WindowApps24", "WindowNew24", "TabDesktop24", "Open24");
        Add("apps", "gold", "Flash24", "Star24", "Sparkle24", "Rocket24", "Lightbulb24", "Trophy24");
        Add("apps", "indigo", "Navigation24", "Grid24", "Board24", "PanelLeft24", "MoreHorizontal24");
        Add("apps", "red", "Heart24", "Fire24", "Record24", "Pin24");
        Add("apps", "purple", "Bot24", "BrainCircuit24", "Cube24", "Diamond24", "Gift24");

        Add("files", "yellow", "Folder24", "FolderOpen24", "Archive24", "Box24");
        Add("files", "blue", "Document24", "DocumentText24", "Save24", "Copy24", "ClipboardPaste24", "Cut24");
        Add("files", "red", "DocumentPdf24");
        Add("files", "teal", "ArrowDownload24", "Cloud24", "Database24", "Storage24", "HardDrive24", "UsbStick24");
        Add("files", "green", "Image24", "Album24");
        Add("files", "gray", "Delete24", "Broom24", "Search24", "Filter24", "Tag24", "Bookmark24");

        Add("system", "gray", "Settings24", "Options24", "Wrench24", "WindowWrench24", "DeveloperBoard24", "Gauge24");
        Add("system", "dark", "WindowConsole20", "Code24", "CodeBlock24", "Braces24", "Bug24", "Branch24", "Server24");
        Add("system", "sky", "Desktop24", "Laptop24", "PhoneLaptop24", "Phone24", "Tv24", "Screenshot24", "Print24", "Keyboard24", "KeyboardLayoutFloat24", "Cursor24");
        Add("system", "red", "Power24", "ArrowClockwise24", "SignOut24", "Prohibited24");
        Add("system", "green", "Shield24", "ShieldLock24", "LockClosed24", "LockOpen24", "Key24", "Fingerprint24");
        Add("system", "blue", "Person24", "People24", "PersonAccounts24", "PeopleTeam24", "Accessibility24");
        Add("system", "teal", "Wifi124", "Bluetooth24", "Router24", "PlugConnected24", "Battery1024", "NetworkCheck24");
        Add("system", "indigo", "Sleep24", "WeatherMoon24", "Eye24", "EyeOff24", "Info24", "Timer24", "Clock24");

        Add("media", "pink", "Play24", "Pause24", "Next24", "Previous24", "MusicNote124", "MusicNote224", "Guitar24");
        Add("media", "teal", "Speaker224", "Speaker024", "SpeakerMute24", "SpeakerBluetooth24", "SoundSource24", "Headphones24", "HeadphonesSoundWave24", "Headset24");
        Add("media", "red", "Mic24", "MicOff24", "Live24", "Megaphone24");
        Add("media", "purple", "Video24", "VideoClip24", "Filmstrip24", "Camera24");

        Add("web", "sky", "Globe24", "GlobeDesktop24", "Earth24", "Link24", "Share24", "Rss24");
        Add("web", "blue", "Mail24", "Send24", "Chat24", "News24", "Translate24");
        Add("web", "orange", "Alert24", "CalendarLtr24", "Location24", "Map24");

        Add("office", "blue", "Calendar24", "Note24", "Notepad24", "NotepadEdit20", "Notebook24", "Book24", "BookOpen24", "Library24");
        Add("office", "green", "Calculator24", "Table24", "ChartMultiple24", "DataBarVertical24", "DataPie24", "DataTrending24");
        Add("office", "orange", "Edit24", "Pen24", "PaintBrush24", "Color24", "Ruler24", "Shapes24", "TextFont24", "TextBulletListLtr24", "TaskListLtr24", "Clipboard24");
        Add("office", "brown", "Briefcase24", "Building24", "Organization24", "Money24", "Wallet24", "Savings24", "Cart24", "ShoppingBag24", "Receipt24", "HatGraduation24");

        Add("fun", "green", "Games24", "XboxController24", "HeadsetVr24", "SportSoccer24", "SportBasketball24", "Ribbon24");
        Add("fun", "orange", "Food24", "DrinkCoffee24", "WeatherSunny24", "Airplane24", "VehicleCar24", "Home24", "Bed24");
        Add("fun", "pink", "Emoji24", "ThumbLike24", "HeartPulse24", "Pulse24", "Stethoscope24", "Beaker24", "Glasses24");

        Add("symbols", "gray", "ArrowLeft24", "ArrowRight24", "ArrowUp24", "ArrowDown24", "ArrowSync24", "ArrowUndo24", "ArrowRedo24", "ArrowCounterclockwise24");
        Add("symbols", "green", "Checkmark24", "Add24");
        Add("symbols", "red", "Dismiss24", "Subtract24", "Warning24");
        Add("symbols", "blue", "Question24", "Circle24", "Square24", "ZoomIn24");

        // Only the symbols this version of the icon font really has.
        return list.Where(e => Enum.TryParse<SymbolFilled>(e.Symbol, out _)).DistinctBy(e => e.Symbol).ToList();
    }

    public static (Color Top, Color Bottom) Gradient(string color)
    {
        foreach (var p in Palette)
            if (p.Key.Equals(color, StringComparison.OrdinalIgnoreCase)) return (p.Top, p.Bottom);
        return (Palette[7].Top, Palette[7].Bottom);
    }

    /// <summary>Every symbol of the font whose name contains <paramref name="query"/> (one size per symbol).</summary>
    public static IEnumerable<string> SearchAllSymbols(string query)
    {
        var q = query.Trim();
        if (q.Length < 2) yield break;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in Enum.GetNames<SymbolFilled>())
        {
            if (!name.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            var baseName = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (baseName.Length == 0 || !seen.Add(baseName)) continue;
            // Prefer the 24 px design of each symbol.
            yield return Enum.IsDefined(typeof(SymbolFilled), baseName + "24") ? baseName + "24" : name;
        }
    }

    // ── rendering ────────────────────────────────────────────────────────

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static GlyphTypeface? _font;

    public static ImageSource? Render(KdIconSpec spec) => Cache.GetOrAdd(spec.ToString(), _ => Draw(spec));

    private static ImageSource? Draw(KdIconSpec spec)
    {
        if (!Enum.TryParse<SymbolFilled>(spec.Symbol, out var symbol) || Font() is not { } font) return null;
        if (!font.CharacterToGlyphMap.TryGetValue((int)symbol, out var glyph)) return null;

        const double size = 48;
        var (top, bottom) = Gradient(spec.Color);
        var gradient = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));
        gradient.Freeze();

        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            // Fixed bounds so every icon keeps the same size and position.
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));

            if (spec.Style == KdIconStyle.Tile)
            {
                var tile = new Rect(2, 2, size - 4, size - 4);
                dc.DrawRoundedRectangle(gradient, null, tile, 11, 11);

                // Soft light on the upper half and a thin darker edge give it some depth.
                var shine = new LinearGradientBrush(Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0, 0.55));
                shine.Freeze();
                dc.DrawRoundedRectangle(shine, null, tile, 11, 11);
                var edge = new Pen(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), 1);
                edge.Freeze();
                dc.DrawRoundedRectangle(null, edge, new Rect(2.5, 2.5, size - 5, size - 5), 10.5, 10.5);

                var g = Glyph(font, glyph, 25, new Rect(0, 0, size, size));
                var shadow = new GeometryGroup { Children = { g }, Transform = new TranslateTransform(0, 1.2) };
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)), null, shadow);
                dc.DrawGeometry(Brushes.White, null, g);
            }
            else
            {
                dc.DrawGeometry(gradient, null, Glyph(font, glyph, 42, new Rect(0, 0, size, size)));
            }
        }
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    /// <summary>Glyph outline scaled so its larger side measures <paramref name="target"/>, centred in <paramref name="box"/>.</summary>
    private static Geometry Glyph(GlyphTypeface font, ushort glyph, double target, Rect box)
    {
        var outline = font.GetGlyphOutline(glyph, 100, 100);
        var b = outline.Bounds;
        if (b.IsEmpty || b.Width <= 0 || b.Height <= 0) return outline;
        var scale = target / Math.Max(b.Width, b.Height);
        var transform = new TransformGroup();
        transform.Children.Add(new TranslateTransform(-b.X - b.Width / 2, -b.Y - b.Height / 2));
        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new TranslateTransform(box.X + box.Width / 2, box.Y + box.Height / 2));
        return new GeometryGroup { FillRule = FillRule.Nonzero, Children = { outline }, Transform = transform };
    }

    private static GlyphTypeface? Font()
    {
        if (_font is not null) return _font;
        var family = Application.Current?.TryFindResource("FluentSystemIconsFilled") as FontFamily
                     ?? new FontFamily(new Uri("pack://application:,,,/Wpf.Ui;component/Resources/Fonts/"), "./#FluentSystemIcons-Filled");
        foreach (var typeface in family.GetTypefaces())
        {
            if (typeface.TryGetGlyphTypeface(out var gt))
            {
                _font = gt;
                break;
            }
        }
        if (_font is null) Core.Log.Warn("No se encontró la fuente de iconos Fluent");
        return _font;
    }

    /// <summary>"ArrowDownload24" → "Arrow download".</summary>
    public static string Humanize(string symbol)
    {
        var name = symbol.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        var sb = new System.Text.StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ').Append(char.ToLowerInvariant(c));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}

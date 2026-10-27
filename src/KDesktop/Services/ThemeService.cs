using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KDesktop.Core.Model;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace KDesktop.Services;

/// <summary>Applies the light/dark theme (WPF-UI controls + K-Desktop's own surfaces) and follows Windows when set to "System".</summary>
public static class ThemeService
{
    private static readonly Color Gold = Color.FromRgb(0xE0, 0xA8, 0x15);
    private static AppTheme _setting = AppTheme.System;

    public static bool IsDark { get; private set; } = true;

    /// <summary>Raised on the UI thread after the effective theme changes.</summary>
    public static event Action? Changed;

    public static void Initialize(AppTheme setting)
    {
        // Every window gets the light theme's cream veil over its Mica (WPF-UI leaves the background transparent).
        EventManager.RegisterClassHandler(typeof(FluentWindow), FrameworkElement.SizeChangedEvent,
            new SizeChangedEventHandler((sender, _) => Veil((Window)sender)));
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color && _setting == AppTheme.System)
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(_setting));
        };
        Apply(setting);
    }

    public static void Apply(AppTheme setting)
    {
        _setting = setting;
        var dark = setting switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => SystemUsesDarkTheme(),
        };
        IsDark = dark;

        var theme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        // The golds go before the theme too: its dictionary takes the accent there is when it loads, so on a live
        // switch the buttons kept the other theme's gold until a restart.
        ApplyAccent(dark, theme);
        // WPF-UI redoes each window's backdrop with a transparent background: the veil waits until it is done.
        _applying = true;
        try { ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, false); }
        finally { _applying = false; }
        ApplyAccent(dark, theme);
        ApplyAccentButtons(dark);
        SwapOwnDictionary(dark);
        WarmLightTheme(dark);
        ApplyLightOverrides(dark);
        foreach (var window in Application.Current.Windows.OfType<FluentWindow>()) Veil(window);
        Changed?.Invoke();
    }

    /// <summary>The dark theme's golds: the ones WPF-UI makes from <see cref="Gold"/>, worked out once.</summary>
    private static (Color Primary, Color Secondary, Color Tertiary)? _darkAccent;

    /// <summary>
    /// Both themes give the three golds outright: WPF-UI's own way (from one colour and the theme) left some of the
    /// other theme's on the buttons after a live switch.
    /// </summary>
    private static void ApplyAccent(bool dark, ApplicationTheme theme)
    {
        if (dark)
        {
            if (_darkAccent is null)
            {
                ApplicationAccentColorManager.Apply(Gold, theme, false, false);
                _darkAccent = (ApplicationAccentColorManager.PrimaryAccent, ApplicationAccentColorManager.SecondaryAccent,
                               ApplicationAccentColorManager.TertiaryAccent);
            }
            var (primary, secondary, tertiary) = _darkAccent.Value;
            ApplicationAccentColorManager.Apply(Gold, primary, secondary, tertiary);
        }
        else ApplicationAccentColorManager.Apply(Gold, Light.Accent, Light.AccentHover, Light.AccentPressed);
    }

    /// <summary>
    /// The gold buttons' fill, given for each theme: WPF-UI picks it from its theme dictionary, and after a live switch
    /// they kept the other theme's pick (the hover gold, or the pale yellow) until a restart.
    /// </summary>
    private static void ApplyAccentButtons(bool dark)
    {
        var (normal, hover, pressed) = dark && _darkAccent is var (_, secondary, _)
            ? (secondary, WithAlpha(secondary, 0xE6), WithAlpha(secondary, 0xCC))
            : (Light.Accent, Light.AccentHover, Light.AccentPressed);
        var resources = Application.Current.Resources;
        resources["AccentButtonBackground"] = Frozen(new SolidColorBrush(normal));
        resources["AccentButtonBackgroundPointerOver"] = Frozen(new SolidColorBrush(hover));
        resources["AccentButtonBackgroundPressed"] = Frozen(new SolidColorBrush(pressed));
    }

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static readonly System.ComponentModel.DependencyPropertyDescriptor BackgroundOf =
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Control.BackgroundProperty, typeof(Window));

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, object> Veiled = [];

    /// <summary>
    /// The window's background: <c>Kd.WindowTint</c> (cream in the light theme, nothing in the dark one). WPF-UI makes it
    /// transparent again when it applies the backdrop (after the first layout too), so it is put back every time.
    /// </summary>
    private static bool _applying;

    private static void Veil(Window window)
    {
        if (_applying) return;
        if (Veiled.TryAdd(window, true))
        {
            EventHandler changed = (_, _) => Veil(window);
            BackgroundOf.AddValueChanged(window, changed);
            window.Closed += (_, _) => BackgroundOf.RemoveValueChanged(window, changed);
        }
        if (window.Background != window.TryFindResource("Kd.WindowTint")) window.SetResourceReference(Control.BackgroundProperty, "Kd.WindowTint");
    }

    /// <summary>
    /// The light theme's surfaces: the window (sidebar and title bar), the content layer beside it, the cards on it and
    /// their edge, how far WPF-UI's neutral greys are warmed (0 = left neutral, 1 = cream), and the golds.
    /// </summary>
    private sealed record LightPalette(Color Window, Color Layer, Color Card, Color CardStroke, double Warmth,
                                       Color Accent, Color AccentHover, Color AccentPressed);

    /// <summary>«Arena» (chosen from three in 5.0.310: neutral, slightly warm and this one).</summary>
    private static readonly LightPalette Light = new(
        Rgb(0xE6, 0xDD, 0xCD), Rgb(0xF2, 0xEC, 0xE0), Rgb(0xFF, 0xFD, 0xF8), Color.FromArgb(0x24, 0x3D, 0x2A, 0x0A), 1,
        Rgb(0xE0, 0xA8, 0x15), Rgb(0xE8, 0xB8, 0x3A), Rgb(0xD0, 0x9A, 0x10));

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    /// <summary>The keys <see cref="WarmLightTheme"/> put in the application's resources (taken away in the dark theme).</summary>
    private static readonly HashSet<string> Warmed = [];

    /// <summary>
    /// WPF-UI's light theme is all white, the background hardly apart from the panels: the light palette sets the
    /// window, the content layer and the cards (with their edge) apart, and warms WPF-UI's neutral near-white surfaces
    /// (buttons, boxes, lists, menus) and black hairlines as far as it says. Texts stay as they are (see <see cref="LightOverrides"/>).
    /// </summary>
    private static void WarmLightTheme(bool dark)
    {
        var resources = Application.Current.Resources;
        foreach (var key in Warmed) resources.Remove(key);
        Warmed.Clear();
        if (dark || WpfUiTheme(resources) is not { } theme) return;
        foreach (var entry in theme.Keys.OfType<string>().ToList())
        {
            if (entry.StartsWith("Text", StringComparison.Ordinal) || entry.StartsWith("Accent", StringComparison.Ordinal)) continue;
            object? warm = theme[entry] switch
            {
                Color color when Warm(color) is { } c => c,
                SolidColorBrush brush when Warm(brush.Color) is { } c => Frozen(new SolidColorBrush(c)),
                _ => null,
            };
            if (warm is null) continue;
            resources[entry] = warm;
            Warmed.Add(entry);
        }
        // The palette's own surfaces: the window and the content layer apart, and the cards solid on them.
        (string Key, Color Color)[] brushes =
        [
            ("Kd.WindowTint", Light.Window),
            // QuickMenus, the launcher and the OSD in the palette's tones too.
            ("Kd.MenuTint", Color.FromArgb(0xD6, Light.Layer.R, Light.Layer.G, Light.Layer.B)),
            ("Kd.MenuTintOpaque", Light.Card),
            ("Kd.OsdBackground", Color.FromArgb(0xF2, Light.Card.R, Light.Card.G, Light.Card.B)),
        ];
        foreach (var (key, color) in brushes)
        {
            resources[key] = Frozen(new SolidColorBrush(color));
            Warmed.Add(key);
        }
        (string Key, Color Color)[] surfaces =
        [
            ("LayerFillColorDefault", Light.Layer),
            ("CardBackgroundFillColorDefault", Light.Card),
            ("CardStrokeColorDefault", Light.CardStroke),
        ];
        foreach (var (key, color) in surfaces)
        {
            resources[key] = color;
            resources[key + "Brush"] = Frozen(new SolidColorBrush(color));
            Warmed.Add(key);
            Warmed.Add(key + "Brush");
        }
        foreach (var key in (string[])["CardBackground", "ExpanderHeaderBackground"])
        {
            resources[key] = Frozen(new SolidColorBrush(Light.Card));
            Warmed.Add(key);
        }
    }

    /// <summary>
    /// A neutral light grey or white → warmer by the palette's <see cref="LightPalette.Warmth"/>; a neutral black
    /// hairline → warm brown; anything else (or a palette without warmth) stays.
    /// </summary>
    private static Color? Warm(Color c)
    {
        if (Light.Warmth <= 0 || c.R != c.G || c.G != c.B || c.A == 0) return null;
        if (c.R >= 0xE6) return Color.FromArgb(c.A, c.R, (byte)(c.G - 7 * Light.Warmth), (byte)(c.B - 20 * Light.Warmth));
        if (c.R == 0 && c.A <= 0x40) return Color.FromArgb(c.A, 0x4A, 0x32, 0x0A);
        return null;
    }

    private static SolidColorBrush Frozen(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>WPF-UI's light theme dictionary among the application's merged ones.</summary>
    private static ResourceDictionary? WpfUiTheme(ResourceDictionary resources)
    {
        foreach (var merged in resources.MergedDictionaries)
        {
            if (merged.Source?.OriginalString.Contains("theme/light", StringComparison.OrdinalIgnoreCase) == true) return merged;
            if (WpfUiTheme(merged) is { } inner) return inner;
        }
        return null;
    }

    /// <summary>
    /// WPF-UI's light text greys are made for its white cards: on K-Desktop's tinted surfaces the secondary and
    /// tertiary ones (descriptions, hints, summaries) were too faint to read. Same three-step hierarchy, darker. Gold
    /// is a light colour, so text on accent buttons goes dark instead of white. They go in the application's own
    /// resources because WPF-UI writes some of these keys there (a merged dictionary would lose against them).
    /// </summary>
    private static readonly (string Key, Color Color)[] LightOverrides =
    [
        ("TextFillColorPrimary", Color.FromArgb(0xF0, 0, 0, 0)),
        ("TextFillColorSecondary", Color.FromArgb(0xC2, 0, 0, 0)),
        ("TextFillColorTertiary", Color.FromArgb(0xA6, 0, 0, 0)),
        ("TextFillColorDisabled", Color.FromArgb(0x6B, 0, 0, 0)),
        ("TextPlaceholderColor", Color.FromArgb(0x8F, 0, 0, 0)),
        ("TextOnAccentFillColorPrimary", Color.FromRgb(0x1F, 0x15, 0x00)),
        ("TextOnAccentFillColorSecondary", Color.FromArgb(0xCC, 0x1F, 0x15, 0x00)),
        ("TextOnAccentFillColorSelectedText", Color.FromRgb(0x1F, 0x15, 0x00)),
    ];

    private static void ApplyLightOverrides(bool dark)
    {
        var resources = Application.Current.Resources;
        foreach (var (key, color) in LightOverrides)
        {
            if (!dark)
            {
                resources[key] = color;
                resources[key + "Brush"] = new SolidColorBrush(color);
                continue;
            }
            // Back to WPF-UI's dark values. The TextOnAccent colours are rewritten by ApplicationAccentColorManager.
            resources.Remove(key + "Brush");
            if (!key.StartsWith("TextOnAccent", StringComparison.Ordinal)) resources.Remove(key);
        }
        // Accent buttons take their text from these brushes (built once from the colours above in WPF-UI's theme).
        foreach (var key in (string[])["AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed"])
        {
            if (dark) resources.Remove(key);
            else resources[key] = new SolidColorBrush(key.EndsWith("Pressed", StringComparison.Ordinal) ? Color.FromArgb(0xCC, 0x1F, 0x15, 0x00) : Color.FromRgb(0x1F, 0x15, 0x00));
        }
    }

    private static void SwapOwnDictionary(bool dark)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        var source = new Uri($"pack://application:,,,/KDesktop;component/Themes/{(dark ? "KDark" : "KLight")}.xaml", UriKind.Absolute);
        var existing = merged.FirstOrDefault(d => d.Source is { } s && (s.OriginalString.Contains("KDark") || s.OriginalString.Contains("KLight")));
        var replacement = new ResourceDictionary { Source = source };
        if (existing is null) merged.Add(replacement);
        else merged[merged.IndexOf(existing)] = replacement;
    }

    public static bool SystemUsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int v || v == 0;
    }
}

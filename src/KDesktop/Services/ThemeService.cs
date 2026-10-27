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
        // Taking the light theme's values away also took WPF-UI's gold texts (same keys): written again.
        if (dark) ApplyAccent(dark, theme);
        ApplyLightOverrides(dark);
        foreach (var window in Application.Current.Windows.OfType<FluentWindow>())
        {
            Veil(window);
            // A window that started in the light theme (its Mica under the cream) kept Windows' light backdrop after a
            // switch to dark: the dark mode is told to Windows here too.
            if (new System.Windows.Interop.WindowInteropHelper(window).Handle is var handle && handle != IntPtr.Zero)
                Native.UiNative.SetDarkMode(handle, dark);
        }
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
    /// their edge, and the golds.
    /// </summary>
    private sealed record LightPalette(Color Window, Color Layer, Color Card, Color CardStroke,
                                       Color Accent, Color AccentHover, Color AccentPressed);

    /// <summary>«Arena» (chosen from three in 5.0.310: neutral, slightly warm and this one).</summary>
    private static readonly LightPalette Light = new(
        Rgb(0xE6, 0xDD, 0xCD), Rgb(0xF2, 0xEC, 0xE0), Rgb(0xFF, 0xFD, 0xF8), Rgb(0xD6, 0xC9, 0xB2),
        Rgb(0xE0, 0xA8, 0x15), Rgb(0xE8, 0xB8, 0x3A), Rgb(0xD0, 0x9A, 0x10));

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    /// <summary>The keys <see cref="WarmLightTheme"/> put in the application's resources (taken away in the dark theme).</summary>
    private static readonly HashSet<string> Warmed = [];

    /// <summary>
    /// WPF-UI's light theme is made for white: its controls are see-through white and its edges, dividers and hovers
    /// black at 2–15 %, which vanish on cream. Each of them gets a part of its own here instead: controls a solid fill a
    /// step darker than the panel with a firm edge, hairlines twice as strong and warm, the content of an expander a
    /// tone apart from its header, notices real colours, links a dark gold. Texts: see <see cref="LightOverrides"/>.
    /// </summary>
    private static void WarmLightTheme(bool dark)
    {
        var resources = Application.Current.Resources;
        foreach (var key in Warmed) resources.Remove(key);
        Warmed.Clear();
        if (dark || WpfUiTheme(resources) is not { } theme) return;

        void Set(string key, Color color)
        {
            resources[key] = Frozen(new SolidColorBrush(color));
            Warmed.Add(key);
        }
        void SetBoth(string key, Color color)
        {
            resources[key] = color;
            Warmed.Add(key);
            Set(key + "Brush", color);
        }

        // WPF-UI's own values, by what they are for (see Light.xaml of WPF-UI).
        foreach (var entry in theme.Keys.OfType<string>().ToList())
        {
            if (entry.StartsWith("TextFill", StringComparison.Ordinal) || entry.StartsWith("TextOnAccent", StringComparison.Ordinal)
                || entry.StartsWith("Accent", StringComparison.Ordinal)) continue;
            switch (theme[entry])
            {
                case Color color when ByPart(color) is { } c:
                    resources[entry] = c;
                    Warmed.Add(entry);
                    break;
                case SolidColorBrush brush when ByPart(brush.Color) is { } c:
                    Set(entry, c);
                    break;
            }
        }

        // The edges of controls are gradients (a darker bottom line): one firm warm line instead.
        foreach (var key in (string[])["ControlElevationBorderBrush", "TextControlElevationBorderBrush", "CircleElevationBorderBrush"])
            Set(key, LightControlEdge);

        // The palette's surfaces: the window, the content layer apart from it, the cards solid on it.
        Set("Kd.WindowTint", Light.Window);
        Set("Kd.MenuTint", Color.FromArgb(0xE6, Light.Layer.R, Light.Layer.G, Light.Layer.B));
        Set("Kd.MenuTintOpaque", Light.Card);
        Set("Kd.OsdBackground", Color.FromArgb(0xF5, Light.Card.R, Light.Card.G, Light.Card.B));
        SetBoth("LayerFillColorDefault", Light.Layer);
        SetBoth("CardBackgroundFillColorDefault", Light.Card);
        SetBoth("CardStrokeColorDefault", Light.CardStroke);
        Set("CardBackground", Light.Card);
        Set("ExpanderHeaderBackground", Light.Card);

        // Notices: real colours with an edge (the pale pink of «Los atajos están desactivados» hardly read as red).
        Set("InfoBarErrorSeverityBackgroundBrush", Rgb(0xF6, 0xD2, 0xCC));
        Set("InfoBarErrorSeverityBorderBrush", Rgb(0xE0, 0x9C, 0x92));
        Set("InfoBarWarningSeverityBackgroundBrush", Rgb(0xF8, 0xE3, 0xB0));
        Set("InfoBarWarningSeverityBorderBrush", Rgb(0xDD, 0xB8, 0x62));
        Set("InfoBarSuccessSeverityBackgroundBrush", Rgb(0xD6, 0xEB, 0xCF));
        Set("InfoBarSuccessSeverityBorderBrush", Rgb(0x9C, 0xC7, 0x8F));
        Set("InfoBarInformationalSeverityBackgroundBrush", LightInset);
        Set("InfoBarInformationalSeverityBorderBrush", LightControlEdge);

        // Links and gold texts: WPF-UI derives them from the gold, far too light to read on cream.
        SetBoth("AccentTextFillColorPrimary", Rgb(0x7A, 0x52, 0x00));
        SetBoth("AccentTextFillColorSecondary", Rgb(0x6A, 0x47, 0x00));
        SetBoth("AccentTextFillColorTertiary", Rgb(0x8A, 0x5E, 0x00));
        Set("HyperlinkButtonForeground", Rgb(0x7A, 0x52, 0x00));
        Set("HyperlinkButtonForegroundPointerOver", Rgb(0x6A, 0x47, 0x00));
        Set("HyperlinkButtonForegroundPressed", Rgb(0x8A, 0x5E, 0x00));
    }

    /// <summary>A control's fill on a card: a step darker than the card, so a box or a button stands out on it.</summary>
    private static readonly Color LightControl = Rgb(0xF3, 0xEC, 0xE0);
    private static readonly Color LightControlHover = Rgb(0xEC, 0xE3, 0xD3);
    private static readonly Color LightControlPressed = Rgb(0xE6, 0xDC, 0xCA);

    /// <summary>The edge of controls and notices.</summary>
    private static readonly Color LightControlEdge = Rgb(0xC9, 0xBA, 0xA0);

    /// <summary>A surface sunk into a card (an expander's content, informational notices).</summary>
    private static readonly Color LightInset = Rgb(0xF5, 0xEF, 0xE4);

    /// <summary>
    /// What a value of WPF-UI's light theme becomes, by the part it plays there: see-through white (controls), their
    /// hover and pressed greys, the solid whites of lists and menus, and black at a low alpha (edges, dividers,
    /// hovers), which comes out twice as strong in a warm brown. Anything else stays.
    /// </summary>
    private static Color? ByPart(Color c)
    {
        if (c.A == 0) return null;
        if (c.R == 0 && c.G == 0 && c.B == 0)
            return c.A <= 0x40 ? Color.FromArgb((byte)Math.Min(0x60, c.A * 2), 0x5A, 0x40, 0x14) : null;
        if (c.R != c.G || c.G != c.B) return null;
        return (c.A, c.R) switch
        {
            (0xB3, 0xFF) => LightControl,
            (0x80, 0xF9) => LightControlHover,
            (0x4D, 0xF9) => Color.FromArgb(0xB3, LightControlPressed.R, LightControlPressed.G, LightControlPressed.B),
            (0x80, 0xF6) => LightInset,
            (0x80, 0xFF) => Light.Layer,
            (0xFF, 0xFF) => Rgb(0xFF, 0xFE, 0xFB),
            (0xFF, 0xFA) => Light.Window,
            (0xFF, >= 0xE6) => Color.FromRgb(c.R, (byte)(c.G - 6), (byte)(c.B - 16)),
            (_, >= 0xE6) => Color.FromArgb(c.A, c.R, (byte)(c.G - 6), (byte)(c.B - 16)),
            _ => null,
        };
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
    /// tertiary ones (descriptions, hints, summaries) were too faint to read. Same three-step hierarchy, darker and warm. Gold
    /// is a light colour, so text on accent buttons goes dark instead of white. They go in the application's own
    /// resources because WPF-UI writes some of these keys there (a merged dictionary would lose against them).
    /// </summary>
    private static readonly (string Key, Color Color)[] LightOverrides =
    [
        ("TextFillColorPrimary", Color.FromArgb(0xF0, 0, 0, 0)),
        ("TextFillColorSecondary", Color.FromRgb(0x47, 0x3C, 0x2E)),
        ("TextFillColorTertiary", Color.FromRgb(0x5E, 0x51, 0x40)),
        ("TextFillColorDisabled", Color.FromRgb(0x9A, 0x8C, 0x77)),
        ("TextPlaceholderColor", Color.FromRgb(0x76, 0x69, 0x57)),
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

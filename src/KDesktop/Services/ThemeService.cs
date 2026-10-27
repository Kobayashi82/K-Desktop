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
        // WPF-UI redoes each window's backdrop with a transparent background: the veil waits until it is done.
        _applying = true;
        try { ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, false); }
        finally { _applying = false; }
        ApplicationAccentColorManager.Apply(Gold, theme, false, false);
        SwapOwnDictionary(dark);
        WarmLightTheme(dark);
        ApplyLightOverrides(dark);
        foreach (var window in Application.Current.Windows.OfType<FluentWindow>()) Veil(window);
        Changed?.Invoke();
    }

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

    /// <summary>The keys <see cref="WarmLightTheme"/> put in the application's resources (taken away in the dark theme).</summary>
    private static readonly HashSet<string> Warmed = [];

    /// <summary>
    /// WPF-UI's light theme is neutral white and grey, which looks cold and glaring: its near-white surfaces (cards,
    /// buttons, boxes, lists, menus) become cream of the same lightness and transparency, and its black hairlines
    /// (borders, dividers, hovers) a warm brown. Texts stay as they are (see <see cref="LightOverrides"/>).
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
        // The cards stand apart from the cream background: solid warm white with a firmer edge.
        foreach (var (key, color) in Cards)
        {
            resources[key] = color;
            resources[key + "Brush"] = Frozen(new SolidColorBrush(color));
            Warmed.Add(key);
            Warmed.Add(key + "Brush");
        }
        foreach (var key in (string[])["CardBackground", "ExpanderHeaderBackground"])
        {
            resources[key] = Frozen(new SolidColorBrush(Cards[0].Color));
            Warmed.Add(key);
        }
    }

    private static readonly (string Key, Color Color)[] Cards =
    [
        ("CardBackgroundFillColorDefault", Color.FromRgb(0xFE, 0xFA, 0xF3)),
        ("CardStrokeColorDefault", Color.FromArgb(0x26, 0x4A, 0x32, 0x0A)),
    ];

    /// <summary>A neutral light grey or white → cream; a neutral black hairline → warm brown; anything else stays.</summary>
    private static Color? Warm(Color c)
    {
        if (c.R != c.G || c.G != c.B || c.A == 0) return null;
        if (c.R >= 0xE6) return Color.FromArgb(c.A, c.R, (byte)(c.G - 7), (byte)(c.B - 20));
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

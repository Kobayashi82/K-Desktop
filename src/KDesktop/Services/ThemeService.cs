using System.Windows;
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
        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, false);
        ApplicationAccentColorManager.Apply(Gold, theme, false, false);
        SwapOwnDictionary(dark);
        ApplyLightOverrides(dark);
        Changed?.Invoke();
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

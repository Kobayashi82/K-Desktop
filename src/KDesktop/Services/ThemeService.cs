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
        Changed?.Invoke();
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

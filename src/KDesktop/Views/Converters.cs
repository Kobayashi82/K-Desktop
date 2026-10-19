using System.Globalization;
using System.Windows;
using System.Windows.Data;
using KDesktop.Core.Input;
using KDesktop.Core.Model;
using KDesktop.Services;

namespace KDesktop.Views;

/// <summary>Command (+ dependencies used only to re-trigger) → icon.</summary>
public sealed class CommandIconConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length > 0 && values[0] is Command cmd ? IconProvider.ForCommand(cmd) : null;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Action → icon (same as the command list uses).</summary>
public sealed class ActionIconConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length > 0 && values[0] is CommandAction a ? IconProvider.ForAction(a) : null;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>"CTRL + ALT + O" → ["CTRL", "ALT", "O"] (rendered as key caps).</summary>
public sealed class HotkeyPartsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string hk || hk.Length == 0) return Array.Empty<string>();
        var (mods, main) = Hotkey.Parse(hk);
        var parts = new List<string>();
        if (mods.HasFlag(Modifiers.Ctrl)) parts.Add("Ctrl");
        if (mods.HasFlag(Modifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(Modifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(Modifiers.Win)) parts.Add("Win");
        if (main.Length > 0) parts.AddRange(main.Split(Hotkey.Separator));
        return parts;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is not null && (value is not string s || s.Length > 0);
        if (Invert) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BoolToCollapsedConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) != Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>[item, open item] → Visible when they are the same object (the row whose editor is open).</summary>
public sealed class SameItemToVisibleConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is not null && ReferenceEquals(values[0], values[1]) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Item index (1-based) inside its ItemsControl, for numbered rows.</summary>
public sealed class IndexConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[1] is System.Collections.IList list ? (list.IndexOf(values[0]) + 1).ToString(culture) : "";

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Playback devices, read at most every couple of seconds (several converters ask at once).</summary>
public static class AudioDeviceCache
{
    private static IReadOnlyList<KDesktop.Core.Platform.AudioDeviceInfo> _devices = [];
    private static long _read;

    public static IReadOnlyList<KDesktop.Core.Platform.AudioDeviceInfo> Devices
    {
        get
        {
            if (Environment.TickCount64 - _read > 2000)
            {
                _devices = KDesktop.Core.Platform.AudioDevices.Playback();
                _read = Environment.TickCount64;
            }
            return _devices;
        }
    }

    public static void Invalidate() => _read = 0;

    public static KDesktop.Core.Platform.AudioDeviceInfo? Find(string id) => Devices.FirstOrDefault(d => d.Id == id);
}

/// <summary>Audio device → "Default" / "Not connected" / "".</summary>
public sealed class AudioDeviceStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not AudioDeviceRef d) return "";
        var info = AudioDeviceCache.Find(d.Id);
        return info is null || !info.IsActive ? KDesktop.Core.Localization.Loc.T("editor.audioDisconnected")
            : info.IsDefault ? KDesktop.Core.Localization.Loc.T("editor.audioDefault") : "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Audio device → Fluent symbol (speakers, headphones, screen…).</summary>
public sealed class AudioDeviceSymbolConverter : IValueConverter
{
    public static Wpf.Ui.Controls.SymbolRegular Symbol(KDesktop.Core.Platform.AudioFormFactor? f) => f switch
    {
        KDesktop.Core.Platform.AudioFormFactor.Headphones => Wpf.Ui.Controls.SymbolRegular.Headphones24,
        KDesktop.Core.Platform.AudioFormFactor.Headset => Wpf.Ui.Controls.SymbolRegular.Headset24,
        KDesktop.Core.Platform.AudioFormFactor.Monitor => Wpf.Ui.Controls.SymbolRegular.Desktop24,
        KDesktop.Core.Platform.AudioFormFactor.Digital => Wpf.Ui.Controls.SymbolRegular.SpeakerUsb24,
        _ => Wpf.Ui.Controls.SymbolRegular.Speaker224,
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Symbol(value is AudioDeviceRef d ? AudioDeviceCache.Find(d.Id)?.FormFactor : null);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>RemapKind → its translated name.</summary>
public sealed class RemapKindNameConverter : IValueConverter
{
    public static RemapKindNameConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is RemapKind k ? KDesktop.Core.Localization.Loc.T($"enum.remapKind.{k}") : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>"CTRL + CapsLock" → ["Ctrl", "Bloq Mayús"]: key caps with the names printed on the keys.</summary>
public sealed class KeyCapsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string hk || hk.Length == 0) return Array.Empty<string>();
        var (mods, main) = Hotkey.Parse(hk);
        var parts = new List<string>();
        if (mods.HasFlag(Modifiers.Ctrl)) parts.Add("Ctrl");
        if (mods.HasFlag(Modifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(Modifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(Modifiers.Win)) parts.Add("Win");
        if (main.Length > 0) parts.AddRange(main.Split(Hotkey.Separator).Select(KeyCaption));
        return parts;
    }

    /// <summary>Name printed on a key, with the side for the modifier keys ("LCtrl" → "Ctrl izq.").</summary>
    public static string KeyCaption(string name) => name switch
    {
        "LCtrl" or "LAlt" or "LShift" or "LWin" => KDesktop.Core.Localization.Loc.T("kb.left", name[1..]),
        "RCtrl" or "RShift" or "RWin" => KDesktop.Core.Localization.Loc.T("kb.right", name[1..]),
        _ => KBoardSurface.Caption(name),
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Event (+ dependencies used only to re-trigger) → its icon, or the one of its type.</summary>
public sealed class EventIconConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length > 0 && values[0] is EventRule rule ? IconProvider.ForEvent(rule) : null;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Command id (in an event) → its name (parameter "name") or icon (parameter "icon").</summary>
public sealed class EventCommandConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var command = value is string id ? App.Current.Config.FindCommand(id) : null;
        return parameter as string == "icon"
            ? command is null ? IconProvider.Kd("Warning24", "orange") : IconProvider.ForCommand(command)
            : command?.Name ?? Core.Localization.Loc.T("event.commandGone");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>When an event last fired: «Hoy 9:00», «Ayer 21:30», «12/10 9:00» or «Nunca».</summary>
public sealed class LastFiredConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not DateTime time) return Core.Localization.Loc.T("event.never");
        var loc = Core.Localization.Loc.Instance.Culture;
        var hour = time.ToString("H:mm", CultureInfo.InvariantCulture);
        if (time.Date == DateTime.Today) return Core.Localization.Loc.T("event.today", hour);
        if (time.Date == DateTime.Today.AddDays(-1)) return Core.Localization.Loc.T("event.yesterday", hour);
        return time.ToString(loc.DateTimeFormat.ShortDatePattern, loc) + " " + hour;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Text of a time box → visible (a warning) when it is not a valid «HH:mm»; empty is fine unless parameter is "required".</summary>
public sealed class InvalidTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;
        var invalid = text.Trim().Length == 0 ? parameter as string == "required" : EventTime.Parse(text) is null;
        return invalid ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>A monitor's GDI name → the resolutions and refresh rates it supports («Pantalla»).</summary>
public sealed class DisplayModesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string monitor && monitor.Length > 0 ? KDesktop.Core.Platform.DisplayTools.Modes(monitor) : Array.Empty<KDesktop.Core.Platform.DisplayModeInfo>();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>The indentation of an action inside the block of a «Si…» → the row's margin.</summary>
public sealed class IndentMarginConverter : IValueConverter
{
    public double Step { get; set; } = 28;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new Thickness(value is int indent ? indent * Step : 0, 0, 0, 8);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

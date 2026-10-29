using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KDesktop.Launcher;

public enum LauncherItemKind { Command, App, Web, Calculator, Clipboard }

/// <summary>One row of the launcher. The icon is only loaded when the row is drawn.</summary>
public sealed partial class LauncherItem(LauncherItemKind kind, string title, string subtitle, string badge, Func<ImageSource?> icon, object? payload)
    : ObservableObject
{
    private ImageSource? _icon;
    private bool _iconLoaded;

    public LauncherItemKind Kind { get; } = kind;
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public string Badge { get; } = badge;

    /// <summary>The command, app, URL or text to copy.</summary>
    public object? Payload { get; } = payload;

    public ImageSource? Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _iconLoaded = true;
                try { _icon = icon(); } catch { _icon = null; }
            }
            return _icon;
        }
    }

    [ObservableProperty] private bool _isSelected;
}

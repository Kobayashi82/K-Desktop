using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using KDesktop.Core.Platform;

namespace KDesktop.Launcher;

/// <summary>One row of the clipboard history, as a launcher row. The icon (a picture's thumbnail) is only loaded when drawn.</summary>
public sealed partial class ClipboardItem(ClipboardEntry entry, string title, string subtitle, string badge, Func<ImageSource?> icon, Func<ImageSource?>? preview)
    : ObservableObject
{
    private ImageSource? _icon, _preview;
    private bool _iconLoaded, _previewLoaded;

    public ClipboardEntry Entry { get; } = entry;
    public string Title { get; } = title;
    public string Subtitle { get; } = subtitle;
    public string Badge { get; } = badge;
    public bool IsPinned => Entry.Pinned;
    public bool IsImage => Entry.Kind == ClipKind.Image;

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

    /// <summary>A bigger picture for the tooltip of an image row (null for the others).</summary>
    public ImageSource? Preview
    {
        get
        {
            if (!_previewLoaded)
            {
                _previewLoaded = true;
                try { _preview = preview?.Invoke(); } catch { _preview = null; }
            }
            return _preview;
        }
    }

    [ObservableProperty] private bool _isSelected;
}

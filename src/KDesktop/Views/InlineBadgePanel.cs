using System.Windows;
using System.Windows.Controls;

namespace KDesktop.Views;

/// <summary>
/// A text followed right away by a small badge ("+2"): the badge always fits, the text is the one that gets
/// trimmed. First child = the text, second = the badge.
/// </summary>
public sealed class InlineBadgePanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count < 2) return default;
        UIElement text = InternalChildren[0], badge = InternalChildren[1];
        badge.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        var badgeWidth = badge.DesiredSize.Width;
        text.Measure(new Size(Math.Max(0, availableSize.Width - badgeWidth), availableSize.Height));
        return new Size(text.DesiredSize.Width + badgeWidth, Math.Max(text.DesiredSize.Height, badge.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count < 2) return finalSize;
        UIElement text = InternalChildren[0], badge = InternalChildren[1];
        var badgeWidth = badge.DesiredSize.Width;
        var textWidth = Math.Min(text.DesiredSize.Width, Math.Max(0, finalSize.Width - badgeWidth));
        text.Arrange(new Rect(0, 0, textWidth, finalSize.Height));
        badge.Arrange(new Rect(textWidth, (finalSize.Height - badge.DesiredSize.Height) / 2, badgeWidth, badge.DesiredSize.Height));
        return finalSize;
    }
}

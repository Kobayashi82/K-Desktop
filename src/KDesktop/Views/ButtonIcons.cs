using Wpf.Ui.Controls;

namespace KDesktop.Views;

/// <summary>Icons of the buttons made in code: the trash can in red (the theme's danger colour), as in the pages.</summary>
internal static class ButtonIcons
{
    public static SymbolIcon For(SymbolRegular symbol)
    {
        var icon = new SymbolIcon { Symbol = symbol };
        if (symbol == SymbolRegular.Delete24) icon.SetResourceReference(SymbolIcon.ForegroundProperty, "Kd.Danger");
        return icon;
    }
}

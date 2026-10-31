using System.Globalization;
using System.Windows.Data;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using Wpf.Ui.Controls;

namespace KDesktop.Views;

/// <summary>The icon of each figure of «Paneles», in its row and in «Añadir dato».</summary>
public sealed class PanelItemSymbol : IValueConverter
{
    public static PanelItemSymbol Instance { get; } = new();

    public static SymbolRegular Of(PanelItemKind kind) => kind switch
    {
        PanelItemKind.NetUp => SymbolRegular.ArrowUp24,
        PanelItemKind.NetDown => SymbolRegular.ArrowDown24,
        PanelItemKind.Cpu => SymbolRegular.DeveloperBoard24,
        PanelItemKind.Ram => SymbolRegular.Ram20,
        PanelItemKind.Gpu => SymbolRegular.Games24,
        PanelItemKind.Vram => SymbolRegular.Memory16,
        PanelItemKind.Spacer => SymbolRegular.BorderNone24,
        _ => SymbolRegular.Temperature24,
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is PanelItemKind kind ? Of(kind) : SymbolRegular.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>What a figure is («Procesador (CPU)»…).</summary>
public sealed class PanelItemName : IValueConverter
{
    public static PanelItemName Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is PanelItemKind kind ? Loc.T("enum.panelItem." + kind) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>The usual short name of a figure («CPU», «↑»…), the hint of its name box.</summary>
public sealed class PanelItemLabel : IValueConverter
{
    public static PanelItemLabel Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is PanelItemKind kind ? InfoPanelItem.DefaultLabel(kind) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

using System.Windows;
using System.Windows.Controls;

namespace KDesktop.Views;

/// <summary>
/// The header of an editor: the name box in the first column of a Grid and, beside it, the element marked
/// <c>IsSide</c> (switch, run, duplicate, delete…). When the window gets so narrow that the name would be left
/// narrower than <c>MinNameWidth</c>, the side element moves to the Grid's last row, under the name, instead of
/// squeezing the name box away.
/// </summary>
public static class ResponsiveHeader
{
    public static readonly DependencyProperty MinNameWidthProperty = DependencyProperty.RegisterAttached(
        "MinNameWidth", typeof(double), typeof(ResponsiveHeader), new PropertyMetadata(0.0, OnMinNameWidthChanged));

    public static double GetMinNameWidth(DependencyObject d) => (double)d.GetValue(MinNameWidthProperty);
    public static void SetMinNameWidth(DependencyObject d, double value) => d.SetValue(MinNameWidthProperty, value);

    public static readonly DependencyProperty IsSideProperty = DependencyProperty.RegisterAttached(
        "IsSide", typeof(bool), typeof(ResponsiveHeader), new PropertyMetadata(false));

    public static bool GetIsSide(DependencyObject d) => (bool)d.GetValue(IsSideProperty);
    public static void SetIsSide(DependencyObject d, bool value) => d.SetValue(IsSideProperty, value);

    /// <summary>Where the side element sits when there is room (and its own margin and natural width there).</summary>
    private sealed record Home(int Row, int Column, Thickness Margin, HorizontalAlignment Alignment)
    {
        public double Width { get; set; }
    }

    private static readonly DependencyProperty HomeProperty = DependencyProperty.RegisterAttached(
        "Home", typeof(Home), typeof(ResponsiveHeader));

    private static void OnMinNameWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Grid grid) return;
        grid.SizeChanged -= OnSizeChanged;
        grid.SizeChanged += OnSizeChanged;
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e) => Update((Grid)sender);

    private static void Update(Grid grid)
    {
        var side = grid.Children.OfType<FrameworkElement>().FirstOrDefault(GetIsSide);
        if (side is null || grid.RowDefinitions.Count < 2) return;
        if (side.GetValue(HomeProperty) is not Home home)
        {
            home = new Home(Grid.GetRow(side), Grid.GetColumn(side), side.Margin, side.HorizontalAlignment);
            side.SetValue(HomeProperty, home);
        }
        var atHome = Grid.GetRow(side) == home.Row;
        // At home it is measured with all the room it wants: that is its natural width.
        if (atHome) home.Width = side.DesiredSize.Width;

        var others = 0.0;
        for (var c = 1; c < grid.ColumnDefinitions.Count; c++)
            if (c != home.Column) others += grid.ColumnDefinitions[c].ActualWidth;
        var wide = grid.ActualWidth - others - home.Width >= GetMinNameWidth(grid);
        if (wide == atHome) return;

        if (wide)
        {
            Grid.SetRow(side, home.Row);
            Grid.SetColumn(side, home.Column);
            Grid.SetColumnSpan(side, 1);
            side.Margin = home.Margin;
            side.HorizontalAlignment = home.Alignment;
        }
        else
        {
            Grid.SetRow(side, grid.RowDefinitions.Count - 1);
            Grid.SetColumn(side, 0);
            Grid.SetColumnSpan(side, Math.Max(1, grid.ColumnDefinitions.Count));
            side.Margin = new Thickness(0, 10, 0, 0);
            side.HorizontalAlignment = HorizontalAlignment.Left;
        }
    }
}

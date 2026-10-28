using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;
using Border = System.Windows.Controls.Border;

namespace KDesktop.Views;

/// <summary>
/// A CardExpander that opens and folds smoothly, with what is under it moving along. WPF-UI shows the content at once
/// (everything below jumps) and then slides it in with a render transform, which takes no room; folding, it slides it
/// out and then takes it away (everything below jumps again). Here the content sits in a <see cref="FoldPanel"/> that
/// takes the room of the part shown, following the same animation value, and the slide is done by it, not by the
/// transform. Set by the implicit CardExpander style (Styles.xaml).
/// </summary>
public static class SmoothExpander
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(SmoothExpander), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CardExpander expander || e.NewValue is not true) return;
        expander.Loaded += (_, _) => Hook(expander);
        if (expander.IsLoaded) Hook(expander);
    }

    private static void Hook(CardExpander expander)
    {
        expander.ApplyTemplate();
        if (expander.Template?.FindName("ContentPresenterBorder", expander) is not Border border || border.Child is FoldPanel or null) return;
        if ((s_slide ??= SlideProperty(expander.Template)) is not { } slide) return;
        var content = border.Child;
        border.Child = null;
        var fold = new FoldPanel { Child = content, ClipToBounds = true };
        // The template's slide: 1 = hidden (above), 0 = in place.
        fold.SetBinding(FoldPanel.HiddenProperty, new Binding { Source = border, Path = new PropertyPath("(0)", slide) });
        border.Child = fold;
        // The slide is the panel's now, inside the room it takes.
        border.RenderTransform = Transform.Identity;
    }

    private static DependencyProperty? s_slide;

    /// <summary>The value the template animates for the slide (WPF-UI's AnimationTagValue, internal): taken from its storyboards.</summary>
    private static DependencyProperty? SlideProperty(ControlTemplate template) =>
        template.Triggers.OfType<Trigger>()
            .SelectMany(t => t.EnterActions.Concat(t.ExitActions)).OfType<BeginStoryboard>()
            .SelectMany(b => b.Storyboard.Children).OfType<DoubleAnimationUsingKeyFrames>()
            .SelectMany(a => Storyboard.GetTargetProperty(a)?.PathParameters.OfType<DependencyProperty>() ?? [])
            .FirstOrDefault(p => p.Name == "AnimationTagValue");
}

/// <summary>Takes the room of the part of its child shown (1 − <see cref="Hidden"/>), the child sliding in from above.</summary>
public sealed class FoldPanel : Decorator
{
    public static readonly DependencyProperty HiddenProperty =
        DependencyProperty.Register(nameof(Hidden), typeof(double), typeof(FoldPanel),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double Hidden { get => (double)GetValue(HiddenProperty); set => SetValue(HiddenProperty, value); }

    private double Shown => 1 - Math.Clamp(Hidden, 0, 1);

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null) return default;
        Child.Measure(new Size(constraint.Width, double.PositiveInfinity));
        var size = Child.DesiredSize;
        return new Size(size.Width, size.Height * Shown);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is null) return arrangeSize;
        var height = Child.DesiredSize.Height;
        Child.Arrange(new Rect(0, -height * (1 - Shown), arrangeSize.Width, height));
        return arrangeSize;
    }
}

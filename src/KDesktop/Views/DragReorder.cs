using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace KDesktop.Views;

/// <summary>Arguments of <see cref="DragReorder.ReorderedEvent"/>.</summary>
public sealed class ReorderedEventArgs(RoutedEvent e, object item, int from, int to) : RoutedEventArgs(e)
{
    public object Item { get; } = item;
    public int From { get; } = from;
    public int To { get; } = to;
}

/// <summary>
/// Drag-to-reorder for a ListBox bound to an ObservableCollection. While dragging, a floating copy of the item follows
/// the mouse and the other items slide out of the way; the list scrolls near its edges and Escape cancels.
/// Mark an element of the item template with <c>DragReorder.IsHandle</c> to only start dragging from it (needed when
/// the item contains text boxes); otherwise the whole item can be dragged.
/// </summary>
public static class DragReorder
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(DragReorder), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    public static readonly DependencyProperty IsHandleProperty =
        DependencyProperty.RegisterAttached("IsHandle", typeof(bool), typeof(DragReorder), new PropertyMetadata(false));

    public static bool GetIsHandle(DependencyObject d) => (bool)d.GetValue(IsHandleProperty);
    public static void SetIsHandle(DependencyObject d, bool value) => d.SetValue(IsHandleProperty, value);

    /// <summary>Raised on the ListBox after an item was moved.</summary>
    public static readonly RoutedEvent ReorderedEvent =
        EventManager.RegisterRoutedEvent("Reordered", RoutingStrategy.Bubble, typeof(EventHandler<ReorderedEventArgs>), typeof(DragReorder));

    public static void AddReorderedHandler(DependencyObject d, EventHandler<ReorderedEventArgs> handler) =>
        (d as UIElement)?.AddHandler(ReorderedEvent, handler);

    public static void RemoveReorderedHandler(DependencyObject d, EventHandler<ReorderedEventArgs> handler) =>
        (d as UIElement)?.RemoveHandler(ReorderedEvent, handler);

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached("State", typeof(DragState), typeof(DragReorder));

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox list) return;
        if ((bool)e.NewValue)
        {
            var state = new DragState(list);
            list.SetValue(StateProperty, state);
            list.PreviewMouseLeftButtonDown += state.OnDown;
            list.PreviewMouseMove += state.OnMove;
            list.PreviewMouseLeftButtonUp += state.OnUp;
            list.LostMouseCapture += state.OnLostCapture;
        }
        else if (list.GetValue(StateProperty) is DragState state)
        {
            list.PreviewMouseLeftButtonDown -= state.OnDown;
            list.PreviewMouseMove -= state.OnMove;
            list.PreviewMouseLeftButtonUp -= state.OnUp;
            list.LostMouseCapture -= state.OnLostCapture;
            list.ClearValue(StateProperty);
        }
    }

    private sealed class DragState(ListBox list)
    {
        private readonly DispatcherTimer _scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
        private Point _start;
        private ListBoxItem? _pressed;
        private ListBoxItem? _dragged;
        private int _from, _to;
        private double _height;
        private GhostAdorner? _ghost;
        private ScrollViewer? _scroller;
        private Window? _window;
        private double _scrollSpeed;
        private readonly Dictionary<ListBoxItem, double> _offsets = [];

        public void OnDown(object sender, MouseButtonEventArgs e)
        {
            _pressed = null;
            if (e.OriginalSource is not DependencyObject source) return;
            if (ItemsControl.ContainerFromElement(list, source) is not ListBoxItem container) return;
            if (!CanStartFrom(source, container)) return;
            _pressed = container;
            _start = e.GetPosition(list);
        }

        public void OnMove(object sender, MouseEventArgs e)
        {
            if (_dragged is not null)
            {
                // Handled: otherwise the ListBox "drag-selects" (and expands) the items under the mouse.
                e.Handled = true;
                Update(e.GetPosition(list));
                return;
            }
            if (_pressed is null || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(list);
            if (Math.Abs(p.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance * 2 &&
                Math.Abs(p.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance * 2) return;
            Begin(_pressed, p);
            if (_dragged is not null) e.Handled = true;
        }

        public void OnUp(object sender, MouseButtonEventArgs e)
        {
            _pressed = null;
            if (_dragged is null) return;
            e.Handled = true;
            End(commit: true);
        }

        public void OnLostCapture(object sender, MouseEventArgs e)
        {
            // The ListBox losing its capture to the dragged item is expected; anything else cancels.
            if (_dragged is not null && !ReferenceEquals(Mouse.Captured, _dragged)) End(commit: false);
        }

        /// <summary>Not from text boxes, buttons, combosâ€¦ unless the element is a drag handle.</summary>
        private static bool CanStartFrom(DependencyObject source, ListBoxItem container)
        {
            var hasHandles = FindHandle(container);
            for (var d = source; d is not null && d != container; d = Parent(d))
            {
                if (GetIsHandle(d)) return true;
                if (d is System.Windows.Controls.Primitives.TextBoxBase or ButtonBase or ComboBox or Slider or ScrollBar or PasswordBox) return false;
            }
            return !hasHandles;
        }

        private static bool FindHandle(DependencyObject root)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (GetIsHandle(child) || FindHandle(child)) return true;
            }
            return false;
        }

        private static DependencyObject? Parent(DependencyObject d) =>
            d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

        private void Begin(ListBoxItem container, Point p)
        {
            var index = list.ItemContainerGenerator.IndexFromContainer(container);
            if (index < 0 || list.ItemsSource is not IList { Count: > 1 }) return;

            // Items that expand when selected (the action editor) are dragged folded; End() selects it again.
            list.SelectedItem = null;
            list.UpdateLayout();
            p = Mouse.GetPosition(list);
            _dragged = container;
            _from = _to = index;
            _height = container.ActualHeight + container.Margin.Top + container.Margin.Bottom;
            _scroller = FindScroller(list);
            _window = Window.GetWindow(list);
            if (_window is not null) _window.PreviewKeyDown += OnKey;

            // The picture is taken once the folded item has been rendered again.
            list.Dispatcher.BeginInvoke(() =>
            {
                if (!ReferenceEquals(_dragged, container)) return;
                if (AdornerLayer.GetAdornerLayer(list) is { } layer)
                {
                    var origin = container.TranslatePoint(new Point(0, 0), list);
                    _ghost = new GhostAdorner(list, container, origin, p.Y);
                    _ghost.MoveTo(Mouse.GetPosition(list).Y);
                    layer.Add(_ghost);
                }
                container.Opacity = 0; // the ghost replaces it; the others slide over its slot
            }, DispatcherPriority.Loaded);
            // Captured by the item, not the ListBox: a captured ListBox "drag-selects" whatever the mouse enters.
            container.CaptureMouse();
            _scrollTimer.Tick += OnScrollTick;
            _scrollTimer.Start();
        }

        private void Update(Point p)
        {
            _ghost?.MoveTo(p.Y);
            UpdateTarget(p.Y);

            // Auto-scroll near the edges of the scrolling area.
            _scrollSpeed = 0;
            if (_scroller is not null)
            {
                var y = list.TranslatePoint(p, _scroller).Y;
                const double edge = 48;
                if (y < edge) _scrollSpeed = -Math.Min(18, (edge - y) / 2.5);
                else if (y > _scroller.ViewportHeight - edge) _scrollSpeed = Math.Min(18, (y - (_scroller.ViewportHeight - edge)) / 2.5);
            }
        }

        private void OnScrollTick(object? sender, EventArgs e)
        {
            if (_scroller is null || _scrollSpeed == 0 || _dragged is null) return;
            _scroller.ScrollToVerticalOffset(_scroller.VerticalOffset + _scrollSpeed);
            var p = Mouse.GetPosition(list);
            _ghost?.MoveTo(p.Y);
            UpdateTarget(p.Y);
        }

        /// <summary>Target index = items (other than the dragged one) whose middle is above the mouse.</summary>
        private void UpdateTarget(double mouseY)
        {
            if (_dragged is null) return;
            var count = list.Items.Count;
            var to = 0;
            for (var i = 0; i < count; i++)
            {
                if (i == _from) continue;
                if (list.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem c || !c.IsVisible)
                {
                    // Not realized (virtualized): items before the visible ones are above the mouse.
                    if (i < FirstRealized()) to++;
                    continue;
                }
                var shift = c.RenderTransform is TranslateTransform t ? t.Y : 0;
                var top = c.TranslatePoint(new Point(0, 0), list).Y - shift;
                if (top + c.ActualHeight / 2 < mouseY) to++;
            }
            if (to == _to) return;
            _to = to;

            for (var i = 0; i < count; i++)
            {
                if (i == _from || list.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem c) continue;
                var offset = _to > _from && i > _from && i <= _to ? -_height
                           : _to < _from && i >= _to && i < _from ? _height
                           : 0;
                if (_offsets.GetValueOrDefault(c) == offset) continue;
                _offsets[c] = offset;
                if (c.RenderTransform is not TranslateTransform tt || tt.IsFrozen)
                {
                    tt = new TranslateTransform();
                    c.RenderTransform = tt;
                }
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, TimeSpan.FromMilliseconds(160)) { EasingFunction = EaseOut });
            }
        }

        private int FirstRealized()
        {
            for (var i = 0; i < list.Items.Count; i++)
                if (list.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem { IsVisible: true }) return i;
            return int.MaxValue;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || _dragged is null) return;
            e.Handled = true;
            End(commit: false);
        }

        private void End(bool commit)
        {
            var dragged = _dragged;
            if (dragged is null) return;
            _dragged = null;
            _pressed = null;
            _scrollTimer.Stop();
            _scrollTimer.Tick -= OnScrollTick;
            if (_window is not null) _window.PreviewKeyDown -= OnKey;
            if (_ghost is not null) AdornerLayer.GetAdornerLayer(list)?.Remove(_ghost);
            _ghost = null;
            if (dragged.IsMouseCaptured) dragged.ReleaseMouseCapture();

            foreach (var c in _offsets.Keys)
                if (c.RenderTransform is TranslateTransform t) t.BeginAnimation(TranslateTransform.YProperty, null);
            _offsets.Clear();
            dragged.Opacity = 1;

            if (!commit || _to == _from || list.ItemsSource is not IList items)
            {
                list.SelectedItem = list.ItemContainerGenerator.ItemFromContainer(dragged);
                return;
            }
            var item = items[_from]!;
            var move = items.GetType().GetMethod("Move", [typeof(int), typeof(int)]);
            if (move is not null) move.Invoke(items, [_from, _to]);
            else
            {
                items.RemoveAt(_from);
                items.Insert(_to, item);
            }
            list.SelectedItem = item;

            // Little "landing" pulse on the moved item.
            list.Dispatcher.BeginInvoke(() =>
            {
                if (list.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem landed)
                {
                    // Stop, not HoldEnd: a held opacity would keep the item visible under its ghost the next time.
                    landed.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(260)) { FillBehavior = FillBehavior.Stop });
                }
            }, DispatcherPriority.Loaded);
            list.RaiseEvent(new ReorderedEventArgs(ReorderedEvent, item, _from, _to));
        }

        private static ScrollViewer? FindScroller(DependencyObject d)
        {
            // The nearest ancestor (or own) ScrollViewer that can actually scroll vertically.
            for (var p = d; p is not null; p = VisualTreeHelper.GetParent(p))
                if (p is ScrollViewer sv && sv.ScrollableHeight > 0) return sv;
            return FindChildScroller(d);
        }

        private static ScrollViewer? FindChildScroller(DependencyObject d)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                if (child is ScrollViewer sv) return sv;
                if (FindChildScroller(child) is { } found) return found;
            }
            return null;
        }
    }

    /// <summary>Floating, slightly enlarged copy of the dragged item.</summary>
    private sealed class GhostAdorner : Adorner
    {
        private readonly Rectangle _visual;
        private readonly TranslateTransform _shift = new();
        private readonly double _grabOffset;
        private readonly Point _origin;

        public GhostAdorner(UIElement adorned, FrameworkElement item, Point origin, double mouseY) : base(adorned)
        {
            IsHitTestVisible = false;
            _origin = origin;
            _grabOffset = mouseY - origin.Y;
            _visual = new Rectangle
            {
                Width = item.ActualWidth,
                Height = item.ActualHeight,
                Fill = Snapshot(item),
                Opacity = 0.92,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 6, Direction = 270, Opacity = 0.35 },
            };
            var scale = new ScaleTransform(1, 1);
            _visual.RenderTransform = new TransformGroup { Children = { scale, _shift } };
            _shift.Y = origin.Y;
            AddVisualChild(_visual);
            var grow = new DoubleAnimation(1, 1.02, TimeSpan.FromMilliseconds(140)) { EasingFunction = EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }

        /// <summary>Picture of the item taken before it is dimmed.</summary>
        private static Brush Snapshot(FrameworkElement item)
        {
            var dpi = VisualTreeHelper.GetDpi(item);
            var w = Math.Max(1, item.ActualWidth);
            var h = Math.Max(1, item.ActualHeight);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            // The brush draws the item where it sits in its parent: the view box starts at that offset (at 0,0 only
            // the first item of the list came out; any other was an empty picture).
            var offset = VisualTreeHelper.GetOffset(item);
            using (var ctx = drawing.RenderOpen()) ctx.DrawRectangle(new VisualBrush(item)
            {
                // Exactly the item bounds (its content bounds may be larger, which would squeeze the picture).
                Viewbox = new Rect(offset.X, offset.Y, w, h), ViewboxUnits = BrushMappingMode.Absolute,
            }, null, new Rect(0, 0, w, h));
            bitmap.Render(drawing);
            bitmap.Freeze();
            return new ImageBrush(bitmap);
        }

        public void MoveTo(double mouseY)
        {
            var max = ((FrameworkElement)AdornedElement).ActualHeight - _visual.Height;
            _shift.Y = Math.Clamp(mouseY - _grabOffset, -_visual.Height / 2, Math.Max(0, max) + _visual.Height / 2);
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _visual;

        protected override Size MeasureOverride(Size constraint)
        {
            _visual.Measure(new Size(_visual.Width, _visual.Height));
            return base.MeasureOverride(constraint);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _visual.Arrange(new Rect(_origin.X, 0, _visual.Width, _visual.Height));
            return finalSize;
        }
    }
}

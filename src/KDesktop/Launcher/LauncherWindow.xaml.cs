using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KDesktop.Native;
using KDesktop.Services;

namespace KDesktop.Launcher;

/// <summary>The launcher's search box and result list. Logic lives in <see cref="LauncherController"/>.</summary>
public partial class LauncherWindow : Window
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private bool _backdrop;

    public LauncherWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        SearchBox.TextChanged += (_, _) =>
        {
            Placeholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            QueryChanged?.Invoke(SearchBox.Text);
        };
        PreviewKeyDown += OnKeyDown;
        // Dragged from its edge or the lens (the text box and the results are for typing and choosing).
        Surface.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && (IsInside(d, SearchBox) || IsInside(d, ResultsControl) || IsInside(d, CloseButton))) return;
            try { DragMove(); } catch (InvalidOperationException) { return; }
            Dropped?.Invoke();
        };
        ResultsControl.MouseMove += (_, e) =>
        {
            if (ItemFrom(e.OriginalSource) is { } item) ItemHovered?.Invoke(item);
        };
        ResultsControl.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (ItemFrom(e.OriginalSource) is not { } item) return;
            e.Handled = true;
            ItemClicked?.Invoke(item);
        };
    }

    public IntPtr Hwnd { get; private set; }

    public event Action<string>? QueryChanged;
    public event Action<int>? MoveRequested;
    public event Action? ExecuteRequested;
    public event Action? CloseRequested;
    public event Action<LauncherItem>? ItemHovered;
    public event Action<LauncherItem>? ItemClicked;

    /// <summary>The user dragged the window somewhere else.</summary>
    public event Action? Dropped;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Hwnd = new WindowInteropHelper(this).Handle;
        if (HwndSource.FromHwnd(Hwnd) is { } src) src.CompositionTarget.BackgroundColor = Colors.Transparent;
        UiNative.AddExStyle(Hwnd, toolWindow: true, noActivate: false);
        UiNative.SetCorners(Hwnd, UiNative.Corner.Round);
        _backdrop = UiNative.SetBackdrop(Hwnd, UiNative.Backdrop.Acrylic);
        ApplyTheme();
    }

    public void ApplyTheme()
    {
        if (Hwnd == IntPtr.Zero) return;
        UiNative.SetDarkMode(Hwnd, ThemeService.IsDark);
        Surface.SetResourceReference(Border.BackgroundProperty, _backdrop ? "Kd.MenuTint" : "Kd.MenuTintOpaque");
    }

    public string Query => SearchBox.Text;

    /// <summary>Empties the box without raising a search (the controller fills the list itself).</summary>
    public void ResetQuery()
    {
        SearchBox.Text = string.Empty;
        Placeholder.Visibility = Visibility.Visible;
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    /// <param name="keepScroll">Rows added to the same results (the files, a moment later): the list stays where it was.</param>
    public void SetResults(IReadOnlyList<LauncherItem> items, string? emptyText, bool keepScroll = false)
    {
        var offset = Scroller.VerticalOffset;
        ResultsControl.ItemsSource = items;
        if (keepScroll) Scroller.ScrollToVerticalOffset(offset);
        else Scroller.ScrollToTop();
        var empty = items.Count == 0 && !string.IsNullOrEmpty(emptyText);
        EmptyText.Text = emptyText ?? string.Empty;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        Divider.Visibility = items.Count > 0 || empty ? Visibility.Visible : Visibility.Collapsed;
        Scroller.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void BringIntoView(LauncherItem item)
    {
        if (ResultsControl.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement fe) fe.BringIntoView();
    }

    // ── animations ───────────────────────────────────────────────────────

    public void PlayShow()
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(170));
        AnimatedRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = EaseOut });
        Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, 0, duration) { EasingFunction = EaseOut });
    }

    public void HideNow()
    {
        AnimatedRoot.BeginAnimation(OpacityProperty, null);
        AnimatedRoot.Opacity = 0;
        Hide();
    }

    // ── input ────────────────────────────────────────────────────────────

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: MoveRequested?.Invoke(1); break;
            case Key.Up: MoveRequested?.Invoke(-1); break;
            case Key.PageDown: MoveRequested?.Invoke(5); break;
            case Key.PageUp: MoveRequested?.Invoke(-5); break;
            case Key.Enter: ExecuteRequested?.Invoke(); break;
            case Key.Escape: CloseRequested?.Invoke(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private static bool IsInside(DependencyObject d, DependencyObject ancestor)
    {
        for (var p = d; p is not null; p = p is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(p) : LogicalTreeHelper.GetParent(p))
            if (ReferenceEquals(p, ancestor)) return true;
        return false;
    }

    private static LauncherItem? ItemFrom(object source) =>
        source is FrameworkElement { DataContext: LauncherItem a } ? a
        : source is FrameworkContentElement { DataContext: LauncherItem b } ? b : null;
}

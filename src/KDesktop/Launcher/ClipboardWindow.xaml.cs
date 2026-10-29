using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KDesktop.Native;
using KDesktop.Services;

namespace KDesktop.Launcher;

/// <summary>The clipboard history's search box, filters and list (as the launcher's window). Logic lives in <see cref="ClipboardController"/>.</summary>
public partial class ClipboardWindow : Window
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private bool _backdrop;

    public ClipboardWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        SearchBox.TextChanged += (_, _) =>
        {
            Placeholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            QueryChanged?.Invoke();
        };
        foreach (var filter in Filters.Children.OfType<RadioButton>()) filter.Checked += (_, _) => QueryChanged?.Invoke();
        PreviewKeyDown += OnKeyDown;
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
        ResultsControl.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (ItemFrom(e.OriginalSource) is not { } item) return;
            e.Handled = true;
            ItemMenu?.Invoke(item, e.OriginalSource as UIElement ?? ResultsControl);
        };
    }

    public IntPtr Hwnd { get; private set; }

    public event Action? QueryChanged;
    public event Action<int>? MoveRequested;

    /// <summary>Enter (with Shift: without its format).</summary>
    public event Action<bool>? PasteRequested;

    /// <summary>Ctrl + 1…9: that row of the list.</summary>
    public event Action<int>? PasteNumber;
    public event Action? PinRequested;
    public event Action? DeleteRequested;
    public event Action? CloseRequested;
    public event Action<ClipboardItem>? ItemHovered;
    public event Action<ClipboardItem>? ItemClicked;
    public event Action<ClipboardItem, UIElement>? ItemMenu;

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

    /// <summary>«all», «text», «images», «files» or «pinned».</summary>
    public string Filter => Filters.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "all";

    /// <summary>Empty box and «Todo», without raising a search (the controller fills the list itself).</summary>
    public void Reset()
    {
        SearchBox.Text = string.Empty;
        Placeholder.Visibility = Visibility.Visible;
        FilterAll.IsChecked = true;
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    public void SetResults(IReadOnlyList<ClipboardItem> items, string? emptyText)
    {
        ResultsControl.ItemsSource = items;
        var empty = items.Count == 0 && !string.IsNullOrEmpty(emptyText);
        EmptyText.Text = emptyText ?? string.Empty;
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        Scroller.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ScrollToTop() => Scroller.ScrollToTop();

    public void BringIntoView(ClipboardItem item)
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
        var mods = Keyboard.Modifiers;
        switch (e.Key)
        {
            case Key.Down: MoveRequested?.Invoke(1); break;
            case Key.Up: MoveRequested?.Invoke(-1); break;
            case Key.PageDown: MoveRequested?.Invoke(5); break;
            case Key.PageUp: MoveRequested?.Invoke(-5); break;
            case Key.Enter: PasteRequested?.Invoke(mods.HasFlag(ModifierKeys.Shift)); break;
            case Key.Escape: CloseRequested?.Invoke(); break;
            case Key.Tab: NextFilter(mods.HasFlag(ModifierKeys.Shift) ? -1 : 1); break;
            case Key.P when mods == ModifierKeys.Control: PinRequested?.Invoke(); break;
            // Supr deletes the row while the box has nothing to delete after the cursor.
            case Key.Delete when SearchBox.CaretIndex >= SearchBox.Text.Length && SearchBox.SelectionLength == 0: DeleteRequested?.Invoke(); break;
            case >= Key.D1 and <= Key.D9 when mods == ModifierKeys.Control: PasteNumber?.Invoke(e.Key - Key.D1); break;
            case >= Key.NumPad1 and <= Key.NumPad9 when mods == ModifierKeys.Control: PasteNumber?.Invoke(e.Key - Key.NumPad1); break;
            default: return;
        }
        e.Handled = true;
    }

    private void NextFilter(int step)
    {
        var filters = Filters.Children.OfType<RadioButton>().ToList();
        var at = filters.FindIndex(r => r.IsChecked == true);
        filters[((at < 0 ? 0 : at) + step + filters.Count) % filters.Count].IsChecked = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private static ClipboardItem? ItemFrom(object source) =>
        source is FrameworkElement { DataContext: ClipboardItem a } ? a
        : source is FrameworkContentElement { DataContext: ClipboardItem b } ? b : null;
}

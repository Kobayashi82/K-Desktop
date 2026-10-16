using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KDesktop.Native;
using KDesktop.Services;

namespace KDesktop.QuickMenus;

/// <summary>One level of an open QuickMenu (the root or a submenu).</summary>
public partial class QuickMenuWindow : Window
{
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private bool _backdrop;

    public QuickMenuWindow(bool isSubmenu)
    {
        IsSubmenu = isSubmenu;
        InitializeComponent();
        ShowActivated = !isSubmenu;
        SourceInitialized += OnSourceInitialized;
        EntriesControl.MouseMove += OnMouseMove;
        EntriesControl.MouseLeave += (_, _) => PointerLeft?.Invoke(this);
        MouseEnter += (_, _) => PointerEntered?.Invoke(this);
        EntriesControl.PreviewMouseUp += OnMouseUp;
        EntriesControl.PreviewMouseDown += (_, e) => e.Handled = e.ChangedButton == MouseButton.Middle;
        ApplySize(1);
    }

    public bool IsSubmenu { get; }
    public int Level { get; set; }

    /// <summary>The entry (in the parent level) that opened this submenu.</summary>
    public QuickMenuEntry? ParentEntry { get; set; }

    public IReadOnlyList<QuickMenuEntry> Entries { get; private set; } = [];

    public event Action<QuickMenuWindow, QuickMenuEntry>? EntryHovered;
    public event Action<QuickMenuWindow, QuickMenuEntry, MouseButton>? EntryClicked;
    public event Action<QuickMenuWindow>? PointerEntered;
    public event Action<QuickMenuWindow>? PointerLeft;

    public IntPtr Hwnd { get; private set; }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Hwnd = new WindowInteropHelper(this).Handle;
        if (HwndSource.FromHwnd(Hwnd) is { } src) src.CompositionTarget.BackgroundColor = Colors.Transparent;
        UiNative.AddExStyle(Hwnd, toolWindow: true, noActivate: IsSubmenu);
        UiNative.SetCorners(Hwnd, UiNative.Corner.Round);
        _backdrop = UiNative.SetBackdrop(Hwnd, UiNative.Backdrop.Acrylic);
        ApplyTheme();
    }

    /// <summary>Updates DWM dark mode and the tint so it matches the current theme.</summary>
    public void ApplyTheme()
    {
        if (Hwnd == IntPtr.Zero) return;
        UiNative.SetDarkMode(Hwnd, ThemeService.IsDark);
        Surface.SetResourceReference(Border.BackgroundProperty, _backdrop ? "Kd.MenuTint" : "Kd.MenuTintOpaque");
    }

    /// <summary>Size preset 0..5 (same scale as K-Desktop 4).</summary>
    public void ApplySize(int size)
    {
        size = Math.Clamp(size, 0, 5);
        FontSize = 12 + size;
        Resources["Qm.IconSize"] = 16.0 + size * 2;
        Resources["Qm.RowHeight"] = 28.0 + size * 2.5;
    }

    public void SetEntries(IReadOnlyList<QuickMenuEntry> entries)
    {
        Entries = entries;
        EntriesControl.ItemsSource = entries;
    }

    public void SetMaxHeight(double dips) => Scroller.MaxHeight = Math.Max(120, dips);

    // ── animations ───────────────────────────────────────────────────────

    public void PlayShow()
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(IsSubmenu ? 140 : 170));
        AnimatedRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = EaseOut });
        if (IsSubmenu)
        {
            Slide.BeginAnimation(TranslateTransform.YProperty, null);
            Slide.Y = 0;
            Slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-8, 0, duration) { EasingFunction = EaseOut });
        }
        else
        {
            Slide.BeginAnimation(TranslateTransform.XProperty, null);
            Slide.X = 0;
            Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, 0, duration) { EasingFunction = EaseOut });
        }
    }

    /// <summary>Fades out quickly and hides the window.</summary>
    public void HideAnimated()
    {
        if (!IsVisible) return;
        var anim = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(90)));
        anim.Completed += (_, _) =>
        {
            if (AnimatedRoot.Opacity < 0.05) Hide();
        };
        AnimatedRoot.BeginAnimation(OpacityProperty, anim);
    }

    public void HideNow()
    {
        AnimatedRoot.BeginAnimation(OpacityProperty, null);
        AnimatedRoot.Opacity = 0;
        Hide();
    }

    // ── geometry ─────────────────────────────────────────────────────────

    /// <summary>Screen rectangle (physical pixels) of an entry row.</summary>
    public Rect? EntryScreenRect(QuickMenuEntry entry)
    {
        if (EntriesControl.ItemContainerGenerator.ContainerFromItem(entry) is not FrameworkElement fe || !fe.IsVisible) return null;
        var topLeft = fe.PointToScreen(new Point(0, 0));
        var bottomRight = fe.PointToScreen(new Point(fe.ActualWidth, fe.ActualHeight));
        return new Rect(topLeft, bottomRight);
    }

    public void BringIntoView(QuickMenuEntry entry)
    {
        if (EntriesControl.ItemContainerGenerator.ContainerFromItem(entry) is FrameworkElement fe) fe.BringIntoView();
    }

    // ── input ────────────────────────────────────────────────────────────

    private static QuickMenuEntry? EntryFrom(object source) =>
        source is FrameworkElement { DataContext: QuickMenuEntry e } ? e
        : source is FrameworkContentElement { DataContext: QuickMenuEntry e2 } ? e2 : null;

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (EntryFrom(e.OriginalSource) is { } entry) EntryHovered?.Invoke(this, entry);
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (EntryFrom(e.OriginalSource) is not { } entry)
        {
            KDesktop.Core.Log.Info($"QuickMenu: clic {e.ChangedButton} fuera de un elemento ({e.OriginalSource.GetType().Name}, nivel {Level})");
            return;
        }
        e.Handled = true;
        EntryClicked?.Invoke(this, entry, e.ChangedButton);
    }
}

using System.Windows;
using KDesktop.Assistant.Sdk;
using static KDesktop.Assistant.Host.Interop.Native;

namespace KDesktop.Assistant.Host.World;

/// <summary>
/// Notices Windows notifications (the toasts at the bottom right) without reading them, from window events (from
/// Kobii). A toast is a <c>Windows.UI.Core.CoreWindow</c> of the shell (ShellExperienceHost or ShellHost) that
/// appears with no height next to the corner of the work area and grows to toast size. If one already showing grows
/// a lot at once, another came on top.
/// </summary>
internal sealed class NotificationWatcher(WindowScanner scanner)
{
    private const string ToastClass = "Windows.UI.Core.CoreWindow";

    /// <summary>Time it has to grow since it appears.</summary>
    private const double GrowSeconds = 3;

    /// <summary>How long it takes to settle after growing (growing meanwhile is not another toast).</summary>
    private const double SettleSeconds = 1.2;

    /// <summary>At least this long between two toasts (a burst of messages is a single toast).</summary>
    private const double MinInterval = 4;

    private readonly Dictionary<nint, Candidate> _candidates = [];
    private double _lastNotice = double.NegativeInfinity;

    /// <summary>One window event. Returns where a new toast appeared, if one did.</summary>
    public PixelRect? OnWindowEvent(uint eventType, nint hwnd, double now, IReadOnlyList<MonitorInfo> monitors)
    {
        switch (eventType)
        {
            case EVENT_OBJECT_UNCLOAKED or EVENT_OBJECT_SHOW:
                // Only new windows: checking the class and process of every window that moves would cost.
                if (_candidates.ContainsKey(hwnd) || !IsShellPopup(hwnd)) return null;
                var small = WindowScanner.TryGetBounds(hwnd, out var start) && start.Bottom - start.Top < 40;
                _candidates[hwnd] = new Candidate(now, small);
                return Check(hwnd, now, monitors);
            case EVENT_OBJECT_LOCATIONCHANGE:
                return _candidates.ContainsKey(hwnd) ? Check(hwnd, now, monitors) : null;
            case EVENT_OBJECT_CLOAKED or EVENT_OBJECT_HIDE or EVENT_OBJECT_DESTROY:
                _candidates.Remove(hwnd);
                return null;
            default:
                return null;
        }
    }

    private PixelRect? Check(nint hwnd, double now, IReadOnlyList<MonitorInfo> monitors)
    {
        var candidate = _candidates[hwnd];
        if (!WindowScanner.TryGetBounds(hwnd, out var bounds)) return null;
        var rect = bounds.ToPixelRect();
        var monitor = MonitorOf(rect, monitors);
        if (monitor is null || !LooksLikeToast(rect, monitor))
        {
            // Not a toast (another shell panel): stop watching it.
            if (!candidate.Announced && now - candidate.Seen > GrowSeconds) _candidates.Remove(hwnd);
            return null;
        }

        var height = rect.Height;
        if (!candidate.Announced)
        {
            // It must have come out with no height and grown at once, like toasts (not another shell panel).
            if (!candidate.StartedSmall || now - candidate.Seen > GrowSeconds)
            {
                _candidates.Remove(hwnd);
                return null;
            }
            candidate.Announced = true;
            candidate.AnnouncedAt = now;
            candidate.Baseline = height;
            return Announce(rect, now);
        }
        if (now - candidate.AnnouncedAt < SettleSeconds)
        {
            candidate.Baseline = height;
            return null;
        }
        if (height > candidate.Baseline + 60 * monitor.DpiScale)
        {
            // Another toast stacked on the first.
            candidate.AnnouncedAt = now;
            candidate.Baseline = height;
            return Announce(rect, now);
        }
        candidate.Baseline = Math.Min(candidate.Baseline, height);
        return null;
    }

    private PixelRect? Announce(PixelRect rect, double now)
    {
        if (now - _lastNotice < MinInterval) return null;
        _lastNotice = now;
        return rect;
    }

    private bool IsShellPopup(nint hwnd) =>
        WindowScanner.GetClassName(hwnd) == ToastClass && scanner.ProcessOf(hwnd).Name is "shellexperiencehost" or "shellhost";

    /// <summary>Toast size, stuck to the bottom-right corner of the work area.</summary>
    private static bool LooksLikeToast(PixelRect rect, MonitorInfo monitor)
    {
        var scale = monitor.DpiScale;
        var work = monitor.WorkArea;
        return rect.Width >= 240 * scale && rect.Width <= 640 * scale
            && rect.Height >= 60 * scale && rect.Height <= Math.Min(720 * scale, work.Height * 0.85)
            && Math.Abs(work.Right - rect.Right) <= 64 * scale
            && Math.Abs(work.Bottom - rect.Bottom) <= 96 * scale;
    }

    private static MonitorInfo? MonitorOf(PixelRect rect, IReadOnlyList<MonitorInfo> monitors)
    {
        var point = new Point(rect.Left + rect.Width / 2.0, rect.Bottom - 1);
        return monitors.FirstOrDefault(m => m.Bounds.Contains(point));
    }

    private sealed class Candidate(double seen, bool startedSmall)
    {
        public double Seen { get; } = seen;
        public bool StartedSmall { get; } = startedSmall;
        public bool Announced { get; set; }
        public double AnnouncedAt { get; set; }
        public int Baseline { get; set; }
    }
}

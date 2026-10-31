using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using KDesktop.Core;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using Microsoft.Win32;

namespace KDesktop.Panels;

/// <summary>
/// The panels of «Paneles» on the desktop: a window for each one that is shown, one reading of the figures for all of
/// them (<see cref="PanelSampler"/>, only what they show, at the pace of the quickest one) and the window in front
/// watched for «Mostrar el escritorio». Nothing shown: nothing reads nor watches.
/// </summary>
public sealed class PanelManager : IDisposable
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0;

    private readonly Func<AppConfig> _config;
    private readonly Action<InfoPanel> _edit;
    private readonly PanelSampler _sampler = new();
    private readonly Dictionary<InfoPanel, InfoPanelWindow> _windows = [];
    private readonly WinEventProc _onForeground;
    private AppConfig? _watched;
    private IntPtr _hook;

    public PanelManager(Func<AppConfig> config, Action<InfoPanel> edit)
    {
        _config = config;
        _edit = edit;
        _onForeground = (_, _, hwnd, _, _, _, _) =>
        {
            foreach (var window in _windows.Values) window.OnForeground(hwnd);
        };
        _sampler.Sampled += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (var window in _windows.Values) window.Refresh();
        });
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Watch();
        Sync();
    }

    /// <summary>After the whole configuration was replaced (imports): the panels of the new one.</summary>
    public void Reload()
    {
        Watch();
        Sync();
    }

    /// <summary>Shows, hides or switches a panel (by id or name; null: all of them). Returns how many changed.</summary>
    public int Set(string? panel, bool? show)
    {
        var targets = Find(panel);
        foreach (var p in targets) p.Enabled = show ?? !p.Enabled;
        return targets.Count;
    }

    /// <summary>Locks or unlocks a panel (by id or name; null: all of them); null switches it.</summary>
    public int Lock(string? panel, bool? locked)
    {
        var targets = Find(panel);
        foreach (var p in targets) p.Locked = locked ?? !p.Locked;
        return targets.Count;
    }

    private List<InfoPanel> Find(string? panel) => string.IsNullOrWhiteSpace(panel)
        ? [.. _config().Panels]
        : [.. _config().Panels.Where(p => p.Id == panel || p.Name.Equals(panel, StringComparison.OrdinalIgnoreCase)).Take(1)];

    // ── following the configuration ────────────────────────────────────

    private void Watch()
    {
        var config = _config();
        if (ReferenceEquals(config, _watched)) return;
        if (_watched is not null)
        {
            _watched.Panels.CollectionChanged -= OnPanelsChanged;
            foreach (var p in _watched.Panels) p.PropertyChanged -= OnPanelChanged;
        }
        _watched = config;
        config.Panels.CollectionChanged += OnPanelsChanged;
        foreach (var p in config.Panels) p.PropertyChanged += OnPanelChanged;
    }

    private void OnPanelsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (InfoPanel p in e.OldItems) p.PropertyChanged -= OnPanelChanged;
        if (e.NewItems is not null)
            foreach (InfoPanel p in e.NewItems) p.PropertyChanged += OnPanelChanged;
        if (e.Action == NotifyCollectionChangedAction.Reset)
            foreach (var p in _config().Panels) p.PropertyChanged += OnPanelChanged;
        Sync();
    }

    private void OnPanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InfoPanel.Enabled) or nameof(InfoPanel.Interval) or nameof(InfoPanel.Summary)) Sync();
    }

    /// <summary>A window for each panel shown and none for the rest; what to read and how often.</summary>
    private void Sync()
    {
        var shown = _config().Panels.Where(p => p.Enabled).ToHashSet();
        foreach (var (panel, window) in _windows.Where(w => !shown.Contains(w.Key)).ToList())
        {
            _windows.Remove(panel);
            window.Close();
        }
        foreach (var panel in shown.Where(p => !_windows.ContainsKey(p)))
        {
            var window = new InfoPanelWindow(panel, _sampler, _edit);
            _windows[panel] = window;
            window.Show();
        }
        _sampler.Want(shown.SelectMany(p => p.Items.Select(i => i.Kind)), shown.Count == 0 ? 1000 : shown.Min(p => p.Interval));
        WatchForeground(shown.Count > 0);
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Application.Current?.Dispatcher.BeginInvoke(() =>
    {
        foreach (var window in _windows.Values) window.Place();
    });

    // ── the window in front ──────────────────────────────────────────────

    private delegate void WinEventProc(IntPtr hook, uint type, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    private void WatchForeground(bool on)
    {
        if (on && _hook == IntPtr.Zero) _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _onForeground, 0, 0, WINEVENT_OUTOFCONTEXT);
        else if (!on && _hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        WatchForeground(false);
        foreach (var window in _windows.Values) window.Close();
        _windows.Clear();
        _sampler.Dispose();
    }
}

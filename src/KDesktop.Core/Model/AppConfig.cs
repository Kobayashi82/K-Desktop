using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KDesktop.Core.Model;

/// <summary>Root of the persisted configuration (config.json).</summary>
public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public ObservableCollection<Command> Commands { get; set; } = [];
    public ObservableCollection<QuickMenu> QuickMenus { get; set; } = [];
    public ObservableCollection<Macro> Macros { get; set; } = [];

    public Command? FindCommand(string? id) =>
        string.IsNullOrEmpty(id) ? null : Commands.FirstOrDefault(c => c.Id == id);

    /// <summary>Fills the display-only names of actions that reference menus/commands.</summary>
    public void RefreshReferenceNames()
    {
        foreach (var action in Commands.SelectMany(c => c.Actions))
        {
            switch (action)
            {
                case OpenQuickMenuAction q: q.MenuName = FindMenu(q.MenuId)?.Name; break;
                case CommandControlAction c: c.CommandName = FindCommand(c.CommandId)?.Name; break;
                case PlayMacroAction p: p.MacroName = FindMacro(p.MacroId)?.Name; break;
            }
        }
    }

    public Macro? FindMacro(string? idOrName) =>
        string.IsNullOrEmpty(idOrName)
            ? null
            : Macros.FirstOrDefault(m => m.Id == idOrName) ??
              Macros.FirstOrDefault(m => m.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));

    public QuickMenu? FindMenu(string? idOrName) =>
        string.IsNullOrEmpty(idOrName)
            ? null
            : QuickMenus.FirstOrDefault(m => m.Id == idOrName) ??
              QuickMenus.FirstOrDefault(m => m.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase));
}

public enum OsdPosition { BottomCenter, TopCenter, Center, BottomRight, TopRight }
public enum AppTheme { System, Dark, Light }

public partial class AppSettings : ObservableObject
{
    // Appearance
    [ObservableProperty] private AppTheme _theme = AppTheme.System;

    /// <summary>Language code ("es", "en"…); null follows the Windows language.</summary>
    [ObservableProperty] private string? _language;

    // General
    [ObservableProperty] private bool _startWithWindows = true;
    [ObservableProperty] private bool _showTrayIcon = true;
    [ObservableProperty] private string _showWindowHotkey = "CTRL + ALT + Home";
    [ObservableProperty] private string _exitHotkey = "CTRL + ALT + End";
    [ObservableProperty] private string? _startupSound;
    [ObservableProperty] private string? _startupMessage;
    [ObservableProperty] private bool _showHiddenCommands;

    // Achievements
    [ObservableProperty] private bool _achievementNotifications = true;
    [ObservableProperty] private bool _achievementSound = true;

    // Macros
    /// <summary>Record mouse movements (not only clicks) when recording a macro.</summary>
    [ObservableProperty] private bool _macroRecordMoves = true;

    // Remote (UDP)
    [ObservableProperty] private bool _udpEnabled = true;
    [ObservableProperty] private bool _udpAllowRemote = true;
    [ObservableProperty] private int _udpPort = 2501;

    // OSD
    [ObservableProperty] private OsdPosition _osdPosition = OsdPosition.BottomCenter;
    [ObservableProperty] private double _osdSeconds = 1.6;

    // Mouse
    /// <summary>Block the mouse back/forward buttons in <see cref="BlockSideButtonsIn"/>.</summary>
    [ObservableProperty] private bool _blockSideButtons = true;

    /// <summary>Processes where K-Desktop hotkeys are ignored ("Desactivar en").</summary>
    public ObservableCollection<string> DisabledIn { get; set { field = value ?? []; field.CollectionChanged += (_, _) => Touch(); } } = [];

    public ObservableCollection<string> BlockSideButtonsIn { get; set { field = value ?? []; field.CollectionChanged += (_, _) => Touch(); } } =
        ["explorer", "chrome", "firefox", "msedge", "brave", "telegram", "systemsettings"];

    public AppSettings()
    {
        DisabledIn.CollectionChanged += (_, _) => Touch();
        BlockSideButtonsIn.CollectionChanged += (_, _) => Touch();
    }

    public event EventHandler? Modified;

    private void Touch() => Modified?.Invoke(this, EventArgs.Empty);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Touch();
    }
}

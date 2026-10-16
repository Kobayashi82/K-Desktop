using System.Diagnostics;
using System.Windows.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;

namespace KDesktop.Views;

/// <summary>A value with a translated display name, for combo boxes.</summary>
public sealed record Option<T>(T Value, string Key)
{
    public string Name => Loc.T(Key);
    public override string ToString() => Name;
}

/// <summary>Item sources for the editors (all display names are translated).</summary>
public static class Lookups
{
    private static IReadOnlyList<Option<T>> Enum<T>(string prefix) where T : struct, System.Enum =>
        System.Enum.GetValues<T>().Select(v => new Option<T>(v, $"{prefix}.{v}")).ToList();

    public static IReadOnlyList<Option<WindowMode>> WindowModes { get; } = Enum<WindowMode>("enum.window");
    public static IReadOnlyList<Option<ProcessPriorityLevel>> Priorities { get; } = Enum<ProcessPriorityLevel>("enum.priority");
    public static IReadOnlyList<Option<AppControlMode>> AppControlModes { get; } = Enum<AppControlMode>("enum.appControl");
    public static IReadOnlyList<Option<WaitCondition>> WaitConditions { get; } = Enum<WaitCondition>("enum.waitFor");
    public static IReadOnlyList<Option<SendInputMode>> SendInputModes { get; } = Enum<SendInputMode>("enum.sendInput");
    public static IReadOnlyList<Option<VolumeMode>> VolumeModes { get; } = Enum<VolumeMode>("enum.volume");
    public static IReadOnlyList<Option<PowerMode>> PowerModes { get; } = Enum<PowerMode>("power");
    public static IReadOnlyList<Option<CommandControlMode>> CommandControlModes { get; } = Enum<CommandControlMode>("enum.commandControl");
    public static IReadOnlyList<Option<KBoardActionMode>> KBoardActionModes { get; } = Enum<KBoardActionMode>("enum.kboardMode");
    public static IReadOnlyList<Option<RemapKind>> RemapKinds { get; } = Enum<RemapKind>("enum.remapKind");
    public static IReadOnlyList<Option<ScriptShell>> ScriptShells { get; } = Enum<ScriptShell>("enum.shell");
    public static IReadOnlyList<Option<AppTheme>> Themes { get; } = Enum<AppTheme>("enum.theme");
    public static IReadOnlyList<Option<UpdateMode>> UpdateModes { get; } = Enum<UpdateMode>("enum.updateMode");
    public static IReadOnlyList<Option<OsdPosition>> OsdPositions { get; } = Enum<OsdPosition>("enum.osdPosition");
    public static IReadOnlyList<Option<MacroStepKind>> MacroStepKinds { get; } = Enum<MacroStepKind>("enum.stepKind");
    public static IReadOnlyList<Option<KDesktop.Core.Input.MouseButton>> MouseButtons { get; } =
        [.. Enum<KDesktop.Core.Input.MouseButton>("macro.button").Where(o => o.Value != KDesktop.Core.Input.MouseButton.None)];

    public static IReadOnlyList<Option<string>> BrowserOptions { get; } =
        [new Option<string>(string.Empty, "browser.default"), .. Browsers.All.Select(b => new Option<string>(b.Id, b.Name))];

    /// <summary>"Custom folder" (null) followed by the special locations.</summary>
    public static IReadOnlyList<Option<string?>> FolderTargets { get; } =
        [new Option<string?>(null, "folder.custom"), .. SpecialFolders.All.Select(e => new Option<string?>(e.Key, $"folder.{e.Key}"))];

    public static IEnumerable<QuickMenu> QuickMenuList => App.Current.Config.QuickMenus;

    public static IEnumerable<Macro> MacroList => App.Current.Config.Macros;

    public static IEnumerable<KBoardProfile> KBoardList => App.Current.Config.KBoards;

    public static IReadOnlyList<Option<double>> MacroSpeeds { get; } =
        [.. new[] { 0.25, 0.5, 0.75, 1, 1.5, 2, 3, 5, 10 }.Select(s => new Option<double>(s, s == 1 ? "editor.speedNormal" : $"×{s:0.##}"))];

    public static IEnumerable<Command> CommandList =>
        App.Current.Config.Commands.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public static IReadOnlyList<string> RunningProcessesNow => RunningProcesses();


    public static IReadOnlyList<int> MenuSizes { get; } = [0, 1, 2, 3, 4, 5];

    /// <summary>Names of the processes that currently have a window.</summary>
    public static IReadOnlyList<string> RunningProcesses()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero && p.Id != Environment.ProcessId) names.Add(p.ProcessName.ToLowerInvariant());
            }
            catch { /* access denied */ }
            finally { p.Dispose(); }
        }
        return names.ToList();
    }
}

/// <summary>Routed commands used by the editor templates and handled by the pages.</summary>
public static class EditorCommands
{
    public static readonly RoutedUICommand BrowseFile = new("BrowseFile", nameof(BrowseFile), typeof(EditorCommands));
    public static readonly RoutedUICommand BrowseFolder = new("BrowseFolder", nameof(BrowseFolder), typeof(EditorCommands));
    public static readonly RoutedUICommand BrowseWorkingDirectory = new("BrowseWorkingDirectory", nameof(BrowseWorkingDirectory), typeof(EditorCommands));
    public static readonly RoutedUICommand CaptureKeys = new("CaptureKeys", nameof(CaptureKeys), typeof(EditorCommands));
    public static readonly RoutedUICommand CaptureMouse = new("CaptureMouse", nameof(CaptureMouse), typeof(EditorCommands));
    public static readonly RoutedUICommand TestAction = new("TestAction", nameof(TestAction), typeof(EditorCommands));
    public static readonly RoutedUICommand RemoveAction = new("RemoveAction", nameof(RemoveAction), typeof(EditorCommands));
    public static readonly RoutedUICommand MoveActionUp = new("MoveActionUp", nameof(MoveActionUp), typeof(EditorCommands));
    public static readonly RoutedUICommand MoveActionDown = new("MoveActionDown", nameof(MoveActionDown), typeof(EditorCommands));
    public static readonly RoutedUICommand DuplicateAction = new("DuplicateAction", nameof(DuplicateAction), typeof(EditorCommands));
    public static readonly RoutedUICommand RemoveHotkey = new("RemoveHotkey", nameof(RemoveHotkey), typeof(EditorCommands));
    public static readonly RoutedUICommand RemoveProcess = new("RemoveProcess", nameof(RemoveProcess), typeof(EditorCommands));
    public static readonly RoutedUICommand RemoveVoicePhrase = new("RemoveVoicePhrase", nameof(RemoveVoicePhrase), typeof(EditorCommands));
    public static readonly RoutedUICommand EditMacro = new("EditMacro", nameof(EditMacro), typeof(EditorCommands));
    public static readonly RoutedUICommand CaptureStepKey = new("CaptureStepKey", nameof(CaptureStepKey), typeof(EditorCommands));
    public static readonly RoutedUICommand CaptureStepPosition = new("CaptureStepPosition", nameof(CaptureStepPosition), typeof(EditorCommands));
    public static readonly RoutedUICommand AddAudioDevice = new("AddAudioDevice", nameof(AddAudioDevice), typeof(EditorCommands));
    public static readonly RoutedUICommand RemoveAudioDevice = new("RemoveAudioDevice", nameof(RemoveAudioDevice), typeof(EditorCommands));
    public static readonly RoutedUICommand EditKBoard = new("EditKBoard", nameof(EditKBoard), typeof(EditorCommands));
}

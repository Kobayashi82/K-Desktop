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

/// <summary>An extra of an installed assistant in the editor of «Asistente: acción».</summary>
public sealed record ExtraChoice(string Target, string Name, string Description, System.Windows.Media.ImageSource? Icon)
{
    public bool HasDescription => Description.Length > 0;

    public override string ToString() => Name;
}

/// <summary>An installed assistant in the editor of «Asistente: preguntar» (empty id: the default one).</summary>
public sealed record AssistantChoice(string Id, string Name, System.Windows.Media.ImageSource? Icon)
{
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
    public static IReadOnlyList<Option<VolumeTarget>> VolumeTargets { get; } = Enum<VolumeTarget>("enum.volumeTarget");
    public static IReadOnlyList<Option<WindowTarget>> WindowTargets { get; } = Enum<WindowTarget>("enum.windowTarget");
    public static IReadOnlyList<Option<WindowOperation>> WindowOperations { get; } = Enum<WindowOperation>("enum.windowOp");
    public static IReadOnlyList<Option<WindowSnap>> WindowSnaps { get; } = Enum<WindowSnap>("enum.windowSnap");
    public static IReadOnlyList<Option<MonitorChoice>> MonitorChoices { get; } = Enum<MonitorChoice>("enum.monitorChoice");
    public static IReadOnlyList<Option<ClipboardMode>> ClipboardModes { get; } = Enum<ClipboardMode>("enum.clipboard");
    public static IReadOnlyList<Option<DisplayOperation>> DisplayOperations { get; } = Enum<DisplayOperation>("enum.displayOp");
    public static IReadOnlyList<Option<ProjectionMode>> Projections { get; } = Enum<ProjectionMode>("enum.projection");
    public static IReadOnlyList<Option<LevelChange>> LevelChanges { get; } = Enum<LevelChange>("enum.levelChange");
    public static IReadOnlyList<Option<PowerPlanMode>> PowerPlanModes { get; } = Enum<PowerPlanMode>("enum.powerPlan");
    public static IReadOnlyList<Option<AssistantBrainOp>> AssistantBrainOps { get; } = Enum<AssistantBrainOp>("enum.assistantBrain");
    public static IReadOnlyList<Option<ThemeChoice>> ThemeChoices { get; } = Enum<ThemeChoice>("enum.themeChoice");
    public static IReadOnlyList<Option<RadioDevice>> RadioDevices { get; } = Enum<RadioDevice>("enum.radio");
    public static IReadOnlyList<Option<SwitchMode>> SwitchModes { get; } = Enum<SwitchMode>("enum.switchMode");
    public static IReadOnlyList<Option<SwitchMode>> DictateModes { get; } = Enum<SwitchMode>("enum.dictateMode");
    public static IReadOnlyList<Option<ConditionKind>> ConditionKinds { get; } = Enum<ConditionKind>("enum.condition");
    public static IReadOnlyList<Option<ConditionElse>> ConditionElses { get; } = Enum<ConditionElse>("enum.conditionElse");
    public static IReadOnlyList<Option<LoadResource>> LoadResources { get; } = Enum<LoadResource>("enum.loadResource");
    public static IReadOnlyList<Option<LoadComparison>> LoadComparisons { get; } = Enum<LoadComparison>("enum.loadComparison");
    public static IReadOnlyList<Option<CaptureArea>> CaptureAreas { get; } = Enum<CaptureArea>("enum.captureArea");

    /// <summary>«Automático» and the languages with OCR installed in Windows (read when the editor opens: they can be added meanwhile).</summary>
    public static IReadOnlyList<Option<string>> OcrLanguages =>
        [new Option<string>(string.Empty, "editor.screenTextAuto"), .. ScreenText.Languages.Select(l => new Option<string>(l.Tag, $"{l.Name} ({l.Tag})"))];
    public static IReadOnlyList<Option<RecordMode>> RecordModes { get; } = Enum<RecordMode>("enum.recordMode");
    public static IReadOnlyList<Option<AudioSource>> AudioSources { get; } = Enum<AudioSource>("enum.audioSource");
    public static IReadOnlyList<Option<ZoomMode>> ZoomModes { get; } =
        [.. new[] { ZoomMode.Lens, ZoomMode.Circle, ZoomMode.FullScreen }.Select(v => new Option<ZoomMode>(v, $"enum.zoomMode.{v}"))];
    public static IReadOnlyList<Option<ZoomLensSize>> ZoomLensSizes { get; } = Enum<ZoomLensSize>("enum.zoomLensSize");

    /// <summary>Zoom levels of «Lupa» (Settings).</summary>
    public static IReadOnlyList<Option<double>> ZoomLevels { get; } =
        [.. new[] { 1.5, 2, 2.5, 3, 4, 5 }.Select(z => new Option<double>(z, string.Create(System.Globalization.CultureInfo.CurrentCulture, $"×{z:0.#}")))];

    /// <summary>Zoom levels of the action: the first one is «the one in Settings».</summary>
    public static IReadOnlyList<Option<double>> ActionZoomLevels { get; } = [new Option<double>(0, "editor.zoomDefault"), .. ZoomLevels];

    /// <summary>The power plans of this PC (read when the editor opens).</summary>
    public static IReadOnlyList<PowerPlan> PowerPlanList => PowerPlans.All();

    /// <summary>The monitors that are on.</summary>
    public static IReadOnlyList<MonitorInfo> MonitorList => DisplayTools.Monitors();

    /// <summary>Names of the devices connected now (for «Si… está conectado»).</summary>
    public static IReadOnlyList<string> ConnectedGadgetNames =>
        PnpDevices.Present().Select(d => d.Name).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
    public static IReadOnlyList<Option<PowerMode>> PowerModes { get; } = Enum<PowerMode>("power");
    public static IReadOnlyList<Option<CommandControlMode>> CommandControlModes { get; } = Enum<CommandControlMode>("enum.commandControl");
    public static IReadOnlyList<Option<KBoardActionMode>> KBoardActionModes { get; } = Enum<KBoardActionMode>("enum.kboardMode");
    public static IReadOnlyList<Option<RemapKind>> RemapKinds { get; } = Enum<RemapKind>("enum.remapKind");
    public static IReadOnlyList<Option<ScriptShell>> ScriptShells { get; } = Enum<ScriptShell>("enum.shell");
    public static IReadOnlyList<Option<HttpVerb>> HttpVerbs { get; } =
        [.. System.Enum.GetValues<HttpVerb>().Select(v => new Option<HttpVerb>(v, v.ToString().ToUpperInvariant()))];
    public static IReadOnlyList<Option<HttpResultDisplay>> HttpResultDisplays { get; } = Enum<HttpResultDisplay>("enum.httpResult");
    public static IReadOnlyList<string> ContentTypes { get; } = ["application/json", "application/x-www-form-urlencoded", "text/plain", "application/xml"];
    public static IReadOnlyList<EventKindInfo> EventKinds => EventCatalog.All;
    public static IReadOnlyList<Option<CommandControlTarget>> CommandControlTargets { get; } = Enum<CommandControlTarget>("enum.controlTarget");

    public static IEnumerable<EventRule> EventList =>
        App.Current.Config.Events.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    public static IReadOnlyList<Option<SessionEvent>> SessionEvents { get; } = Enum<SessionEvent>("enum.session");
    public static IReadOnlyList<Option<PowerSource>> PowerSources { get; } = Enum<PowerSource>("enum.powerSource");
    public static IReadOnlyList<Option<NetworkState>> NetworkStates { get; } = Enum<NetworkState>("enum.networkState");
    public static IReadOnlyList<Option<AudioDeviceEvent>> AudioDeviceEvents { get; } = Enum<AudioDeviceEvent>("enum.audioDeviceEvent");

    /// <summary>Names of the devices one connects, paired Bluetooth ones included (for «Dispositivo conectado»).</summary>
    public static IReadOnlyList<string> GadgetNames =>
        PnpDevices.Present(pairedToo: true).Select(d => d.Name).Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Names of the playback devices (for the editable box of «Dispositivo de audio»).</summary>
    public static IReadOnlyList<string> AudioDeviceNames => AudioDevices.Playback().Select(d => d.Name).Distinct().ToList();

    /// <summary>Names of the networks connected now (for the editable box of «Red»).</summary>
    public static IReadOnlyList<string> ConnectedNetworksNow => NetworkInfo.ConnectedNetworks();
    public static IReadOnlyList<Option<AppTheme>> Themes { get; } = Enum<AppTheme>("enum.theme");
    public static IReadOnlyList<Option<UpdateMode>> UpdateModes { get; } = Enum<UpdateMode>("enum.updateMode");
    public static IReadOnlyList<Option<OsdPosition>> OsdPositions { get; } = Enum<OsdPosition>("enum.osdPosition");
    public static IReadOnlyList<Option<ScreenChoice>> ScreenChoices { get; } = Enum<ScreenChoice>("enum.screenChoice");
    public static IReadOnlyList<Option<OsdFullscreen>> OsdFullscreens { get; } = Enum<OsdFullscreen>("enum.osdFullscreen");
    public static IReadOnlyList<Option<NotifyCorner>> NotifyCorners { get; } = Enum<NotifyCorner>("enum.notifyCorner");
    public static IReadOnlyList<Option<NotifyFullscreen>> NotifyFullscreens { get; } = Enum<NotifyFullscreen>("enum.notifyFullscreen");
    public static IReadOnlyList<Option<Core.Remote.RemoteScope>> RemoteScopes { get; } = Enum<Core.Remote.RemoteScope>("enum.remoteScope");
    public static IReadOnlyList<Option<NotifyColor>> NotifyColors { get; } = Enum<NotifyColor>("enum.notifyColor");
    public static IReadOnlyList<Option<NotifyPlace>> NotifyPlaces { get; } = Enum<NotifyPlace>("enum.notifyPlace");
    public static IReadOnlyList<Option<NotifyClick>> NotifyClicks { get; } = Enum<NotifyClick>("enum.notifyClick");
    public static IReadOnlyList<Option<FileOperation>> FileOperations { get; } = Enum<FileOperation>("enum.fileOp");
    public static IReadOnlyList<Option<FileExists>> FileExistsChoices { get; } = Enum<FileExists>("enum.fileExists");
    public static IReadOnlyList<Option<BackupMode>> BackupModes { get; } = Enum<BackupMode>("enum.backupMode");
    public static IReadOnlyList<Option<MacroStepKind>> MacroStepKinds { get; } = Enum<MacroStepKind>("enum.stepKind");
    public static IReadOnlyList<Option<KDesktop.Core.Input.MouseButton>> MouseButtons { get; } =
        [.. Enum<KDesktop.Core.Input.MouseButton>("macro.button").Where(o => o.Value != KDesktop.Core.Input.MouseButton.None)];

    public static IReadOnlyList<Option<string>> BrowserOptions { get; } =
        [new Option<string>(string.Empty, "browser.default"), .. Browsers.All.Select(b => new Option<string>(b.Id, b.Name))];

    /// <summary>"Custom folder" (null) followed by the special locations in alphabetical order (of the current language).</summary>
    public static IReadOnlyList<Option<string?>> FolderTargets =>
        [new Option<string?>(null, "folder.custom"),
         .. SpecialFolders.All.Select(e => new Option<string?>(e.Key, $"folder.{e.Key}")).OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)];

    public static IEnumerable<QuickMenu> QuickMenuList => App.Current.Config.QuickMenus;

    public static IEnumerable<Macro> MacroList => App.Current.Config.Macros;

    public static IEnumerable<KBoardProfile> KBoardList => App.Current.Config.KBoards;

    public static IReadOnlyList<Option<double>> MacroSpeeds { get; } =
        [.. new[] { 0.25, 0.5, 0.75, 1, 1.5, 2, 3, 5, 10 }.Select(s => new Option<double>(s, s == 1 ? "editor.speedNormal" : $"×{s:0.##}"))];

    public static IEnumerable<Command> CommandList =>
        App.Current.Config.Commands.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public static IReadOnlyList<string> RunningProcessesNow => RunningProcesses();

    /// <summary>The extras of the installed assistants («Lander: Modo manual»), read when an editor opens.</summary>
    public static IReadOnlyList<ExtraChoice> AssistantExtraList =>
        [.. Core.Assistant.Packages.AssistantExtras.All().Select(e => new ExtraChoice(e.Target, e.DisplayName(Loc.Instance.Language),
            AssistantLook.Text(e.Extra.Description), Services.IconProvider.ForExtra(e.Assistant.Id, e.Extra.Id)))];


    /// <summary>K-Desktop and the installed assistants (an action always names its assistant, 5.0.113).</summary>
    public static IReadOnlyList<AssistantChoice> AssistantList =>
        [new(Core.Assistant.AssistantTargeting.KDesktop, "K-Desktop", AssistantLook.SystemIcon),
         .. Core.Assistant.Packages.AssistantPackages.Installed().Select(a => new AssistantChoice(a.Id, a.Manifest.Name, AssistantLook.Icon(a)))];

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
    public static readonly RoutedUICommand AddHttpHeader = new("AddHttpHeader", nameof(AddHttpHeader), typeof(EditorCommands));
    public static readonly RoutedUICommand RemoveHttpHeader = new("RemoveHttpHeader", nameof(RemoveHttpHeader), typeof(EditorCommands));
    public static readonly RoutedUICommand TestHttpRequest = new("TestHttpRequest", nameof(TestHttpRequest), typeof(EditorCommands));
    public static readonly RoutedUICommand FindMac = new("FindMac", nameof(FindMac), typeof(EditorCommands));
    public static readonly RoutedUICommand PickNotificationIcon = new("PickNotificationIcon", nameof(PickNotificationIcon), typeof(EditorCommands));
    public static readonly RoutedUICommand ResetNotificationIcon = new("ResetNotificationIcon", nameof(ResetNotificationIcon), typeof(EditorCommands));
    public static readonly RoutedUICommand AddFileSource = new("AddFileSource", nameof(AddFileSource), typeof(EditorCommands));
    public static readonly RoutedUICommand AddFileSourceFiles = new("AddFileSourceFiles", nameof(AddFileSourceFiles), typeof(EditorCommands));
    public static readonly RoutedUICommand AddFileSourceFolder = new("AddFileSourceFolder", nameof(AddFileSourceFolder), typeof(EditorCommands));
    public static readonly RoutedUICommand RemoveFileSource = new("RemoveFileSource", nameof(RemoveFileSource), typeof(EditorCommands));
}

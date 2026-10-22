using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KDesktop.Core;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Remote.Web;
using KDesktop.Core.Storage;
using KDesktop.Core.Updates;
using KDesktop.Core.Voice;
using KDesktop.Services;
using Microsoft.Win32;

namespace KDesktop.Views.Pages;

public partial class SettingsPage : UserControl
{
    private sealed record LanguageOption(string? Code, string Name);

    private readonly App _app = App.Current;

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = _app.Config.Settings;

        LoadLanguages();
        BuildNav();
        StartupToggle.IsChecked = StartupTask.IsRegistered();
        DataPathText.Text = AppPaths.DataDir;
        ShowBackupStatus();
        UpdateTrayImage();
        IpInfo.ToolTip = string.Join(Environment.NewLine, NetworkInfo.LocalIPv4());

        // Pages are kept while the window lives: follow the server only while this one is shown.
        void OnWebState() => Dispatcher.BeginInvoke(UpdateWebState);
        void OnDevices() => Dispatcher.BeginInvoke(LoadDevices);
        void OnVoiceModels() => Dispatcher.BeginInvoke(UpdateVoiceModels);
        void OnUpdates() => Dispatcher.BeginInvoke(UpdateUpdates);
        LoadVoice();
        LoadMedia();
        LoadLauncher();
        Loaded += (_, _) =>
        {
            UpdateWebState();
            LoadDevices();
            UpdateVoiceModels();
            UpdateUpdates();
            FillAndroid();
            _app.Updates.Changed += OnUpdates;
            _app.Web.StateChanged += OnWebState;
            _app.Web.Devices.Changed += OnDevices;
            _app.Voice.Models.Changed += OnVoiceModels;
        };
        Unloaded += (_, _) =>
        {
            _app.Updates.Changed -= OnUpdates;
            _app.Web.StateChanged -= OnWebState;
            _app.Web.Devices.Changed -= OnDevices;
            _app.Voice.Models.Changed -= OnVoiceModels;
        };
    }

    // ── sections (the tree is in SettingsPage.Nav.cs) ───────────────────────

    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>
    /// Opens a section by its key ("web", "remote"…), e.g. from the status icons. «assistants/&lt;id&gt;» opens that
    /// assistant's own settings, and «assistants/&lt;id&gt;/personal» its «Personalidad y voz».
    /// </summary>
    public void ShowSection(string key)
    {
        if (key.StartsWith("assistants/", StringComparison.Ordinal) && key.EndsWith("/personal", StringComparison.Ordinal))
        {
            ShowSection(key[..^"/personal".Length]);
            AssistantsSection.ShowPersonal();
            return;
        }
        if (SearchBox.Text.Length > 0) SearchBox.Text = string.Empty;
        // Until 5.0.111 K-Desktop's own was a section of its own; since 5.0.112 it is an assistant.
        if (key == "bodiless") key = "assistants/" + Core.Assistant.AssistantTargeting.KDesktop;
        if (Find(key) is not { } item)
        {
            // An assistant that is not installed: the list.
            if (!key.StartsWith("assistants/", StringComparison.Ordinal) || Find("assistants") is not { } list) return;
            item = list;
        }
        Reveal(item);
        Select(item);
    }

    /// <summary>Shows only that section's cards (each card's Tag names its section).</summary>
    private void ShowCard(string key, bool animate)
    {
        foreach (var card in Sections.Children.OfType<FrameworkElement>())
            card.Visibility = card.Tag as string == key ? Visibility.Visible : Visibility.Collapsed;
        if (!animate) return;
        SectionScroll.ScrollToTop();
        Sections.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(180)));
        SectionShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
    }

    // ── multimedia ───────────────────────────────────────────────────────

    private sealed record StepOption(int Value, string Name);
    private sealed record HandlerOption(MusicHandler Value, string Name);

    private static readonly int[] MediaSteps = [2, 5, 10, 15, 20, 25];
    private static readonly int[] VoiceUnloadMinutes = [1, 5, 10, 30, 60, 0];
    private static readonly int[] VoiceDuckPercents = [0, 10, 20, 35, 50, 80];

    private void LoadMedia()
    {
        MediaStepBox.ItemsSource = MediaSteps.Select(p => new StepOption(p, $"{p} %")).ToList();
        MusicOnBox.ItemsSource = Enum.GetValues<MusicHandler>().Select(h => new HandlerOption(h, Loc.T($"settings.musicOn.{h}"))).ToList();
        MusicOffBox.ItemsSource = Enum.GetValues<MusicHandler>().Select(h => new HandlerOption(h, Loc.T($"settings.musicOff.{h}"))).ToList();
    }

    private void OnBrowseMusicProgram(object sender, RoutedEventArgs e)
    {
        var s = _app.Config.Settings;
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("editor.chooseFile"),
            Filter = Loc.T("editor.filterPrograms") + "|*.exe;*.lnk|" + Loc.T("editor.filterAll") + "|*.*",
            DereferenceLinks = false,
        };
        if (Path.GetDirectoryName(s.VoiceMusicOnProgram.Trim()) is { Length: > 0 } dir && Directory.Exists(dir)) dlg.InitialDirectory = dir;
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) s.VoiceMusicOnProgram = dlg.FileName;
    }

    private void OnMusicProcessDropDown(object sender, EventArgs e)
    {
        if (sender is ComboBox box) box.ItemsSource = Lookups.RunningProcesses();
    }

    // ── voice ────────────────────────────────────────────────────────────

    private sealed record MicOption(string? Id, string Name);
    private sealed record MinutesOption(int Value, string Name);

    private bool _loadingVoice;

    private void LoadVoice()
    {
        _loadingVoice = true;
        var mics = new List<MicOption> { new(null, Loc.T("settings.voiceMicDefault")) };
        mics.AddRange(MicCapture.Devices().Select(d => new MicOption(d.Id, d.Name)));
        var chosen = _app.Config.Settings.VoiceMicId;
        // A microphone that is unplugged now stays chosen (it is used again when it comes back).
        if (chosen is not null && mics.All(m => m.Id != chosen)) mics.Add(new MicOption(chosen, Loc.T("settings.voiceMicMissing")));
        VoiceMicBox.ItemsSource = mics;
        VoiceMicBox.SelectedItem = mics.First(m => m.Id == chosen);
        UpdateOptionalHotkeys();
        UpdateWakeWords();
        VoiceUnloadBox.ItemsSource = VoiceUnloadMinutes
            .Select(m => new MinutesOption(m, m == 0 ? Loc.T("settings.voiceUnloadNever") : Loc.T("settings.voiceUnloadMinutes", m))).ToList();
        VoiceDuckBox.ItemsSource = VoiceDuckPercents.Select(p => new MinutesOption(p, $"{p} %")).ToList();
        _loadingVoice = false;
    }

    private void OnVoiceMicChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingVoice && VoiceMicBox.SelectedItem is MicOption mic) _app.Config.Settings.VoiceMicId = mic.Id;
    }

    private static string Megabytes(long bytes) => (bytes / 1048576.0).ToString("N0", Loc.Instance.Culture) + " MB";

    private void UpdateVoiceModels()
    {
        var download = _app.Voice.Models;
        var ready = VoiceModels.AllInstalled;
        // The meaning model is optional (the rest works without it) but it is offered with the others.
        var complete = VoiceModels.Complete;
        var installed = ModelDownloader.InstalledSize();
        VoiceModelsText.Text = download.IsDownloading
            ? Loc.T("settings.voiceDownloading", Megabytes(download.Done), Megabytes(ModelDownloader.TotalSize))
            : complete ? Loc.T("settings.voiceInstalled", Megabytes(installed))
            // Some already there (e.g. after an update adds a model): only the rest is downloaded.
            : (installed > 0 ? Loc.T("settings.voicePartial", Megabytes(Math.Max(1048576, ModelDownloader.TotalSize - installed)))
                             : Loc.T("settings.voiceMissing", Megabytes(ModelDownloader.TotalSize))) +
              (download.Error is { } error ? " " + Loc.T("settings.voiceLastError", error) : "");
        VoiceProgress.Visibility = download.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        VoiceProgress.Value = (double)download.Done / ModelDownloader.TotalSize;
        VoiceDownloadButton.Visibility = !download.IsDownloading && !complete ? Visibility.Visible : Visibility.Collapsed;
        VoiceCancelButton.Visibility = download.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        VoiceDeleteButton.Visibility = !download.IsDownloading && installed > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Recording a wake word needs the models (silence detection and transcription).
        WakeAddButton.IsEnabled = ready && !download.IsDownloading;
        WakeAddButton.ToolTip = ready ? null : Loc.T("wake.needModels");
    }

    // ── wake words ───────────────────────────────────────────────────────

    /// <summary>Who a word can be for (5.0.109): K-Desktop, each installed assistant, any assistant or nobody.</summary>
    public sealed record WakeOwnerOption(string? Owner, string Name)
    {
        public override string ToString() => Name;
    }

    /// <summary>A recorded word of the pool, with whose it is.</summary>
    public sealed class WakeRow(WakeWord word, IReadOnlyList<WakeOwnerOption> owners, WakeOwnerOption owner)
    {
        public WakeWord Word { get; } = word;
        public IReadOnlyList<WakeOwnerOption> Owners { get; } = owners;
        public WakeOwnerOption Owner { get; } = owner;
        public override string ToString() => Word.Name;
    }

    private bool _fillingWake;

    private void UpdateWakeWords()
    {
        _fillingWake = true;
        List<WakeOwnerOption> owners =
        [
            new(WakeOwners.Commands, Loc.T("settings.wakeOwnerKDesktop")),
            new(Core.Assistant.AssistantTargeting.KDesktop, Loc.T("settings.wakeOwnerKDesktopAssistant")),
            .. Core.Assistant.Packages.AssistantPackages.Installed().Select(a => new WakeOwnerOption(a.Id, a.Manifest.Name)),
            new(Core.Assistant.AssistantTargeting.AnyAssistant, Loc.T("settings.wakeOwnerAny")),
            new(null, Loc.T("settings.wakeOwnerNone")),
        ];
        WakeList.ItemsSource = _app.Voice.WakeWords.Words.Select(w =>
        {
            var owner = _app.WakeOwnerOf(w.Id);
            // An assistant that is no longer installed still has it until it is given to someone else.
            if (owners.FirstOrDefault(o => o.Owner == owner) is { } known) return new WakeRow(w, owners, known);
            var gone = new WakeOwnerOption(owner, App.AssistantName(owner!));
            return new WakeRow(w, [.. owners.SkipLast(1), gone, owners[^1]], gone);
        }).ToList();
        WakeNone.Visibility = _app.Voice.WakeWords.Words.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _fillingWake = false;
    }

    /// <summary>A word recorded here calls K-Desktop's commands.</summary>
    private void OnAddWakeWord(object sender, RoutedEventArgs e)
    {
        if (Dialogs.WakeWordDialog.Record(Window.GetWindow(this)) is not { } word) return;
        _app.SetWakeOwner(word.Id, WakeOwners.Commands);
        UpdateWakeWords();
    }

    private void OnWakeOwnerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_fillingWake || sender is not ComboBox { DataContext: WakeRow row, SelectedItem: WakeOwnerOption option } || option == row.Owner) return;
        // After this event: the list is made again with the new owners.
        Dispatcher.BeginInvoke(() =>
        {
            _app.SetWakeOwner(row.Word.Id, option.Owner);
            UpdateWakeWords();
        });
    }

    private void OnTestWakeWord(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WakeRow row }) Dialogs.WakeWordDialog.Test(Window.GetWindow(this), row.Word);
    }

    private void OnRerecordWakeWord(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WakeRow row } && Dialogs.WakeWordDialog.Record(Window.GetWindow(this), row.Word) is not null)
            UpdateWakeWords();
    }

    private async void OnDeleteWakeWord(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WakeRow { Word: var word } }) return;
        if (!await Confirm(Loc.T("settings.wakeDeleteConfirm", word.Name))) return;
        _app.ForgetWakeWord(word.Id);
        _app.Voice.WakeWords.Delete(word);
        UpdateWakeWords();
    }

    private void OnVoiceDownload(object sender, RoutedEventArgs e) => _app.Voice.Models.Start();

    private void OnVoiceCancel(object sender, RoutedEventArgs e) => _app.Voice.Models.Cancel();

    private async void OnVoiceDelete(object sender, RoutedEventArgs e)
    {
        if (!await Confirm(Loc.T("settings.voiceDeleteConfirm"))) return;
        _app.Voice.DeleteModels();
        UpdateVoiceModels();
    }

    private void UpdateOptionalHotkeys()
    {
        ShowOptionalHotkey(VoiceHotkeyButton, VoiceHotkeyClear, _app.Config.Settings.VoiceHotkey);
        ShowOptionalHotkey(DisabledInHotkeyButton, DisabledInHotkeyClear, _app.Config.Settings.DisabledInHotkey);
        ShowOptionalHotkey(LauncherHotkeyButton, LauncherHotkeyClear, _app.Config.Settings.LauncherHotkey);
        ShowOptionalHotkey(PauseHotkeyButton, PauseHotkeyClear, _app.Config.Settings.PauseHotkey);
    }

    /// <summary>A hotkey that may be unset: «Sin atajo» and no clear button then.</summary>
    private static void ShowOptionalHotkey(Wpf.Ui.Controls.Button button, Wpf.Ui.Controls.Button clear, string hotkey)
    {
        var none = string.IsNullOrWhiteSpace(hotkey);
        button.Content = none ? Loc.T("settings.voiceNoHotkey") : hotkey;
        clear.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CaptureOptionalHotkey(Wpf.Ui.Controls.Button button, Action<string> set)
    {
        button.Content = Loc.T("commands.pressHotkey");
        HotkeyCapture.Start(
            preview => button.Content = preview + "…",
            hotkey =>
            {
                if (hotkey is not null) set(Hotkey.Normalize(hotkey));
                UpdateOptionalHotkeys();
            },
            Window.GetWindow(this));
    }

    private void OnCaptureVoiceHotkey(object sender, RoutedEventArgs e) =>
        CaptureOptionalHotkey(VoiceHotkeyButton, hk => _app.Config.Settings.VoiceHotkey = hk);

    private void OnClearVoiceHotkey(object sender, RoutedEventArgs e)
    {
        _app.Config.Settings.VoiceHotkey = string.Empty;
        UpdateOptionalHotkeys();
    }

    private void OnCaptureDisabledInHotkey(object sender, RoutedEventArgs e) =>
        CaptureOptionalHotkey(DisabledInHotkeyButton, hk => _app.Config.Settings.DisabledInHotkey = hk);

    private void OnClearDisabledInHotkey(object sender, RoutedEventArgs e)
    {
        _app.Config.Settings.DisabledInHotkey = string.Empty;
        UpdateOptionalHotkeys();
    }

    // ── launcher ─────────────────────────────────────────────────────────

    private sealed record EngineOption(string Id, string Name);

    private void LoadLauncher()
    {
        LauncherEngineBox.ItemsSource = KDesktop.Launcher.SearchEngines.All.Select(e => new EngineOption(e.Id, e.Name))
            .Append(new EngineOption("custom", Loc.T("settings.launcherEngineCustom"))).ToList();
        LauncherEngineBox.SelectionChanged += (_, _) => UpdateLauncherUrl();
        UpdateLauncherUrl();
    }

    /// <summary>The address box is only for «Otro».</summary>
    private void UpdateLauncherUrl() =>
        LauncherUrlBox.Visibility = _app.Config.Settings.LauncherSearchEngine == "custom" ? Visibility.Visible : Visibility.Collapsed;

    private void OnCaptureLauncherHotkey(object sender, RoutedEventArgs e) =>
        CaptureOptionalHotkey(LauncherHotkeyButton, hk => _app.Config.Settings.LauncherHotkey = hk);

    private void OnClearLauncherHotkey(object sender, RoutedEventArgs e)
    {
        _app.Config.Settings.LauncherHotkey = string.Empty;
        UpdateOptionalHotkeys();
    }

    private void OnResetLauncherPlace(object sender, RoutedEventArgs e)
    {
        _app.Config.Settings.LauncherPosition = string.Empty;
        _app.Ui.ShowOsd(Loc.T("settings.launcherPlaceReset"), OsdKind.Success);
    }

    private void OnForgetLauncherUsage(object sender, RoutedEventArgs e)
    {
        _app.Launcher.ClearUsage();
        _app.Ui.ShowOsd(Loc.T("settings.launcherForgotten"), OsdKind.Success);
    }

    // ── web ──────────────────────────────────────────────────────────────

    private sealed record DeviceRow(string Id, string Name, string Detail);

    private void LoadDevices()
    {
        var rows = _app.Web.Devices.List()
            .Select(d => new DeviceRow(d.Id, d.Name, Loc.T("settings.webDeviceDetail", d.LastSeen.ToString("g", Loc.Instance.Culture), d.LastIp ?? "?")))
            .ToList();
        DevicesList.ItemsSource = rows;
        NoDevicesText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The address the phone must open: the configured one, the one seen through the proxy, or this PC's.</summary>
    private string PairingBaseUrl()
    {
        var s = _app.Config.Settings;
        if (_app.Web.PublicAddress is { } address) return address;
        return WebServer.LocalUrls(s.WebPort) is [var first, ..] ? first : $"http://localhost:{s.WebPort}/";
    }

    private void OnPairWeb(object sender, RoutedEventArgs e)
    {
        if (!_app.Web.IsRunning)
        {
            _app.Ui.ShowOsd(Loc.T("settings.webNotRunning"), OsdKind.Warning);
            return;
        }
        if (Dialogs.WebPairDialog.Show(Window.GetWindow(this), _app.Web.Devices, PairingBaseUrl()) is { } device)
            _app.Ui.ShowOsd(Loc.T("settings.webPaired", device.Name), OsdKind.Success);
    }

    private async void OnRevokeDevice(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id, DataContext: DeviceRow row }) return;
        if (!await Confirm(Loc.T("settings.webRevokeConfirm", row.Name))) return;
        _app.Web.Devices.Revoke(id);
    }

    private void UpdateWebState()
    {
        var s = _app.Config.Settings;
        var running = _app.Web.State == WebServerState.Running;
        WebStateIcon.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        WebStateInfo.ToolTip = running ? string.Join(Environment.NewLine, WebServer.LocalUrls(s.WebPort)) : null;
        WebStateText.Text = _app.Web.State switch
        {
            WebServerState.Running => Loc.T("settings.udpAddresses"),
            WebServerState.LocalOnly => Loc.T("settings.webLocalOnly"),
            WebServerState.Error => Loc.T("settings.webError"),
            _ => !_app.WebAllowed ? Loc.T("settings.webDisabledArg") : Loc.T("settings.webStopped"),
        };
    }

    private void OnOpenWeb(object sender, RoutedEventArgs e)
    {
        var s = _app.Config.Settings;
        ProcessLauncher.Launch(new LaunchSpec($"http://localhost:{s.WebPort}/"), asAdmin: false);
    }

    // ── Android app ──────────────────────────────────────────────────────

    /// <summary>The Android app this build carries: its version, and the button to save a copy.</summary>
    private void FillAndroid()
    {
        if (AndroidApp.Current is not { } apk)
        {
            AndroidVersionText.Text = Loc.T("settings.androidNone");
            AndroidInfoText.Text = Loc.T("settings.androidNoneHint");
            AndroidSaveButton.IsEnabled = false;
            return;
        }
        AndroidVersionText.Text = Loc.T("settings.androidVersion", apk.Version);
        AndroidInfoText.Text = Loc.T("settings.androidInfo", AppPaths.FullVersion, Math.Max(1, apk.Bytes.Length / 1024), apk.Code);
        AndroidSaveButton.IsEnabled = true;
    }

    private void OnSaveApk(object sender, RoutedEventArgs e)
    {
        if (AndroidApp.Current is not { } apk) return;
        var dlg = new SaveFileDialog
        {
            Title = Loc.T("settings.androidSave"),
            Filter = Loc.T("settings.androidFilter") + "|*.apk",
            FileName = $"KDesktop-{apk.Version}.apk",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, apk.Bytes);
            _app.Ui.ShowOsd(Loc.T("settings.androidSaved", Path.GetFileName(dlg.FileName)), OsdKind.Success);
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo guardar la APK", ex);
            _app.Ui.ShowOsd(ex.Message, OsdKind.Warning);
        }
    }

    private void LoadLanguages() =>
        LanguageBox.ItemsSource = new[] { new LanguageOption(null, Loc.T("settings.languageSystem")) }
            .Concat(Loc.Available().Select(l => new LanguageOption(l.Code, l.Name))).ToList();

    /// <summary>Looks for new language files and reads the chosen one again (after editing a translation).</summary>
    private void OnReloadLanguages(object sender, RoutedEventArgs e)
    {
        _app.ReloadLanguage();
        LoadLanguages();
        LanguageBox.SelectedValue = _app.Config.Settings.Language;
        _app.Ui.ShowOsd(Loc.T("settings.languagesReloaded"), OsdKind.Success);
    }

    // ── updates ──────────────────────────────────────────────────────────

    private void UpdateUpdates()
    {
        var u = _app.Updates;
        UpdateCurrentText.Text = Loc.T("updates.current", UpdateService.CurrentVersion.ToString(3));
        UpdateStatusText.Text = u.State switch
        {
            UpdateState.Checking => Loc.T("updates.checking"),
            UpdateState.UpToDate => Loc.T("updates.upToDate") + LastCheck(),
            UpdateState.Error => Loc.T("updates.error", u.Error ?? "?") + LastCheck(),
            UpdateState.Available or UpdateState.Downloading or UpdateState.Ready when u.Available is { } a => Loc.T("updates.available", a.Version.ToString(3)),
            _ => u.LastCheck is null ? string.Empty : LastCheck().TrimStart(' ', '·'),
        };
        UpdateCheckButton.IsEnabled = u.State is not (UpdateState.Checking or UpdateState.Downloading);

        var info = u.Available;
        UpdatePanel.Visibility = info is not null && u.State is UpdateState.Available or UpdateState.Downloading or UpdateState.Ready
            ? Visibility.Visible : Visibility.Collapsed;
        if (info is null) return;
        UpdateTitleText.Text = info.Date is { } date
            ? Loc.T("updates.availableDate", info.Version.ToString(3), date.ToString("d", Loc.Instance.Culture))
            : Loc.T("updates.available", info.Version.ToString(3));
        UpdateNotesText.Text = info.Notes ?? string.Empty;
        UpdateNotesText.Visibility = string.IsNullOrWhiteSpace(info.Notes) ? Visibility.Collapsed : Visibility.Visible;
        var downloading = u.State == UpdateState.Downloading;
        UpdateProgress.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.Value = u.Progress;
        UpdateCancelButton.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        UpdatePortableText.Visibility = UpdateService.CanInstall ? Visibility.Collapsed : Visibility.Visible;
        UpdateInstallButton.IsEnabled = !downloading;
        UpdateInstallButton.Content = !UpdateService.CanInstall ? Loc.T("updates.openDownload")
            : downloading ? Loc.T("updates.downloading", u.Progress.ToString("P0", Loc.Instance.Culture))
            : Loc.T("updates.install");
    }

    private string LastCheck() =>
        _app.Updates.LastCheck is { } t ? " · " + Loc.T("updates.lastCheck", t.ToString("g", Loc.Instance.Culture)) : string.Empty;

    private void OnCheckUpdates(object sender, RoutedEventArgs e) => _ = _app.CheckForUpdatesAsync(manual: true);

    private void OnInstallUpdate(object sender, RoutedEventArgs e) => _ = _app.InstallUpdateAsync();

    private void OnCancelUpdate(object sender, RoutedEventArgs e) => _app.Updates.CancelDownload();

    private void OnResetUpdateUrl(object sender, RoutedEventArgs e) => _app.Config.Settings.UpdateUrl = AppSettings.DefaultUpdateUrl;

    private void UpdateTrayImage()
    {
        var custom = _app.Config.Settings.TrayIcon;
        TrayImagePreview.Source = (string.IsNullOrEmpty(custom) ? null : IconProvider.FromConfigIcon(custom, adaptToTheme: false))
            ?? TrayIcons.Default();
        TrayImageReset.IsEnabled = !string.IsNullOrEmpty(custom);
    }

    private void OnChangeTrayImage(object sender, RoutedEventArgs e)
    {
        // "" = the picker's "Automatic", which here means K-Desktop's own icon.
        if (Dialogs.IconPicker.Pick(Window.GetWindow(this), _app.Config.Settings.TrayIcon, "tray") is not { } choice) return;
        _app.Config.Settings.TrayIcon = choice.Length == 0 ? null : choice;
        UpdateTrayImage();
    }

    private void OnResetTrayImage(object sender, RoutedEventArgs e)
    {
        _app.Config.Settings.TrayIcon = null;
        UpdateTrayImage();
    }

    private void OnRefreshFavicons(object sender, RoutedEventArgs e)
    {
        Favicons.Clear();
        IconProvider.BumpRevision(); // back to the generic icons until the new ones arrive
        Favicons.Prefetch(_app.Config.Commands.SelectMany(c => c.Actions));
        _app.Ui.ShowOsd(Loc.T("settings.faviconsRefreshing"), OsdKind.Info);
    }

    /// <summary>Saves the built-in English texts as the starting point of a new translation.</summary>
    private void OnExportLanguageTemplate(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.LanguagesDir);
        var dlg = new SaveFileDialog
        {
            Title = Loc.T("settings.exportTemplateTitle"),
            Filter = "JSON|*.json",
            InitialDirectory = AppPaths.LanguagesDir,
            FileName = "xx.json",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, Loc.Template(Path.GetFileNameWithoutExtension(dlg.FileName)));
            LoadLanguages();
            _app.Ui.ShowOsd(Loc.T("settings.templateExported"), OsdKind.Success);
        }
        catch (Exception ex)
        {
            Log.Error("Error exportando la plantilla de idioma", ex);
            _app.Ui.ShowOsd(Loc.T("settings.templateError"), OsdKind.Warning);
        }
    }

    private void OnOpenLanguages(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.LanguagesDir);
        ProcessLauncher.Launch(new LaunchSpec(AppPaths.LanguagesDir), asAdmin: false);
    }

    private void OnPauseToggle(object sender, RoutedEventArgs e) => _app.SetPaused(PauseToggle.IsChecked == true);

    private void OnCapturePauseHotkey(object sender, RoutedEventArgs e) =>
        CaptureOptionalHotkey(PauseHotkeyButton, hk => _app.Config.Settings.PauseHotkey = hk);

    private void OnClearPauseHotkey(object sender, RoutedEventArgs e)
    {
        _app.Config.Settings.PauseHotkey = string.Empty;
        UpdateOptionalHotkeys();
    }

    private void OnStartupToggle(object sender, RoutedEventArgs e)
    {
        var enable = StartupToggle.IsChecked == true;
        StartupTask.Apply(enable);
        _app.Config.Settings.StartWithWindows = enable;
        StartupToggle.IsChecked = StartupTask.IsRegistered();
    }

    private void OnCaptureShowHotkey(object sender, RoutedEventArgs e) =>
        Capture(ShowHotkeyButton, hk => _app.Config.Settings.ShowWindowHotkey = hk);

    private void OnCaptureExitHotkey(object sender, RoutedEventArgs e) =>
        Capture(ExitHotkeyButton, hk => _app.Config.Settings.ExitHotkey = hk);

    private void Capture(Wpf.Ui.Controls.Button button, Action<string> set)
    {
        var binding = button.GetBindingExpression(ContentProperty)?.ParentBinding;
        button.Content = Loc.T("commands.pressHotkey");
        HotkeyCapture.Start(
            preview => button.Content = preview + "…",
            hotkey =>
            {
                if (hotkey is not null) set(Hotkey.Normalize(hotkey));
                if (binding is not null) button.SetBinding(ContentProperty, binding);
            },
            Window.GetWindow(this));
    }

    private void OnTestOsd(object sender, RoutedEventArgs e) => _app.Ui.ShowOsd(Loc.T("settings.osdSample"), OsdKind.Success);

    // ── process lists ────────────────────────────────────────────────────

    /// <summary>The list a process combo adds to.</summary>
    private ObservableCollection<string>? ListOf(object? box) =>
        ReferenceEquals(box, DisabledInBox) ? _app.Config.Settings.DisabledIn
        : ReferenceEquals(box, SideButtonsBox) ? _app.Config.Settings.BlockSideButtonsIn : null;

    private void OnProcessDropDown(object? sender, EventArgs e)
    {
        if (sender is ComboBox box) ProcessCombo.Fill(box, ListOf(box));
    }

    private void OnProcessPicked(object? sender, EventArgs e)
    {
        if (sender is ComboBox box) ProcessCombo.AddPicked(box, ListOf(box));
    }

    private void OnProcessKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter || sender is not ComboBox box) return;
        ProcessCombo.AddTyped(box, ListOf(box));
        e.Handled = true;
    }

    private void OnAddDisabledIn(object sender, RoutedEventArgs e) => ProcessCombo.AddTyped(DisabledInBox, _app.Config.Settings.DisabledIn);

    private void OnAddSideButtons(object sender, RoutedEventArgs e) => ProcessCombo.AddTyped(SideButtonsBox, _app.Config.Settings.BlockSideButtonsIn);

    private void OnPickDisabledIn(object sender, RoutedEventArgs e) => PickInto(_app.Config.Settings.DisabledIn);

    private void OnPickSideButtons(object sender, RoutedEventArgs e) => PickInto(_app.Config.Settings.BlockSideButtonsIn);

    private static void PickInto(ObservableCollection<string> list) =>
        WindowPicker.Pick(name =>
        {
            if (!list.Contains(name)) list.Add(name);
        });

    private void OnRemoveChip(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ObservableCollection<string> list, DataContext: string item }) list.Remove(item);
    }

    // ── data ─────────────────────────────────────────────────────────────

    private void OnOpenData(object sender, RoutedEventArgs e)
    {
        _app.Achievements.Unlock("curious");
        ShellTools.OpenSpecial("KDesktopData");
    }

    /// <summary>«Última copia: …» under the automatic backup switch.</summary>
    private void ShowBackupStatus()
    {
        var list = AutoBackup.List(AutoBackup.FolderOf(_app.Config.Settings));
        var last = list.Count > 0 ? list[0] : default;
        AutoBackupStatus.Text = last.Path is null
            ? Loc.T("settings.autoBackupNone")
            : Loc.T("settings.autoBackupLast", last.Time.ToString("g", System.Globalization.CultureInfo.CurrentCulture));
    }

    private void OnBrowseCaptureFolder(object sender, RoutedEventArgs e)
    {
        var s = _app.Config.Settings;
        var dlg = new OpenFolderDialog { Title = Loc.T("editor.chooseFolder"), InitialDirectory = CommandEngine.FolderFor(null, s.CaptureFolder, Environment.SpecialFolder.MyPictures) };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) s.CaptureFolder = dlg.FolderName;
    }

    private void OnBrowseRecordFolder(object sender, RoutedEventArgs e)
    {
        var s = _app.Config.Settings;
        var dlg = new OpenFolderDialog { Title = Loc.T("editor.chooseFolder"), InitialDirectory = CommandEngine.FolderFor(null, s.RecordFolder, Environment.SpecialFolder.MyMusic) };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) s.RecordFolder = dlg.FolderName;
    }

    private void OnBrowseBackupFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("editor.chooseFolder"), InitialDirectory = AutoBackup.FolderOf(_app.Config.Settings) };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true)
        {
            _app.Config.Settings.AutoBackupFolder = dlg.FolderName;
            ShowBackupStatus();
        }
    }

    private async void OnBackupNow(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await _app.BackupIfDueAsync(force: true) is { } path)
                _app.Ui.ShowOsd(Loc.T("settings.backupDone", Path.GetFileName(path)), OsdKind.Success);
        }
        catch (Exception ex)
        {
            _app.Ui.ShowOsd(Loc.T("settings.backupError", ex.Message), OsdKind.Warning);
        }
        ShowBackupStatus();
    }

    private void OnOpenBackups(object sender, RoutedEventArgs e)
    {
        var folder = AutoBackup.FolderOf(_app.Config.Settings);
        Directory.CreateDirectory(folder);
        ProcessLauncher.Launch(new LaunchSpec("explorer.exe", $"\"{folder}\""), asAdmin: false);
    }

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        var log = Path.Combine(AppPaths.LogDir, "kdesktop.log");
        if (File.Exists(log)) ProcessLauncher.Launch(new LaunchSpec(log), asAdmin: false);
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = Loc.T("settings.archiveFilter") + "|*.zip",
            FileName = $"K-Desktop {DateTime.Now:yyyy-MM-dd}.zip",
        };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            SaveAll();
            DataArchive.Export(dlg.FileName);
            _app.Ui.ShowOsd(Loc.T("settings.exported"), OsdKind.Success);
            _app.Achievements.Unlock("export");
        }
        catch (Exception ex)
        {
            Log.Error("Error exportando la copia", ex);
            _app.Ui.ShowOsd(Loc.T("settings.exportError"), OsdKind.Warning);
        }
    }

    private void SaveAll()
    {
        _app.Store.SaveNow();
        _app.Achievements.Flush();
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        // A .zip from "Exportar copia", or a bare config.json (older copies).
        var dlg = new OpenFileDialog { Filter = Loc.T("settings.archiveFilter") + "|*.zip;*.json" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

        DataArchive.Contents? archive = null;
        AppConfig config;
        try
        {
            if (Path.GetExtension(dlg.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                archive = DataArchive.Read(dlg.FileName);
                config = archive.Config;
            }
            else config = Json.Deserialize<AppConfig>(await File.ReadAllTextAsync(dlg.FileName)) ?? throw new InvalidDataException();
        }
        catch (Exception ex)
        {
            Log.Error("Error leyendo la copia a importar", ex);
            _app.Ui.ShowOsd(Loc.T("settings.importError"), OsdKind.Warning);
            return;
        }
        if (!await Confirm(Loc.T(archive is null ? "settings.importConfirmJson" : "settings.importConfirm"))) return;

        try
        {
            // What is replaced is kept first, in case the copy was not the right one.
            SaveAll();
            Directory.CreateDirectory(AppPaths.BackupDir);
            DataArchive.Export(Path.Combine(AppPaths.BackupDir, $"antes de importar {DateTime.Now:yyyy-MM-dd HHmmss}.zip"));

            if (archive is not null)
            {
                DataArchive.ExtractFiles(archive);
                if (archive.StatsJson is { } stats) _app.Achievements.Replace(stats);
            }
            _app.ReplaceConfig(config);
            _app.ShowMainWindow(w => w.Navigate("commands"));
            _app.Ui.ShowOsd(Loc.T("settings.imported"), OsdKind.Success);
            _app.Achievements.Unlock("import");
            if (archive is not null) await _app.OfferMissingAssistantsAsync(archive.Assistants);
        }
        catch (Exception ex)
        {
            Log.Error("Error importando la copia", ex);
            _app.Ui.ShowOsd(Loc.T("settings.importError"), OsdKind.Warning);
        }
    }

    private async void OnReimportLegacy(object sender, RoutedEventArgs e)
    {
        if (!LegacyImporter.IsAvailable())
        {
            _app.Ui.ShowOsd(Loc.T("settings.noLegacy"), OsdKind.Warning);
            return;
        }
        if (!await Confirm(Loc.T("settings.reimportConfirm"))) return;
        var report = _app.ReimportLegacy();
        _app.Ui.ShowOsd(Loc.T("settings.reimported", report.Commands), OsdKind.Success);
    }

    private async void OnResetAchievements(object sender, RoutedEventArgs e)
    {
        if (!await Confirm(Loc.T("ach.resetConfirm"))) return;
        _app.Achievements.Reset();
        _app.Ui.ShowOsd(Loc.T("ach.resetDone"), OsdKind.Success);
    }

    private static async Task<bool> Confirm(string text)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = "K-Desktop",
            Content = text,
            PrimaryButtonText = Loc.T("common.continue"),
            CloseButtonText = Loc.T("common.cancel"),
        };
        return await box.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }
}

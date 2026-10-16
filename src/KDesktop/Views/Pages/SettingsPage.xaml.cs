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
        SectionList.SelectedIndex = Math.Clamp(_lastSection, 0, SectionList.Items.Count - 1);
        StartupToggle.IsChecked = StartupTask.IsRegistered();
        DataPathText.Text = AppPaths.DataDir;
        IpText.Text = Loc.T("settings.udpAddresses", string.Join("  ·  ", NetworkInfo.LocalIPv4()));

        // Pages are kept while the window lives: follow the server only while this one is shown.
        void OnWebState() => Dispatcher.BeginInvoke(UpdateWebState);
        void OnDevices() => Dispatcher.BeginInvoke(LoadDevices);
        void OnVoiceModels() => Dispatcher.BeginInvoke(UpdateVoiceModels);
        LoadVoice();
        Loaded += (_, _) =>
        {
            UpdateWebState();
            LoadDevices();
            UpdateVoiceModels();
            _app.Web.StateChanged += OnWebState;
            _app.Web.Devices.Changed += OnDevices;
            _app.Voice.Models.Changed += OnVoiceModels;
        };
        Unloaded += (_, _) =>
        {
            _app.Web.StateChanged -= OnWebState;
            _app.Web.Devices.Changed -= OnDevices;
            _app.Voice.Models.Changed -= OnVoiceModels;
        };
    }

    // ── sections ─────────────────────────────────────────────────────────

    /// <summary>Section shown last time, for when the window is opened again.</summary>
    private static int _lastSection;

    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Opens a section by its key ("web", "remote"…), e.g. from the status icons.</summary>
    public void ShowSection(string key)
    {
        if (SectionList.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag as string == key) is { } item)
            SectionList.SelectedItem = item;
    }

    /// <summary>Shows only the chosen section (each card's Tag names its section).</summary>
    private void OnSectionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (SectionList.SelectedItem is not ListBoxItem { Tag: string key })
        {
            // Never nothing selected: clicking the chosen one again keeps it.
            if (e.RemovedItems is [var previous, ..]) SectionList.SelectedItem = previous;
            return;
        }
        _lastSection = SectionList.SelectedIndex;
        foreach (var card in Sections.Children.OfType<FrameworkElement>())
            card.Visibility = card.Tag as string == key ? Visibility.Visible : Visibility.Collapsed;
        SectionScroll.ScrollToTop();

        Sections.BeginAnimation(OpacityProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(180)));
        SectionShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = EaseOut });
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
        UpdateVoiceHotkey();
        UpdateWakeWords();
        VoiceUnloadBox.ItemsSource = new[] { 1, 5, 10, 30, 60 }.Select(m => new MinutesOption(m, Loc.T("settings.voiceUnloadMinutes", m))).ToList();
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
        var ready = VoiceModels.Ready;
        var installed = ModelDownloader.InstalledSize();
        VoiceModelsText.Text = download.IsDownloading
            ? Loc.T("settings.voiceDownloading", Megabytes(download.Done), Megabytes(ModelDownloader.TotalSize))
            : ready ? Loc.T("settings.voiceInstalled", Megabytes(installed))
            : Loc.T("settings.voiceMissing", Megabytes(ModelDownloader.TotalSize)) +
              (download.Error is { } error ? " " + Loc.T("settings.voiceLastError", error) : "");
        VoiceProgress.Visibility = download.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        VoiceProgress.Value = (double)download.Done / ModelDownloader.TotalSize;
        VoiceDownloadButton.Visibility = !download.IsDownloading && !ready ? Visibility.Visible : Visibility.Collapsed;
        VoiceCancelButton.Visibility = download.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        VoiceDeleteButton.Visibility = !download.IsDownloading && installed > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Recording a wake word needs the models (silence detection and transcription).
        WakeAddButton.IsEnabled = ready && !download.IsDownloading;
        WakeAddButton.ToolTip = ready ? null : Loc.T("wake.needModels");
    }

    // ── wake words ───────────────────────────────────────────────────────

    private void UpdateWakeWords()
    {
        WakeList.ItemsSource = _app.Voice.WakeWords.Words;
        WakeNone.Visibility = _app.Voice.WakeWords.Words.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAddWakeWord(object sender, RoutedEventArgs e)
    {
        if (Dialogs.WakeWordDialog.Record(Window.GetWindow(this)) is not null) UpdateWakeWords();
    }

    private void OnTestWakeWord(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WakeWord word }) Dialogs.WakeWordDialog.Test(Window.GetWindow(this), word);
    }

    private void OnRerecordWakeWord(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WakeWord word } && Dialogs.WakeWordDialog.Record(Window.GetWindow(this), word) is not null)
            UpdateWakeWords();
    }

    private async void OnDeleteWakeWord(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WakeWord word }) return;
        if (!await Confirm(Loc.T("settings.wakeDeleteConfirm", word.Name))) return;
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

    private void UpdateVoiceHotkey()
    {
        var hotkey = _app.Config.Settings.VoiceHotkey;
        var none = string.IsNullOrWhiteSpace(hotkey);
        VoiceHotkeyButton.Content = none ? Loc.T("settings.voiceNoHotkey") : hotkey;
        VoiceHotkeyClear.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnCaptureVoiceHotkey(object sender, RoutedEventArgs e)
    {
        VoiceHotkeyButton.Content = Loc.T("commands.pressHotkey");
        HotkeyCapture.Start(
            preview => VoiceHotkeyButton.Content = preview + "…",
            hotkey =>
            {
                if (hotkey is not null) _app.Config.Settings.VoiceHotkey = Hotkey.Normalize(hotkey);
                UpdateVoiceHotkey();
            },
            Window.GetWindow(this));
    }

    private void OnClearVoiceHotkey(object sender, RoutedEventArgs e)
    {
        _app.Config.Settings.VoiceHotkey = string.Empty;
        UpdateVoiceHotkey();
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
        if (!string.IsNullOrWhiteSpace(s.WebPublicUrl)) return s.WebPublicUrl.Trim().TrimEnd('/') + "/";
        if (_app.Web.SeenPublicUrl is { } seen) return seen;
        return WebServer.LocalUrls(s.WebPort, s.WebBasePath).FirstOrDefault()
               ?? $"http://localhost:{s.WebPort}{WebServer.NormalizeBasePath(s.WebBasePath)}/";
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
        WebStateText.Text = _app.Web.State switch
        {
            WebServerState.Running => Loc.T("settings.webRunning", string.Join("  ·  ", WebServer.LocalUrls(s.WebPort, s.WebBasePath))),
            WebServerState.LocalOnly => Loc.T("settings.webLocalOnly"),
            WebServerState.Error => Loc.T("settings.webError"),
            _ => !_app.WebAllowed ? Loc.T("settings.webDisabledArg") : Loc.T("settings.webStopped"),
        };
    }

    private void OnOpenWeb(object sender, RoutedEventArgs e)
    {
        var s = _app.Config.Settings;
        ProcessLauncher.Launch(new LaunchSpec($"http://localhost:{s.WebPort}{WebServer.NormalizeBasePath(s.WebBasePath)}/"), asAdmin: false);
    }

    private void LoadLanguages() =>
        LanguageBox.ItemsSource = new[] { new LanguageOption(null, Loc.T("settings.languageSystem")) }
            .Concat(Loc.Available().Select(l => new LanguageOption(l.Code, l.Name))).ToList();

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
        if (e.Key == System.Windows.Input.Key.Enter && sender is ComboBox box) ProcessCombo.AddTyped(box, ListOf(box));
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

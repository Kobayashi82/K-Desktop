using System.Collections.ObjectModel;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using KDesktop.Core;
using KDesktop.Core.Engine;
using KDesktop.Core.Input;
using KDesktop.Core.Localization;
using KDesktop.Core.Model;
using KDesktop.Core.Platform;
using KDesktop.Core.Storage;
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

        LanguageBox.ItemsSource = new[] { new LanguageOption(null, Loc.T("settings.languageSystem")) }
            .Concat(Loc.Available().Select(l => new LanguageOption(l.Code, l.Name))).ToList();
        StartupSoundBox.ItemsSource = Sounds.Available();
        StartupToggle.IsChecked = StartupTask.IsRegistered();
        DataPathText.Text = AppPaths.DataDir;
        IpText.Text = Loc.T("settings.udpAddresses", string.Join("  ·  ", LocalAddresses()));
    }

    private static IEnumerable<string> LocalAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .Distinct()
                .ToList();
        }
        catch { return []; }
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

    private void OnProcessDropDown(object? sender, EventArgs e)
    {
        if (sender is ComboBox box) box.ItemsSource = Lookups.RunningProcesses();
    }

    private void OnAddDisabledIn(object sender, RoutedEventArgs e) => AddProcess(DisabledInBox, _app.Config.Settings.DisabledIn);

    private void OnAddSideButtons(object sender, RoutedEventArgs e) => AddProcess(SideButtonsBox, _app.Config.Settings.BlockSideButtonsIn);

    private void OnPickDisabledIn(object sender, RoutedEventArgs e) => PickInto(_app.Config.Settings.DisabledIn);

    private void OnPickSideButtons(object sender, RoutedEventArgs e) => PickInto(_app.Config.Settings.BlockSideButtonsIn);

    private static void PickInto(ObservableCollection<string> list) =>
        WindowPicker.Pick(name =>
        {
            if (!list.Contains(name)) list.Add(name);
        });

    private static void AddProcess(ComboBox box, ObservableCollection<string> list)
    {
        var name = ProcessNames.Normalize(box.Text);
        if (name.Length > 0 && !list.Contains(name)) list.Add(name);
        box.Text = string.Empty;
    }

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
        var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = $"K-Desktop {DateTime.Now:yyyy-MM-dd}.json" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        _app.Store.SaveNow();
        File.Copy(AppPaths.ConfigFile, dlg.FileName, true);
        _app.Ui.ShowOsd(Loc.T("settings.exported"), OsdKind.Success);
        _app.Achievements.Unlock("export");
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        if (!await Confirm(Loc.T("settings.importConfirm"))) return;
        try
        {
            var config = Json.Deserialize<AppConfig>(await File.ReadAllTextAsync(dlg.FileName)) ?? throw new InvalidDataException();
            _app.Store.Replace(config);
            _app.ApplySettings();
            _app.ShowMainWindow(w => w.Navigate("commands"));
            _app.Ui.ShowOsd(Loc.T("settings.imported"), OsdKind.Success);
            _app.Achievements.Unlock("import");
        }
        catch (Exception ex)
        {
            Log.Error("Error importando configuración", ex);
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

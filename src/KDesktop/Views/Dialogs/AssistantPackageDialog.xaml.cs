using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Localization;
using Wpf.Ui.Controls;

namespace KDesktop.Views.Dialogs;

/// <summary>
/// The card of an assistant package (§11.7): what it is, who made it, what it can do and the warning that it carries
/// code. The package is only read (never loaded); «Install» puts it in place and, if asked, on the desktop.
/// </summary>
public partial class AssistantPackageDialog : FluentWindow
{
    private readonly App _app;
    private PackageInfo? _package;

    private AssistantPackageDialog(App app, string file)
    {
        InitializeComponent();
        _app = app;
        Loaded += async (_, _) => await InspectAsync(file);
    }

    /// <summary>Opens the card of <paramref name="file"/> (not modal: K-Desktop keeps working meanwhile).</summary>
    public static void Open(App app, Window? owner, string file)
    {
        var dialog = new AssistantPackageDialog(app, file);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.Show();
        dialog.Activate();
    }

    private async Task InspectAsync(string file)
    {
        try
        {
            _package = await Task.Run(() => AssistantPackages.Inspect(file));
            ShowCard(_package);
        }
        catch (PackageException ex)
        {
            ErrorFile.Text = file;
            ErrorText.Text = ex.Message;
            ErrorPanel.Visibility = Visibility.Visible;
            CancelButton.Content = Loc.T("common.close");
        }
        LoadingPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowCard(PackageInfo package)
    {
        var manifest = package.Manifest;
        NameText.Text = manifest.Name;
        ByText.Text = Loc.T("assistantCard.by", manifest.Version, manifest.Author);
        ActionText.Text = package.Action switch
        {
            PackageAction.Update => Loc.T("assistantCard.update", package.Installed!.Version),
            PackageAction.Reinstall => Loc.T("assistantCard.reinstall"),
            PackageAction.Downgrade => Loc.T("assistantCard.downgrade", package.Installed!.Version),
            _ => Loc.T("assistantCard.new"),
        };
        if (package.Action == PackageAction.Downgrade)
        {
            ActionBadge.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "Kd.DangerSoft");
            ActionText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Kd.Danger");
        }

        if (Bitmap(package.Icon) is { } icon)
        {
            IconImage.Source = icon;
            IconFallback.Visibility = Visibility.Collapsed;
        }
        if (Bitmap(package.Preview) is { } preview)
        {
            PreviewImage.Source = preview;
            PreviewBorder.Visibility = Visibility.Visible;
        }

        DescriptionText.Text = manifest.Description?.In(Loc.Instance.Language) ?? string.Empty;
        if (DescriptionText.Text.Length == 0) DescriptionText.Visibility = Visibility.Collapsed;
        GestureList.ItemsSource = package.Gestures.Select(g => Loc.T("assistant.gesture." + g)).ToList();
        DetailsText.Text = Loc.T("assistantCard.details", FormatSize(package.Size), package.Files)
                           + (manifest.License is { Length: > 0 } license ? " · " + Loc.T("assistantCard.license", license) : "");

        var settings = _app.Config.Settings;
        KeepDataText.Visibility = package.Installed is null ? Visibility.Collapsed : Visibility.Visible;
        // The one on the desktop is replaced by the new version anyway.
        if (settings.AssistantActive == manifest.Id) ActivateCheck.Visibility = Visibility.Collapsed;
        if (!settings.AssistantEnabled) ModuleOffText.Visibility = Visibility.Visible;

        InstallButton.Content = Loc.T(package.Action switch
        {
            PackageAction.Update => "assistantCard.updateButton",
            PackageAction.Reinstall => "assistantCard.reinstallButton",
            _ => "assistantCard.install",
        });
        InstallButton.Visibility = Visibility.Visible;
        CardPanel.Visibility = Visibility.Visible;
    }

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        if (_package is not { } package) return;
        InstallButton.IsEnabled = CancelButton.IsEnabled = false;
        InstallError.Visibility = Visibility.Collapsed;
        try
        {
            await _app.InstallAssistantAsync(package, ActivateCheck.Visibility == Visibility.Visible && ActivateCheck.IsChecked == true);
            Close();
        }
        catch (PackageException ex)
        {
            InstallError.Text = ex.Message;
            InstallError.Visibility = Visibility.Visible;
            InstallButton.IsEnabled = CancelButton.IsEnabled = true;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private static BitmapImage? Bitmap(byte[]? bytes)
    {
        if (bytes is null) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
        {
            return null; // a damaged image: the card shows the default icon
        }
    }

    private static string FormatSize(long bytes) => bytes < 1L << 20
        ? string.Create(Loc.Instance.Culture, $"{Math.Max(1, bytes >> 10)} KB")
        : string.Create(Loc.Instance.Culture, $"{bytes / 1048576.0:0.#} MB");
}

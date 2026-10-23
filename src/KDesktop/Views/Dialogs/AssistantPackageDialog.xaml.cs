using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using KDesktop.Core;
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
    private List<HotkeyOffer> _offers = [];
    /// <summary>The model it suggests that Ollama does not have, or null.</summary>
    private App.SuggestedModel? _suggestedModel;

    /// <summary>A command the card offers to create: an extra with the hotkey it suggests.</summary>
    private sealed class HotkeyOffer(ManifestExtra extra, string hotkey, string text, string? conflict)
    {
        public ManifestExtra Extra { get; } = extra;
        public string Hotkey { get; } = hotkey;
        public string Text { get; } = text;

        /// <summary>Who else uses that hotkey already (then it is not ticked), or null.</summary>
        public string? Conflict { get; } = conflict;

        public bool IsChecked { get; set; } = conflict is null;

        public override string ToString() => Text;
    }

    private AssistantPackageDialog(App app, string file)
    {
        InitializeComponent();
        _app = app;
        Loaded += async (_, _) => await InspectAsync(file);
    }

    /// <summary>Opens the card of <paramref name="file"/> (not modal: K-Desktop keeps working meanwhile).</summary>
    /// <param name="downloaded">A temporary download: it is deleted when the card closes.</param>
    public static void Open(App app, Window? owner, string file, bool downloaded = false)
    {
        var dialog = new AssistantPackageDialog(app, file);
        if (downloaded) dialog.Closed += (_, _) => AssistantPackages.ForgetDownload(file);
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
            ShowError(file, ex.Message);
        }
        catch (Exception ex)
        {
            // Never leave the card spinning: anything unexpected is said, and logged.
            Log.Error($"Asistente: no se ha podido revisar «{file}»", ex);
            ShowError(file, Loc.T("assistantPkg.installFailed", Path.GetFileName(file), ex.Message));
        }
        LoadingPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string file, string message)
    {
        ErrorFile.Text = file;
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        CancelButton.Content = Loc.T("common.close");
    }

    /// <summary>Who signed it (stage 12): «verificado», someone K-Desktop does not know, or nobody; and whether that changed.</summary>
    private void ShowSignature(PackageInfo package)
    {
        var signature = package.Signature;
        switch (signature.State)
        {
            case SignatureState.Verified:
                SignatureText.Text = Loc.T("assistantCard.verified", signature.Publisher);
                SignatureIcon.SetResourceReference(Wpf.Ui.Controls.SymbolIcon.ForegroundProperty, "Kd.Success");
                SignatureText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Kd.Success");
                WarningText.Text = Loc.T("assistantCard.warningVerified", signature.Publisher);
                WarningIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ShieldCheckmark24;
                WarningIcon.SetResourceReference(Wpf.Ui.Controls.SymbolIcon.ForegroundProperty, "Kd.Success");
                WarningBox.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "Kd.Success");
                break;
            case SignatureState.Unknown:
                SignatureText.Text = Loc.T("assistantCard.signedBy", signature.Publisher);
                SignatureIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Shield24;
                WarningText.Text = Loc.T("assistantCard.warningUnknown", signature.Publisher);
                break;
            default:
                SignatureText.Text = Loc.T("assistantCard.unsigned");
                SignatureIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ShieldDismiss24;
                break;
        }
        if (package.SignerChanged && package.Installed is { } installed)
        {
            var before = PackageSignature.OfInstalled(installed.Folder);
            SignerChangedText.Text = Loc.T("assistantCard.signerChanged", before.Publisher ?? "?");
            SignerChangedText.Visibility = Visibility.Visible;
        }
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

        ShowSignature(package);
        if (manifest.IsCharacter && package.Signature.State != SignatureState.Verified)
        {
            // Pictures and JSON only (phase 12): nothing of it runs.
            WarningText.Text = Loc.T("assistantCard.noCode");
            WarningIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Image24;
            WarningIcon.SetResourceReference(Wpf.Ui.Controls.SymbolIcon.ForegroundProperty, "Kd.AccentText");
            WarningBox.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
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
        if (package.Action == PackageAction.Update && manifest.Changes?.In(Loc.Instance.Language) is { Length: > 0 } changes)
        {
            ChangesTitle.Text = Loc.T("assistantCard.changes", manifest.Version);
            ChangesText.Text = changes;
            ChangesPanel.Visibility = Visibility.Visible;
        }
        GestureList.ItemsSource = package.Gestures.Select(g => Loc.T("assistant.gesture." + g)).ToList();
        DetailsText.Text = Loc.T("assistantCard.details", FormatSize(package.Size), package.Files)
                           + (manifest.License is { Length: > 0 } license ? " · " + Loc.T("assistantCard.license", license) : "");

        var settings = _app.Config.Settings;
        KeepDataText.Visibility = package.Installed is null ? Visibility.Collapsed : Visibility.Visible;
        // The one on the desktop is replaced by the new version anyway.
        if (settings.AssistantsOnDesktop.Contains(manifest.Id)) ActivateCheck.Visibility = Visibility.Collapsed;
        if (!settings.AssistantEnabled) ModuleOffText.Visibility = Visibility.Visible;
        ShowHotkeyOffers(manifest);
        ShowWakeOffer(manifest, updating: package.Installed is not null);
        ModelPanel.Visibility = Visibility.Collapsed;
        _ = OfferModelAsync(manifest);

        InstallButton.Content = Loc.T(package.Action switch
        {
            PackageAction.Update => "assistantCard.updateButton",
            PackageAction.Reinstall => "assistantCard.reinstallButton",
            _ => "assistantCard.install",
        });
        InstallButton.Visibility = Visibility.Visible;
        CardPanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Its extras that suggest a hotkey (and do not have a command yet, as after an update): ticked unless something
    /// else already answers that hotkey.
    /// </summary>
    private void ShowHotkeyOffers(AssistantManifest manifest)
    {
        var config = _app.Config;
        var language = Loc.Instance.Language;
        _offers = [.. manifest.Extras
            .Where(e => Core.Input.Hotkey.Normalize(e.Hotkey ?? string.Empty).Length > 0 && !AssistantExtras.CommandsFor(config, manifest.Id, e.Id).Any())
            .Select(e =>
            {
                var hotkey = Core.Input.Hotkey.Normalize(e.Hotkey!);
                var users = config.HotkeyUsers(hotkey);
                return new HotkeyOffer(e, hotkey, Loc.T("assistantCard.hotkeyOffer", hotkey, e.Name.In(language)),
                    users.Count > 0 ? Loc.T("assistantCard.hotkeyUsed", string.Join(", ", users)) : null);
            })];
        HotkeyList.ItemsSource = _offers;
        HotkeyPanel.Visibility = _offers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Installing it, without a way to call it by voice yet: a free recorded word with the name it suggests is given to
    /// it, or (with the voice models) the user records it after installing. Ticked: it is how it is meant to be talked to.
    /// Not when updating (that was decided when it was installed).
    /// </summary>
    /// <summary>The model it suggests, when it is not here: offered with a box left unticked (Hugging Face is asked first).</summary>
    private async Task OfferModelAsync(AssistantManifest manifest)
    {
        _suggestedModel = await _app.MissingSuggestedModelAsync(manifest);
        if (_suggestedModel is not { } model) return;
        ModelCheck.Content = new System.Windows.Controls.TextBlock
        {
            Text = Loc.T("assistantCard.modelOffer", model.Model.Name, (model.Size / 1e9).ToString("0.0", Loc.Instance.Culture)),
            TextWrapping = TextWrapping.Wrap,
        };
        ModelPanel.Visibility = Visibility.Visible;
    }

    private void ShowWakeOffer(AssistantManifest manifest, bool updating)
    {
        var name = manifest.WakeWord is { Length: > 0 } suggested ? suggested : manifest.Name;
        var free = _app.FreeWakeWordNamed(name);
        var offer = !updating && !_app.HasWakeWord(manifest.Id, name) && (free is not null || Core.Voice.VoiceModels.AllInstalled);
        WakeCheck.Content = new System.Windows.Controls.TextBlock { Text = Loc.T(free is null ? "assistantCard.wakeOffer" : "assistantCard.wakeUse", name), TextWrapping = TextWrapping.Wrap };
        WakePanel.Visibility = offer ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        if (_package is not { } package) return;
        InstallButton.IsEnabled = CancelButton.IsEnabled = false;
        InstallError.Visibility = Visibility.Collapsed;
        try
        {
            var recordWake = WakePanel.Visibility == Visibility.Visible && WakeCheck.IsChecked == true;
            if (recordWake) _app.SkipWakeTip(package.Manifest.Id);
            var installed = await _app.InstallAssistantAsync(package, ActivateCheck.Visibility == Visibility.Visible && ActivateCheck.IsChecked == true);
            _app.CreateExtraCommands(_offers.Where(o => o.IsChecked)
                .Select(o => installed.Manifest.Extras.FirstOrDefault(x => x.Id == o.Extra.Id))
                .OfType<ManifestExtra>()
                .Select(x => new InstalledExtra(installed, x)));
            if (_suggestedModel is { } model && ModelCheck.IsChecked == true) _app.StartModelDownload(model.Model, model.Projector, installed.Id);
            var owner = Owner;
            Close();
            if (recordWake) _app.OfferAssistantWakeWord(installed.Id, owner);
        }
        catch (Exception ex)
        {
            if (ex is not PackageException) Log.Error($"Asistente: no se ha podido instalar «{package.Manifest.Id}»", ex);
            InstallError.Text = ex is PackageException ? ex.Message : Loc.T("assistantPkg.installFailed", package.Manifest.Name, ex.Message);
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

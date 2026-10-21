using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;

namespace KDesktop.Views.Pages;

/// <summary>
/// Options → Assistant → Brain (stage 3.1): the provider, its address and key, the general model, how it is now, and the
/// way to get it ready on a PC without anything (install Ollama, open it, download the recommended model) with its
/// progress. The work is the assistant process's: this sends <c>brain</c> requests and shows its <c>brainState</c>.
/// While shown, it asks again every few seconds (a model loads and unloads by itself).
/// </summary>
public partial class AssistantBrainSection : UserControl
{
    private sealed record ProviderOption(BrainProvider Value, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record ModelOption(string Value, string Name)
    {
        public override string ToString() => Name;
    }

    public sealed record FoundOption(BrainFound Found, string Text)
    {
        public override string ToString() => Text;
    }

    private enum SetupAction { None, Install, DownloadPage, Start, Pull, Cancel }

    private static readonly BrainProvider[] Providers = [BrainProvider.Ollama, BrainProvider.LlamaServer, BrainProvider.LlamaSwap, BrainProvider.OpenAICompatible, BrainProvider.None];

    /// <summary>Minutes a model may stay unused in the graphics card (0: until asked).</summary>
    private static readonly int[] KeepChoices = [5, 10, 15, 30, 60, 0];

    private sealed record KeepOption(int Minutes, string Name)
    {
        public override string ToString() => Name;
    }

    private readonly App _app = App.Current;
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(8) };
    private bool _filling;
    private (SetupAction Action, string? Model) _main, _other;

    public AssistantBrainSection()
    {
        InitializeComponent();
        ProviderBox.ItemsSource = Providers.Select(p => new ProviderOption(p, ProviderName(p))).ToList();
        KeepBox.ItemsSource = KeepChoices.Select(m => new KeepOption(m, m switch
        {
            0 => Loc.T("brainOptions.keepNever"),
            60 => Loc.T("brainOptions.keepHour"),
            _ => Loc.T("brainOptions.keepMinutes", m),
        })).ToList();
        _poll.Tick += (_, _) => _app.AskAssistantBrain("check");
        Loaded += (_, _) =>
        {
            _app.AssistantChanged += Fill;
            _app.AssistantBrainChanged += Fill;
            Fill();
        };
        Unloaded += (_, _) =>
        {
            _app.AssistantChanged -= Fill;
            _app.AssistantBrainChanged -= Fill;
            _poll.Stop();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _poll.Stop();
                return;
            }
            _app.AskAssistantBrain("check");
            _poll.Start();
            Fill();
        };
    }

    public static string ProviderName(BrainProvider provider) => provider switch
    {
        BrainProvider.OpenAICompatible => Loc.T("brainOptions.provider.openai"),
        BrainProvider.None => Loc.T("brainOptions.provider.none"),
        BrainProvider.LlamaServer => Loc.T("brainOptions.provider.llamaServer"),
        _ => provider.DisplayName(),
    };

    private static CultureInfo Culture => Loc.Instance.Culture;

    private static string Gb(double bytes) => (bytes / 1e9).ToString(bytes >= 10e9 ? "0" : "0.0", Culture);

    private void Fill()
    {
        _filling = true;
        try
        {
            var s = _app.Config.Settings;
            var brain = _app.AssistantBrain;
            var config = new BrainConfig(s.AssistantBrainProvider, s.AssistantBrainEndpoint);
            OffBox.Visibility = s.AssistantEnabled ? Visibility.Collapsed : Visibility.Visible;
            Body.IsEnabled = s.AssistantEnabled;

            ProviderBox.SelectedItem = ProviderBox.Items.OfType<ProviderOption>().FirstOrDefault(p => p.Value == config.Provider);
            ProviderHint.Text = Loc.T("brainOptions.provider." + config.Provider.Key() + ".hint");
            ServerRows.Visibility = config.Provider == BrainProvider.None ? Visibility.Collapsed : Visibility.Visible;
            var local = config.Provider == BrainProvider.LlamaServer;
            KeyRow.Visibility = config.Provider.UsesApiKey() ? Visibility.Visible : Visibility.Collapsed;
            EndpointRow.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
            LocalServerRows.Visibility = BrowseModelButton.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
            ModelHint.Text = Loc.T(local ? "brainOptions.modelHintGguf" : "brainOptions.modelHint");
            if (!ServerBox.IsKeyboardFocusWithin) ServerBox.Text = s.AssistantBrainServer;
            if (!ServerArgsBox.IsKeyboardFocusWithin) ServerArgsBox.Text = s.AssistantBrainServerArgs;
            KeepRow.Visibility = config.Provider is BrainProvider.Ollama or BrainProvider.LlamaServer ? Visibility.Visible : Visibility.Collapsed;
            KeepBox.SelectedItem = KeepBox.Items.OfType<KeepOption>().FirstOrDefault(k => k.Minutes == s.AssistantBrainKeepMinutes)
                ?? KeepBox.Items.OfType<KeepOption>().First(k => k.Minutes == BrainConfig.DefaultKeepMinutes);
            if (!EndpointBox.IsKeyboardFocusWithin) EndpointBox.Text = s.AssistantBrainEndpoint;
            EndpointBox.PlaceholderText = config.Provider.DefaultEndpoint();
            if (!KeyBox.IsKeyboardFocusWithin && KeyBox.Password != _app.AssistantBrainApiKey) KeyBox.Password = _app.AssistantBrainApiKey;
            var host = Uri.TryCreate(config.Address, UriKind.Absolute, out var uri) ? uri.Host : config.Address;
            PlaceText.Text = BrainProviders.PlaceOf(config.Address) switch
            {
                EndpointPlace.ThisPc => Loc.T("brainOptions.place.thisPc"),
                EndpointPlace.LocalNetwork => Loc.T("brainOptions.place.network", host),
                EndpointPlace.Internet => Loc.T("brainOptions.place.internet", host),
                _ => Loc.T("brainOptions.place.invalid"),
            };

            FillModels(s.AssistantBrainModel, brain);
            FillState(brain);
            FillSetup(config, brain);
            FillFound(config, brain);
            FillFree(config, brain);
        }
        finally
        {
            _filling = false;
        }
    }

    private void FillModels(string chosen, BrainStateMessage? brain)
    {
        var options = new List<ModelOption>
        {
            new(string.Empty, brain is { Automatic: true, Model: { } picked } ? Loc.T("brainOptions.modelAutoIs", BrainProviders.ModelLabel(picked)) : Loc.T("brainOptions.modelAuto")),
        };
        foreach (var model in brain?.Models ?? [])
        {
            var name = BrainProviders.ModelLabel(model.Name);
            if (name != model.Name) name += " · " + Gb(model.SizeBytes) + " GB";
            else if (model.Billions > 0) name += " · " + Loc.T("brainOptions.billions", model.Billions.ToString("0.#", Culture));
            if (!model.Tools) name += " · " + Loc.T("brainOptions.noTools");
            options.Add(new ModelOption(model.Name, name));
        }
        if (chosen.Length > 0 && !options.Any(o => o.Value.Length > 0 && BrainProviders.SameModel(o.Value, chosen)))
            options.Add(new ModelOption(chosen, Loc.T("brainOptions.modelNotThere", BrainProviders.ModelLabel(chosen))));
        if (!ModelBox.IsDropDownOpen)
        {
            ModelBox.ItemsSource = options;
            ModelBox.SelectedItem = options.FirstOrDefault(o => chosen.Length == 0 ? o.Value.Length == 0 : o.Value.Length > 0 && BrainProviders.SameModel(o.Value, chosen)) ?? options[0];
        }
    }

    private void FillState(BrainStateMessage? brain)
    {
        var (text, detail, brush) = (_app.Assistant, brain) switch
        {
            (null, _) => (Loc.T("brainOptions.state.moduleOff"), null, "TextFillColorTertiaryBrush"),
            (_, null) => (Loc.T("brainOptions.state.waiting"), null, "TextFillColorTertiaryBrush"),
            (_, { State: "off" }) => (Loc.T("brainOptions.state.off"), null, "TextFillColorTertiaryBrush"),
            (_, { State: "checking" }) => (Loc.T("brainOptions.state.checking"), null, "TextFillColorTertiaryBrush"),
            (_, { State: "ready", Loading: true, Model: { } model }) => (Loc.T("brainOptions.state.loading", BrainProviders.ModelLabel(model)), null, "Kd.Warning"),
            (_, { State: "ready", Model: { } model } b) => (b.Loaded?.Contains(model, StringComparer.OrdinalIgnoreCase) == true
                ? Loc.T("brainOptions.state.readyLoaded", BrainProviders.ModelLabel(model)) : Loc.T("brainOptions.state.ready", BrainProviders.ModelLabel(model)), null, "Kd.Success"),
            // llama-server not there: what is missing (the program or the model), not an address.
            (_, { State: "unreachable", Provider: "llamaServer" } b) => (b.Message ?? Loc.T("brain.error.noServer"), null, "Kd.Danger"),
            (_, { State: "unreachable" } b) => (Loc.T("brainOptions.state.unreachable", ProviderName(BrainProviders.Parse(b.Provider)), b.Endpoint), b.Message, "Kd.Danger"),
            (_, { State: "noModels" } b) => (Loc.T("brainOptions.state.noModels", ProviderName(BrainProviders.Parse(b.Provider))), null, "Kd.Warning"),
            (_, { State: "modelMissing" } b) => (Loc.T("brainOptions.state.modelMissing", BrainProviders.ModelLabel(b.Model ?? ""), ProviderName(BrainProviders.Parse(b.Provider))), null, "Kd.Warning"),
            (_, { } b) => (Loc.T("brainOptions.state.error"), b.Message, "Kd.Danger"),
        };
        StateText.Text = text;
        StateDetail.Text = detail ?? string.Empty;
        StateDetail.Visibility = string.IsNullOrWhiteSpace(detail) ? Visibility.Collapsed : Visibility.Visible;
        StateDot.SetResourceReference(Shape.FillProperty, brush);
        CheckButton.IsEnabled = brain is not null;
    }

    private void FillSetup(BrainConfig config, BrainStateMessage? brain)
    {
        _main = _other = (SetupAction.None, null);
        string? text = null;
        JobProgress.Visibility = JobText.Visibility = Visibility.Collapsed;
        JobText.Text = string.Empty;

        if (brain?.Job is { State: "running" } job)
        {
            if (job.Kind == "pull")
            {
                text = Loc.T("brainOptions.setup.pulling", job.Model ?? "");
                JobProgress.IsIndeterminate = job.Total <= 0;
                JobProgress.Value = job.Total > 0 ? (double)job.Completed / job.Total : 0;
                JobText.Text = job.Total > 0
                    ? Loc.T("brainOptions.setup.pullProgress", Gb(job.Completed), Gb(job.Total), (100.0 * job.Completed / job.Total).ToString("0", Culture))
                    : Loc.T("brainOptions.setup.pullStarting");
                JobText.Visibility = Visibility.Visible;
                _main = (SetupAction.Cancel, null);
            }
            else
            {
                text = Loc.T("brainOptions.setup.installing");
                JobProgress.IsIndeterminate = true;
            }
            JobProgress.Visibility = Visibility.Visible;
        }
        else if (brain is not null)
        {
            if (brain.Job is { State: "failed" } failed)
            {
                JobText.Text = Loc.T(failed.Kind == "pull" ? "brainOptions.setup.pullFailed" : "brainOptions.setup.installFailed", failed.Error ?? "");
                JobText.Visibility = Visibility.Visible;
            }
            var ollama = config.Provider == BrainProvider.Ollama;
            if (config.Provider == BrainProvider.LlamaServer && brain.State == "noModels")
            {
                text = Loc.T("brainOptions.setup.noGguf");
            }
            else if (brain.State == "unreachable" && brain.Ollama is { Installed: false } absent)
            {
                text = Loc.T("brainOptions.setup.noOllama");
                _main = absent.CanInstall ? (SetupAction.Install, null) : (SetupAction.DownloadPage, null);
                if (absent.CanInstall) _other = (SetupAction.DownloadPage, null);
            }
            else if (brain.State == "unreachable" && brain.Ollama is { Installed: true })
            {
                text = Loc.T("brainOptions.setup.ollamaClosed");
                _main = (SetupAction.Start, null);
            }
            else if (ollama && brain.State == "noModels" && brain.Suggested is { } suggestion)
            {
                text = brain.VideoMemoryGb is { } gb
                    ? Loc.T("brainOptions.setup.noModelsFor", gb.ToString("0.#", Culture), suggestion.Name, suggestion.DownloadGb.ToString("0.0", Culture))
                    : Loc.T("brainOptions.setup.noModels", suggestion.Name, suggestion.DownloadGb.ToString("0.0", Culture));
                _main = (SetupAction.Pull, suggestion.Name);
            }
            else if (ollama && brain.State == "modelMissing" && brain.Model is { Length: > 0 } missing)
            {
                text = Loc.T("brainOptions.setup.missing", missing);
                _main = (SetupAction.Pull, missing);
            }
            else if (ollama && brain.State == "ready" && brain.Suggested is { } better)
            {
                // A model without tools was the only one: one with tools does much better.
                text = Loc.T("brainOptions.setup.noToolModel", better.Name, better.DownloadGb.ToString("0.0", Culture));
                _main = (SetupAction.Pull, better.Name);
            }
        }

        SetupBox.Visibility = text is null && JobText.Visibility != Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        SetupText.Text = text ?? string.Empty;
        SetupText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        SetupMain.Parent.SetValue(VisibilityProperty, text is null ? Visibility.Collapsed : Visibility.Visible);
        ShowSetupButton(SetupMain, _main);
        ShowSetupButton(SetupOther, _other);
    }

    private static void ShowSetupButton(Wpf.Ui.Controls.Button button, (SetupAction Action, string? Model) setup)
    {
        button.Visibility = setup.Action == SetupAction.None ? Visibility.Collapsed : Visibility.Visible;
        button.Content = setup.Action switch
        {
            SetupAction.Install => Loc.T("brainOptions.setup.install"),
            SetupAction.DownloadPage => Loc.T("brainOptions.setup.downloadPage"),
            SetupAction.Start => Loc.T("brainOptions.setup.start"),
            SetupAction.Pull => Loc.T("brainOptions.setup.pull", setup.Model ?? ""),
            SetupAction.Cancel => Loc.T("common.cancel"),
            _ => null,
        };
        button.Icon = new Wpf.Ui.Controls.SymbolIcon(setup.Action switch
        {
            SetupAction.Install or SetupAction.Pull => Wpf.Ui.Controls.SymbolRegular.ArrowDownload24,
            SetupAction.DownloadPage => Wpf.Ui.Controls.SymbolRegular.Open24,
            SetupAction.Start => Wpf.Ui.Controls.SymbolRegular.Play24,
            _ => Wpf.Ui.Controls.SymbolRegular.Dismiss24,
        });
    }

    private void FillFound(BrainConfig config, BrainStateMessage? brain)
    {
        var found = brain is { State: not "ready" } ? brain.Found ?? [] : [];
        var options = found
            .Where(f => !(BrainProviders.Parse(f.Provider) == config.Provider && string.Equals(f.Endpoint.TrimEnd('/'), config.Address.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
            .Select(f => new FoundOption(f, Loc.T(f.Models > 0 ? "brainOptions.found" : "brainOptions.foundEmpty", ProviderName(BrainProviders.Parse(f.Provider)), f.Endpoint, f.Models)))
            .ToList();
        if (FoundList.Children.OfType<ContentPresenter>().Select(p => p.Content).SequenceEqual(options)) return;
        FoundList.Children.Clear();
        var template = (DataTemplate)FoundList.Resources["FoundTemplate"];
        foreach (var option in options) FoundList.Children.Add(new ContentPresenter { Content = option, ContentTemplate = template });
        FoundList.Visibility = options.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FillFree(BrainConfig config, BrainStateMessage? brain)
    {
        // An OpenAI-compatible server neither says what it has loaded nor lets it be unloaded.
        FreeRow.Visibility = config.Provider == BrainProvider.OpenAICompatible ? Visibility.Collapsed : Visibility.Visible;
        var loaded = brain?.Loaded ?? [];
        var loading = brain?.Loading == true;
        FreeText.Text = loaded.Count > 0 ? Loc.T("brainOptions.freeLoaded", string.Join(", ", loaded.Select(BrainProviders.ModelLabel))) : Loc.T("brainOptions.freeHint");
        FreeButton.IsEnabled = loaded.Count > 0 || loading;
        LoadButton.Visibility = config.Provider is BrainProvider.Ollama or BrainProvider.LlamaServer ? Visibility.Visible : Visibility.Collapsed;
        LoadButton.IsEnabled = brain is { State: "ready", Model: not null } && loaded.Count == 0 && !loading;
    }

    // ── changes ──────────────────────────────────────────────────────────

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || ProviderBox.SelectedItem is not ProviderOption option) return;
        var s = _app.Config.Settings;
        if (s.AssistantBrainProvider == option.Value) return;
        s.AssistantBrainProvider = option.Value;
        s.AssistantBrainEndpoint = string.Empty;
        s.AssistantBrainModel = string.Empty;
        Fill();
    }

    private void OnEndpointLeft(object sender, RoutedEventArgs e)
    {
        var s = _app.Config.Settings;
        var typed = EndpointBox.Text.Trim().TrimEnd('/');
        if (string.Equals(typed, s.AssistantBrainProvider.DefaultEndpoint(), StringComparison.OrdinalIgnoreCase)) typed = string.Empty;
        if (typed == s.AssistantBrainEndpoint) return;
        s.AssistantBrainEndpoint = typed;
        Fill();
    }

    private void OnKeyLeft(object sender, RoutedEventArgs e)
    {
        if (KeyBox.Password.Trim() != _app.AssistantBrainApiKey) _app.SetAssistantBrainApiKey(KeyBox.Password);
    }

    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || ModelBox.SelectedItem is not ModelOption option) return;
        _app.Config.Settings.AssistantBrainModel = option.Value;
    }

    /// <summary>Looks again; when it is not ready, also in the usual places (another server may be there).</summary>
    private void OnCheck(object sender, RoutedEventArgs e)
    {
        _app.AskAssistantBrain("check");
        if (_app.AssistantBrain is not { State: "ready" }) _app.AskAssistantBrain("scan");
    }

    private void OnSetupMain(object sender, RoutedEventArgs e) => RunSetup(_main);

    private void OnSetupOther(object sender, RoutedEventArgs e) => RunSetup(_other);

    private void RunSetup((SetupAction Action, string? Model) setup)
    {
        switch (setup.Action)
        {
            case SetupAction.Install: _app.AskAssistantBrain("installOllama"); break;
            case SetupAction.DownloadPage: _app.AskAssistantBrain("downloadPage"); break;
            case SetupAction.Start: _app.AskAssistantBrain("startOllama"); break;
            case SetupAction.Pull: _app.AskAssistantBrain("pull", setup.Model); break;
            case SetupAction.Cancel: _app.AskAssistantBrain("cancel"); break;
        }
    }

    private void OnUseFound(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FoundOption { Found: var found }) return;
        var s = _app.Config.Settings;
        var provider = BrainProviders.Parse(found.Provider);
        s.AssistantBrainProvider = provider;
        s.AssistantBrainEndpoint = string.Equals(found.Endpoint, provider.DefaultEndpoint(), StringComparison.OrdinalIgnoreCase) ? string.Empty : found.Endpoint;
        s.AssistantBrainModel = string.Empty;
        Fill();
    }

    private void OnFree(object sender, RoutedEventArgs e) => _app.AskAssistantBrain("unload");

    private void OnLoad(object sender, RoutedEventArgs e) => _app.AskAssistantBrain("load");

    private void OnKeepChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || KeepBox.SelectedItem is not KeepOption option) return;
        _app.Config.Settings.AssistantBrainKeepMinutes = option.Minutes;
    }

    private void OnServerLeft(object sender, RoutedEventArgs e)
    {
        var typed = ServerBox.Text.Trim().Trim('"');
        if (typed == _app.Config.Settings.AssistantBrainServer) return;
        _app.Config.Settings.AssistantBrainServer = typed;
        Fill();
    }

    private void OnServerArgsLeft(object sender, RoutedEventArgs e)
    {
        var typed = ServerArgsBox.Text.Trim();
        if (typed != _app.Config.Settings.AssistantBrainServerArgs) _app.Config.Settings.AssistantBrainServerArgs = typed;
    }

    private void OnBrowseServer(object sender, RoutedEventArgs e)
    {
        var s = _app.Config.Settings;
        if (Browse(Loc.T("brainOptions.browseServerTitle"), "llama-server.exe|llama-server.exe|*.exe|*.exe", s.AssistantBrainServer) is not { } path) return;
        s.AssistantBrainServer = path;
        Fill();
    }

    private void OnBrowseModel(object sender, RoutedEventArgs e)
    {
        var s = _app.Config.Settings;
        var near = s.AssistantBrainModel.Length > 0 ? s.AssistantBrainModel : s.AssistantBrainServer;
        if (Browse(Loc.T("brainOptions.browseModelTitle"), "*.gguf|*.gguf", near) is not { } path) return;
        s.AssistantBrainModel = path;
        Fill();
    }

    /// <summary>A file chosen in the usual dialog, starting next to <paramref name="near"/>; null when cancelled.</summary>
    private string? Browse(string title, string filter, string near)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        try
        {
            if (near.Length > 0 && System.IO.Path.GetDirectoryName(near) is { } folder && System.IO.Directory.Exists(folder)) dialog.InitialDirectory = folder;
        }
        catch (ArgumentException)
        {
            // A path typed by hand that is not one.
        }
        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
    }

    private void OnGoGeneral(object sender, RoutedEventArgs e) =>
        (Window.GetWindow(this) as MainWindow)?.Navigate("settings/assistant");
}

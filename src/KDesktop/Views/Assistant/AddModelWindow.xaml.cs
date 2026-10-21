using System.Net.Http;
using System.Net;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using KDesktop.Core.Assistant.Brain;
using KDesktop.Core.Localization;
using KDesktop.Core.Platform;
using KDesktop.Views.Pages;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace KDesktop.Views.Assistant;

/// <summary>
/// «Añadir modelo» (5.0.107): what the user pastes —a link of Hugging Face or «owner/repo», with a file or «:Q4_K_M»—
/// lists the repository's models with their size and whether they fit in the graphics card (the biggest that fits is
/// chosen); with vision on, its projector goes along. «Descargar» starts the download, which goes on in Options → Brain.
/// </summary>
public partial class AddModelWindow : FluentWindow
{
    /// <summary>Video memory left for Windows and the context when saying that a model fits.</summary>
    private const double SpareGb = 2.5;

    private readonly App _app = App.Current;
    private IReadOnlyList<HfModel> _models = [];
    private HfModel? _chosen;
    private string? _repo;

    public AddModelWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RepoBox.Focus();
            if (Clipboard.ContainsText() && HuggingFace.Parse(Clipboard.GetText()) is { } pasted && Clipboard.GetText().Contains("huggingface", StringComparison.OrdinalIgnoreCase))
            {
                RepoBox.Text = pasted.ToString();
                _ = SearchAsync();
            }
        };
    }

    private double? VideoGb => _app.AssistantBrain?.VideoMemoryGb;

    private void OnRepoKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        _ = SearchAsync();
    }

    private void OnSearch(object sender, RoutedEventArgs e) => _ = SearchAsync();

    private async Task SearchAsync()
    {
        if (HuggingFace.Parse(RepoBox.Text) is not { } reference)
        {
            StatusText.Text = Loc.T("addModel.notARepo");
            return;
        }
        Busy.Visibility = Visibility.Visible;
        StatusText.Text = Loc.T("addModel.searching", reference.Repo);
        Choices.Children.Clear();
        DownloadButton.IsEnabled = false;
        VisionCheck.Visibility = PageButton.Visibility = Visibility.Collapsed;
        try
        {
            _models = await _app.HuggingFaceListAsync(reference.Repo);
            _repo = reference.Repo;
        }
        catch (HttpRequestException ex)
        {
            StatusText.Text = ex.StatusCode switch
            {
                HttpStatusCode.NotFound => Loc.T("addModel.notFound", reference.Repo),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Loc.T("addModel.gated", reference.Repo),
                _ => Loc.T("addModel.failed", ex.Message),
            };
            return;
        }
        catch (Exception ex) when (ex is TaskCanceledException or System.Text.Json.JsonException)
        {
            StatusText.Text = Loc.T("addModel.failed", ex.Message);
            return;
        }
        finally
        {
            Busy.Visibility = Visibility.Collapsed;
        }
        PageButton.Visibility = Visibility.Visible;
        var talk = _models.Where(m => !m.IsProjector).ToList();
        if (talk.Count == 0)
        {
            StatusText.Text = Loc.T("addModel.noGguf", reference.Repo);
            return;
        }
        StatusText.Text = VideoGb is { } gb ? Loc.T("addModel.found", talk.Count, gb.ToString("0.#", Loc.Instance.Culture)) : Loc.T("addModel.foundNoGpu", talk.Count);
        var room = Room;
        _chosen = HuggingFace.Pick(_models, reference)
            ?? talk.Where(m => m.Size <= room).OrderByDescending(m => m.Size).FirstOrDefault()
            ?? talk.OrderBy(m => m.Size).First();
        var have = (_app.AssistantBrain?.Models ?? []).Where(m => ModelRefs.IsLocal(m.Name)).Select(m => System.IO.Path.GetFileName(m.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var model in talk) Choices.Children.Add(Choice(model, have.Contains(System.IO.Path.GetFileName(model.File))));
        ShowVision();
    }

    private double Room => VideoGb is { } gb ? (gb - SpareGb) * 1e9 : double.MaxValue;

    private RadioButton Choice(HfModel model, bool here)
    {
        var fits = model.Size <= Room ? "fits" : VideoGb is { } gb && model.Size <= (gb - 0.8) * 1e9 ? "tight" : "big";
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        text.Inlines.Add(new System.Windows.Documents.Run(model.Name) { FontWeight = FontWeights.SemiBold });
        var detail = new System.Windows.Documents.Run($"   {AssistantBrainSection.Gb(model.Size)} GB · {Loc.T("addModel." + fits)}{(here ? " · " + Loc.T("addModel.here") : "")}");
        detail.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, fits switch
        {
            "fits" => "Kd.Success",
            "tight" => "Kd.Warning",
            _ => "Kd.Danger",
        });
        text.Inlines.Add(detail);
        var radio = new RadioButton { Content = text, GroupName = "model", IsChecked = model == _chosen, Margin = new Thickness(0, 3, 0, 3), Tag = model };
        AutomationProperties.SetName(radio, $"{model.Name} {AssistantBrainSection.Gb(model.Size)} GB");
        radio.Checked += (_, _) =>
        {
            _chosen = model;
            ShowVision();
        };
        return radio;
    }

    private void ShowVision()
    {
        DownloadButton.IsEnabled = _chosen is not null;
        var projector = _chosen is null ? null : HuggingFace.ProjectorFor(_models, _chosen);
        VisionCheck.Visibility = projector is null ? Visibility.Collapsed : Visibility.Visible;
        if (projector is null) return;
        VisionCheck.Content = Loc.T("addModel.vision", projector.Name, AssistantBrainSection.Gb(projector.Size));
        VisionCheck.IsChecked = _app.Config.Settings.AssistantBrainVision;
    }

    private void OnDownload(object sender, RoutedEventArgs e)
    {
        if (_chosen is not { } model) return;
        var projector = VisionCheck.Visibility == Visibility.Visible && VisionCheck.IsChecked == true ? HuggingFace.ProjectorFor(_models, model) : null;
        if (!_app.StartModelDownload(model, projector))
        {
            StatusText.Text = Loc.T("addModel.busy");
            return;
        }
        Close();
    }

    private void OnPage(object sender, RoutedEventArgs e)
    {
        if (_repo is not null) ProcessLauncher.Launch(new LaunchSpec(HuggingFace.PageOf(_repo), string.Empty), asAdmin: false);
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}

using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Localization;
using KDesktop.Services;

namespace KDesktop;

/// <summary>
/// The showcase of each installed assistant (stages 13.9 and 16.1): what it can show (the host's entrances, exits, states,
/// reactions, gestures and what it does to a window, and its own animations) and «do this one now», on its stage — another
/// copy of it, on a desktop of its own drawn off the screen (<see cref="AssistantStage"/>), whose pictures come here live. The
/// «Try» tab of its card opens it and closes it; one stage at a time.
/// </summary>
public partial class App
{
    private AssistantStage? _stage;
    private string? _stageId;
    private MemoryMappedFile? _stageMap;
    private MemoryMappedViewAccessor? _stageView;
    private string? _stageMapName;
    private StageFrameMessage? _stageFrame;
    private long _stageShown;
    private int _stagePainting;
    private byte[] _stagePixels = [];

    /// <summary>The stage's picture (UI thread), redrawn in place as new ones come; null without a stage.</summary>
    public WriteableBitmap? AssistantStagePicture { get; private set; }

    /// <summary>The assistant of the stage open now, or null.</summary>
    public string? AssistantStageId => _stageId;

    /// <summary>The stage of an assistant changed: it is ready (true) or it closed (false) (UI thread).</summary>
    public event Action<string, bool>? AssistantStageChanged;

    /// <summary>A new picture of the stage is in <see cref="AssistantStagePicture"/> (UI thread).</summary>
    public event Action? AssistantStagePainted;

    /// <summary>What an assistant can show came (UI thread).</summary>
    public event Action<ShowcaseListMessage>? AssistantShowcaseListed;

    /// <summary>An item of the showcase ended (UI thread).</summary>
    public event Action<ShowcaseDoneMessage>? AssistantShowcaseDone;

    /// <summary>Opens the stage of an installed assistant (the one before closes); false when it is not installed.</summary>
    public bool OpenAssistantStage(string id)
    {
        if (_stageId == id && _stage is not null) return true;
        CloseAssistantStage();
        if (AssistantPackages.Find(id) is not { } installed) return false;
        var stage = new AssistantStage(installed.Folder, StageWelcome);
        _stage = stage;
        _stageId = id;
        stage.Received += message => OnStageMessage(stage, message);
        stage.Ended += reason => Dispatcher.BeginInvoke(() =>
        {
            if (_stage != stage) return;
            _ = DropStageAsync();
            AssistantStageChanged?.Invoke(id, false);
        });
        stage.Start();
        return true;
    }

    /// <summary>Closes the stage open now (if any).</summary>
    public void CloseAssistantStage()
    {
        if (_stage is null) return;
        var id = _stageId!;
        _ = DropStageAsync();
        AssistantStageChanged?.Invoke(id, false);
    }

    private async Task DropStageAsync()
    {
        var stage = _stage;
        _stage = null;
        _stageId = null;
        _stageFrame = null;
        _stageView?.Dispose();
        _stageMap?.Dispose();
        _stageView = null;
        _stageMap = null;
        _stageMapName = null;
        AssistantStagePicture = null;
        if (stage is not null) await stage.DisposeAsync().ConfigureAwait(true);
    }

    /// <summary>What the stage needs to start: K-Desktop's language and theme, and the sound, size and personality of its assistant.</summary>
    private WelcomeMessage StageWelcome() =>
        new(AssistantLanguage, ThemeService.IsDark ? "dark" : "light", false,
            new() { ["general"] = AssistantGeneral(), ["personal"] = AssistantPersonalWelcome(), ["common"] = AssistantCommonWelcome() });

    /// <summary>A message from the stage (pool thread).</summary>
    private void OnStageMessage(AssistantStage stage, PipeMessage message)
    {
        if (message is StageFrameMessage frame)
        {
            Volatile.Write(ref _stageFrame, frame);
            if (Interlocked.Exchange(ref _stagePainting, 1) == 0) Dispatcher.BeginInvoke(PaintStage, System.Windows.Threading.DispatcherPriority.Render);
            return;
        }
        Dispatcher.BeginInvoke(() =>
        {
            if (_stage != stage) return;
            switch (message)
            {
                case LoadedMessage { Ok: true } loaded:
                    AssistantStageChanged?.Invoke(loaded.Assistant, true);
                    break;
                case LoadedMessage loaded:
                    Core.Log.Warn($"Asistente: el escenario de «{_stageId}» no ha podido cargarlo ({loaded.Error})");
                    CloseAssistantStage();
                    break;
                case ShowcaseListMessage showcase:
                    AssistantShowcaseListed?.Invoke(showcase);
                    break;
                case ShowcaseDoneMessage showcaseDone:
                    AssistantShowcaseDone?.Invoke(showcaseDone);
                    break;
            }
        });
    }

    /// <summary>The newest picture of the stage into <see cref="AssistantStagePicture"/> (UI thread; older ones that come late are skipped).</summary>
    private void PaintStage()
    {
        Volatile.Write(ref _stagePainting, 0);
        if (_stage is null || Volatile.Read(ref _stageFrame) is not { } frame || frame.Seq <= _stageShown) return;
        try
        {
            if (_stageMapName != frame.Map)
            {
                _stageView?.Dispose();
                _stageMap?.Dispose();
                _stageMap = MemoryMappedFile.OpenExisting(@"Local\" + frame.Map, MemoryMappedFileRights.Read);
                _stageView = _stageMap.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                _stageMapName = frame.Map;
            }
            if (AssistantStagePicture is not { } picture || picture.PixelWidth != frame.Width || picture.PixelHeight != frame.Height)
                AssistantStagePicture = picture = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null);
            var size = frame.Width * frame.Height * 4;
            if (_stagePixels.Length != size) _stagePixels = new byte[size];
            _stageView!.ReadArray((long)frame.Slot * size, _stagePixels, 0, size);
            picture.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), _stagePixels, frame.Width * 4, 0);
            _stageShown = frame.Seq;
            AssistantStagePainted?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException or ArgumentException)
        {
            Core.Log.Warn($"Asistente: no se puede leer la imagen del escenario ({ex.Message})");
        }
    }

    /// <summary>The user's pointer over the stage's picture (pixels of the stage): <c>cursor</c>, <c>leave</c>, <c>down</c>, <c>up</c>.</summary>
    public void PointAtStage(string op, int x, int y) => _stage?.Send(new StageMessage(op, x, y));

    /// <summary>Its windows back where they started.</summary>
    public void ResetAssistantStage() => _stage?.Send(new StageMessage(StageMessage.Reset));

    /// <summary>Asks what it can show (answered with <see cref="AssistantShowcaseListed"/>); false when its stage is not ready.</summary>
    public bool AskAssistantShowcase(string id)
    {
        if (_stageId != id || _stage is not { IsConnected: true } stage) return false;
        stage.Send(new ShowcaseMessage(id, ShowcaseMessage.List));
        return true;
    }

    /// <summary>«Do this one now» (the one before stops); <see cref="AssistantShowcaseDone"/> says how it went.</summary>
    public bool PlayAssistantShowcase(string id, ShowcaseEntry item)
    {
        if (_stageId != id || _stage is not { IsConnected: true } stage) return false;
        Core.Log.Info($"Asistente: escaparate de «{id}»: {item.Id}");
        stage.Send(new ShowcaseMessage(id, ShowcaseMessage.Play, item.Id, item.Own));
        return true;
    }

    public void StopAssistantShowcase(string id)
    {
        if (_stageId == id) _stage?.Send(new ShowcaseMessage(id, ShowcaseMessage.Stop));
    }

    /// <summary>The settings of an assistant changed: its stage, if open, gets them too.</summary>
    private void SendStageSettings(string id, JsonObject values)
    {
        if (_stageId == id) _stage?.Send(new ConfigMessage("assistantSettings", values, id));
    }

    /// <summary>The common settings changed (size, volume…): its stage, if open, gets them too.</summary>
    private void SendStageCommon()
    {
        _stage?.Send(new ConfigMessage("common", AssistantCommonWelcome()));
    }

    /// <summary>
    /// The name of an item: the host's ones by K-Desktop's texts; its own ones by their names in the language of K-Desktop
    /// (else English, else any), or K-Desktop's text for that id (the animations of the assistants without code), or the id.
    /// </summary>
    public static string ShowcaseName(ShowcaseEntry item)
    {
        if (item.Own && item.Names is { Count: > 0 } names)
        {
            var language = Loc.Instance.Language.Split('-')[0];
            foreach (var wanted in new[] { language, "en" })
                if (names.FirstOrDefault(n => n.Key.Split('-')[0].Equals(wanted, StringComparison.OrdinalIgnoreCase)).Value is { Length: > 0 } name) return name;
            return names.Values.First();
        }
        var key = (item.Own ? "showcase.own." : "showcase.item.") + item.Id;
        var text = Loc.T(key);
        return text == key ? item.Id : text;
    }

    /// <summary>The title of a group (one K-Desktop does not know: «Other»).</summary>
    public static string ShowcaseGroupName(string group)
    {
        var key = "showcase.group." + group;
        var text = Loc.T(key);
        return text == key ? Loc.T("showcase.group.other") : text;
    }
}

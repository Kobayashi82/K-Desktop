using System.Text.Json.Nodes;
using KDesktop.Core;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Assistant.Voice;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop;

/// <summary>
/// How the assistants speak, from K-Desktop's side (stage 5.4): the settings of Options → Assistant → Voice (the
/// <c>voice</c> section of <c>config</c>), the download of Pocket TTS for the interface language (the process only reads
/// it) and «Probar». The process says which voice really speaks with <c>voiceState</c>.
/// </summary>
public partial class App
{
    /// <summary>The <c>voice</c> section last sent, to send only changes.</summary>
    private string? _assistantVoiceSent;
    private ModelDownloader? _pocketDownload;

    /// <summary>The last state the process sent of its voice (null while it is not running).</summary>
    public VoiceStateMessage? AssistantVoice { get; private set; }

    /// <summary>Raised (UI thread) when <see cref="AssistantVoice"/> changes or the download of Pocket TTS moves.</summary>
    public event Action? AssistantVoiceChanged;

    /// <summary>The download of Pocket TTS (the model of the language and the voice chosen).</summary>
    public ModelDownloader PocketDownload
    {
        get
        {
            if (_pocketDownload is not null) return _pocketDownload;
            _pocketDownload = new ModelDownloader(() => PocketModels.Files(PocketLanguage, PocketVoice), "la voz de los asistentes");
            _pocketDownload.Changed += () => Dispatcher.BeginInvoke(() =>
            {
                if (!_pocketDownload.IsDownloading)
                {
                    if (_pocketDownload.Error is { } error) Ui.ShowOsd(Loc.T("assistantVoice.downloadError", error), Core.Engine.OsdKind.Warning, TimeSpan.FromSeconds(6));
                    else if (PocketModels.Installed(PocketLanguage, PocketVoice)) Ui.ShowOsd(Loc.T("assistantVoice.downloaded"), Core.Engine.OsdKind.Success);
                    // The process looks at the files on each sentence: it says which voice speaks now.
                    AskAssistantVoice("state");
                }
                AssistantVoiceChanged?.Invoke();
            });
            return _pocketDownload;
        }
    }

    /// <summary>The language of Pocket TTS: the interface's (what the assistants speak).</summary>
    public static string PocketLanguage => Loc.Instance.Language;

    /// <summary>The voice of Pocket TTS chosen (one of the language's).</summary>
    public string PocketVoice => PocketModels.VoiceOrDefault(PocketLanguage, Config.Settings.AssistantVoicePocket);

    private JsonObject AssistantVoiceConfig()
    {
        var s = Config.Settings;
        return new JsonObject
        {
            ["engine"] = s.AssistantVoiceEngine,
            ["profile"] = s.AssistantVoiceProfile,
            ["pocketVoice"] = PocketVoice,
            ["windowsVoice"] = s.AssistantVoiceWindows,
            ["speed"] = Math.Clamp(s.AssistantVoiceSpeed, 0.5, 2),
            ["volume"] = Math.Clamp(s.AssistantVoiceVolume, 0, 100) / 100.0,
            ["pan"] = s.AssistantVoicePan,
            ["night"] = s.AssistantVoiceNight,
            ["fullscreen"] = s.AssistantVoiceFullscreen,
        };
    }

    /// <summary>After a settings change: the voice section, if it changed (UI thread).</summary>
    private void SendAssistantVoiceConfig()
    {
        var voice = AssistantVoiceConfig();
        var json = voice.ToJsonString();
        if (json == _assistantVoiceSent) return;
        _assistantVoiceSent = json;
        _assistant?.Send(new ConfigMessage("voice", voice));
    }

    /// <summary>Asks the process about its voice (<c>state</c>, <c>unload</c>).</summary>
    public void AskAssistantVoice(string op) => _assistant?.Send(new VoiceMessage(op));

    /// <summary>
    /// «Probar»: <paramref name="who"/> says it (null: the one who answers what is not addressed to anyone; empty: only the
    /// voice), in <paramref name="mood"/> (null: plain).
    /// </summary>
    public void TestAssistantVoice(string text, string? mood = null, string? who = null)
    {
        if (_assistant is not { IsRunning: true } supervisor || text.Trim().Length == 0) return;
        supervisor.Send(new VoiceMessage("test", text.Trim(), who is null ? PickAssistant() : who.Length > 0 ? who : null, mood));
    }

    /// <summary>Stops the test that is sounding.</summary>
    public void StopAssistantVoiceTest() => _assistant?.Send(new VoiceMessage("stop"));

    /// <summary>A Pocket TTS voice chosen: it is downloaded if the model is there and it is not (a few MB).</summary>
    public void ChoosePocketVoice(string voice)
    {
        Config.Settings.AssistantVoicePocket = voice;
        if (PocketModels.ModelInstalled(PocketLanguage) && !PocketModels.Installed(PocketLanguage, voice) && !PocketDownload.IsDownloading) PocketDownload.Start();
    }

    /// <summary>Removes Pocket TTS (the process lets it go first).</summary>
    public async Task DeletePocketAsync()
    {
        PocketDownload.Cancel();
        AskAssistantVoice("unload");
        await Task.Delay(800);
        await Task.Run(PocketModels.Delete);
        AskAssistantVoice("state");
        AssistantVoiceChanged?.Invoke();
    }

    private void OnAssistantVoiceState(VoiceStateMessage state)
    {
        AssistantVoice = state;
        AssistantVoiceChanged?.Invoke();
    }

    /// <summary>The process stopped: nothing is known of its voice.</summary>
    private void ForgetAssistantVoice()
    {
        if (AssistantVoice is null) return;
        AssistantVoice = null;
        AssistantVoiceChanged?.Invoke();
    }
}

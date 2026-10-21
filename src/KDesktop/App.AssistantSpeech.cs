using System.Text.Json.Nodes;
using KDesktop.Core;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Assistant.Voice;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop;

/// <summary>
/// How the assistants speak, from K-Desktop's side (stage 5.4; Kokoro since 5.0.94): the settings of Options → Assistant
/// → Voice (the <c>voice</c> section of <c>config</c>), the download of Kokoro for the interface language (the model, the
/// voices chosen and, for English, French, Italian and Portuguese, espeak-ng; the process only reads them) and «Probar».
/// The process says which voice really speaks with <c>voiceState</c>.
/// </summary>
public partial class App
{
    /// <summary>The <c>voice</c> section last sent, to send only changes.</summary>
    private string? _assistantVoiceSent;
    private ModelDownloader? _kokoroDownload;
    private static int _oldVoiceCleaned;

    /// <summary>The last state the process sent of its voice (null while it is not running).</summary>
    public VoiceStateMessage? AssistantVoice { get; private set; }

    /// <summary>Raised (UI thread) when <see cref="AssistantVoice"/> changes or the download of Kokoro moves.</summary>
    public event Action? AssistantVoiceChanged;

    /// <summary>The download of Kokoro (the model, the voice or mix chosen and, if the language needs it, espeak-ng).</summary>
    public ModelDownloader KokoroDownload
    {
        get
        {
            if (_kokoroDownload is not null) return _kokoroDownload;
            // The general voice and the own ones of the assistants (another language, another voice).
            _kokoroDownload = new ModelDownloader(KokoroFiles, "la voz de los asistentes");
            _kokoroDownload.Changed += () =>
            {
                // espeak-ng comes packed: it is unpacked here, before the process looks for it (pool thread).
                if (!_kokoroDownload.IsDownloading && _kokoroDownload.Error is null) KokoroModels.UnpackEspeak();
                Dispatcher.BeginInvoke(() =>
                {
                    if (!_kokoroDownload.IsDownloading)
                    {
                        if (_kokoroDownload.Error is { } error) Ui.ShowOsd(Loc.T("assistantVoice.downloadError", error), Core.Engine.OsdKind.Warning, TimeSpan.FromSeconds(6));
                        else if (!KokoroMissing) Ui.ShowOsd(Loc.T("assistantVoice.downloaded"), Core.Engine.OsdKind.Success);
                        // The process looks at the files on each sentence: it says which voice speaks now.
                        AskAssistantVoice("state");
                    }
                    AssistantVoiceChanged?.Invoke();
                });
            };
            return _kokoroDownload;
        }
    }

    /// <summary>The language of Kokoro: the one the assistants speak in.</summary>
    public string KokoroLanguage => AssistantLanguage;

    /// <summary>The voice or mix of Kokoro chosen (of the language; else its first one).</summary>
    public string KokoroVoice => KokoroModels.VoiceOrDefault(KokoroLanguage, Config.Settings.AssistantVoiceKokoro);

    private JsonObject AssistantVoiceConfig()
    {
        // Pocket TTS, the voice until 5.0.93, leaves the disk once.
        if (Interlocked.Exchange(ref _oldVoiceCleaned, 1) == 0) _ = Task.Run(KokoroModels.DeleteOldPocket);
        var s = Config.Settings;
        return new JsonObject
        {
            ["engine"] = s.AssistantVoiceEngine,
            ["profile"] = s.AssistantVoiceProfile,
            ["kokoroVoice"] = KokoroVoice,
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
        Voice.OwnVoices.Said("test", text.Trim(), DateTime.Now);
        supervisor.Send(new VoiceMessage("test", text.Trim(), who is null ? PickAssistant() : who.Length > 0 ? who : null, mood));
    }

    /// <summary>The test sentence in the language it is said in (it may not be K-Desktop's).</summary>
    public static string TestSentence(string language, string name)
    {
        var text = Loc.Strings(language, "assistantVoice.testText").GetValueOrDefault("assistantVoice.testText") ?? Loc.T("assistantVoice.testText");
        return string.Format(Loc.Instance.Culture, text, name);
    }

    /// <summary>Stops the test that is sounding.</summary>
    public void StopAssistantVoiceTest() => _assistant?.Send(new VoiceMessage("stop"));

    /// <summary>
    /// A Kokoro voice or mix chosen («ef_dora», «ef_dora:0.7,em_alex:0.3»): with the model already there, a voice that is
    /// missing is downloaded at once (half a megabyte).
    /// </summary>
    public void ChooseKokoroVoice(string spec)
    {
        Config.Settings.AssistantVoiceKokoro = spec;
        if (KokoroModels.ModelInstalled && !KokoroModels.Installed(KokoroLanguage, KokoroVoice) && !KokoroDownload.IsDownloading) KokoroDownload.Start();
    }

    /// <summary>Removes Kokoro and espeak-ng (the process lets them go first).</summary>
    public async Task DeleteKokoroAsync()
    {
        KokoroDownload.Cancel();
        AskAssistantVoice("unload");
        await Task.Delay(800);
        await Task.Run(KokoroModels.Delete);
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

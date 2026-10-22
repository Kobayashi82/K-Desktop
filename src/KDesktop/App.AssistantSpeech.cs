using System.Text.Json.Nodes;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Assistant.Voice;
using KDesktop.Core.Localization;
using KDesktop.Core.Voice;

namespace KDesktop;

/// <summary>
/// How the assistants speak, from K-Desktop's side (stage 5.4; Kokoro since 5.0.94, Piper since 5.0.110): the settings of
/// Options → Assistant → Voice (the <c>voice</c> section of <c>config</c>), the downloads of Kokoro (the model, the voices
/// chosen and, for English, French, Italian and Portuguese, espeak-ng) and of Piper (each voice chosen, and espeak-ng) for
/// the language the assistants speak in (the process only reads them) and «Probar». The process says which voice really
/// speaks with <c>voiceState</c>.
/// </summary>
public partial class App
{
    /// <summary>The <c>voice</c> section last sent, to send only changes.</summary>
    private string? _assistantVoiceSent;
    private ModelDownloader? _kokoroDownload;
    private ModelDownloader? _piperDownload;
    private static int _oldVoiceCleaned;

    /// <summary>The last state the process sent of its voice (null while it is not running).</summary>
    public VoiceStateMessage? AssistantVoice { get; private set; }

    /// <summary>Raised (UI thread) when <see cref="AssistantVoice"/> changes or a download of a voice moves.</summary>
    public event Action? AssistantVoiceChanged;

    /// <summary>The download of the Piper voices in use (the general one and the assistants' own) and, the first time, espeak-ng.</summary>
    public ModelDownloader PiperDownload
    {
        get
        {
            if (_piperDownload is not null) return _piperDownload;
            _piperDownload = new ModelDownloader(PiperFiles, "las voces de Piper");
            _piperDownload.Changed += () => VoiceDownloadChanged(_piperDownload, () => PiperMissing);
            return _piperDownload;
        }
    }

    /// <summary>
    /// A download of a voice moved (pool thread): espeak-ng comes packed and is unpacked here, before the process looks for
    /// it; at the end, a notice, and the process says which voice speaks now (it looks at the files on each sentence).
    /// </summary>
    private void VoiceDownloadChanged(ModelDownloader download, Func<bool> missing)
    {
        if (!download.IsDownloading && download.Error is null) EspeakFiles.Unpack();
        Dispatcher.BeginInvoke(() =>
        {
            if (!download.IsDownloading)
            {
                if (download.Error is { } error) Ui.ShowOsd(Loc.T("assistantVoice.downloadError", error), Core.Engine.OsdKind.Warning, TimeSpan.FromSeconds(6));
                else if (!missing()) Ui.ShowOsd(Loc.T("assistantVoice.downloaded"), Core.Engine.OsdKind.Success);
                AskAssistantVoice("state");
            }
            AssistantVoiceChanged?.Invoke();
        });
    }

    /// <summary>The download of Kokoro (the model, the voice or mix chosen and, if the language needs it, espeak-ng).</summary>
    public ModelDownloader KokoroDownload
    {
        get
        {
            if (_kokoroDownload is not null) return _kokoroDownload;
            // The general voice and the own ones of the assistants (another language, another voice).
            _kokoroDownload = new ModelDownloader(KokoroFiles, "la voz de los asistentes");
            _kokoroDownload.Changed += () => VoiceDownloadChanged(_kokoroDownload, () => KokoroMissing);
            return _kokoroDownload;
        }
    }

    /// <summary>The language of Kokoro: the one the assistants speak in.</summary>
    public string KokoroLanguage => AssistantLanguage;

    /// <summary>The voice or mix of Kokoro chosen (of the language; else its first one).</summary>
    public string KokoroVoice => KokoroModels.VoiceOrDefault(KokoroLanguage, Config.Settings.AssistantVoiceKokoro);

    /// <summary>The Piper voice chosen, of the language the assistants speak in (else its default one).</summary>
    public string PiperVoice => PiperModels.VoiceOrDefault(AssistantLanguage, Config.Settings.AssistantVoicePiper);

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
            ["piperVoice"] = PiperVoice,
            ["piperFolders"] = new JsonArray([.. s.AssistantPiperFolders.Select(f => (JsonNode?)f)]),
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
    /// «Probar»: <paramref name="who"/> says it (null: the last one talked to, or K-Desktop with nobody active), in
    /// <paramref name="mood"/> (null: plain). K-Desktop, or one that is not on the desktop, has no bubble: the OSD shows it.
    /// </summary>
    public void TestAssistantVoice(string text, string? mood = null, string? who = null)
    {
        if (_assistant is not { IsRunning: true } supervisor || text.Trim().Length == 0) return;
        text = text.Trim();
        var id = who is { Length: > 0 } ? who : PickAssistant() ?? AssistantTargeting.KDesktop;
        Voice.OwnVoices.Said("test", text, DateTime.Now);
        supervisor.Send(new VoiceMessage("test", text, ProcessIdOf(id), mood));
        if (AssistantTargeting.IsSystem(id) || !IsOnDesktop(id))
            Ui.ShowOsd($"{AssistantName(id)}: {text}", Core.Engine.OsdKind.Info, TimeSpan.FromSeconds(Math.Clamp(2 + text.Length * 0.06, 4, 15)));
    }

    /// <summary>
    /// The editor of voice profiles (stage 9.4): <paramref name="text"/> said with <paramref name="profile"/> as it is now (not
    /// kept yet), in <paramref name="mood"/>, by the voice alone (no bubble, no OSD: it sounds again with every change).
    /// </summary>
    public bool TestVoiceProfile(string text, VoiceProfile profile, string? mood = null)
    {
        if (_assistant is not { IsRunning: true } supervisor || text.Trim().Length == 0) return false;
        text = text.Trim();
        Voice.OwnVoices.Said("test", text, DateTime.Now);
        return supervisor.Send(new VoiceMessage("test", text, null, mood, profile.ToJson()));
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

    /// <summary>Removes Kokoro, and espeak-ng if Piper does not need it (the process lets them go first).</summary>
    public async Task DeleteKokoroAsync()
    {
        KokoroDownload.Cancel();
        AskAssistantVoice("unload");
        await Task.Delay(800);
        await Task.Run(KokoroModels.Delete);
        AskAssistantVoice("state");
        AssistantVoiceChanged?.Invoke();
    }

    /// <summary>
    /// A Piper voice chosen («key» or «key#speaker»): if Piper is already in use (some voice on disk), a voice that is
    /// missing is downloaded at once; the first one waits for «Descargar» (they are 20–115 MB).
    /// </summary>
    public void ChoosePiperVoice(string spec)
    {
        Config.Settings.AssistantVoicePiper = spec;
        if (PiperModels.AnyInstalled && PiperMissing && !PiperDownload.IsDownloading) PiperDownload.Start();
    }

    /// <summary>The Piper voices in use: the general one and those of the installed assistants.</summary>
    public IReadOnlyCollection<string> PiperVoicesInUse() =>
        [.. new[] { PiperVoice }.Concat(Config.Settings.AssistantPersonal.Keys.Where(HasOwner).Select(PiperVoiceOf))
            .Select(PiperModels.KeyOf).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Removes the Piper voices (with <paramref name="onlyUnused"/>, those no one uses), and espeak-ng if nothing needs it
    /// any more (the process lets them go first).
    /// </summary>
    public async Task DeletePiperAsync(bool onlyUnused)
    {
        PiperDownload.Cancel();
        AskAssistantVoice("unload");
        await Task.Delay(800);
        var keep = onlyUnused ? PiperVoicesInUse() : null;
        await Task.Run(() => PiperModels.Delete(keep));
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

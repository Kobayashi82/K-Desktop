using System.IO;
using System.Text.Json.Nodes;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Assistant.Voice;
using KDesktop.Core.Localization;

namespace KDesktop;

/// <summary>
/// What each assistant has of its own (5.0.105, Options → Assistants → it → «Personalidad y voz»): its personality,
/// its language and its voice; what is left empty is the general one. Sent to the process in the <c>personal</c> section
/// of <c>config</c>, with the language and the Kokoro and Piper voices resolved here.
/// </summary>
public partial class App
{
    /// <summary>The <c>personal</c> section last sent, to send only changes.</summary>
    private string? _assistantPersonalSent;

    /// <summary>The language each assistant was last told to speak in, to load again those whose language changed.</summary>
    private readonly Dictionary<string, string> _assistantLanguagesSent = new(StringComparer.Ordinal);

    /// <summary>What <paramref name="id"/> has of its own (empty: nothing); K-Desktop's is under its id (5.0.112).</summary>
    public AssistantPersonal AssistantPersonalOf(string id) => Config.Settings.AssistantPersonal.GetValueOrDefault(id) ?? new AssistantPersonal();

    /// <summary>Whether what is kept for <paramref name="id"/> is still of someone: an installed assistant, or K-Desktop.</summary>
    private static bool HasOwner(string id) => IsAssistant(id);

    /// <summary>Keeps what <paramref name="id"/> has of its own (nothing: it is forgotten) and tells the process.</summary>
    public void SetAssistantPersonal(string id, AssistantPersonal personal)
    {
        var all = new Dictionary<string, AssistantPersonal>(Config.Settings.AssistantPersonal, StringComparer.Ordinal);
        if (personal.IsEmpty) all.Remove(id);
        else all[id] = personal;
        Config.Settings.AssistantPersonal = all;
        // A voice of its own (or of its language) that is not on disk comes now, if Kokoro is already there: half a megabyte.
        if (KokoroModels.ModelInstalled && !KokoroDownload.IsDownloading && KokoroMissing) KokoroDownload.Start();
        // Its own Piper voice too, if Piper is already in use.
        if (PiperModels.AnyInstalled && !PiperDownload.IsDownloading && PiperMissing) PiperDownload.Start();
        AssistantVoiceChanged?.Invoke();
    }

    /// <summary>The language <paramref name="id"/> speaks in (null: K-Desktop): its own, or the one all of them speak in.</summary>
    public string LanguageOfAssistant(string? id) =>
        AssistantPersonalOf(id ?? AssistantTargeting.KDesktop).Language is { Length: > 0 } own && Loc.Available().Any(l => l.Code == own) ? own : AssistantLanguage;

    /// <summary>The Kokoro voice <paramref name="id"/> speaks with: its own, or the general one if it is of its language (else the language's first).</summary>
    public string KokoroVoiceOf(string? id)
    {
        var language = LanguageOfAssistant(id);
        var own = AssistantPersonalOf(id ?? AssistantTargeting.KDesktop).KokoroVoice;
        return KokoroModels.VoiceOrDefault(language, own.Length > 0 ? own : Config.Settings.AssistantVoiceKokoro);
    }

    /// <summary>
    /// What Kokoro needs on disk: the general language and voice, and those of the installed assistants that speak in
    /// another language or with another voice.
    /// </summary>
    internal IReadOnlyList<Core.Voice.ModelFile> KokoroFiles()
    {
        var pairs = new List<(string Language, string Voice)> { (KokoroLanguage, KokoroVoice) };
        foreach (var id in Config.Settings.AssistantPersonal.Keys)
            if (HasOwner(id) && KokoroModels.Supports(LanguageOfAssistant(id))) pairs.Add((LanguageOfAssistant(id), KokoroVoiceOf(id)));
        return [.. pairs.Distinct().SelectMany(p => KokoroModels.Files(p.Language, p.Voice)).DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Something Kokoro needs (for all of them or for one) is not on disk yet.</summary>
    public bool KokoroMissing => KokoroFiles().Any(f => !File.Exists(f.Path) || new FileInfo(f.Path).Length != f.Size);

    /// <summary>The Piper voice <paramref name="id"/> speaks with: its own, or the general one if it is of its language (else the language's default).</summary>
    public string PiperVoiceOf(string? id)
    {
        var language = LanguageOfAssistant(id);
        var own = AssistantPersonalOf(id ?? AssistantTargeting.KDesktop).PiperVoice;
        return PiperModels.VoiceOrDefault(language, own.Length > 0 ? own : Config.Settings.AssistantVoicePiper);
    }

    /// <summary>
    /// What Piper needs on disk: the general voice, and those of the installed assistants that chose one of their own or
    /// speak with Piper (not every language an assistant speaks in: a voice is 20–115 MB).
    /// </summary>
    internal IReadOnlyList<Core.Voice.ModelFile> PiperFiles()
    {
        var voices = new List<string> { PiperVoice };
        foreach (var (id, own) in Config.Settings.AssistantPersonal)
            if (HasOwner(id) && (own.PiperVoice.Length > 0 || own.Engine == "piper")) voices.Add(PiperVoiceOf(id));
        return [.. voices.Select(PiperModels.KeyOf).Distinct(StringComparer.Ordinal).SelectMany(PiperModels.Files).DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Something Piper needs (for all of them or for one) is not on disk yet.</summary>
    public bool PiperMissing => PiperFiles().Any(f => !File.Exists(f.Path) || new FileInfo(f.Path).Length != f.Size);

    /// <summary>The personality its package brings, in the language it speaks in (to show it, and «Restablecer»).</summary>
    public static string PackagePrompt(InstalledAssistant assistant, string language) =>
        AssistantPersona.Read(assistant.Folder, assistant.Manifest, language).Prompt;

    private JsonObject AssistantPersonalConfig()
    {
        var resolved = new Dictionary<string, AssistantPersonal>(StringComparer.Ordinal);
        foreach (var (id, own) in Config.Settings.AssistantPersonal)
        {
            if (own.IsEmpty) continue;
            var language = own.Language.Length > 0 ? LanguageOfAssistant(id) : string.Empty;
            resolved[id] = own with
            {
                Language = language,
                // Of its language: the one it chose, or the general one when it speaks another language.
                KokoroVoice = own.KokoroVoice.Length > 0 || language.Length > 0 ? KokoroVoiceOf(id) : string.Empty,
                PiperVoice = own.PiperVoice.Length > 0 || language.Length > 0 ? PiperVoiceOf(id) : string.Empty,
                Speed = own.Speed is { } speed ? Math.Clamp(speed, 0.5, 2) : null,
                Volume = own.Volume is { } volume ? Math.Clamp(volume, 0, 1) : null,
            };
        }
        return AssistantPersonal.ToJson(resolved);
    }

    /// <summary>
    /// After a settings change: the personal section, if it changed (UI thread); an assistant on the desktop whose language
    /// changed is loaded again (its code reads the language when it starts).
    /// </summary>
    private void SendAssistantPersonalConfig()
    {
        var personal = AssistantPersonalConfig();
        var json = personal.ToJsonString();
        if (json == _assistantPersonalSent || _assistant is not { IsRunning: true } supervisor) return;
        _assistantPersonalSent = json;
        supervisor.Send(new ConfigMessage("personal", personal));
        foreach (var id in AssistantsOnDesktop.ToList())
        {
            var language = LanguageOfAssistant(id);
            if (_assistantLanguagesSent.TryGetValue(id, out var before) && before != language)
            {
                Log.Info($"Asistente: «{id}» habla ahora en {language}: se vuelve a cargar");
                LoadAssistant(supervisor, id, LoadMessage.Reloaded);
            }
        }
    }

    /// <summary>The welcome's personal section (what the process knows from the start).</summary>
    private JsonObject AssistantPersonalWelcome()
    {
        var personal = AssistantPersonalConfig();
        _assistantPersonalSent = personal.ToJsonString();
        _assistantLanguagesSent.Clear();
        return personal;
    }

    /// <summary>An assistant goes onto the desktop: the language it starts with (to load it again if it changes).</summary>
    private void LoadingInLanguage(string id) => _assistantLanguagesSent[id] = LanguageOfAssistant(id);
}

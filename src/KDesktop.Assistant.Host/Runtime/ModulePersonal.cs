using System.Text.Json.Nodes;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Localization;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// What each assistant has of its own (5.0.105, the <c>personal</c> section of <c>config</c>): the language it speaks
/// in, its personality and its voice. Without anything of its own, the general ones. Any thread.
/// </summary>
internal sealed class ModulePersonal
{
    private volatile IReadOnlyDictionary<string, AssistantPersonal> _all = new Dictionary<string, AssistantPersonal>();

    /// <summary>Raised after a change, with the ids whose personality changed (its persona is read again).</summary>
    public event Action<IReadOnlyCollection<string>>? Changed;

    public void Configure(JsonObject? section)
    {
        if (section is null) return;
        var before = _all;
        var now = AssistantPersonal.FromJson(section);
        _all = now;
        var changed = before.Keys.Union(now.Keys).Where(id => before.GetValueOrDefault(id) != now.GetValueOrDefault(id)).ToList();
        if (changed.Count == 0) return;
        foreach (var id in changed)
            Log.Info($"Asistente: lo propio de «{id}»: {Describe(now.GetValueOrDefault(id))}");
        Changed?.Invoke(changed);
    }

    /// <summary>What <paramref name="id"/> has of its own (null: nothing, or no assistant).</summary>
    public AssistantPersonal? Of(string? id) => id is not null && _all.TryGetValue(id, out var own) ? own : null;

    /// <summary>The language <paramref name="id"/> speaks in: its own, or the process's (the general one).</summary>
    public string LanguageOf(string? id) =>
        Of(id) is { Language.Length: > 0 } own && Loc.Available().Any(l => l.Code.Equals(own.Language, StringComparison.OrdinalIgnoreCase))
            ? own.Language
            : Loc.Instance.Language;

    private static string Describe(AssistantPersonal? own) => own is null || own.IsEmpty ? "nada (todo lo general)" : string.Join(", ", new[]
    {
        own.Language.Length > 0 ? "idioma " + own.Language : null,
        own.Prompt is not null ? $"personalidad propia ({own.Prompt.Length} letras)" : null,
        own.Engine.Length > 0 ? "motor " + own.Engine : null,
        own.KokoroVoice.Length > 0 ? "voz " + own.KokoroVoice : null,
        own.PiperVoice.Length > 0 ? "voz de Piper " + own.PiperVoice : null,
        own.WindowsVoice.Length > 0 ? "voz de Windows " + own.WindowsVoice : null,
        own.Profile.Length > 0 ? "perfil " + own.Profile : null,
        own.Speed is { } speed ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"velocidad {speed:0.00}") : null,
    }.OfType<string>());
}

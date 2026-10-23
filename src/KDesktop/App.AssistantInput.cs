using System.Text.Json.Nodes;
using System.Windows.Input;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Input;
using KDesktop.Core.Platform;

namespace KDesktop;

/// <summary>
/// What the assistant process gets through K-Desktop's hook (it cannot hook input itself over elevated windows): the
/// keys of a game, swallowed while it plays (<c>keys</c> / <c>key</c>), every turn of the wheel while an assistant asks
/// (<c>watchWheel</c> / <c>wheel</c>) and hard scrolls (<c>fastScroll</c>). And the module's own settings it needs
/// (<c>config</c> of <c>general</c>: the sound).
/// </summary>
public partial class App
{
    /// <summary>The keys each assistant captures (virtual-key codes), read on the hook thread: replaced whole.</summary>
    private volatile (string Assistant, IReadOnlySet<int> Keys)[] _assistantKeys = [];

    private readonly FastScrollMeter _fastScroll = new();
    private volatile bool _assistantWatchesWheel;
    private string? _assistantGeneralSent;

    /// <summary>The module's settings the process needs (<c>config</c> of <c>general</c>, also in the welcome).</summary>
    private JsonObject AssistantGeneral()
    {
        var s = Config.Settings;
        return new JsonObject
        {
            ["sound"] = s.AssistantSound,
            ["volume"] = Math.Clamp(s.AssistantVolume, 0, 100) / 100.0,
            // How long gestures take, also those that do not come with a step (a tool's, catching the eye: stage 13.6).
            ["manoeuvres"] = s.AssistantManoeuvres,
        };
    }

    /// <summary>After a settings change: the general section, if it changed (UI thread).</summary>
    private void SendAssistantGeneral()
    {
        var general = AssistantGeneral();
        var json = general.ToJsonString();
        if (json == _assistantGeneralSent) return;
        _assistantGeneralSent = json;
        _assistant?.Send(new ConfigMessage("general", general));
    }

    /// <summary>A new process: nothing captured or watched yet (it sends again what it needs).</summary>
    private void ResetAssistantInput()
    {
        _assistantKeys = [];
        _assistantWatchesWheel = false;
        _assistantGeneralSent = AssistantGeneral().ToJsonString();
        UpdateAssistantInput();
    }

    /// <summary>An assistant wants these keys, or none (UI thread).</summary>
    private void OnAssistantKeys(KeysMessage message)
    {
        var keys = new HashSet<int>();
        foreach (var name in message.Capture)
        {
            var vk = Enum.TryParse<Key>(name, ignoreCase: false, out var key) ? KeyInterop.VirtualKeyFromKey(key) : 0;
            // Modifiers stay out: they are what lets the user's hotkeys through while it plays.
            if (vk is > 0 and < 256 && !KeyNames.IsModifierVk(vk)) keys.Add(vk);
            else Log.Warn($"Asistente: «{message.Assistant}» pide una tecla que no se captura: «{name}»");
        }
        SetAssistantKeys(message.Assistant, keys);
    }

    /// <summary>Lets go of the keys of an assistant that leaves.</summary>
    private void ReleaseAssistantKeys(string assistant) => SetAssistantKeys(assistant, new HashSet<int>());

    private void SetAssistantKeys(string assistant, IReadOnlySet<int> keys)
    {
        var others = _assistantKeys.Where(k => k.Assistant != assistant);
        var before = _assistantKeys.Length;
        _assistantKeys = keys.Count > 0 ? [.. others, (assistant, keys)] : [.. others];
        if (keys.Count > 0 || _assistantKeys.Length != before)
            Log.Info(keys.Count > 0 ? $"Asistente: «{assistant}» captura {keys.Count} teclas" : $"Asistente: «{assistant}» suelta las teclas");
        UpdateAssistantInput();
    }

    /// <summary>
    /// Puts the hook in step: the keys of all the assistants (none while K-Desktop is paused: it stops getting in the
    /// way), and the wheel while the process runs.
    /// </summary>
    private void UpdateAssistantInput()
    {
        var owners = _assistantKeys;
        var keys = Config.Settings.Paused || _assistant is null ? null : owners.SelectMany(k => k.Keys).ToHashSet();
        Router.SetGameKeys(keys, keys is { Count: > 0 } ? OnAssistantKey : null);
        Router.WheelObserver = _assistant is null ? null : OnAssistantWheel;
    }

    /// <summary>A captured key went down or up (hook thread): to every assistant that captures it.</summary>
    private void OnAssistantKey(int vk, bool down)
    {
        if (_assistant is not { } supervisor) return;
        var name = KeyInterop.KeyFromVirtualKey(vk).ToString();
        foreach (var (assistant, keys) in _assistantKeys)
            if (keys.Contains(vk)) supervisor.Send(new KeyMessage(assistant, name, down));
    }

    /// <summary>A physical turn of the wheel (hook thread): a hard scroll always; every turn if watched.</summary>
    private void OnAssistantWheel(MouseInput e)
    {
        if (_assistant is not { IsRunning: true } supervisor) return;
        var horizontal = e.Message == MouseMessage.HWheel;
        if (_fastScroll.Add(Environment.TickCount64 / 1000.0, e.WheelDelta, horizontal) is { } strength)
            supervisor.Send(new FastScrollMessage(Math.Round(strength, 3), e.X, e.Y));
        if (!_assistantWatchesWheel) return;
        supervisor.Send(new WheelMessage(e.X, e.Y, e.WheelDelta, horizontal, WindowTools.TopLevelAt(e.X, e.Y)));
    }

    private void OnAssistantWatchWheel(WatchWheelMessage message) => _assistantWatchesWheel = message.Watch;
}

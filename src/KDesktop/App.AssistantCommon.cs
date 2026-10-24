using System.Text.Json.Nodes;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop;

/// <summary>
/// The settings every assistant has the same way (stage 13.13): size, volume of its sounds, liveliness and dragging, in
/// the «General» tab of its card. They travel in the <c>common</c> section of <c>config</c>; the process gives each
/// assistant those its manifest takes.
/// </summary>
public partial class App
{
    /// <summary>The <c>common</c> section last sent (only changes are sent again).</summary>
    private string? _assistantCommonSent;

    /// <summary>An assistant's common settings (as usual when it has none).</summary>
    public AssistantCommon AssistantCommonOf(string id) => Config.Settings.AssistantCommon.GetValueOrDefault(id) ?? AssistantCommon.Default;

    /// <summary>Keeps an assistant's common settings (as usual: forgotten); they reach it at once.</summary>
    public void SetAssistantCommon(string id, AssistantCommon common)
    {
        common = common.Clamped();
        if (common == AssistantCommonOf(id)) return;
        var all = new Dictionary<string, AssistantCommon>(Config.Settings.AssistantCommon, StringComparer.Ordinal);
        if (common.IsDefault) all.Remove(id);
        else all[id] = common;
        Config.Settings.AssistantCommon = all;
        // At once (not after the settings' pause): its size follows the slider.
        SendAssistantCommonConfig();
    }

    private JsonObject AssistantCommonConfig() => AssistantCommon.ToJson(Config.Settings.AssistantCommon);

    /// <summary>After a settings change: the common section, if it changed (UI thread).</summary>
    private void SendAssistantCommonConfig()
    {
        var common = AssistantCommonConfig();
        var json = common.ToJsonString();
        SendStageCommon();
        if (json == _assistantCommonSent || _assistant is not { IsRunning: true } supervisor) return;
        _assistantCommonSent = json;
        supervisor.Send(new ConfigMessage("common", common));
    }

    /// <summary>The welcome's common section (what the process knows from the start).</summary>
    private JsonObject AssistantCommonWelcome()
    {
        var common = AssistantCommonConfig();
        _assistantCommonSent = common.ToJsonString();
        return common;
    }
}

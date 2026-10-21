using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Engine;
using KDesktop.Core.Model;

namespace KDesktop;

/// <summary>
/// The adornment (stage 6.7, Options → Assistant → General, off by default): the assistant that answers what is not
/// addressed to anyone also shows the commands launched other ways (hotkeys, menus, K-Desktop's voice, the phone…). Each
/// action goes to it as <c>observed</c> just before it runs; nothing waits for it, so they work exactly as always.
/// </summary>
public partial class App
{
    /// <summary>How each way of launching a command is called in <c>observed</c>; null: not adorned.</summary>
    private static string? AdornSource(TriggerSource source) => source switch
    {
        TriggerSource.Hotkey => "hotkey",
        TriggerSource.QuickMenu => "menu",
        TriggerSource.Launcher => "launcher",
        TriggerSource.Voice => "voice",
        TriggerSource.Web => "web",
        TriggerSource.Remote => "udp",
        TriggerSource.Event => "event",
        TriggerSource.Manual => "manual",
        _ => null,
    };

    /// <summary>An action is about to run (engine thread).</summary>
    private void AdornAction(CommandAction action, Core.Engine.ExecutionContext context)
    {
        var settings = Config.Settings;
        if (!settings.AssistantAdorn || !settings.AssistantEnabled || context.Choreography is not null || context.IsRepeat
            || _assistant is not { } supervisor || AdornSource(context.Source) is not { } source
            || action is ConditionAction or CommandControlAction { Mode: CommandControlMode.Run })
            return;

        // A long command: only what happens to windows, as with the orders.
        var max = settings.AssistantAnimateMax;
        if (max > 0 && !ActionGestures.IsWindowAction(action) && context.Command.Actions.Count(a => a.Enabled && a is not ConditionAction) > max) return;
        if (PickAssistant() is not { } who || !supervisor.IsLoaded(who)) return;

        // Decoration never gets in the way of the action.
        try
        {
            supervisor.Send(new ObservedMessage(who, source, context.Command.Id,
                [new ObservedStep(action.Kind.ToString(), ActionGestures.OpOf(action), action.Summary, StepTargets.Of(action, context.Foreground))]));
        }
        catch (Exception ex)
        {
            Core.Log.Error("Asistente: no se ha podido mandar el adorno", ex);
        }
    }
}

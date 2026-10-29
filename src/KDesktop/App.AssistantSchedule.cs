using System.Collections.Concurrent;
using KDesktop.Core;
using KDesktop.Core.Assistant;
using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Model;

namespace KDesktop;

/// <summary>
/// The orders the assistants program (the tool <c>schedule</c>): events of K-Desktop that, at their time, give the order
/// to the assistant with what was approved when it was asked for (<see cref="ScheduledPermit"/>). Then nobody is asked:
/// the commands approved run, the rest that would ask does not.
/// </summary>
public partial class App
{
    /// <summary>The orders fired lately, by their key: what they may run without asking.</summary>
    private readonly ConcurrentDictionary<string, ScheduledPermit> _permits = new(StringComparer.Ordinal);

    /// <summary>
    /// An event's order programmed by an assistant, now due: to it (or, if it is not on the desktop, to K-Desktop's own),
    /// with its permit.
    /// </summary>
    private void AskScheduledFromEvent(EventAsk ask)
    {
        var now = DateTime.UtcNow;
        foreach (var (key, old) in _permits)
            if (!old.IsValid(now)) _permits.TryRemove(key, out _);
        var id = ask.Assistant;
        if (_assistant is not null && !AssistantTargeting.IsSystem(id) && (!IsAssistant(id) || !IsOnDesktop(id)) && IsOnDesktop(AssistantTargeting.KDesktop))
        {
            Log.Info($"Asistente: «{id}» no está en el escritorio para su orden programada: la atiende K-Desktop");
            id = AssistantTargeting.KDesktop;
        }
        var permit = ScheduledPermit.Of(ask, now) with { Assistant = id };
        _permits[permit.Key] = permit;
        Log.Info($"Asistente: orden programada para «{id}»: «{ask.Text}» (aprobado: {string.Join(", ", permit.Tools.Concat(permit.Commands.Select(c => Config.FindCommand(c)?.Name ?? c)))})");
        if (!SendToAssistant(id, ask.Text, HeardMessage.SourceScheduled, permit: permit)) _permits.TryRemove(permit.Key, out _);
    }

    /// <summary>The permit of an order that is still running (null: none, or it expired).</summary>
    private ScheduledPermit? PermitOf(string? key) =>
        key is not null && _permits.TryGetValue(key, out var permit) && permit.IsValid(DateTime.UtcNow) ? permit : null;

    /// <summary>
    /// A request that carries an order's key: with a valid permit, whether what would ask was approved; without one, null
    /// (it asks as usual). An unknown or expired key is refused outright.
    /// </summary>
    private Func<Command, bool>? CommandPermit(string? key, bool act)
    {
        if (key is null) return null;
        if (PermitOf(key) is not { } permit)
        {
            Log.Warn("Asistente: una orden programada pide algo con un permiso que ya no vale: no se ejecuta lo que preguntaría");
            return _ => false;
        }
        return act ? _ => permit.AllowsActs : permit.Allows;
    }

    /// <summary>Makes or removes an event of an assistant (create_event, B8), on the thread of the configuration.</summary>
    private void OnMakeEvent(AssistantSupervisor supervisor, MakeEventMessage request) =>
        _ = Dispatcher.InvokeAsync(() =>
        {
            MadeEventMessage done;
            try { done = AssistantEvents.Handle(Config, request, DateTime.Now); }
            catch (Exception ex)
            {
                Log.Error("Asistente: no se ha podido crear el evento", ex);
                done = new MadeEventMessage(request.Id, false, Reason: "error");
            }
            supervisor.Send(done);
        });

    /// <summary>Adds, lists or removes a programmed order, on the thread of the configuration (Eventos shows them).</summary>
    private void OnSchedule(AssistantSupervisor supervisor, ScheduleMessage request) =>
        _ = Dispatcher.InvokeAsync(() =>
        {
            ScheduleDoneMessage done;
            try { done = AssistantSchedule.Handle(Config, request, DateTime.Now); }
            catch (Exception ex)
            {
                Log.Error("Asistente: no se ha podido programar la orden", ex);
                done = new ScheduleDoneMessage(request.Id, false, Reason: "error");
            }
            supervisor.Send(done);
        });
}

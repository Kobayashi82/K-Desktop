using KDesktop.Core.Assistant.Pipe;
using KDesktop.Core.Remote.Web;

namespace KDesktop.Core.Assistant;

/// <summary>K-Desktop's side of <see cref="SendPhoneMessage"/> (B11): into the phone inbox, telling to whom it went.</summary>
public static class AssistantPhone
{
    /// <param name="from">The assistant's name, shown on the phone.</param>
    public static PhoneSentMessage Handle(SendPhoneMessage request, string from, PhoneInbox? inbox = null)
    {
        inbox ??= PhoneInbox.Current;
        try
        {
            var sent = inbox.Send(request.Title, request.Text, request.Url, request.File, from, request.Device);
            return sent.Reason is { } reason
                ? new PhoneSentMessage(request.Id, false, [], reason, reason == "noDevice" ? [.. inbox.Targets().Select(d => d.Name)] : null)
                : new PhoneSentMessage(request.Id, true, sent.Names);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Asistente: no se pudo mandar al móvil: " + ex.Message);
            return new PhoneSentMessage(request.Id, false, [], "error");
        }
    }
}

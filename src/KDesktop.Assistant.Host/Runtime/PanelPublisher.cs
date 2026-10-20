using System.Text.Json.Nodes;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// What assistants publish for their panel (<see cref="Sdk.IAssistantHost.PublishPanel"/>) goes to K-Desktop at most
/// every <see cref="Interval"/>: publishing in between is merged into the next message, so an assistant can publish on
/// every frame without flooding the pipe. The first one after a quiet while goes at once.
/// </summary>
internal sealed class PanelPublisher(Action<PanelMessage> send)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private readonly Lock _lock = new();
    private JsonObject? _pending;
    private string? _pendingFor;
    private long _lastSent = -Interval.Ticks;
    private bool _scheduled;

    /// <summary>Any thread. <paramref name="data"/> is copied: the assistant can keep changing its object.</summary>
    public void Publish(string assistant, JsonObject data)
    {
        var copy = (JsonObject)data.DeepClone();
        lock (_lock)
        {
            // Another assistant's leftovers go first, on their own.
            if (_pending is not null && _pendingFor != assistant) SendPending();
            if (_pending is null)
            {
                _pending = copy;
                _pendingFor = assistant;
            }
            else AssistantUi.Merge(_pending, copy);

            if (_scheduled) return;
            var wait = Interval - TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond - _lastSent);
            if (wait <= TimeSpan.Zero)
            {
                SendPending();
                return;
            }
            _scheduled = true;
            _ = Task.Delay(wait).ContinueWith(_ =>
            {
                lock (_lock)
                {
                    _scheduled = false;
                    SendPending();
                }
            }, TaskScheduler.Default);
        }
    }

    private void SendPending()
    {
        if (_pending is not { } data) return;
        _pending = null;
        _lastSent = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
        send(new PanelMessage(data, _pendingFor));
    }
}

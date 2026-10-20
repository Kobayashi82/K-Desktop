using System.Text.Json.Nodes;
using KDesktop.Core.Assistant.Packages;
using KDesktop.Core.Assistant.Pipe;

namespace KDesktop.Assistant.Host.Runtime;

/// <summary>
/// What assistants publish for their panel (<see cref="Sdk.IAssistantHost.PublishPanel"/>) goes to K-Desktop at most
/// every <see cref="Interval"/>: publishing in between is merged into the next message of that assistant, so each one
/// can publish on every frame without flooding the pipe. The first one after a quiet while goes at once.
/// </summary>
internal sealed class PanelPublisher(Action<PanelMessage> send)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private readonly Lock _lock = new();
    /// <summary>What each assistant published since the last message, in the order they first published.</summary>
    private readonly Dictionary<string, JsonObject> _pending = new(StringComparer.Ordinal);
    private long _lastSent = -Interval.Ticks;
    private bool _scheduled;

    /// <summary>Any thread. <paramref name="data"/> is copied: the assistant can keep changing its object.</summary>
    public void Publish(string assistant, JsonObject data)
    {
        var copy = (JsonObject)data.DeepClone();
        lock (_lock)
        {
            if (_pending.TryGetValue(assistant, out var pending)) AssistantUi.Merge(pending, copy);
            else _pending[assistant] = copy;

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

    /// <summary>One message per assistant with something pending.</summary>
    private void SendPending()
    {
        if (_pending.Count == 0) return;
        _lastSent = Environment.TickCount64 * TimeSpan.TicksPerMillisecond;
        foreach (var (assistant, data) in _pending) send(new PanelMessage(data, assistant));
        _pending.Clear();
    }
}

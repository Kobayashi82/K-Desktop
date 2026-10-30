using KDesktop.Core.Calendar;

namespace KDesktop.Core.Engine.Events;

public sealed partial class EventService
{
    /// <summary>The calendar the events of citas look at; set by K-Desktop at start.</summary>
    public CalendarService? Calendar { get; set; }
}

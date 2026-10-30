using KDesktop.Core.Calendar;

namespace KDesktop.Core.Engine;

public sealed partial class CommandEngine
{
    /// <summary>The calendar («Crear cita», «Abrir la reunión»); set by K-Desktop at start.</summary>
    public CalendarService? Calendar { get; set; }
}

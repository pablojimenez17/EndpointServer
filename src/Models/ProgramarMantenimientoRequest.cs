namespace Tradipres.EndpointServer.Models;

// Solicitud de programación desde la web (/equipos/ → ficha → Mantenimiento).
// Dos tipos:
//  · "Unico": se ejecuta una vez a partir de FechaInicio. NO se pide FechaFin:
//    la columna NOT NULL se guarda con FechaInicio como provisional y el fin
//    real lo escribe quien cierre el mantenimiento (agente o botón "Cerrar").
//  · "Diario": recurrente. La HORA de FechaInicio es la hora diaria de
//    ejecución; la FECHA de FechaInicio marca desde cuándo empieza a estar
//    activo. Dias indica los días de la semana (0=domingo … 6=sábado, el
//    enum DayOfWeek de .NET) en los que se ejecuta. FechaFin es opcional:
//    si se indica y su día ya pasó, el servidor cierra el mantenimiento.
public class ProgramarMantenimientoRequest
{
    public Guid Guid { get; set; }
    public string Tipo { get; set; } = "Unico";   // "Unico" | "Diario"
    public DateTime FechaInicio { get; set; }

    // Diario: días de la semana en los que se ejecuta (0=domingo … 6=sábado).
    public List<int>? Dias { get; set; }

    // Diario: fecha de finalización de la recurrencia (null = sin fin).
    public DateTime? FechaFin { get; set; }

    public bool CopiaOutlook { get; set; }
    public bool Mantenimiento { get; set; }
    public bool ReparacionWindows { get; set; }
    public bool ApagarEquipo { get; set; }

    // Orden de ejecución de las acciones marcadas (prioridad).
    // Ej: ["CopiaOutlook", "ReparacionWindows", "ApagarEquipo"].
    // El agente las ejecuta en este orden; ApagarEquipo siempre al final.
    public List<string>? OrdenAcciones { get; set; }

    public string? Mensaje { get; set; }
}

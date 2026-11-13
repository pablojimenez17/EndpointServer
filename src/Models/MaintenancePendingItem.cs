namespace Tradipres.EndpointServer.Models;

// Elemento que el agente recibe en GET /api/maintenance/pending.
// FechaLimite solo se informa en los Diarios (cierre de la ventana diaria,
// informativa). Los Unicos no tienen fin: lo escribe quien cierre el
// mantenimiento (el agente al terminar o el botón "Cerrar" de la web).
public class MaintenancePendingItem
{
    public int Id { get; set; }
    public string Tipo { get; set; } = "Unico";   // "Unico" | "Diario"
    public bool CopiaOutlook { get; set; }
    public bool Mantenimiento { get; set; }
    public bool ReparacionWindows { get; set; }
    public bool ApagarEquipo { get; set; }

    // Orden de ejecución de las acciones (prioridad desde la web).
    // Ej: ["CopiaOutlook", "ReparacionWindows", "ApagarEquipo"].
    // Null = el agente usa su propio orden por defecto.
    public List<string>? OrdenAcciones { get; set; }

    public string? Mensaje { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime? FechaLimite { get; set; }
}

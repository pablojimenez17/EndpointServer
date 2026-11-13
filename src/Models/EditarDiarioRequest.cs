namespace Tradipres.EndpointServer.Models;

// Solicitud de edición de un mantenimiento Diario desde la web.
// Permite cambiar días, hora, fecha de fin, acciones, orden y mensaje
// sin cancelar y recrear el mantenimiento.
public class EditarDiarioRequest
{
    public int MantenimientoId { get; set; }

    // Hora diaria de ejecución (la fecha se ignora; solo importa la hora).
    public DateTime FechaInicio { get; set; }

    // Días de la semana (0=domingo … 6=sábado).
    public List<int>? Dias { get; set; }

    // Fecha de fin de la recurrencia (null = sin fin).
    public DateTime? FechaFin { get; set; }

    public bool CopiaOutlook { get; set; }
    public bool Mantenimiento { get; set; }
    public bool ReparacionWindows { get; set; }
    public bool ApagarEquipo { get; set; }

    // Orden de ejecución de las acciones (prioridad).
    public List<string>? OrdenAcciones { get; set; }

    public string? Mensaje { get; set; }
}

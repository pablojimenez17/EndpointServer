namespace Tradipres.EndpointServer.Models;

// Reporte que envía el agente tras (o durante) la ejecución de un
// mantenimiento. Si MantenimientoId viene informado, el servidor ACTUALIZA
// esa fila (ciclo de estados de un Único: Ejecutando → Completado/Error);
// si viene null, INSERTA una fila nueva (ejecuciones de un Diario).
public class MaintenanceReportRequest
{
    public Guid Guid { get; set; }
    public int? MantenimientoId { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public string Estado { get; set; } = "";
    public int DuracionSegundos { get; set; }
    public bool CopiaOutlook { get; set; }
    public bool Mantenimiento { get; set; }
    public bool ReparacionWindows { get; set; }
    public bool ApagarEquipo { get; set; }
    public string? Mensaje { get; set; }
    public string? DetalleTecnico { get; set; }
}

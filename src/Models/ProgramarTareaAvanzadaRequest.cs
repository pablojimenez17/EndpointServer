namespace Tradipres.EndpointServer.Models;

// POST /api/advanced-tasks — programa una tarea avanzada desde la ficha del
// equipo. El comando se ejecutará con PowerShell en la fecha indicada.
public sealed class ProgramarTareaAvanzadaRequest
{
    public Guid Guid { get; set; }
    public DateTime FechaEjecucion { get; set; }
    public string? Descripcion { get; set; }
    public string Comando { get; set; } = "";
}

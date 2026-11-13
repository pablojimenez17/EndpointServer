namespace Tradipres.EndpointServer.Models;

// POST /api/advanced-tasks/report — el agente informa el resultado de una
// tarea avanzada (código de salida, salida de PowerShell y error, si hubo).
public sealed class TareaAvanzadaReportRequest
{
    public Guid Guid { get; set; }
    public int Id { get; set; }
    public string Estado { get; set; } = "";
    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public int DuracionSegundos { get; set; }
    public int? CodigoSalida { get; set; }
    public string? Salida { get; set; }
    public string? Error { get; set; }
}

// Elemento que devuelve GET /api/advanced-tasks/pending al agente. Solo
// incluye lo necesario para ejecutar el comando, sin datos internos del
// equipo ni del estado.
public sealed class TareaAvanzadaPendiente
{
    public int Id { get; set; }
    public DateTime FechaEjecucion { get; set; }
    public string? Descripcion { get; set; }
    public string Comando { get; set; } = "";
}

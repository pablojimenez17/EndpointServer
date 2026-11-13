namespace Tradipres.EndpointServer.Models;

// Tarea avanzada: un comando de PowerShell que se ejecuta una sola vez en el
// equipo a la fecha/hora indicada desde la ficha del equipo.
//
// La definición y el resultado se guardan en tareas_avanzadas.json (la cuenta
// endpoint_app no tiene permisos DDL para crear una tabla nueva), igual que
// diarios.json guarda la recurrencia de los mantenimientos.
public sealed class TareaAvanzada
{
    public int Id { get; set; }
    public int EquipoId { get; set; }
    public string EquipoGuid { get; set; } = "";
    public string NombreEquipo { get; set; } = "";

    public string? Descripcion { get; set; }
    public string Comando { get; set; } = "";

    // Fecha y hora local en la que el agente debe ejecutar el comando.
    public DateTime FechaEjecucion { get; set; }

    // Programada → Ejecutando → Completada / Error / Cancelada.
    public string Estado { get; set; } = "Programada";

    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaInicioEjecucion { get; set; }
    public DateTime? FechaFinEjecucion { get; set; }
    public int? DuracionSegundos { get; set; }
    public int? CodigoSalida { get; set; }
    public string? Salida { get; set; }
    public string? Error { get; set; }
}

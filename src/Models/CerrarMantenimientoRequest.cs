namespace Tradipres.EndpointServer.Models;

// POST /api/maintenance/{id}/cerrar — cierre manual desde la web.
// El detalle técnico es opcional (lo que la persona quiera anotar sobre
// lo hecho, p. ej. "cambiado el disco duro").
public class CerrarMantenimientoRequest
{
    public string? DetalleTecnico { get; set; }
}

namespace Tradipres.EndpointServer.Models;

// POST /api/maintenance/interrumpidos — el agente informa al arrancar que
// su proceso se reinició; el servidor pasa sus "Ejecutando" a Error.
public class InterrumpidosRequest
{
    public Guid Guid { get; set; }
}

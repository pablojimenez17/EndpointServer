using Microsoft.AspNetCore.SignalR;

namespace Tradipres.EndpointServer.Services;

// Eventos que la web recibe en tiempo real por SignalR (ver RealtimeHub y
// wwwroot/js/realtime.js).
//
// Singleton inyectado en los endpoints (Program.cs) y en los repositories.
// TODOS los métodos tragan sus propias excepciones: un fallo de SignalR
// nunca debe romper el flujo del agente (p. ej. el reclamo de un
// mantenimiento o el envío de un reporte).
public sealed class RealtimeNotifier
{
    private readonly IHubContext<RealtimeHub> _hub;
    private readonly ILogger<RealtimeNotifier> _logger;

    public RealtimeNotifier(IHubContext<RealtimeHub> hub, ILogger<RealtimeNotifier> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    // Cambio de estado de un mantenimiento: Programado → Ejecutando →
    // Completado/Error/Cancelado, entrega de un Diario, etc.
    public Task MantenimientoCambiadoAsync(
        Guid equipoGuid,
        int? mantenimientoId,
        string estado,
        string? detalle = null) =>
        EnviarAsync("mantenimientoCambiado", new
        {
            tipo = "mantenimiento",
            equipoGuid,
            mantenimientoId,
            estado,
            detalle,
            fecha = DateTime.Now
        });

    // Heartbeat de un equipo: la ficha y el sidebar pintan el badge Online
    // al instante. El paso a Offline es por silencio (sin evento) y lo sigue
    // cubriendo el timer de 15 s.
    public Task EquipoHeartbeatAsync(Guid equipoGuid, string? nombreEquipo = null) =>
        EnviarAsync("equipoHeartbeat", new
        {
            tipo = "heartbeat",
            equipoGuid,
            nombreEquipo,
            estado = "Online",
            fecha = DateTime.Now
        });

    // Cambio de estado de una tarea avanzada (PowerShell programado).
    public Task TareaAvanzadaCambiadaAsync(
        Guid equipoGuid,
        int tareaId,
        string estado,
        string? detalle = null) =>
        EnviarAsync("tareaAvanzadaCambiada", new
        {
            tipo = "tareaAvanzada",
            equipoGuid,
            tareaId,
            estado,
            detalle,
            fecha = DateTime.Now
        });

    // Equipo recién registrado (aparece en el dashboard sin recargar).
    public Task EquipoRegistradoAsync(Guid equipoGuid, string? nombreEquipo = null) =>
        EnviarAsync("equipoRegistrado", new
        {
            tipo = "registro",
            equipoGuid,
            nombreEquipo,
            fecha = DateTime.Now
        });

    private async Task EnviarAsync(string metodo, object payload)
    {
        try
        {
            await _hub.Clients.Group("global").SendAsync(metodo, payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se ha podido emitir el evento '{Metodo}' por SignalR.", metodo);
        }
    }
}

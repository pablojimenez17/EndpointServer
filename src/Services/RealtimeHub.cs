using Microsoft.AspNetCore.SignalR;

namespace Tradipres.EndpointServer.Services;

// Hub mínimo de SignalR para los cambios en tiempo real.
//
// Todos los clientes entran en el grupo "global": la web tiene pocas
// pestañas abiertas y el filtrado por equipo se hace en el cliente con el
// campo equipoGuid del payload. Si hiciera falta ahorrar tráfico en el
// futuro, se añade aquí un método UnirseEquipo(guid) y grupos por equipo.
public sealed class RealtimeHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "global");
        await base.OnConnectedAsync();
    }
}

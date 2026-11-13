using System.Text.Json;
using System.Text.Json.Serialization;
using Tradipres.EndpointServer.Models;

namespace Tradipres.EndpointServer.Services;

/// <summary>
/// Responsable de cada equipo según el fichero equipos.json (raíz del
/// servidor). La búsqueda del nombre del equipo NO distingue mayúsculas
/// (el agente puede reportar "PTRADI14" y el JSON tener "PTradi14").
/// El fichero se relée solo si ha cambiado su fecha de modificación,
/// así que se puede editar sin reiniciar el servidor.
/// </summary>
public sealed class EquiposResponsablesService
{
    private readonly IHostEnvironment _environment;
    private readonly object _sync = new();
    private Dictionary<string, string> _mapa = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _fechaCarga = DateTime.MinValue;

    public EquiposResponsablesService(IHostEnvironment environment)
    {
        _environment = environment;
    }

    /// <summary>Devuelve el usuario responsable del equipo, o null si no consta.</summary>
    public string? ObtenerResponsable(string? nombreEquipo)
    {
        if (string.IsNullOrWhiteSpace(nombreEquipo))
        {
            return null;
        }

        var mapa = CargarMapa();
        return mapa.TryGetValue(nombreEquipo.Trim(), out var usuario) ? usuario : null;
    }

    private Dictionary<string, string> CargarMapa()
    {
        // Primero ContentRoot (al ejecutar con cwd = src) y después la
        // carpeta del binario (publish suelto), por si acaso.
        var ruta = Path.Combine(_environment.ContentRootPath, "equipos.json");
        if (!File.Exists(ruta))
        {
            ruta = Path.Combine(AppContext.BaseDirectory, "equipos.json");
        }

        if (!File.Exists(ruta))
        {
            lock (_sync)
            {
                _mapa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _fechaCarga = DateTime.MinValue;
            }

            return _mapa;
        }

        var fecha = File.GetLastWriteTime(ruta);

        lock (_sync)
        {
            if (fecha != _fechaCarga)
            {
                _mapa = Leer(ruta);
                _fechaCarga = fecha;
            }

            return _mapa;
        }
    }

    private static Dictionary<string, string> Leer(string ruta)
    {
        var mapa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var items = JsonSerializer.Deserialize<List<EquiposJsonItem>>(
                File.ReadAllText(ruta));

            foreach (var item in items ?? new List<EquiposJsonItem>())
            {
                var equipo = item.Equipo?.Trim();
                var usuario = item.Usuario?.Trim();

                // Ignora entradas sin equipo y sin usuario; si hay duplicados,
                // gana la primera fila que tenga usuario (p. ej. "Integra" > null).
                if (string.IsNullOrEmpty(equipo) || string.IsNullOrEmpty(usuario))
                {
                    continue;
                }

                if (!mapa.ContainsKey(equipo))
                {
                    mapa[equipo] = usuario;
                }
            }
        }
        catch
        {
            // JSON roto: se queda con el mapa vacío y la web muestra "—".
        }

        return mapa;
    }
}

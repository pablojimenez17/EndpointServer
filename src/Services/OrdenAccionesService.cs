using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;

namespace Tradipres.EndpointServer.Services;

/*
 * OrdenAccionesService — guarda el orden de ejecución de las acciones de
 * cada mantenimiento en un fichero JSON (orden_acciones.json), ya que la
 * BD no tiene columna para ello.
 *
 * Formato del fichero: lista de entradas { mantenimientoId, orden }.
 * El orden es una lista de nombres ("CopiaOutlook", "ReparacionWindows"…).
 * Se consulta al construir los pendientes para el agente.
 */
public class OrdenAccionesService
{
    private sealed class OrdenEntry
    {
        public int MantenimientoId { get; set; }
        public List<string> Orden { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Opciones = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _sync = new();
    private readonly string _ruta;

    public OrdenAccionesService(IHostEnvironment environment)
    {
        var contenido = Path.Combine(environment.ContentRootPath, "orden_acciones.json");
        var baseDir = Path.Combine(AppContext.BaseDirectory, "orden_acciones.json");
        _ruta = File.Exists(contenido) || Directory.Exists(environment.ContentRootPath)
            ? contenido
            : baseDir;

        AsegurarFichero();
    }

    // Guarda (o sobrescribe) el orden para un mantenimiento dado.
    public void Guardar(int mantenimientoId, List<string> orden)
    {
        var limpio = Normalizar(orden);
        if (limpio.Count == 0) return;

        lock (_sync)
        {
            var lista = LeerFichero();
            lista.RemoveAll(e => e.MantenimientoId == mantenimientoId);
            lista.Add(new OrdenEntry { MantenimientoId = mantenimientoId, Orden = limpio });
            GuardarFichero(lista);
        }
    }

    // Devuelve el orden para un mantenimiento, o null si no hay configurado.
    // Se desduplica por si el fichero quedó con acciones repetidas.
    public List<string>? Obtener(int mantenimientoId)
    {
        lock (_sync)
        {
            var orden = LeerFichero()
                .FirstOrDefault(e => e.MantenimientoId == mantenimientoId)
                ?.Orden;
            return orden is null ? null : Normalizar(orden);
        }
    }

    // Quita nombres vacíos y duplicados (sin distinguir mayúsculas), conservando
    // el orden de la primera aparición.
    private static List<string> Normalizar(IEnumerable<string> orden)
    {
        var resultado = new List<string>();
        foreach (var nombre in orden ?? Enumerable.Empty<string>())
        {
            var limpio = (nombre ?? string.Empty).Trim();
            if (limpio.Length == 0) continue;
            if (resultado.Any(x => string.Equals(x, limpio, StringComparison.OrdinalIgnoreCase))) continue;
            resultado.Add(limpio);
        }
        return resultado;
    }

    // Elimina la entrada de un mantenimiento (al cancelar o cerrar).
    public void Eliminar(int mantenimientoId)
    {
        lock (_sync)
        {
            var lista = LeerFichero();
            if (lista.RemoveAll(e => e.MantenimientoId == mantenimientoId) > 0)
            {
                GuardarFichero(lista);
            }
        }
    }

    // -------------------------------------------------------------------
    // Internos: lectura/escritura del fichero.
    // -------------------------------------------------------------------

    private void AsegurarFichero()
    {
        if (!File.Exists(_ruta))
        {
            try { File.WriteAllText(_ruta, "[]"); } catch { /* se recreará al guardar */ }
        }
    }

    private List<OrdenEntry> LeerFichero()
    {
        try
        {
            if (!File.Exists(_ruta)) return new List<OrdenEntry>();
            var json = File.ReadAllText(_ruta);
            return JsonSerializer.Deserialize<List<OrdenEntry>>(json, Opciones)
                   ?? new List<OrdenEntry>();
        }
        catch
        {
            return new List<OrdenEntry>();
        }
    }

    private void GuardarFichero(List<OrdenEntry> lista)
    {
        var json = JsonSerializer.Serialize(lista, Opciones);
        var tmp = _ruta + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _ruta, overwrite: true);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Tradipres.EndpointServer.Models;

namespace Tradipres.EndpointServer.Services;

// ---------------------------------------------------------------------------
// TareasAvanzadasService — almacén de las tareas avanzadas (comandos de
// PowerShell programados desde la ficha de un equipo).
//
// La cuenta endpoint_app no tiene permisos DDL, así que no se puede crear una
// tabla nueva: las tareas y sus resultados se guardan en tareas_avanzadas.json,
// al lado del servidor, con el mismo patrón que diarios.json y
// orden_acciones.json (lectura-modificación-escritura atómica bajo lock).
//
// Ciclo de estados:
//   Programada → (el agente la reclama) → Ejecutando →
//   Completada / Error. También puede pasar a Cancelada desde la web mientras
//   siga Programada. Si el agente se reinicia a mitad de la ejecución, al
//   arrancar la marca como Error ("Interrumpida por reinicio").
// ---------------------------------------------------------------------------
public sealed class TareasAvanzadasService
{
    private const int MaximoComando = 8000;
    private const int MaximoSalida = 20000;

    private static readonly JsonSerializerOptions OpcionesJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _sync = new();
    private readonly string _ruta;

    public TareasAvanzadasService(IHostEnvironment environment)
    {
        var contenido = Path.Combine(environment.ContentRootPath, "tareas_avanzadas.json");
        var baseDir = Path.Combine(AppContext.BaseDirectory, "tareas_avanzadas.json");
        _ruta = File.Exists(contenido) || Directory.Exists(environment.ContentRootPath)
            ? contenido
            : baseDir;

        AsegurarFichero();
    }

    // -------------------------------------------------------------------
    // Web: alta, listado y cancelación.
    // -------------------------------------------------------------------

    public TareaAvanzada Crear(
        ProgramarTareaAvanzadaRequest request,
        int equipoId,
        string nombreEquipo)
    {
        lock (_sync)
        {
            var lista = LeerFichero();
            int nuevoId = lista.Count == 0 ? 1 : lista.Max(t => t.Id) + 1;

            var tarea = new TareaAvanzada
            {
                Id = nuevoId,
                EquipoId = equipoId,
                EquipoGuid = request.Guid.ToString("D"),
                NombreEquipo = nombreEquipo,
                Descripcion = string.IsNullOrWhiteSpace(request.Descripcion)
                    ? null
                    : request.Descripcion.Trim(),
                Comando = request.Comando.Trim(),
                FechaEjecucion = request.FechaEjecucion,
                Estado = "Programada",
                FechaCreacion = DateTime.Now
            };

            lista.Add(tarea);
            GuardarFichero(lista);
            return tarea;
        }
    }

    // Listado para la pestaña de la ficha: primero las más recientes.
    public List<TareaAvanzada> ObtenerPorEquipo(Guid equipoGuid)
    {
        string guid = equipoGuid.ToString("D");

        lock (_sync)
        {
            return LeerFichero()
                .Where(t => string.Equals(t.EquipoGuid, guid, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.FechaCreacion)
                .ThenByDescending(t => t.Id)
                .ToList();
        }
    }

    // Cancela una tarea que todavía no ha empezado. Devuelve la tarea
    // cancelada (para poder emitir el evento de tiempo real) o null si no
    // existe o ya no está Programada. Si se indica el GUID, solo se permite
    // cancelar tareas de ese equipo.
    public TareaAvanzada? Cancelar(int id, Guid? equipoGuid = null)
    {
        lock (_sync)
        {
            var lista = LeerFichero();
            var tarea = lista.FirstOrDefault(t => t.Id == id);
            if (tarea is null) return null;

            if (equipoGuid is Guid guid &&
                !string.Equals(tarea.EquipoGuid, guid.ToString("D"), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!string.Equals(tarea.Estado, "Programada", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            tarea.Estado = "Cancelada";
            tarea.FechaFinEjecucion = DateTime.Now;
            GuardarFichero(lista);
            return tarea;
        }
    }

    // -------------------------------------------------------------------
    // Agente: pendientes, reporte e interrupciones.
    // -------------------------------------------------------------------

    // Devuelve las tareas vencidas del equipo y las marca como Ejecutando
    // antes de entregarlas, para que un segundo sondeo no las duplique.
    public List<TareaAvanzadaPendiente> ObtenerPendientes(Guid equipoGuid, DateTime ahora)
    {
        string guid = equipoGuid.ToString("D");
        var pendientes = new List<TareaAvanzadaPendiente>();

        lock (_sync)
        {
            var lista = LeerFichero();
            bool cambios = false;

            foreach (var tarea in lista)
            {
                if (!string.Equals(tarea.EquipoGuid, guid, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.Equals(tarea.Estado, "Programada", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (tarea.FechaEjecucion > ahora)
                {
                    continue;
                }

                tarea.Estado = "Ejecutando";
                tarea.FechaInicioEjecucion = ahora;
                cambios = true;

                pendientes.Add(new TareaAvanzadaPendiente
                {
                    Id = tarea.Id,
                    FechaEjecucion = tarea.FechaEjecucion,
                    Descripcion = tarea.Descripcion,
                    Comando = tarea.Comando
                });
            }

            if (cambios)
            {
                GuardarFichero(lista);
            }
        }

        return pendientes;
    }

    public bool Reportar(TareaAvanzadaReportRequest request)
    {
        lock (_sync)
        {
            var lista = LeerFichero();
            var tarea = lista.FirstOrDefault(t => t.Id == request.Id);
            if (tarea is null) return false;

            if (request.Guid != Guid.Empty &&
                !string.Equals(tarea.EquipoGuid, request.Guid.ToString("D"),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            tarea.Estado = string.IsNullOrWhiteSpace(request.Estado)
                ? "Error"
                : request.Estado.Trim();
            tarea.FechaInicioEjecucion = request.FechaInicio;
            tarea.FechaFinEjecucion = request.FechaFin;
            tarea.DuracionSegundos = Math.Max(0, request.DuracionSegundos);
            tarea.CodigoSalida = request.CodigoSalida;
            tarea.Salida = Truncar(request.Salida, MaximoSalida);
            tarea.Error = Truncar(request.Error, MaximoSalida);

            GuardarFichero(lista);
            return true;
        }
    }

    // Al arrancar el agente: sus tareas que quedaron en "Ejecutando" (el
    // proceso murió con el reinicio) pasan a Error para no quedarse colgadas.
    public int MarcarInterrumpidas(Guid equipoGuid)
    {
        string guid = equipoGuid.ToString("D");

        lock (_sync)
        {
            var lista = LeerFichero();
            int marcadas = 0;

            foreach (var tarea in lista)
            {
                if (!string.Equals(tarea.EquipoGuid, guid, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(tarea.Estado, "Ejecutando", StringComparison.OrdinalIgnoreCase)) continue;

                tarea.Estado = "Error";
                tarea.Error = "Interrumpida por reinicio del equipo.";
                tarea.FechaFinEjecucion = DateTime.Now;
                if (tarea.FechaInicioEjecucion.HasValue)
                {
                    tarea.DuracionSegundos = (int)Math.Max(
                        0, (DateTime.Now - tarea.FechaInicioEjecucion.Value).TotalSeconds);
                }
                marcadas++;
            }

            if (marcadas > 0)
            {
                GuardarFichero(lista);
            }

            return marcadas;
        }
    }

    // -------------------------------------------------------------------
    // Internos.
    // -------------------------------------------------------------------

    public static bool ComandoValido(string? comando) =>
        !string.IsNullOrWhiteSpace(comando) && comando.Trim().Length <= MaximoComando;

    private static string? Truncar(string? texto, int maximo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        string limpio = texto.Trim();
        return limpio.Length <= maximo ? limpio : limpio[..maximo] + "\n… (salida truncada)";
    }

    private void AsegurarFichero()
    {
        lock (_sync)
        {
            if (File.Exists(_ruta)) return;

            try
            {
                var directorio = Path.GetDirectoryName(_ruta);
                if (!string.IsNullOrEmpty(directorio))
                {
                    Directory.CreateDirectory(directorio);
                }

                File.WriteAllText(_ruta, "[]");
            }
            catch
            {
                // Si no se puede crear ahora, se reintentará en cada lectura.
            }
        }
    }

    private List<TareaAvanzada> LeerFichero()
    {
        try
        {
            if (!File.Exists(_ruta)) return [];

            string json = File.ReadAllText(_ruta);
            if (string.IsNullOrWhiteSpace(json)) return [];

            return JsonSerializer.Deserialize<List<TareaAvanzada>>(json, OpcionesJson) ?? [];
        }
        catch (JsonException)
        {
            ApartarFicheroCorrupto();
            return [];
        }
        catch
        {
            return [];
        }
    }

    private void GuardarFichero(List<TareaAvanzada> lista)
    {
        string json = JsonSerializer.Serialize(lista, OpcionesJson);
        string temporal = _ruta + ".tmp";
        File.WriteAllText(temporal, json);
        File.Move(temporal, _ruta, overwrite: true);
    }

    private void ApartarFicheroCorrupto()
    {
        try
        {
            if (!File.Exists(_ruta)) return;
            string respaldo = $"{_ruta}.corrupt-{DateTime.Now:yyyyMMddHHmmss}.bak";
            File.Move(_ruta, respaldo, overwrite: true);
        }
        catch
        {
            // Si no se puede apartar, se ignora y se reintentará.
        }
    }
}

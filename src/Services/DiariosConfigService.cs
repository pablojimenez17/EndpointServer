using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tradipres.EndpointServer.Services;

// ---------------------------------------------------------------------------
// Configuración de recurrencia de un mantenimiento "Diario".
//
// La definición (flags, mensaje, hora de inicio, estado) vive en la tabla
// dbo.Mantenimientos como cualquier otro mantenimiento; lo que NO cabe en el
// esquema de la BD (sin permisos DDL) es la recurrencia propiamente dicha,
// así que se guarda en un fichero JSON al lado del servidor (diarios.json),
// del mismo modo que equipos.json guarda los responsables.
//
// Formato del fichero (array de entradas):
// [
//   {
//     "mantenimientoId": 4,
//     "equipoId": 1,
//     "equipoGuid": "C7EB94D9-...",
//     "dias": [1,2,3,4,5],          // 0=domingo … 6=sábado (DayOfWeek .NET)
//     "fechaInicio": "2026-10-14T15:00:00",
//     "fechaFin": "2026-12-31T00:00:00",   // null = sin fin (recurrente eterno)
//     "ultimaEjecucion": "2026-10-15T15:00:33",
//     "activo": true
//   }
// ]
//
// "activo" permite desactivar una entrada (cierre o vigencia cumplida) sin
// borrar el histórico; "ultimaEjecucion" evita duplicados el mismo día aunque
// el agente se reinicie (el servidor es quien reclama, no el agente).
// ---------------------------------------------------------------------------
public sealed class DiarioConfig
{
    [JsonPropertyName("mantenimientoId")]
    public int MantenimientoId { get; set; }

    [JsonPropertyName("equipoId")]
    public int EquipoId { get; set; }

    [JsonPropertyName("equipoGuid")]
    public string? EquipoGuid { get; set; }

    // Días de la semana en los que se ejecuta (0=domingo … 6=sábado).
    [JsonPropertyName("dias")]
    public List<int> Dias { get; set; } = [];

    // Fecha y hora de la primera ejecución; solo la hora se reutiliza como
    // hora diaria, la fecha marca cuándo empieza la programación.
    [JsonPropertyName("fechaInicio")]
    public DateTime FechaInicio { get; set; }

    // Fin de la recurrencia (null = hasta que se cierre manualmente).
    [JsonPropertyName("fechaFin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? FechaFin { get; set; }

    // Último día en que el servidor entregó el diario al agente (deduplicado).
    [JsonPropertyName("ultimaEjecucion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? UltimaEjecucion { get; set; }

    [JsonPropertyName("activo")]
    public bool Activo { get; set; } = true;
}

// ---------------------------------------------------------------------------
// DiariosConfigService — lectura/escritura del fichero diarios.json.
//
// Patrón idéntico a EquiposResponsablesService (raíz del servidor), pero con
// escritura: cada operación de escritura relee el fichero, aplica el cambio
// sobre la copia fresca y lo vuelve a guardar de forma atómica (temporal +
// reemplazo) para no perder configuraciones de otros mantenimientos ante
// escrituras concurrentes. Si el JSON está corrupto se aparta a un .bak y se
// empieza de cero (no se borra la información: queda respaldada).
// ---------------------------------------------------------------------------
public sealed class DiariosConfigService
{
    private static readonly JsonSerializerOptions OpcionesJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _sync = new();
    private readonly string _ruta;

    public DiariosConfigService(IHostEnvironment environment)
    {
        // Misma ubicación que equipos.json: ContentRoot (dotnet watch desde
        // src) con fallback a la carpeta del binario (publish suelto).
        var contenido = Path.Combine(environment.ContentRootPath, "diarios.json");
        var baseDir = Path.Combine(AppContext.BaseDirectory, "diarios.json");
        _ruta = File.Exists(contenido) || Directory.Exists(environment.ContentRootPath)
            ? contenido
            : baseDir;

        AsegurarFichero();
    }

    // -------------------------------------------------------------------
    // Lectura.
    // -------------------------------------------------------------------

    public List<DiarioConfig> ObtenerTodos()
    {
        lock (_sync)
        {
            return LeerFichero();
        }
    }

    public DiarioConfig? ObtenerPorMantenimiento(int mantenimientoId)
    {
        lock (_sync)
        {
            return LeerFichero()
                .FirstOrDefault(c => c.MantenimientoId == mantenimientoId);
        }
    }

    public List<DiarioConfig> ObtenerActivosPorEquipo(Guid equipoGuid)
    {
        string guid = equipoGuid.ToString("D");
        lock (_sync)
        {
            return LeerFichero()
                .Where(c => c.Activo &&
                            string.Equals(c.EquipoGuid, guid, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    // -------------------------------------------------------------------
    // Escritura (todas releen → modifican → guardan atómicamente).
    // -------------------------------------------------------------------

    // Alta o actualización por MantenimientoId: si ya existe se sobrescribe
    // su configuración sin tocar las de los demás (sin duplicar entradas).
    public void Upsert(DiarioConfig config)
    {
        lock (_sync)
        {
            var lista = LeerFichero();
            int idx = lista.FindIndex(c => c.MantenimientoId == config.MantenimientoId);

            if (idx >= 0)
            {
                lista[idx] = config;
            }
            else
            {
                lista.Add(config);
            }

            GuardarFichero(lista);
        }
    }

    // Marca la última ejecución de un diario (reclamado por el servidor).
    public void MarcarEjecucion(int mantenimientoId, DateTime momento)
    {
        lock (_sync)
        {
            var lista = LeerFichero();
            var entrada = lista.FirstOrDefault(c => c.MantenimientoId == mantenimientoId);
            if (entrada is null) return;

            entrada.UltimaEjecucion = momento;
            GuardarFichero(lista);
        }
    }

    // Desactiva la entrada (cierre o vigencia cumplida) sin borrarla.
    public void Desactivar(int mantenimientoId)
    {
        lock (_sync)
        {
            var lista = LeerFichero();
            var entrada = lista.FirstOrDefault(c => c.MantenimientoId == mantenimientoId);
            if (entrada is null) return;

            entrada.Activo = false;
            GuardarFichero(lista);
        }
    }

    // Elimina la entrada (mantenimiento cancelado o cerrado manualmente).
    public void Eliminar(int mantenimientoId)
    {
        lock (_sync)
        {
            var lista = LeerFichero();
            int eliminadas = lista.RemoveAll(c => c.MantenimientoId == mantenimientoId);
            if (eliminadas == 0) return;

            GuardarFichero(lista);
        }
    }

    // -------------------------------------------------------------------
    // Internos.
    // -------------------------------------------------------------------

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

    private List<DiarioConfig> LeerFichero()
    {
        try
        {
            if (!File.Exists(_ruta))
            {
                return [];
            }

            string json = File.ReadAllText(_ruta);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<DiarioConfig>>(json, OpcionesJson) ?? [];
        }
        catch (JsonException)
        {
            // JSON corrupto: se aparta a un .bak con marca de tiempo y se
            // continúa con una lista vacía (la información queda respaldada,
            // no se pierde ni se pisca al escribir).
            ApartarFicheroCorrupto();
            return [];
        }
        catch
        {
            // Error de E/S (permisos, bloqueo…): no se puede leer; se asume
            // vacío para no tumbar el endpoint, y no se escribe por encima.
            return [];
        }
    }

    private void GuardarFichero(List<DiarioConfig> lista)
    {
        string json = JsonSerializer.Serialize(lista, OpcionesJson);
        string temporal = _ruta + ".tmp";

        // Escritura atómica: primero a un temporal y después se reemplaza,
        // para que un fallo a mitad de escritura no deje el fichero a medias.
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
            // Si ni siquiera se puede apartar, se ignora: la próxima lectura
            // volverá a fallar y se reintentará el respaldo.
        }
    }
}

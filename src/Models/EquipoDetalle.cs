namespace Tradipres.EndpointServer.Models;

public class EquipoDetalle
{
    public required Equipo Equipo { get; set; }
    public InventarioEquipoDetalle? Inventario { get; set; }
    public List<DiscoDetalle> Discos { get; set; } = [];
    public MantenimientoDetalle? UltimoMantenimiento { get; set; }
}

public class InventarioEquipoDetalle
{
    public string Fabricante { get; set; } = "";
    public string Modelo { get; set; } = "";
    public string NumeroSerie { get; set; } = "";
    public string Procesador { get; set; } = "";
    public int Nucleos { get; set; }
    public decimal MemoriaRamGB { get; set; }
    public string Arquitectura { get; set; } = "";
    public string VersionWindows { get; set; } = "";
    public DateTime? FechaInstalacion { get; set; }
    public string IpPrincipal { get; set; } = "";
    public string MacPrincipal { get; set; } = "";
    public DateTime FechaInventario { get; set; }
}

public class DiscoDetalle
{
    public string Unidad { get; set; } = "";
    public string? Etiqueta { get; set; }
    public string? SistemaArchivos { get; set; }
    public string? TipoUnidad { get; set; }
    public long TamanoBytes { get; set; }
    public long LibreBytes { get; set; }
}

public class MantenimientoDetalle
{
    public int Id { get; set; }

    // "Unico" (una vez) o "Diario" (recurrente). Se deriva del Estado en la
    // BD: Estado="Diario" marca la definición del recurrente; el resto son
    // ejecuciones o programaciones únicas.
    public string Tipo { get; set; } = "Unico";

    public DateTime FechaInicio { get; set; }
    public DateTime FechaFin { get; set; }
    public string Estado { get; set; } = "";
    public int DuracionSegundos { get; set; }
    public bool CopiaOutlook { get; set; }
    public bool Mantenimiento { get; set; }
    public bool ReparacionWindows { get; set; }
    public bool ApagarEquipo { get; set; }
    public string? Mensaje { get; set; }
    public string? DetalleTecnico { get; set; }
    public DateTime FechaRecepcion { get; set; }

    // Solo para Tipo="Diario": días de la semana (0=domingo … 6=sábado) y
    // fecha de fin de la recurrencia (null = sin fin). Se guardan en el
    // fichero diarios.json (sin ALTER a la tabla).
    public List<int>? Dias { get; set; }
    public DateTime? VigenciaFin { get; set; }
}

// MantenimientoConEquipo — fila de la página /Mantenimientos/ (solo
// visualización). Extiende MantenimientoDetalle con los datos del equipo
// para no tener que hacer N peticiones desde el navegador.
public class MantenimientoConEquipo : MantenimientoDetalle
{
    public int EquipoId { get; set; }
    public string NombreEquipo { get; set; } = "";
    public string EstadoEquipo { get; set; } = "";
}

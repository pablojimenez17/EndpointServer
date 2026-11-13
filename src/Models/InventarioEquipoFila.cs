namespace Tradipres.EndpointServer.Models;

public class InventarioEquipoFila
{
    public int Id { get; set; }
    public Guid Guid { get; set; }
    public string NombreEquipo { get; set; } = "";
    public string Dominio { get; set; } = "";
    public string Usuario { get; set; } = "";
    public string DireccionIP { get; set; } = "";
    public string VersionAgente { get; set; } = "";
    public DateTime UltimoHeartbeat { get; set; }
    public string Estado { get; set; } = "";
    public bool TieneInventario { get; set; }
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
    public DateTime? FechaInventario { get; set; }

    // Responsable según equipos.json (null si el equipo no consta allí).
    public string? Responsable { get; set; }
}

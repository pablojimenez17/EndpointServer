namespace Tradipres.EndpointServer.Models;

public class InventoryReportRequest
{
    public Guid Guid { get; set; }
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
    public List<InventoryDiskRequest> Discos { get; set; } = [];
}

public class InventoryDiskRequest
{
    public string Unidad { get; set; } = "";
    public string? Etiqueta { get; set; }
    public string? SistemaArchivos { get; set; }
    public string? TipoUnidad { get; set; }
    public long TamanoBytes { get; set; }
    public long LibreBytes { get; set; }
}

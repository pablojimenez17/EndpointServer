namespace Tradipres.EndpointServer.Models;

public sealed class SoftwareReportRequest
{
    public Guid Guid { get; set; }
    public List<SoftwareItemRequest> Software { get; set; } = [];
}

public sealed class SoftwareItemRequest
{
    public string Nombre { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Fabricante { get; set; }
    public DateTime? FechaInstalacion { get; set; }
    public string? Arquitectura { get; set; }
}

public sealed class SoftwareEquipoItem
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Fabricante { get; set; }
    public DateTime? FechaInstalacion { get; set; }
    public string? Arquitectura { get; set; }
    public DateTime FechaInventario { get; set; }
}

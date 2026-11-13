namespace Tradipres.EndpointServer.Models;

public class Equipo
{
    public int Id { get; set; }
    public Guid Guid { get; set; }
    public string NombreEquipo { get; set; } = "";
    public string Usuario { get; set; } = "";
    public string SistemaOperativo { get; set; } = "";
    public string VersionSO { get; set; } = "";
    public string Dominio { get; set; } = "";
    public string DireccionIP { get; set; } = "";
    public string VersionAgente { get; set; } = "";
    public DateTime FechaRegistro { get; set; }
    public DateTime UltimoHeartbeat { get; set; }
    public string Estado { get; set; } = "";
}

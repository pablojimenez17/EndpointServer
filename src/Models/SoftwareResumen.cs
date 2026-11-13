namespace Tradipres.EndpointServer.Models;

public class SoftwareResumen
{
    public int Programas { get; set; }
    public long Instalaciones { get; set; }
    public int EquiposConSoftware { get; set; }
    public List<SoftwareResumenItem> Items { get; set; } = [];
}

public class SoftwareResumenItem
{
    public string Nombre { get; set; } = "";
    public string Version { get; set; } = "";
    public string Fabricante { get; set; } = "";
    public string Arquitectura { get; set; } = "";
    public int Equipos { get; set; }
}

public class SoftwareGrupoEquipo
{
    public string NombreEquipo { get; set; } = "";
    public string Dominio { get; set; } = "";
    public string DireccionIP { get; set; } = "";
    public string Estado { get; set; } = "";
}

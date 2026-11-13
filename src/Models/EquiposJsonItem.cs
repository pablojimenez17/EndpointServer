using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tradipres.EndpointServer.Models;

// Elemento del fichero equipos.json: { "usuario": "Pablo", "equipo": "PTradi14" }
public sealed class EquiposJsonItem
{
    [JsonPropertyName("usuario")]
    public string? Usuario { get; set; }

    [JsonPropertyName("equipo")]
    public string? Equipo { get; set; }
}

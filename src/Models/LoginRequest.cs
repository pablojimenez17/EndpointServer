namespace Tradipres.EndpointServer.Models;

public sealed class LoginRequest
{
    public string? Usuario { get; set; }
    public string? Password { get; set; }
}

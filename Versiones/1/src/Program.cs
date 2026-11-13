using Microsoft.Data.SqlClient;
using Tradipres.EndpointServer.Models;
using Tradipres.EndpointServer.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddScoped<EquipoRepository>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", async (IConfiguration configuration) =>
{
    var connectionString = configuration.GetConnectionString("EndpointManager");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return Results.Json(new
        {
            api = "OK",
            database = "SIN CONFIGURAR",
            version = "0.3.0",
            utc = DateTimeOffset.UtcNow
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    try
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand(
            "SELECT CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)), DB_NAME()",
            connection);

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return Results.Ok(new
        {
            api = "OK",
            database = "OK",
            server = reader.IsDBNull(0) ? null : reader.GetString(0),
            databaseName = reader.IsDBNull(1) ? null : reader.GetString(1),
            version = "0.3.0",
            utc = DateTimeOffset.UtcNow
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            api = "OK",
            database = "ERROR",
            error = ex.Message,
            version = "0.3.0",
            utc = DateTimeOffset.UtcNow
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/register", async (
    RegistrarEquipoRequest request,
    EquipoRepository repo) =>
{
    if (request.Guid == Guid.Empty)
    {
        return Results.BadRequest(new
        {
            error = "El GUID del equipo es obligatorio."
        });
    }

    if (string.IsNullOrWhiteSpace(request.NombreEquipo))
    {
        return Results.BadRequest(new
        {
            error = "El nombre del equipo es obligatorio."
        });
    }

    if (string.IsNullOrWhiteSpace(request.VersionAgente))
    {
        return Results.BadRequest(new
        {
            error = "La versión del agente es obligatoria."
        });
    }

    var equipo = await repo.RegistrarOActualizar(request);
    return Results.Ok(equipo);
});

app.MapPost("/api/heartbeat", async (
    HeartbeatRequest request,
    EquipoRepository repo) =>
{
    if (request.Guid == Guid.Empty)
    {
        return Results.BadRequest(new
        {
            error = "El GUID del equipo es obligatorio."
        });
    }

    var actualizado = await repo.ActualizarHeartbeat(request.Guid);

    if (!actualizado)
    {
        return Results.NotFound(new
        {
            error = "No existe ningún equipo registrado con ese GUID."
        });
    }

    return Results.Ok(new
    {
        ok = true,
        guid = request.Guid,
        estado = "Online",
        utc = DateTimeOffset.UtcNow
    });
});

app.MapGet("/api/equipos", async (EquipoRepository repo) =>
{
    var equipos = await repo.ObtenerEquipos();
    return Results.Ok(equipos);
});

app.MapHealthChecks("/health");
app.MapFallbackToFile("index.html");

app.Run();

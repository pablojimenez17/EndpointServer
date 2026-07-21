using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", async (IConfiguration configuration) =>
{
    var connectionString = configuration.GetConnectionString("EndpointManager");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return Results.Problem(
            title: "Configuracion incompleta",
            detail: "No existe la cadena de conexion EndpointManager.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    try
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                @@SERVERNAME AS ServidorSql,
                DB_NAME() AS BaseDatosSql,
                GETDATE() AS FechaServidor;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return Results.Ok(new
        {
            api = "OK",
            baseDatos = "OK",
            servidorSql = reader["ServidorSql"]?.ToString(),
            baseDatosSql = reader["BaseDatosSql"]?.ToString(),
            fechaServidor = reader.GetDateTime(reader.GetOrdinal("FechaServidor")),
            version = "0.2.0"
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            api = "OK",
            baseDatos = "ERROR",
            error = ex.Message,
            version = "0.2.0"
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/equipos", () =>
{
    return Results.Ok(Array.Empty<object>());
});

app.MapPost("/api/heartbeat", () =>
{
    return Results.StatusCode(StatusCodes.Status501NotImplemented);
});

app.MapFallbackToFile("index.html");

app.Run();

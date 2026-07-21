using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

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
            version = "0.2.0",
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
            version = "0.2.0",
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
            version = "0.2.0",
            utc = DateTimeOffset.UtcNow
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/equipos", () => Results.Ok(Array.Empty<object>()));
app.MapHealthChecks("/health");
app.MapFallbackToFile("index.html");

app.Run();

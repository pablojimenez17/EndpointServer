using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.SqlClient;
using Tradipres.EndpointServer.Models;
using Tradipres.EndpointServer.Repositories;
using Tradipres.EndpointServer.Services;

// ============================================================================
// Program.cs — Punto de entrada del servidor web "Tradipres Endpoint Manager".
//
// Servidor ASP.NET Core minimal API + web estática (wwwroot). Los datos se
// guardan en SQL Server (TRADI23 / EndpointManager) mediante los repositorios
// de la carpeta Repositories, que se inyectan por petición (scoped).
//
// Endpoints de la API:
//   GET  /api/status                              estado del servidor y la BD
//   POST /api/register                            alta/actualización de equipo (agente)
//   POST /api/heartbeat                           marca un equipo como Online (agente)
//   GET  /api/equipos                             lista de equipos registrados
//   GET  /api/equipos/{id}/detalle                equipo + inventario + discos
//   POST /api/maintenance/report                  el agente informa un mantenimiento ejecutado
//   POST /api/inventory                           el agente envía hardware y discos
//   POST /api/software                            el agente envía el software instalado
//   GET  /api/equipos/{id}/software               software de un equipo
//   GET  /api/inventario                          filas de la tabla de /equipos/
//   GET  /api/software/resumen                    agregado para /Software/
//   GET  /api/software/grupo                      equipos donde está instalado un programa
//   GET  /api/equipos/{id}/mantenimientos         mantenimientos de un equipo
//   POST /api/maintenance/programar               programa un mantenimiento (web)
//   POST /api/maintenance/programar/{id}/cancelar cancela uno programado (web)
//   POST /api/maintenance/{id}/cerrar             cierra manualmente (web)
//   GET  /api/maintenance/pending                 pendientes para el agente
//   POST /api/maintenance/interrumpidos           marca Ejecutando como Error (agente)
//   POST /api/advanced-tasks                      programa una tarea de PowerShell (web)
//   GET  /api/equipos/{id}/advanced-tasks         tareas avanzadas de un equipo (web)
//   POST /api/advanced-tasks/{id}/cancel          cancela una tarea programada (web)
//   GET  /api/advanced-tasks/pending              tareas vencidas para el agente
//   POST /api/advanced-tasks/report               resultado de una tarea (agente)
//   POST /api/advanced-tasks/interrupted          recupera tareas tras reinicio (agente)
//   GET  /health                                  health checks de ASP.NET
//
// NOTA DE SEGURIDAD: la web usa autenticación por cookie con credenciales
// fijadas en este archivo (red interna). La API de los agentes queda abierta
// y la cadena de conexión va en appsettings.json en claro.
// ============================================================================

var builder = WebApplication.CreateBuilder(args);

const string UsuarioPanel = "admin";
const string PasswordPanel = "Tradipres2026!";

// Servicios del contenedor: problem details para errores, health checks y
// los repositories de acceso a la base de datos (uno por contexto de datos).
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "Tradipres.Endpoint.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.LoginPath = "/login.html";
        options.Events.OnRedirectToLogin = context =>
        {
            if (EsRutaApi(context.Request.Path))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddScoped<EquipoRepository>();          // equipos: registro, heartbeat, listado
builder.Services.AddScoped<MantenimientoRepository>();   // mantenimientos: listar, programar, cancelar
builder.Services.AddScoped<InventarioRepository>();      // inventario de hardware de /equipos/
builder.Services.AddScoped<EquipoDetalleRepository>();   // detalle completo (equipo + inventario + discos)
builder.Services.AddScoped<SoftwareRepository>();        // software: guardar y resumir
builder.Services.AddSingleton<EquiposResponsablesService>(); // equipos.json → responsable de cada equipo
builder.Services.AddSingleton<DiariosConfigService>();      // diarios.json → recurrencia de los Diarios
builder.Services.AddSingleton<OrdenAccionesService>();       // orden_acciones.json → prioridad de acciones
builder.Services.AddSingleton<TareasAvanzadasService>();     // tareas avanzadas: comandos PowerShell programados

// Tiempo real (SignalR): cambios de estado de mantenimientos y heartbeats
// empujados a las pestañas abiertas. SignalR viene en el framework de
// ASP.NET Core (sin paquetes NuGet nuevos).
builder.Services.AddSignalR();
builder.Services.AddSingleton<RealtimeNotifier>();

var app = builder.Build();

// Manejador global de excepciones + servidor de ficheros estáticos (wwwroot).
app.UseExceptionHandler();
app.UseAuthentication();

// Puerta de acceso: sin cookie válida solo se sirven el login, sus recursos y
// los endpoints que usan los agentes. El resto de la web redirige al login y
// las rutas de API/hub responden 401 en lugar de HTML.
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;

    if (context.User.Identity?.IsAuthenticated == true)
    {
        if (path.Equals("/login.html", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect("/");
            return;
        }

        await next();
        return;
    }

    if (EsRutaPublica(path))
    {
        await next();
        return;
    }

    if (EsRutaApi(context.Request.Path))
    {
        await Results.Json(new { error = "No autenticado." },
            statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(context);
        return;
    }

    context.Response.Redirect("/login.html");
});

app.UseStaticFiles();

// ---------------------------------------------------------------------------
// GET /api/status — salud del sistema (API + base de datos).
// Devuelve 200 si la BD responde y 503 si no está configurada o falla;
// lo usan el sidebar y el dashboard para pintar el indicador de estado.
// ---------------------------------------------------------------------------
app.MapGet("/api/status", async (IConfiguration configuration) =>
{
    var connectionString = configuration.GetConnectionString("EndpointManager");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return Results.Json(new
        {
            api = "OK",
            database = "SIN CONFIGURAR",
            version = "0.8.0",
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
            version = "0.8.0",
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
            version = "0.8.0",
            utc = DateTimeOffset.UtcNow
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

// ---------------------------------------------------------------------------
// POST /api/login — valida el usuario y la contraseña fijados en el código y
// emite la cookie de sesión. Se usa desde /login.html.
// POST /api/logout — elimina la cookie de sesión.
// ---------------------------------------------------------------------------
app.MapPost("/api/login", async (LoginRequest request, HttpContext context) =>
{
    if (!CredencialesValidas(request.Usuario, request.Password))
    {
        return Results.Json(new { error = "Usuario o contraseña incorrectos." },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    var claims = new[]
    {
        new Claim(ClaimTypes.Name, UsuarioPanel),
        new Claim(ClaimTypes.Role, "Operador")
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
        });

    return Results.Ok(new { ok = true, usuario = UsuarioPanel });
});

app.MapPost("/api/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { ok = true });
});

// ---------------------------------------------------------------------------
// POST /api/register — alta o actualización de un equipo (lo llama el agente
// al arrancar). Si el GUID ya existe actualiza los datos; si no, crea la fila.
// ---------------------------------------------------------------------------
app.MapPost("/api/register", async (
    RegistrarEquipoRequest request,
    EquipoRepository repo,
    MantenimientoRepository mantenimientos,
    RealtimeNotifier realtime) =>
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

    var (equipo, esNuevo) = await repo.RegistrarOActualizar(request);

    // Al registrar un equipo nuevo, se crea automáticamente un mantenimiento
    // diario de copia de Outlook: hoy a las 19:00, lunes a viernes, sin fin.
    if (esNuevo)
    {
        try
        {
            var hoy19 = DateTime.Today.AddHours(19);

            await mantenimientos.Programar(new ProgramarMantenimientoRequest
            {
                Guid = request.Guid,
                Tipo = "Diario",
                FechaInicio = hoy19,
                Dias = new List<int> { 1, 2, 3, 4, 5 },
                CopiaOutlook = true,
                Mensaje = "Copia de seguridad automática de Outlook."
            });
        }
        catch
        {
            // Si falla la creación del diario por defecto, el registro
            // del equipo no debe fallar — se ignora y el equipo queda registrado.
        }
    }

    await realtime.EquipoRegistradoAsync(request.Guid, equipo.NombreEquipo);

    return Results.Ok(equipo);
});

// ---------------------------------------------------------------------------
// POST /api/heartbeat — el agente avisa periódicamente de que sigue vivo;
// se actualiza la fecha del último heartbeat (la web calcula Online/Offline
// comparándola con la hora actual).
// ---------------------------------------------------------------------------
app.MapPost("/api/heartbeat", async (
    HeartbeatRequest request,
    EquipoRepository repo,
    RealtimeNotifier realtime) =>
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

    await realtime.EquipoHeartbeatAsync(request.Guid);

    return Results.Ok(new
    {
        ok = true,
        guid = request.Guid,
        estado = "Online",
        utc = DateTimeOffset.UtcNow
    });
});

// ---------------------------------------------------------------------------
// GET /api/equipos — listado de equipos registrados (lo usa el dashboard).
// Se enriquece con el responsable del equipos.json (búsqueda sin distinguir
// mayúsculas, p. ej. "PTRADI14" del agente ↔ "PTradi14" del JSON).
// ---------------------------------------------------------------------------
app.MapGet("/api/equipos", async (
    EquipoRepository repo,
    EquiposResponsablesService responsables) =>
{
    var equipos = await repo.ObtenerEquipos();

    foreach (var equipo in equipos)
    {
        equipo.Responsable = responsables.ObtenerResponsable(equipo.NombreEquipo);
    }

    return Results.Ok(equipos);
});

// ---------------------------------------------------------------------------
// GET /api/equipos/{id}/detalle — ficha completa: datos del equipo, su
// inventario de hardware y sus discos (pestañas General/Hardware/Discos).
// ---------------------------------------------------------------------------
app.MapGet("/api/equipos/{id:int}/detalle", async (
    int id,
    EquipoDetalleRepository repo) =>
{
    if (id <= 0)
    {
        return Results.BadRequest(new
        {
            error = "El identificador del equipo no es válido."
        });
    }

    var detalle = await repo.ObtenerDetalle(id);

    return detalle is null
        ? Results.NotFound(new { error = "No existe ningún equipo con ese identificador." })
        : Results.Ok(detalle);
});

// ---------------------------------------------------------------------------
// POST /api/maintenance/report — el agente notifica un mantenimiento que ha
// ejecutado (estado, duración, acciones, mensaje). Tras guardarlo se emite
// el evento de tiempo real para que la web lo pinte sin recargar.
// ---------------------------------------------------------------------------
app.MapPost("/api/maintenance/report", async (
    MaintenanceReportRequest request,
    MantenimientoRepository repo,
    RealtimeNotifier realtime) =>
{
    if (request.Guid == Guid.Empty)
    {
        return Results.BadRequest(new
        {
            error = "El GUID del equipo es obligatorio."
        });
    }

    if (request.FechaInicio == default || request.FechaFin == default)
    {
        return Results.BadRequest(new
        {
            error = "Las fechas de inicio y fin son obligatorias."
        });
    }

    if (request.FechaFin < request.FechaInicio)
    {
        return Results.BadRequest(new
        {
            error = "La fecha de fin no puede ser anterior a la fecha de inicio."
        });
    }

    if (string.IsNullOrWhiteSpace(request.Estado))
    {
        return Results.BadRequest(new
        {
            error = "El estado del mantenimiento es obligatorio."
        });
    }

    if (request.Estado.Trim().Length > 30)
    {
        return Results.BadRequest(new
        {
            error = "El estado no puede superar los 30 caracteres."
        });
    }

    if (request.DuracionSegundos < 0)
    {
        return Results.BadRequest(new
        {
            error = "La duración no puede ser negativa."
        });
    }

    if (request.Mensaje?.Length > 500)
    {
        return Results.BadRequest(new
        {
            error = "El mensaje no puede superar los 500 caracteres."
        });
    }

    var mantenimientoId = await repo.GuardarMantenimiento(request);

    if (mantenimientoId is null)
    {
        return Results.NotFound(new
        {
            error = "No existe ningún equipo registrado con ese GUID."
        });
    }

    await realtime.MantenimientoCambiadoAsync(
        request.Guid,
        request.MantenimientoId,
        request.Estado.Trim(),
        request.DetalleTecnico);

    return Results.Created($"/api/maintenance/{mantenimientoId}", new
    {
        ok = true,
        id = mantenimientoId,
        guid = request.Guid,
        estado = request.Estado.Trim(),
        fechaRecepcionUtc = DateTimeOffset.UtcNow
    });
});

// ---------------------------------------------------------------------------
// POST /api/inventory — el agente envía el inventario de hardware: CPU,
// núcleos, RAM, arquitectura, Windows y la lista de discos (validando que
// no haya unidades duplicadas ni tamaños inconsistentes).
// ---------------------------------------------------------------------------
app.MapPost("/api/inventory", async (
    InventoryReportRequest request,
    InventarioRepository repo) =>
{
    if (request.Guid == Guid.Empty)
    {
        return Results.BadRequest(new
        {
            error = "El GUID del equipo es obligatorio."
        });
    }

    if (request.Nucleos < 0)
    {
        return Results.BadRequest(new
        {
            error = "El número de núcleos no puede ser negativo."
        });
    }

    if (request.MemoriaRamGB < 0)
    {
        return Results.BadRequest(new
        {
            error = "La memoria RAM no puede ser negativa."
        });
    }

    if (request.Discos is null)
    {
        return Results.BadRequest(new
        {
            error = "La colección de discos es obligatoria."
        });
    }

    if (request.Discos.Any(d => string.IsNullOrWhiteSpace(d.Unidad)))
    {
        return Results.BadRequest(new
        {
            error = "Todos los discos deben indicar una unidad."
        });
    }

    if (request.Discos
        .GroupBy(d => d.Unidad.Trim(), StringComparer.OrdinalIgnoreCase)
        .Any(g => g.Count() > 1))
    {
        return Results.BadRequest(new
        {
            error = "No puede haber unidades de disco duplicadas."
        });
    }

    if (request.Discos.Any(d =>
        d.TamanoBytes < 0 ||
        d.LibreBytes < 0 ||
        d.LibreBytes > d.TamanoBytes))
    {
        return Results.BadRequest(new
        {
            error = "Los tamaños de disco no son válidos."
        });
    }

    var equipoId = await repo.GuardarInventario(request);

    if (equipoId is null)
    {
        return Results.NotFound(new
        {
            error = "No existe ningún equipo registrado con ese GUID."
        });
    }

    return Results.Ok(new
    {
        ok = true,
        equipoId,
        guid = request.Guid,
        discos = request.Discos.Count,
        fechaInventarioUtc = DateTimeOffset.UtcNow
    });
});

// ---------------------------------------------------------------------------
// POST /api/software — el agente envía el software instalado del equipo
// (hasta 10.000 entradas); se guarda reemplazando el inventario anterior.
// ---------------------------------------------------------------------------
app.MapPost("/api/software", async (
    SoftwareReportRequest request,
    SoftwareRepository repo) =>
{
    if (request.Guid == Guid.Empty)
    {
        return Results.BadRequest(new { error = "El GUID del equipo es obligatorio." });
    }

    if (request.Software is null)
    {
        return Results.BadRequest(new { error = "La colección de software es obligatoria." });
    }

    if (request.Software.Any(x => string.IsNullOrWhiteSpace(x.Nombre)))
    {
        return Results.BadRequest(new { error = "Todos los programas deben indicar un nombre." });
    }

    if (request.Software.Count > 10000)
    {
        return Results.BadRequest(new { error = "El inventario contiene demasiadas entradas." });
    }

    var equipoId = await repo.GuardarSoftware(request);
    if (equipoId is null)
    {
        return Results.NotFound(new { error = "No existe ningún equipo registrado con ese GUID." });
    }

    return Results.Ok(new
    {
        ok = true,
        equipoId,
        guid = request.Guid,
        programas = request.Software.Count,
        fechaInventarioUtc = DateTimeOffset.UtcNow
    });
});

// ---------------------------------------------------------------------------
// GET /api/equipos/{id}/software — programas instalados de un equipo
// (pestaña Software de la ficha en /equipos/).
// ---------------------------------------------------------------------------
app.MapGet("/api/equipos/{id:int}/software", async (
    int id,
    SoftwareRepository repo) =>
{
    if (id <= 0)
    {
        return Results.BadRequest(new { error = "El identificador del equipo no es válido." });
    }

    var software = await repo.ObtenerSoftware(id);
    return software is null
        ? Results.NotFound(new { error = "No existe ningún equipo con ese identificador." })
        : Results.Ok(software);
});

// ---------------------------------------------------------------------------
// GET /api/inventario — una fila por equipo con su inventario de hardware
// (tabla principal de la página /equipos/). Incluye el responsable del
// equipos.json, igual que /api/equipos.
// ---------------------------------------------------------------------------
app.MapGet("/api/inventario", async (
    InventarioRepository repo,
    EquiposResponsablesService responsables) =>
{
    var filas = await repo.ObtenerInventarioEquipos();

    foreach (var fila in filas)
    {
        fila.Responsable = responsables.ObtenerResponsable(fila.NombreEquipo);
    }

    return Results.Ok(filas);
});

// ---------------------------------------------------------------------------
// GET /api/software/resumen — software agregado por programa+versión+
// fabricante+arquitectura con el nº de equipos; alimenta la página /Software/
// (tabla, etiquetas por fabricante y agrupación).
// ---------------------------------------------------------------------------
app.MapGet("/api/software/resumen", async (SoftwareRepository repo) =>
{
    var resumen = await repo.ObtenerSoftwareResumen();
    return Results.Ok(resumen);
});

// ---------------------------------------------------------------------------
// GET /api/software/grupo — equipos donde está instalado un programa
// concreto (diálogo "Equipos con este programa" de /Software/).
// ---------------------------------------------------------------------------
app.MapGet("/api/software/grupo", async (
    string nombre,
    string? version,
    string? fabricante,
    string? arquitectura,
    SoftwareRepository repo) =>
{
    if (string.IsNullOrWhiteSpace(nombre))
    {
        return Results.BadRequest(new { error = "El nombre del programa es obligatorio." });
    }

    var equipos = await repo.ObtenerEquiposConPrograma(
        nombre.Trim(), version, fabricante, arquitectura);

    return Results.Ok(equipos);
});

// ---------------------------------------------------------------------------
// GET /api/equipos/{id}/mantenimientos — historial de mantenimientos del
// equipo (pestaña Mantenimiento de la ficha).
// ---------------------------------------------------------------------------
app.MapGet("/api/equipos/{id:int}/mantenimientos", async (
    int id,
    MantenimientoRepository repo) =>
{
    if (id <= 0)
    {
        return Results.BadRequest(new { error = "El identificador del equipo no es válido." });
    }

    var mantenimientos = await repo.ObtenerMantenimientos(id);
    return Results.Ok(mantenimientos);
});

// ---------------------------------------------------------------------------
// GET /api/maintenance — TODOS los mantenimientos de TODOS los equipos
// (página /Mantenimientos/, solo visualización). Devuelve el nombre y el
// estado de equipo junto a cada tarea para no tener que hacer N peticiones.
// ---------------------------------------------------------------------------
app.MapGet("/api/maintenance", async (MantenimientoRepository repo) =>
{
    var mantenimientos = await repo.ObtenerTodos();
    return Results.Ok(mantenimientos);
});

// ---------------------------------------------------------------------------
// POST /api/maintenance/programar — crea un mantenimiento desde la web.
// Tipo "Unico" → estado "Programado" (requiere GUID, inicio no anterior a hoy
// y al menos una acción). Tipo "Diario" → estado "Diario" (recurrente: además
// exige los días de la semana y admite una fecha de fin opcional). El tipo
// Unico NO se pide fin: lo escribe quien cierre el mantenimiento (el agente
// o el botón "Cerrar" de la ficha).
// ---------------------------------------------------------------------------
app.MapPost("/api/maintenance/programar", async (
    ProgramarMantenimientoRequest request,
    MantenimientoRepository repo,
    RealtimeNotifier realtime) =>
{
    if (request.Guid == Guid.Empty)
    {
        return Results.BadRequest(new { error = "El GUID del equipo es obligatorio." });
    }

    if (request.FechaInicio == default)
    {
        return Results.BadRequest(new { error = "La fecha de inicio es obligatoria." });
    }

    // La fecha/hora de inicio debe ser, como mínimo, 1 minuto posterior a
    // la hora actual: nunca se puede programar un mantenimiento en el pasado.
    if (request.FechaInicio < DateTime.Now.AddMinutes(1))
    {
        return Results.BadRequest(new
        {
            error = "La fecha de inicio debe ser al menos 1 minuto posterior a la hora actual."
        });
    }

    bool esDiario = string.Equals(request.Tipo?.Trim(), "Diario", StringComparison.OrdinalIgnoreCase);

    if (!esDiario && !string.Equals(request.Tipo?.Trim(), "Unico", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "El tipo debe ser \"Unico\" o \"Diario\"." });
    }

    if (esDiario)
    {
        // Un Diario necesita al menos un día de la semana válido (0=domingo …
        // 6=sábado, el enum DayOfWeek de .NET).
        if (request.Dias is null || request.Dias.Count == 0)
        {
            return Results.BadRequest(new
            {
                error = "Un mantenimiento diario debe indicar al menos un día de la semana."
            });
        }

        if (request.Dias.Any(d => d < 0 || d > 6))
        {
            return Results.BadRequest(new
            {
                error = "Los días de la semana deben estar entre 0 (domingo) y 6 (sábado)."
            });
        }

        // La fecha de fin es opcional, pero si se indica no puede ser
        // anterior al inicio de la programación.
        if (request.FechaFin.HasValue && request.FechaFin.Value.Date < request.FechaInicio.Date)
        {
            return Results.BadRequest(new
            {
                error = "La fecha de fin no puede ser anterior a la fecha de inicio."
            });
        }
    }

    if (request.Mensaje?.Length > 500)
    {
        return Results.BadRequest(new { error = "El mensaje no puede superar los 500 caracteres." });
    }

    if (!request.CopiaOutlook && !request.Mantenimiento &&
        !request.ReparacionWindows && !request.ApagarEquipo)
    {
        return Results.BadRequest(new
        {
            error = "Debes indicar al menos una acción del mantenimiento."
        });
    }

    var mantenimientoId = await repo.Programar(request);

    if (mantenimientoId is null)
    {
        return Results.NotFound(new { error = "No existe ningún equipo registrado con ese GUID." });
    }

    await realtime.MantenimientoCambiadoAsync(
        request.Guid,
        mantenimientoId,
        esDiario ? "Diario" : "Programado");

    return Results.Created($"/api/maintenance/programar/{mantenimientoId}", new
    {
        ok = true,
        id = mantenimientoId,
        guid = request.Guid,
        estado = esDiario ? "Diario" : "Programado",
        fechaInicio = request.FechaInicio,
        fechaRecepcionUtc = DateTimeOffset.UtcNow
    });
});

// ---------------------------------------------------------------------------
// POST /api/maintenance/programar/{id}/cancelar — marca un mantenimiento
// "Programado" como "Cancelado" (solo si sigue programado).
// ---------------------------------------------------------------------------
app.MapPost("/api/maintenance/programar/{id:int}/cancelar", async (
    int id,
    MantenimientoRepository repo,
    RealtimeNotifier realtime) =>
{
    if (id <= 0)
    {
        return Results.BadRequest(new { error = "El identificador del mantenimiento no es válido." });
    }

    var cancelado = await repo.Cancelar(id);

    if (!cancelado)
    {
        return Results.NotFound(new
        {
            error = "No existe ningún mantenimiento programado con ese identificador."
        });
    }

    var guidEquipo = await repo.ObtenerGuidEquipo(id);
    if (guidEquipo is Guid guid)
    {
        await realtime.MantenimientoCambiadoAsync(guid, id, "Cancelado");
    }

    return Results.Ok(new { ok = true, id, estado = "Cancelado" });
});

// ---------------------------------------------------------------------------
// PUT /api/maintenance/diario/editar — edición de un Diario desde la web.
// Actualiza días, hora, vigencia, acciones, orden y mensaje sin cancelar
// ni recrear el mantenimiento.
// ---------------------------------------------------------------------------
app.MapPut("/api/maintenance/diario/editar", async (
    EditarDiarioRequest request,
    MantenimientoRepository repo,
    RealtimeNotifier realtime) =>
{
    if (request.MantenimientoId <= 0)
    {
        return Results.BadRequest(new { error = "El identificador del mantenimiento no es válido." });
    }

    if (request.Dias is null || request.Dias.Count == 0)
    {
        return Results.BadRequest(new { error = "Selecciona al menos un día de la semana." });
    }

    if (request.Dias.Any(d => d < 0 || d > 6))
    {
        return Results.BadRequest(new { error = "Los días deben estar entre 0 (domingo) y 6 (sábado)." });
    }

    bool algunaAccion = request.CopiaOutlook || request.Mantenimiento ||
                        request.ReparacionWindows || request.ApagarEquipo;
    if (!algunaAccion)
    {
        return Results.BadRequest(new { error = "Marca al menos una acción." });
    }

    var editado = await repo.EditarDiario(request);

    if (!editado)
    {
        return Results.NotFound(new
        {
            error = "No existe ningún mantenimiento Diario con ese identificador."
        });
    }

    var guidDiario = await repo.ObtenerGuidEquipo(request.MantenimientoId);
    if (guidDiario is Guid guid)
    {
        await realtime.MantenimientoCambiadoAsync(guid, request.MantenimientoId, "Diario");
    }

    return Results.Ok(new { ok = true, id = request.MantenimientoId });
});

// ---------------------------------------------------------------------------
// POST /api/maintenance/{id}/cerrar — cierre manual desde la web. Pone el
// estado "Completado", escribe FechaFin = hora del clic y calcula la duración.
// Sirve para cerrar cualquier mantenimiento (programado, en ejecución…),
// p. ej. los que llevan la acción manual "Mantenimiento" que el agente no
// puede ejecutar.
// ---------------------------------------------------------------------------
app.MapPost("/api/maintenance/{id:int}/cerrar", async (
    int id,
    CerrarMantenimientoRequest request,
    MantenimientoRepository repo,
    RealtimeNotifier realtime) =>
{
    if (id <= 0)
    {
        return Results.BadRequest(new { error = "El identificador del mantenimiento no es válido." });
    }

    if (request.DetalleTecnico?.Length > 2000)
    {
        return Results.BadRequest(new { error = "El detalle técnico no puede superar los 2000 caracteres." });
    }

    var cerrado = await repo.Cerrar(id, request.DetalleTecnico);

    if (!cerrado)
    {
        return Results.NotFound(new
        {
            error = "No existe ningún mantenimiento abierto con ese identificador."
        });
    }

    var guidCierre = await repo.ObtenerGuidEquipo(id);
    if (guidCierre is Guid guid)
    {
        await realtime.MantenimientoCambiadoAsync(guid, id, "Completado", request.DetalleTecnico);
    }

    return Results.Ok(new { ok = true, id, estado = "Completado" });
});

// ---------------------------------------------------------------------------
// GET /api/maintenance/pending?guid=… — pendientes del agente. Reclama los
// Únicos vencidos a su hora de inicio (Estado → Ejecutando) y devuelve los
// debidos (Únicos + Diarios de su ciclo).
// ---------------------------------------------------------------------------
app.MapGet("/api/maintenance/pending", async (
    Guid? guid,
    MantenimientoRepository repo) =>
{
    if (guid is null || guid == Guid.Empty)
    {
        return Results.BadRequest(new { error = "El GUID del equipo es obligatorio." });
    }

    var pendientes = await repo.ObtenerPendientes(guid.Value);
    return Results.Ok(pendientes);
});

// ---------------------------------------------------------------------------
// POST /api/maintenance/interrumpidos — el agente lo llama al arrancar para
// que sus mantenimientos que siguen "Ejecutando" (la ejecución murió con el
// proceso al reiniciar) pasen a Error.
// ---------------------------------------------------------------------------
app.MapPost("/api/maintenance/interrumpidos", async (
    InterrumpidosRequest request,
    MantenimientoRepository repo,
    RealtimeNotifier realtime) =>
{
    if (request.Guid == Guid.Empty)
    {
        return Results.BadRequest(new { error = "El GUID del equipo es obligatorio." });
    }

    var marcados = await repo.MarcarInterrumpidos(request.Guid);

    // El repository devuelve solo el número de filas afectadas (no los ids):
    // se emite un evento sin id concreto para que la ficha se refresque.
    if (marcados > 0)
    {
        await realtime.MantenimientoCambiadoAsync(
            request.Guid, null, "Error", "Interrumpido por reinicio del equipo");
    }

    return Results.Ok(new { ok = true, guid = request.Guid, marcados });
});

// ---------------------------------------------------------------------------
// Tareas avanzadas — comandos de PowerShell programados desde la ficha.
//
// POST /api/advanced-tasks                    crea una tarea desde la web
// GET  /api/equipos/{id}/advanced-tasks       listado de la ficha
// POST /api/advanced-tasks/{id}/cancel        cancela una tarea Programada
// GET  /api/advanced-tasks/pending?guid=…     pendientes para el agente
// POST /api/advanced-tasks/report             resultado que envía el agente
// POST /api/advanced-tasks/interrupted        recuperación tras reinicio
// ---------------------------------------------------------------------------

app.MapPost("/api/advanced-tasks", async (
    ProgramarTareaAvanzadaRequest request,
    EquipoRepository equipos,
    TareasAvanzadasService tareas,
    RealtimeNotifier realtime) =>
{
    if (request.Guid == Guid.Empty)
    {
        return Results.BadRequest(new { error = "El GUID del equipo es obligatorio." });
    }

    if (request.FechaEjecucion == default)
    {
        return Results.BadRequest(new { error = "La fecha de ejecución es obligatoria." });
    }

    if (request.FechaEjecucion < DateTime.Now.AddMinutes(1))
    {
        return Results.BadRequest(new
        {
            error = "La fecha de ejecución debe ser al menos 1 minuto posterior a la hora actual."
        });
    }

    if (!TareasAvanzadasService.ComandoValido(request.Comando))
    {
        return Results.BadRequest(new
        {
            error = "El comando de PowerShell es obligatorio y no puede superar los 8000 caracteres."
        });
    }

    if (request.Descripcion?.Length > 200)
    {
        return Results.BadRequest(new { error = "La descripción no puede superar los 200 caracteres." });
    }

    var equipo = await equipos.ObtenerEquipoPorGuid(request.Guid);
    if (equipo is null)
    {
        return Results.NotFound(new { error = "No existe ningún equipo registrado con ese GUID." });
    }

    var tarea = tareas.Crear(request, equipo.Id, equipo.NombreEquipo);

    await realtime.TareaAvanzadaCambiadaAsync(request.Guid, tarea.Id, tarea.Estado);

    return Results.Created($"/api/advanced-tasks/{tarea.Id}", tarea);
});

app.MapGet("/api/equipos/{id:int}/advanced-tasks", async (
    int id,
    EquipoRepository equipos,
    TareasAvanzadasService tareas) =>
{
    if (id <= 0)
    {
        return Results.BadRequest(new { error = "El identificador del equipo no es válido." });
    }

    var equipo = await equipos.ObtenerEquipoPorId(id);
    if (equipo is null)
    {
        return Results.NotFound(new { error = "No existe ningún equipo con ese identificador." });
    }

    return Results.Ok(tareas.ObtenerPorEquipo(equipo.Guid));
});

app.MapPost("/api/advanced-tasks/{id:int}/cancel", async (
    int id,
    TareasAvanzadasService tareas,
    RealtimeNotifier realtime) =>
{
    if (id <= 0)
    {
        return Results.BadRequest(new { error = "El identificador de la tarea no es válido." });
    }

    var tarea = tareas.Cancelar(id);
    if (tarea is null)
    {
        return Results.NotFound(new
        {
            error = "No existe ninguna tarea programada con ese identificador."
        });
    }

    if (Guid.TryParse(tarea.EquipoGuid, out var guid))
    {
        await realtime.TareaAvanzadaCambiadaAsync(guid, id, "Cancelada");
    }

    return Results.Ok(new { ok = true, id, estado = "Cancelada" });
});

app.MapGet("/api/advanced-tasks/pending", async (
    Guid? guid,
    TareasAvanzadasService tareas,
    RealtimeNotifier realtime) =>
{
    if (guid is null || guid == Guid.Empty)
    {
        return Results.BadRequest(new { error = "El GUID del equipo es obligatorio." });
    }

    var pendientes = tareas.ObtenerPendientes(guid.Value, DateTime.Now);

    foreach (var pendiente in pendientes)
    {
        await realtime.TareaAvanzadaCambiadaAsync(guid.Value, pendiente.Id, "Ejecutando");
    }

    return Results.Ok(pendientes);
});

app.MapPost("/api/advanced-tasks/report", async (
    TareaAvanzadaReportRequest request,
    TareasAvanzadasService tareas,
    RealtimeNotifier realtime) =>
{
    if (request.Guid == Guid.Empty)
    {
        return Results.BadRequest(new { error = "El GUID del equipo es obligatorio." });
    }

    if (request.Id <= 0)
    {
        return Results.BadRequest(new { error = "El identificador de la tarea es obligatorio." });
    }

    if (request.FechaInicio == default || request.FechaFin == default)
    {
        return Results.BadRequest(new { error = "Las fechas de inicio y fin son obligatorias." });
    }

    if (request.DuracionSegundos < 0)
    {
        return Results.BadRequest(new { error = "La duración no puede ser negativa." });
    }

    var estado = request.Estado?.Trim() ?? "";
    if (!string.Equals(estado, "Completada", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(estado, "Error", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(estado, "Cancelada", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "El estado debe ser Completada, Error o Cancelada." });
    }

    if (!tareas.Reportar(request))
    {
        return Results.NotFound(new { error = "No existe ninguna tarea con ese identificador." });
    }

    await realtime.TareaAvanzadaCambiadaAsync(request.Guid, request.Id, estado, request.Error);

    return Results.Ok(new { ok = true, id = request.Id, estado });
});

app.MapPost("/api/advanced-tasks/interrupted", async (
    InterrumpidosRequest request,
    TareasAvanzadasService tareas,
    RealtimeNotifier realtime) =>
{
    if (request.Guid == Guid.Empty)
    {
        return Results.BadRequest(new { error = "El GUID del equipo es obligatorio." });
    }

    int marcadas = tareas.MarcarInterrumpidas(request.Guid);

    if (marcadas > 0)
    {
        await realtime.TareaAvanzadaCambiadaAsync(
            request.Guid, 0, "Error", "Interrumpida por reinicio del equipo");
    }

    return Results.Ok(new { ok = true, guid = request.Guid, marcadas });
});

// Health checks nativos de ASP.NET (usado por supervisores/infraestructura).
app.MapHealthChecks("/health");

// ---------------------------------------------------------------------------
// Hub de tiempo real: la web se conecta a /hub/tiemporeal y recibe los
// cambios de estado de mantenimientos y los heartbeats sin recargar.
// Los segmentos literales ("/hub/...") tienen prioridad sobre el
// MapFallback de abajo, no lo pisa.
// ---------------------------------------------------------------------------
app.MapHub<RealtimeHub>("/hub/tiemporeal");

// ---------------------------------------------------------------------------
// MapFallback — enrutado de la web por carpetas.
// Al no usar UseDefaultFiles/MapFallbackToFile, esta ruta decide a mano:
//   · /api/...  → 404 JSON (nunca HTML) para rutas de API inexistentes.
//   · Rutas con "..", "\" o ":" → 404 (protección contra path traversal).
//   · Cualquier otra → <carpeta>/index.html si existe (así /equipos/ y
//     /Software/ sirven su propio index.html en lugar del de la raíz).
// ---------------------------------------------------------------------------
app.MapFallback(async (HttpContext context) =>
{
    var path = context.Request.Path.Value?.Trim('/') ?? "";

    if (path.Equals("api", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
    {
        await Results.NotFound(new { error = "La ruta solicitada no existe." })
            .ExecuteAsync(context);
        return;
    }

    if (path.Contains("..") || path.Contains('\\') || path.Contains(':'))
    {
        await Results.NotFound().ExecuteAsync(context);
        return;
    }

    // Busca <carpeta>/index.html en wwwroot y lo sirve como HTML.
    var relative = string.IsNullOrEmpty(path) ? "index.html" : $"{path}/index.html";
    var environment = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
    var file = environment.WebRootFileProvider.GetFileInfo(relative);

    if (file.Exists && !file.IsDirectory && file.PhysicalPath is not null)
    {
        await Results.File(file.PhysicalPath, "text/html; charset=utf-8")
            .ExecuteAsync(context);
        return;
    }

    await Results.NotFound().ExecuteAsync(context);
});

// Rutas que se sirven sin sesión: el propio login, sus recursos y los
// endpoints que consumen los agentes instalados en los equipos.
static bool EsRutaPublica(string path) =>
    path.Equals("/login.html", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/css/login.css", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/js/login.js", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/api/login", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/api/logout", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/api/register", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/api/heartbeat", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/api/inventory", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/api/software", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/api/maintenance/report", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/api/maintenance/pending", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/api/maintenance/interrumpidos", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/api/advanced-tasks/pending", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/api/advanced-tasks/report", StringComparison.OrdinalIgnoreCase) ||
    path.Equals("/api/advanced-tasks/interrupted", StringComparison.OrdinalIgnoreCase);

// /api/... y /hub/... responden 401 en vez de redirigir al login, para que
// fetch() y SignalR reciban un error claro y no HTML.
static bool EsRutaApi(PathString path) =>
    path.StartsWithSegments("/api") || path.StartsWithSegments("/hub");

static bool CredencialesValidas(string? usuario, string? password)
{
    var usuarioValido = string.Equals(
        usuario?.Trim(), UsuarioPanel, StringComparison.OrdinalIgnoreCase);

    var passwordValida = CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(password ?? string.Empty),
        Encoding.UTF8.GetBytes(PasswordPanel));

    return usuarioValido && passwordValida;
}

// Arranca el servidor (Kestrel) con la configuración de appsettings.json
// y las variables ASPNETCORE_URLS / ASPNETCORE_ENVIRONMENT del entorno.
app.Run();

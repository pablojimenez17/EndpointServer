using Microsoft.Data.SqlClient;
using System.Data;
using Tradipres.EndpointServer.Models;
using Tradipres.EndpointServer.Services;

namespace Tradipres.EndpointServer.Repositories;

// Acceso a dbo.Mantenimientos.
//
// SIN cambios de esquema (la cuenta endpoint_app no tiene permisos DDL):
//  · Tipo "Unico"  → Estado del ciclo Programado→Ejecutando→Completado/Error.
//  · Tipo "Diario" → Estado="Diario" (definición recurrente activa). La hora
//    diaria de ejecución se toma de la HORA de FechaInicio; los días de la
//    semana, la vigencia (FechaFin) y la última ejecución (deduplicado) viven
//    en el fichero diarios.json (ver DiariosConfigService), no en la BD.
public class MantenimientoRepository
{
    private readonly IConfiguration _configuration;
    private readonly DiariosConfigService _diarios;
    private readonly OrdenAccionesService _ordenAcciones;
    private readonly RealtimeNotifier _realtime;

    public MantenimientoRepository(
        IConfiguration configuration,
        DiariosConfigService diarios,
        OrdenAccionesService ordenAcciones,
        RealtimeNotifier realtime)
    {
        _configuration = configuration;
        _diarios = diarios;
        _ordenAcciones = ordenAcciones;
        _realtime = realtime;
    }

    // -------------------------------------------------------------------
    // GUID del equipo dueño de un mantenimiento (para emitir los eventos
    // de tiempo real desde endpoints que solo reciben el id).
    // -------------------------------------------------------------------
    public async Task<Guid?> ObtenerGuidEquipo(int mantenimientoId)
    {
        var conexion = ObtenerCadenaConexion();

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();

        const string sql = """
        SELECT e.Guid
        FROM dbo.Mantenimientos m
        INNER JOIN dbo.Equipos e ON e.Id = m.EquipoId
        WHERE m.Id = @Id;
        """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = mantenimientoId;

        var resultado = await cmd.ExecuteScalarAsync();
        return resultado is Guid guid ? guid : null;
    }

    // -------------------------------------------------------------------
    // Reporte del agente: si MantenimientoId viene informado, actualiza esa
    // fila (Único reclamado); si no, inserta una fila nueva (ejecución de un
    // Diario o reporte suelto).
    // -------------------------------------------------------------------
    public async Task<int?> GuardarMantenimiento(MaintenanceReportRequest request)
    {
        var conexion = ObtenerCadenaConexion();

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();

        if (request.MantenimientoId is int idExistente)
        {
            const string sqlUpdate = """
            DECLARE @EquipoId INT;

            SELECT @EquipoId = Id
            FROM dbo.Equipos
            WHERE Guid = @Guid;

            UPDATE dbo.Mantenimientos
            SET Estado = @Estado,
                FechaInicio = @FechaInicio,
                FechaFin = @FechaFin,
                DuracionSegundos = @DuracionSegundos,
                CopiaOutlook = @CopiaOutlook,
                Mantenimiento = @Mantenimiento,
                ReparacionWindows = @ReparacionWindows,
                ApagarEquipo = @ApagarEquipo,
                Mensaje = @Mensaje,
                DetalleTecnico = @DetalleTecnico
            WHERE Id = @Id
              AND EquipoId = @EquipoId;

            SELECT CASE WHEN @@ROWCOUNT > 0 THEN @Id ELSE CAST(NULL AS INT) END;
            """;

            await using var cmd = new SqlCommand(sqlUpdate, cn);
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = idExistente;
            AgregarParametrosReporte(cmd, request);

            var resultado = await cmd.ExecuteScalarAsync();
            return resultado is null || resultado == DBNull.Value
                ? null
                : Convert.ToInt32(resultado);
        }

        const string sqlInsert = """
        DECLARE @EquipoId INT;

        SELECT @EquipoId = Id
        FROM dbo.Equipos
        WHERE Guid = @Guid;

        IF @EquipoId IS NULL
        BEGIN
            SELECT CAST(NULL AS INT);
            RETURN;
        END;

        INSERT INTO dbo.Mantenimientos
        (
            EquipoId,
            FechaInicio,
            FechaFin,
            Estado,
            DuracionSegundos,
            CopiaOutlook,
            Mantenimiento,
            ReparacionWindows,
            ApagarEquipo,
            Mensaje,
            DetalleTecnico
        )
        OUTPUT INSERTED.Id
        VALUES
        (
            @EquipoId,
            @FechaInicio,
            @FechaFin,
            @Estado,
            @DuracionSegundos,
            @CopiaOutlook,
            @Mantenimiento,
            @ReparacionWindows,
            @ApagarEquipo,
            @Mensaje,
            @DetalleTecnico
        );
        """;

        await using var cmdInsert = new SqlCommand(sqlInsert, cn);
        AgregarParametrosReporte(cmdInsert, request);

        var resultadoInsert = await cmdInsert.ExecuteScalarAsync();

        return resultadoInsert is null || resultadoInsert == DBNull.Value
            ? null
            : Convert.ToInt32(resultadoInsert);
    }

    // -------------------------------------------------------------------
    // Historial de mantenimientos de un equipo (pestaña de la ficha).
    // -------------------------------------------------------------------
    public async Task<List<MantenimientoDetalle>> ObtenerMantenimientos(int equipoId)
    {
        var lista = new List<MantenimientoDetalle>();
        var conexion = ObtenerCadenaConexion();

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();

        const string sql = """
        SELECT
            Id, FechaInicio, FechaFin, Estado, DuracionSegundos,
            CopiaOutlook, Mantenimiento, ReparacionWindows, ApagarEquipo,
            Mensaje, DetalleTecnico, FechaRecepcion
        FROM dbo.Mantenimientos
        WHERE EquipoId = @EquipoId
        ORDER BY FechaInicio DESC, Id DESC;
        """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@EquipoId", SqlDbType.Int).Value = equipoId;

        // Los Diarios exponen días y vigencia desde diarios.json.
        var configsDiarios = _diarios.ObtenerTodos()
            .ToDictionary(c => c.MantenimientoId);

        await using var dr = await cmd.ExecuteReaderAsync();
        while (await dr.ReadAsync())
        {
            var detalle = new MantenimientoDetalle
            {
                Id = dr.GetInt32(0),
                FechaInicio = dr.GetDateTime(1),
                FechaFin = dr.GetDateTime(2),
                Estado = LeerTexto(dr, 3),
                DuracionSegundos = dr.GetInt32(4),
                CopiaOutlook = dr.GetBoolean(5),
                Mantenimiento = dr.GetBoolean(6),
                ReparacionWindows = dr.GetBoolean(7),
                ApagarEquipo = dr.GetBoolean(8),
                Mensaje = LeerTextoNulo(dr, 9),
                DetalleTecnico = LeerTextoNulo(dr, 10),
                FechaRecepcion = dr.GetDateTime(11)
            };

            // Un Diario expone sus días y su vigencia para la web.
            if (EsDiario(detalle.Estado) &&
                configsDiarios.TryGetValue(detalle.Id, out var config))
            {
                detalle.Tipo = "Diario";
                detalle.Dias = config.Dias;
                detalle.VigenciaFin = config.FechaFin;
                detalle.DetalleTecnico = null;
            }

            lista.Add(detalle);
        }

        return lista;
    }

    // -------------------------------------------------------------------
    // GET /api/maintenance — todos los mantenimientos de TODOS los equipos
    // (página /Mantenimientos/, solo visualización). JOIN con dbo.Equipos
    // para devolver nombre, GUID y estado del equipo junto a cada tarea.
    // -------------------------------------------------------------------
    public async Task<List<MantenimientoConEquipo>> ObtenerTodos()
    {
        var lista = new List<MantenimientoConEquipo>();
        var conexion = ObtenerCadenaConexion();

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();

        const string sql = """
        SELECT
            m.Id, m.EquipoId, m.FechaInicio, m.FechaFin, m.Estado, m.DuracionSegundos,
            m.CopiaOutlook, m.Mantenimiento, m.ReparacionWindows, m.ApagarEquipo,
            m.Mensaje, m.DetalleTecnico, m.FechaRecepcion,
            e.NombreEquipo, e.Estado AS EstadoEquipo
        FROM dbo.Mantenimientos m
        INNER JOIN dbo.Equipos e ON e.Id = m.EquipoId
        ORDER BY m.FechaInicio DESC, m.Id DESC;
        """;

        await using var cmd = new SqlCommand(sql, cn);

        var configsDiarios = _diarios.ObtenerTodos()
            .ToDictionary(c => c.MantenimientoId);

        await using var dr = await cmd.ExecuteReaderAsync();
        while (await dr.ReadAsync())
        {
            var item = new MantenimientoConEquipo
            {
                Id = dr.GetInt32(0),
                EquipoId = dr.GetInt32(1),
                FechaInicio = dr.GetDateTime(2),
                FechaFin = dr.GetDateTime(3),
                Estado = LeerTexto(dr, 4),
                DuracionSegundos = dr.GetInt32(5),
                CopiaOutlook = dr.GetBoolean(6),
                Mantenimiento = dr.GetBoolean(7),
                ReparacionWindows = dr.GetBoolean(8),
                ApagarEquipo = dr.GetBoolean(9),
                Mensaje = LeerTextoNulo(dr, 10),
                DetalleTecnico = LeerTextoNulo(dr, 11),
                FechaRecepcion = dr.GetDateTime(12),
                NombreEquipo = LeerTexto(dr, 13),
                EstadoEquipo = LeerTexto(dr, 14)
            };

            if (EsDiario(item.Estado) &&
                configsDiarios.TryGetValue(item.Id, out var config))
            {
                item.Tipo = "Diario";
                item.Dias = config.Dias;
                item.VigenciaFin = config.FechaFin;
                item.DetalleTecnico = null;
            }

            lista.Add(item);
        }

        return lista;
    }

    // -------------------------------------------------------------------
    // Programación desde la web.
    //  · Unico  → Estado="Programado". NO se pide fin: la columna NOT NULL
    //    se rellena con FechaInicio como provisional; el fin real lo escribe
    //    quien cierre el mantenimiento (agente o botón "Cerrar" de la web).
    //  · Diario → Estado="Diario", y la recurrencia (días, vigencia y
    //    última ejecución) se guarda en diarios.json (sin tocar la BD).
    // -------------------------------------------------------------------
    public async Task<int?> Programar(ProgramarMantenimientoRequest request)
    {
        var conexion = ObtenerCadenaConexion();
        bool esDiario = EsTipoDiario(request.Tipo);

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();

        string estado = esDiario ? "Diario" : "Programado";
        // En ambos tipos la columna NOT NULL se rellena con FechaInicio como
        // provisional: para el Unico hasta que alguien lo cierre; para el
        // Diario la vigencia real vive en diarios.json.
        DateTime fechaFinProvisional = request.FechaInicio;

        const string sql = """
        DECLARE @EquipoId INT;

        SELECT @EquipoId = Id
        FROM dbo.Equipos
        WHERE Guid = @Guid;

        IF @EquipoId IS NULL
        BEGIN
            SELECT CAST(NULL AS INT);
            RETURN;
        END;

        INSERT INTO dbo.Mantenimientos
        (
            EquipoId,
            FechaInicio,
            FechaFin,
            Estado,
            DuracionSegundos,
            CopiaOutlook,
            Mantenimiento,
            ReparacionWindows,
            ApagarEquipo,
            Mensaje,
            DetalleTecnico
        )
        OUTPUT INSERTED.Id, @EquipoId
        VALUES
        (
            @EquipoId,
            @FechaInicio,
            @FechaFin,
            @Estado,
            0,
            @CopiaOutlook,
            @Mantenimiento,
            @ReparacionWindows,
            @ApagarEquipo,
            @Mensaje,
            NULL
        );
        """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Guid", SqlDbType.UniqueIdentifier).Value = request.Guid;
        cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime2).Value = request.FechaInicio;
        cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime2).Value = fechaFinProvisional;
        cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 30).Value = estado;
        cmd.Parameters.Add("@CopiaOutlook", SqlDbType.Bit).Value = request.CopiaOutlook;
        cmd.Parameters.Add("@Mantenimiento", SqlDbType.Bit).Value = request.Mantenimiento;
        cmd.Parameters.Add("@ReparacionWindows", SqlDbType.Bit).Value = request.ReparacionWindows;
        cmd.Parameters.Add("@ApagarEquipo", SqlDbType.Bit).Value = request.ApagarEquipo;
        cmd.Parameters.Add("@Mensaje", SqlDbType.NVarChar, 500).Value =
            string.IsNullOrWhiteSpace(request.Mensaje) ? DBNull.Value : request.Mensaje.Trim();

        int? mantenimientoId = null;
        int? equipoId = null;

        await using (var dr = await cmd.ExecuteReaderAsync())
        {
            if (await dr.ReadAsync())
            {
                if (!dr.IsDBNull(0)) mantenimientoId = dr.GetInt32(0);
                if (!dr.IsDBNull(1)) equipoId = dr.GetInt32(1);
            }
        }

        // Un Diario registra además su recurrencia en diarios.json.
        if (esDiario && mantenimientoId is int idDiario && equipoId is int equipo)
        {
            _diarios.Upsert(new DiarioConfig
            {
                MantenimientoId = idDiario,
                EquipoId = equipo,
                EquipoGuid = request.Guid.ToString("D"),
                Dias = (request.Dias ?? []).Distinct().OrderBy(d => d).ToList(),
                FechaInicio = request.FechaInicio,
                FechaFin = request.FechaFin,
                UltimaEjecucion = null,
                Activo = true
            });
        }

        // Guardar el orden de acciones (prioridad) en JSON si se envió.
        if (mantenimientoId is int idConOrden && request.OrdenAcciones is { Count: > 0 })
        {
            _ordenAcciones.Guardar(idConOrden, request.OrdenAcciones);
        }

        return mantenimientoId;
    }

    // Cancela un Programado o desactiva un Diario (los dos pasan a Cancelado
    // y se borra su configuración de recurrencia).
    public async Task<bool> Cancelar(int mantenimientoId)
    {
        var conexion = ObtenerCadenaConexion();

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();

        const string sql = """
        UPDATE dbo.Mantenimientos
        SET Estado = 'Cancelado'
        WHERE Id = @Id
          AND Estado IN ('Programado', 'Diario');
        """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = mantenimientoId;

        bool cancelado = await cmd.ExecuteNonQueryAsync() > 0;

        if (cancelado)
        {
            _diarios.Eliminar(mantenimientoId);
            _ordenAcciones.Eliminar(mantenimientoId);
        }

        return cancelado;
    }

    // -------------------------------------------------------------------
    // Edición de un Diario: actualiza la BD (acciones, mensaje, hora),
    // diarios.json (días, vigencia) y orden_acciones.json (prioridad).
    // Devuelve false si el mantenimiento no existe o no es Diario.
    // -------------------------------------------------------------------
    public async Task<bool> EditarDiario(EditarDiarioRequest request)
    {
        var conexion = ObtenerCadenaConexion();

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();

        // 1) Actualizar la fila en la BD: acciones, mensaje y hora de inicio.
        //    La fecha de FechaInicio se mantiene (solo cambia la hora).
        const string sql = """
        UPDATE dbo.Mantenimientos
        SET CopiaOutlook = @CopiaOutlook,
            Mantenimiento = @Mantenimiento,
            ReparacionWindows = @ReparacionWindows,
            ApagarEquipo = @ApagarEquipo,
            Mensaje = @Mensaje,
            FechaInicio = DATEADD(DAY, DATEDIFF(DAY, FechaInicio, @NuevaFecha), @NuevaFecha)
        WHERE Id = @Id
          AND Estado = 'Diario';
        """;

        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@Id", SqlDbType.Int).Value = request.MantenimientoId;
            cmd.Parameters.Add("@CopiaOutlook", SqlDbType.Bit).Value = request.CopiaOutlook;
            cmd.Parameters.Add("@Mantenimiento", SqlDbType.Bit).Value = request.Mantenimiento;
            cmd.Parameters.Add("@ReparacionWindows", SqlDbType.Bit).Value = request.ReparacionWindows;
            cmd.Parameters.Add("@ApagarEquipo", SqlDbType.Bit).Value = request.ApagarEquipo;
            cmd.Parameters.Add("@Mensaje", SqlDbType.NVarChar, 500).Value =
                string.IsNullOrWhiteSpace(request.Mensaje) ? DBNull.Value : request.Mensaje.Trim();
            cmd.Parameters.Add("@NuevaFecha", SqlDbType.DateTime2).Value = request.FechaInicio;

            int affected = await cmd.ExecuteNonQueryAsync();
            if (affected == 0) return false;
        }

        // 2) Actualizar diarios.json: días, hora y vigencia.
        var config = _diarios.ObtenerPorMantenimiento(request.MantenimientoId);
        if (config != null)
        {
            config.Dias = (request.Dias ?? []).Distinct().OrderBy(d => d).ToList();
            config.FechaInicio = request.FechaInicio;
            config.FechaFin = request.FechaFin;
            config.Activo = true;
            _diarios.Upsert(config);
        }

        // 3) Actualizar orden_acciones.json.
        _ordenAcciones.Eliminar(request.MantenimientoId);
        if (request.OrdenAcciones is { Count: > 0 })
        {
            _ordenAcciones.Guardar(request.MantenimientoId, request.OrdenAcciones);
        }

        return true;
    }

    // -------------------------------------------------------------------
    // Recuperación tras reinicio (POST /api/maintenance/interrumpidos).
    // El agente lo llama al arrancar: sus mantenimientos que siguen en
    // "Ejecutando" se marcan Error (la ejecución murió con el proceso).
    // Solo afecta a los Unicos: un Diario nunca pasa a "Ejecutando" (su
    // definición permanece en estado "Diario" y cada ejecución es una fila
    // nueva insertada al terminar), así que un reinicio a mitad de un
    // Diario simplemente deja esa ejecución sin registrar y el próximo día
    // compatible volverá a ejecutarlo.
    // Devuelve cuántos se han marcado.
    // -------------------------------------------------------------------
    public async Task<int> MarcarInterrumpidos(Guid guid)
    {
        var conexion = ObtenerCadenaConexion();

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();

        const string sql = """
        UPDATE m
        SET m.Estado = 'Error',
            m.DetalleTecnico = 'Interrumpido por reinicio del equipo',
            m.FechaFin = @Ahora,
            m.DuracionSegundos = CASE
                WHEN DATEDIFF(SECOND, m.FechaInicio, @Ahora) > 0
                THEN DATEDIFF(SECOND, m.FechaInicio, @Ahora)
                ELSE 0
            END
        FROM dbo.Mantenimientos m
        INNER JOIN dbo.Equipos e ON e.Id = m.EquipoId
        WHERE e.Guid = @Guid
          AND m.Estado = 'Ejecutando';
        """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Guid", SqlDbType.UniqueIdentifier).Value = guid;
        cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = DateTime.Now;

        return await cmd.ExecuteNonQueryAsync();
    }

    // -------------------------------------------------------------------
    // Cierre manual desde la web (POST /api/maintenance/{id}/cerrar).
    // Pone Estado="Completado", FechaFin=hora del clic y calcula la duración.
    // Válido para cualquier estado salvo los ya terminales "Completado",
    // "Cerrado" (legado) y "Cancelado". Si era un Diario, se borra su
    // configuración de recurrencia.
    // -------------------------------------------------------------------
    public async Task<bool> Cerrar(int mantenimientoId, string? detalleTecnico)
    {
        var conexion = ObtenerCadenaConexion();

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();

        const string sql = """
        UPDATE dbo.Mantenimientos
        SET Estado = 'Completado',
            FechaFin = @Ahora,
            DuracionSegundos = CASE
                WHEN DATEDIFF(SECOND, FechaInicio, @Ahora) > 0
                THEN DATEDIFF(SECOND, FechaInicio, @Ahora)
                ELSE 0
            END,
            DetalleTecnico = COALESCE(@DetalleTecnico, DetalleTecnico)
        WHERE Id = @Id
          AND Estado NOT IN ('Completado', 'Cerrado', 'Cancelado');
        """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = mantenimientoId;
        cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = DateTime.Now;
        cmd.Parameters.Add("@DetalleTecnico", SqlDbType.NVarChar, -1).Value =
            string.IsNullOrWhiteSpace(detalleTecnico) ? DBNull.Value : detalleTecnico.Trim();

        bool cerrado = await cmd.ExecuteNonQueryAsync() > 0;

        if (cerrado)
        {
            _diarios.Eliminar(mantenimientoId);
            _ordenAcciones.Eliminar(mantenimientoId);
        }

        return cerrado;
    }

    // -------------------------------------------------------------------
    // Pendientes para el agente (GET /api/maintenance/pending).
    //
    // Reglas:
    //  1. Unicos: se reclaman en cuanto pasa su FechaInicio (Estado →
    //    "Ejecutando"). NO hay ventana de fin ni estado "Vencido": el
    //    fin real lo escribe quien cierre (agente o botón "Cerrar").
    //  2. Diarios: el servidor decide. Solo se entrega un Diario cuando
    //    coinciden todas las condiciones (ya empezó, hoy es día seleccionado,
    //    ya pasó la hora de inicio, no se ha ejecutado hoy y no se ha
    //    cumplido la vigencia). Al entregarlo se marca la ejecución de hoy
    //    en diarios.json para no duplicar; si la vigencia ya pasó, se cierra
    //    el mantenimiento (estado "Cerrado").
    // -------------------------------------------------------------------
    public async Task<List<MaintenancePendingItem>> ObtenerPendientes(Guid guid)
    {
        var pendientes = new List<MaintenancePendingItem>();
        var conexion = ObtenerCadenaConexion();
        DateTime ahora = DateTime.Now;
        DateTime hoy = ahora.Date;

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();

        // Unicos pendientes: desde que toca su FechaInicio hasta que alguien
        // los cierra o cancela.
        const string sqlUnicos = """
        SELECT
            m.Id, m.FechaInicio,
            m.CopiaOutlook, m.Mantenimiento, m.ReparacionWindows, m.ApagarEquipo,
            m.Mensaje
        FROM dbo.Mantenimientos m
        INNER JOIN dbo.Equipos e ON e.Id = m.EquipoId
        WHERE e.Guid = @Guid
          AND m.Estado = 'Programado'
          AND m.FechaInicio <= @Ahora
        ORDER BY m.FechaInicio;
        """;

        await using (var cmd = new SqlCommand(sqlUnicos, cn))
        {
            cmd.Parameters.Add("@Guid", SqlDbType.UniqueIdentifier).Value = guid;
            cmd.Parameters.Add("@Ahora", SqlDbType.DateTime2).Value = ahora;

            await using var dr = await cmd.ExecuteReaderAsync();
            while (await dr.ReadAsync())
            {
                int pendId = dr.GetInt32(0);
                pendientes.Add(new MaintenancePendingItem
                {
                    Id = pendId,
                    Tipo = "Unico",
                    FechaInicio = dr.GetDateTime(1),
                    // Sin ventana de fin: el cierre real lo escribe quien
                    // finalice el mantenimiento (agente o botón "Cerrar").
                    FechaLimite = null,
                    CopiaOutlook = dr.GetBoolean(2),
                    Mantenimiento = dr.GetBoolean(3),
                    ReparacionWindows = dr.GetBoolean(4),
                    ApagarEquipo = dr.GetBoolean(5),
                    OrdenAcciones = _ordenAcciones.Obtener(pendId),
                    Mensaje = LeerTextoNulo(dr, 6)
                });
            }
        }

        if (pendientes.Count > 0)
        {
            await using var cmd = new SqlCommand("""
                UPDATE dbo.Mantenimientos
                SET Estado = 'Ejecutando',
                    DetalleTecnico = @Nota
                WHERE Id IN ({0}) AND Estado = 'Programado';
                """.Replace("{0}", string.Join(",", pendientes.Select(p => p.Id))), cn);

            cmd.Parameters.Add("@Nota", SqlDbType.NVarChar, -1).Value =
                $"Reclamado por el agente el {ahora:dd/MM/yyyy HH:mm:ss}";
            await cmd.ExecuteNonQueryAsync();

            // Tiempo real: la web pasa a ver "Ejecutando" sin recargar.
            // Solo los que estaban "Programado" (el UPDATE puede no tocar a
            // ninguno si otro agente los reclamó entre medias): se emite
            // igualmente porque la lista devuelta es la que se va a ejecutar.
            foreach (var reclamado in pendientes)
            {
                await _realtime.MantenimientoCambiadoAsync(
                    guid, reclamado.Id, "Ejecutando",
                    $"Reclamado por el agente el {ahora:dd/MM/yyyy HH:mm:ss}");
            }
        }

        // Diarios: la definición está en la BD y la recurrencia en
        // diarios.json. El servidor filtra aquí (días, hora, vigencia y
        // deduplicado) y solo entrega los que tocan hoy.
        var configsActivas = _diarios.ObtenerActivosPorEquipo(guid)
            .ToDictionary(c => c.MantenimientoId);

        const string sqlDiarios = """
        SELECT
            m.Id, m.FechaInicio,
            m.CopiaOutlook, m.Mantenimiento, m.ReparacionWindows, m.ApagarEquipo,
            m.Mensaje
        FROM dbo.Mantenimientos m
        INNER JOIN dbo.Equipos e ON e.Id = m.EquipoId
        WHERE e.Guid = @Guid
          AND m.Estado = 'Diario';
        """;

        var diariosDebidos = new List<MaintenancePendingItem>();
        // Vigencias cumplidas, que se cierran DESPUÉS de cerrar el reader
        // (no se puede ejecutar un UPDATE en la misma conexión mientras el
        // DataReader sigue abierto).
        var diariosVencidos = new List<(int Id, DateTime FechaFin)>();

        await using (var cmd = new SqlCommand(sqlDiarios, cn))
        {
            cmd.Parameters.Add("@Guid", SqlDbType.UniqueIdentifier).Value = guid;

            await using var dr = await cmd.ExecuteReaderAsync();
            while (await dr.ReadAsync())
            {
                int id = dr.GetInt32(0);

                // Sin configuración de recurrencia (o desactivado) → no se ejecuta.
                if (!configsActivas.TryGetValue(id, out var config))
                {
                    continue;
                }

                // Vigencia cumplida → se cierra el mantenimiento y se deja de
                // ejecutar. La fecha de cierre es la vigencia pedida.
                if (config.FechaFin.HasValue && hoy > config.FechaFin.Value.Date)
                {
                    diariosVencidos.Add((id, config.FechaFin.Value));
                    continue;
                }

                // Aún no ha llegado su primer día.
                if (hoy < config.FechaInicio.Date)
                {
                    continue;
                }

                // Hoy no es uno de los días seleccionados.
                if (!config.Dias.Contains((int)ahora.DayOfWeek))
                {
                    continue;
                }

                // Todavía no es la hora de ejecución de hoy (la hora se toma
                // de la HORA de FechaInicio).
                if (ahora.TimeOfDay < config.FechaInicio.TimeOfDay)
                {
                    continue;
                }

                // Ya se ejecutó hoy (evita duplicados aunque el agente pete
                // o se reinicie a mitad del día).
                if (config.UltimaEjecucion.HasValue &&
                    config.UltimaEjecucion.Value.Date == hoy)
                {
                    continue;
                }

                diariosDebidos.Add(new MaintenancePendingItem
                {
                    Id = id,
                    Tipo = "Diario",
                    // El agente usa la hora de inicio; la fecha completa solo
                    // marca el arranque de la programación.
                    FechaInicio = config.FechaInicio,
                    FechaLimite = config.FechaFin,
                    CopiaOutlook = dr.GetBoolean(2),
                    Mantenimiento = dr.GetBoolean(3),
                    ReparacionWindows = dr.GetBoolean(4),
                    ApagarEquipo = dr.GetBoolean(5),
                    OrdenAcciones = _ordenAcciones.Obtener(id),
                    Mensaje = LeerTextoNulo(dr, 6)
                });
            }
        }

        // Cerrar los diarios vencidos (reader ya liberado) y desactivarlos.
        foreach (var (idVencido, fechaFinVencida) in diariosVencidos)
        {
            await CerrarDiarioVencidoAsync(cn, idVencido, fechaFinVencida);
            _diarios.Desactivar(idVencido);
            await _realtime.MantenimientoCambiadoAsync(
                guid, idVencido, "Completado", "Vigencia finalizada automáticamente");
        }

        // Reclamar los diarios debidos: marcar la ejecución de hoy en el JSON
        // ANTES de devolverlos, para que un segundo ping no los duplique.
        foreach (var diario in diariosDebidos)
        {
            _diarios.MarcarEjecucion(diario.Id, ahora);
            pendientes.Add(diario);
            // El Diario sigue en estado "Diario" (la fila de la ejecución de
            // hoy la crea el reporte del agente); el evento sirve para que la
            // ficha se refresque en el momento en que empieza a ejecutarse.
            await _realtime.MantenimientoCambiadoAsync(
                guid, diario.Id, "Diario", "Ejecución de hoy en curso");
        }

        return pendientes;
    }

    // -------------------------------------------------------------------
    // Auxiliares.
    // -------------------------------------------------------------------

    // Cierra un Diario cuya vigencia ya pasó (estado "Completado", con la
    // fecha de fin pedida y sin duración, al ser una recurrencia).
    private static async Task CerrarDiarioVencidoAsync(
        SqlConnection cn, int id, DateTime fechaFin)
    {
        const string sql = """
        UPDATE dbo.Mantenimientos
        SET Estado = 'Completado',
            FechaFin = @FechaFin,
            DuracionSegundos = 0,
            DetalleTecnico = 'Vigencia finalizada automáticamente'
        WHERE Id = @Id
          AND Estado = 'Diario';
        """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add("@Id", SqlDbType.Int).Value = id;
        cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime2).Value = fechaFin;
        await cmd.ExecuteNonQueryAsync();
    }

    private static bool EsDiario(string estado) =>
        string.Equals(estado?.Trim(), "Diario", StringComparison.OrdinalIgnoreCase);

    private static bool EsTipoDiario(string? tipo) =>
        string.Equals(tipo?.Trim(), "Diario", StringComparison.OrdinalIgnoreCase);

    private void AgregarParametrosReporte(SqlCommand cmd, MaintenanceReportRequest request)
    {
        cmd.Parameters.Add("@Guid", SqlDbType.UniqueIdentifier).Value = request.Guid;
        cmd.Parameters.Add("@FechaInicio", SqlDbType.DateTime2).Value = request.FechaInicio;
        cmd.Parameters.Add("@FechaFin", SqlDbType.DateTime2).Value = request.FechaFin;
        cmd.Parameters.Add("@Estado", SqlDbType.NVarChar, 30).Value = request.Estado.Trim();
        cmd.Parameters.Add("@DuracionSegundos", SqlDbType.Int).Value = request.DuracionSegundos;
        cmd.Parameters.Add("@CopiaOutlook", SqlDbType.Bit).Value = request.CopiaOutlook;
        cmd.Parameters.Add("@Mantenimiento", SqlDbType.Bit).Value = request.Mantenimiento;
        cmd.Parameters.Add("@ReparacionWindows", SqlDbType.Bit).Value = request.ReparacionWindows;
        cmd.Parameters.Add("@ApagarEquipo", SqlDbType.Bit).Value = request.ApagarEquipo;
        cmd.Parameters.Add("@Mensaje", SqlDbType.NVarChar, 500).Value =
            string.IsNullOrWhiteSpace(request.Mensaje) ? DBNull.Value : request.Mensaje.Trim();
        cmd.Parameters.Add("@DetalleTecnico", SqlDbType.NVarChar, -1).Value =
            string.IsNullOrWhiteSpace(request.DetalleTecnico) ? DBNull.Value : request.DetalleTecnico.Trim();
    }

    private string ObtenerCadenaConexion()
    {
        var conexion = _configuration.GetConnectionString("EndpointManager");

        if (string.IsNullOrWhiteSpace(conexion))
        {
            throw new InvalidOperationException(
                "No se ha configurado la conexión EndpointManager.");
        }

        return conexion;
    }

    private static string LeerTexto(SqlDataReader dr, int ordinal) =>
        dr.IsDBNull(ordinal) ? "" : dr.GetString(ordinal);

    private static string? LeerTextoNulo(SqlDataReader dr, int ordinal) =>
        dr.IsDBNull(ordinal) ? null : dr.GetString(ordinal);
}

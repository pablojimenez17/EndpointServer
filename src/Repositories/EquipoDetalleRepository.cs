using Microsoft.Data.SqlClient;
using Tradipres.EndpointServer.Models;

namespace Tradipres.EndpointServer.Repositories;

public class EquipoDetalleRepository
{
    private readonly IConfiguration _configuration;

    public EquipoDetalleRepository(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<EquipoDetalle?> ObtenerDetalle(int equipoId)
    {
        var conexion = ObtenerCadenaConexion();

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();

        const string sql = """
        SELECT
            Id, Guid, NombreEquipo, Usuario, SistemaOperativo,
            VersionSO, Dominio, DireccionIP, VersionAgente,
            FechaRegistro, UltimoHeartbeat,
            CASE
                WHEN UltimoHeartbeat < DATEADD(MINUTE, -3, SYSDATETIME())
                    THEN 'Offline'
                ELSE Estado
            END AS Estado
        FROM dbo.Equipos
        WHERE Id = @EquipoId;

        SELECT
            Fabricante, Modelo, NumeroSerie, Procesador, Nucleos,
            MemoriaRamGB, Arquitectura, VersionWindows, FechaInstalacion,
            IpPrincipal, MacPrincipal, FechaInventario
        FROM dbo.InventarioEquipos
        WHERE EquipoId = @EquipoId;

        SELECT
            Unidad, Etiqueta, SistemaArchivos, TipoUnidad,
            TamanoBytes, LibreBytes
        FROM dbo.InventarioDiscos
        WHERE EquipoId = @EquipoId
        ORDER BY Unidad;

        SELECT TOP (1)
            Id, FechaInicio, FechaFin, Estado, DuracionSegundos,
            CopiaOutlook, Mantenimiento, ReparacionWindows, ApagarEquipo,
            Mensaje, DetalleTecnico, FechaRecepcion
        FROM dbo.Mantenimientos
        WHERE EquipoId = @EquipoId
        ORDER BY FechaInicio DESC, Id DESC;
        """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@EquipoId", equipoId);

        await using var dr = await cmd.ExecuteReaderAsync();

        if (!await dr.ReadAsync())
        {
            return null;
        }

        var detalle = new EquipoDetalle
        {
            Equipo = LeerEquipo(dr)
        };

        if (await dr.NextResultAsync() && await dr.ReadAsync())
        {
            detalle.Inventario = new InventarioEquipoDetalle
            {
                Fabricante = LeerTexto(dr, 0),
                Modelo = LeerTexto(dr, 1),
                NumeroSerie = LeerTexto(dr, 2),
                Procesador = LeerTexto(dr, 3),
                Nucleos = dr.IsDBNull(4) ? 0 : dr.GetInt32(4),
                MemoriaRamGB = dr.IsDBNull(5) ? 0 : dr.GetDecimal(5),
                Arquitectura = LeerTexto(dr, 6),
                VersionWindows = LeerTexto(dr, 7),
                FechaInstalacion = dr.IsDBNull(8) ? null : dr.GetDateTime(8),
                IpPrincipal = LeerTexto(dr, 9),
                MacPrincipal = LeerTexto(dr, 10),
                FechaInventario = dr.GetDateTime(11)
            };
        }

        if (await dr.NextResultAsync())
        {
            while (await dr.ReadAsync())
            {
                detalle.Discos.Add(new DiscoDetalle
                {
                    Unidad = LeerTexto(dr, 0),
                    Etiqueta = LeerTextoNulo(dr, 1),
                    SistemaArchivos = LeerTextoNulo(dr, 2),
                    TipoUnidad = LeerTextoNulo(dr, 3),
                    TamanoBytes = dr.GetInt64(4),
                    LibreBytes = dr.GetInt64(5)
                });
            }
        }

        if (await dr.NextResultAsync() && await dr.ReadAsync())
        {
            detalle.UltimoMantenimiento = new MantenimientoDetalle
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
        }

        return detalle;
    }

    private string ObtenerCadenaConexion()
    {
        var conexion = _configuration.GetConnectionString("EndpointManager");

        if (string.IsNullOrWhiteSpace(conexion))
        {
            throw new InvalidOperationException("No se ha configurado la conexión EndpointManager.");
        }

        return conexion;
    }

    private static Equipo LeerEquipo(SqlDataReader dr)
    {
        return new Equipo
        {
            Id = dr.GetInt32(0),
            Guid = dr.GetGuid(1),
            NombreEquipo = dr.GetString(2),
            Usuario = LeerTexto(dr, 3),
            SistemaOperativo = LeerTexto(dr, 4),
            VersionSO = LeerTexto(dr, 5),
            Dominio = LeerTexto(dr, 6),
            DireccionIP = LeerTexto(dr, 7),
            VersionAgente = dr.GetString(8),
            FechaRegistro = dr.GetDateTime(9),
            UltimoHeartbeat = dr.GetDateTime(10),
            Estado = dr.GetString(11)
        };
    }

    private static string LeerTexto(SqlDataReader dr, int ordinal) =>
        dr.IsDBNull(ordinal) ? "" : dr.GetString(ordinal);

    private static string? LeerTextoNulo(SqlDataReader dr, int ordinal) =>
        dr.IsDBNull(ordinal) ? null : dr.GetString(ordinal);
}

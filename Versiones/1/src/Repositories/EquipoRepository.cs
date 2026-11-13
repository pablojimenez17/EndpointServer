using Microsoft.Data.SqlClient;
using Tradipres.EndpointServer.Models;

namespace Tradipres.EndpointServer.Repositories;

public class EquipoRepository
{
    private readonly IConfiguration _configuration;

    public EquipoRepository(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<Equipo> RegistrarOActualizar(RegistrarEquipoRequest request)
    {
        var conexion = ObtenerCadenaConexion();

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();

        var sql = """
        IF EXISTS (SELECT 1 FROM dbo.Equipos WHERE Guid = @Guid)
        BEGIN
            UPDATE dbo.Equipos
            SET NombreEquipo = @NombreEquipo,
                Usuario = @Usuario,
                Dominio = @Dominio,
                SistemaOperativo = @SistemaOperativo,
                VersionSO = @VersionSO,
                DireccionIP = @DireccionIP,
                VersionAgente = @VersionAgente,
                UltimoHeartbeat = SYSDATETIME(),
                Estado = 'Online'
            WHERE Guid = @Guid;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.Equipos
            (
                Guid, NombreEquipo, Usuario, Dominio, SistemaOperativo,
                VersionSO, DireccionIP, VersionAgente,
                FechaRegistro, UltimoHeartbeat, Estado
            )
            VALUES
            (
                @Guid, @NombreEquipo, @Usuario, @Dominio, @SistemaOperativo,
                @VersionSO, @DireccionIP, @VersionAgente,
                SYSDATETIME(), SYSDATETIME(), 'Online'
            );
        END;

        SELECT
            Id, Guid, NombreEquipo, Usuario, SistemaOperativo,
            VersionSO, Dominio, DireccionIP, VersionAgente,
            FechaRegistro, UltimoHeartbeat, Estado
        FROM dbo.Equipos
        WHERE Guid = @Guid;
        """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Guid", request.Guid);
        cmd.Parameters.AddWithValue("@NombreEquipo", request.NombreEquipo);
        cmd.Parameters.AddWithValue("@Usuario", request.Usuario);
        cmd.Parameters.AddWithValue("@Dominio", request.Dominio);
        cmd.Parameters.AddWithValue("@SistemaOperativo", request.SistemaOperativo);
        cmd.Parameters.AddWithValue("@VersionSO", request.VersionSO);
        cmd.Parameters.AddWithValue("@DireccionIP", request.DireccionIP);
        cmd.Parameters.AddWithValue("@VersionAgente", request.VersionAgente);

        await using var dr = await cmd.ExecuteReaderAsync();

        if (!await dr.ReadAsync())
        {
            throw new InvalidOperationException(
                "No se ha podido recuperar el equipo registrado.");
        }

        return LeerEquipo(dr);
    }

    public async Task<bool> ActualizarHeartbeat(Guid guid)
    {
        var conexion = ObtenerCadenaConexion();

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();

        const string sql = """
        UPDATE dbo.Equipos
        SET UltimoHeartbeat = SYSDATETIME(),
            Estado = 'Online'
        WHERE Guid = @Guid;
        """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Guid", guid);

        var filasAfectadas = await cmd.ExecuteNonQueryAsync();
        return filasAfectadas > 0;
    }

    public async Task<List<Equipo>> ObtenerEquipos()
    {
        var lista = new List<Equipo>();
        var conexion = ObtenerCadenaConexion();

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();

        var sql = """
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
        ORDER BY NombreEquipo;
        """;

        await using var cmd = new SqlCommand(sql, cn);
        await using var dr = await cmd.ExecuteReaderAsync();

        while (await dr.ReadAsync())
        {
            lista.Add(LeerEquipo(dr));
        }

        return lista;
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

    private static Equipo LeerEquipo(SqlDataReader dr)
    {
        return new Equipo
        {
            Id = dr.GetInt32(0),
            Guid = dr.GetGuid(1),
            NombreEquipo = dr.GetString(2),
            Usuario = dr.IsDBNull(3) ? "" : dr.GetString(3),
            SistemaOperativo = dr.IsDBNull(4) ? "" : dr.GetString(4),
            VersionSO = dr.IsDBNull(5) ? "" : dr.GetString(5),
            Dominio = dr.IsDBNull(6) ? "" : dr.GetString(6),
            DireccionIP = dr.IsDBNull(7) ? "" : dr.GetString(7),
            VersionAgente = dr.GetString(8),
            FechaRegistro = dr.GetDateTime(9),
            UltimoHeartbeat = dr.GetDateTime(10),
            Estado = dr.GetString(11)
        };
    }
}

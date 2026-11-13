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

    public async Task<(Equipo Equipo, bool EsNuevo)> RegistrarOActualizar(RegistrarEquipoRequest request)
    {
        var conexion = ObtenerCadenaConexion();

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();

        const string sql = """
        DECLARE @Existe BIT = 0;

        IF EXISTS (SELECT 1 FROM dbo.Equipos WHERE Guid = @Guid)
        BEGIN
            SET @Existe = 1;
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
            @Existe AS EsExiste,
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
            throw new InvalidOperationException("No se ha podido recuperar el equipo registrado.");
        }

        bool esNuevo = !dr.GetBoolean(0);
        var equipo = LeerEquipoDesde(dr, offset: 1);
        return (equipo, esNuevo);
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

        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<Equipo>> ObtenerEquipos()
    {
        var lista = new List<Equipo>();
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

    public async Task<Equipo?> ObtenerEquipoPorId(int id)
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
        WHERE Id = @Id;
        """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Id", id);
        await using var dr = await cmd.ExecuteReaderAsync();

        return await dr.ReadAsync() ? LeerEquipo(dr) : null;
    }

    public async Task<Equipo?> ObtenerEquipoPorGuid(Guid guid)
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
        WHERE Guid = @Guid;
        """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@Guid", guid);
        await using var dr = await cmd.ExecuteReaderAsync();

        return await dr.ReadAsync() ? LeerEquipo(dr) : null;
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
        return LeerEquipoDesde(dr, 0);
    }

    private static Equipo LeerEquipoDesde(SqlDataReader dr, int offset)
    {
        return new Equipo
        {
            Id = dr.GetInt32(offset + 0),
            Guid = dr.GetGuid(offset + 1),
            NombreEquipo = dr.GetString(offset + 2),
            Usuario = dr.IsDBNull(offset + 3) ? "" : dr.GetString(offset + 3),
            SistemaOperativo = dr.IsDBNull(offset + 4) ? "" : dr.GetString(offset + 4),
            VersionSO = dr.IsDBNull(offset + 5) ? "" : dr.GetString(offset + 5),
            Dominio = dr.IsDBNull(offset + 6) ? "" : dr.GetString(offset + 6),
            DireccionIP = dr.IsDBNull(offset + 7) ? "" : dr.GetString(offset + 7),
            VersionAgente = dr.GetString(offset + 8),
            FechaRegistro = dr.GetDateTime(offset + 9),
            UltimoHeartbeat = dr.GetDateTime(offset + 10),
            Estado = dr.GetString(offset + 11)
        };
    }
}

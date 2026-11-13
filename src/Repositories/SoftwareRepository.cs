using Microsoft.Data.SqlClient;
using System.Data;
using Tradipres.EndpointServer.Models;

namespace Tradipres.EndpointServer.Repositories;

public sealed class SoftwareRepository
{
    private readonly IConfiguration _configuration;

    public SoftwareRepository(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<int?> GuardarSoftware(SoftwareReportRequest request)
    {
        await using var cn = new SqlConnection(ObtenerCadenaConexion());
        await cn.OpenAsync();
        await using var tx = await cn.BeginTransactionAsync();

        try
        {
            int? equipoId;
            await using (var cmdEquipo = new SqlCommand(
                "SELECT Id FROM dbo.Equipos WHERE Guid = @Guid;", cn, (SqlTransaction)tx))
            {
                cmdEquipo.Parameters.AddWithValue("@Guid", request.Guid);
                var value = await cmdEquipo.ExecuteScalarAsync();
                equipoId = value is null || value == DBNull.Value ? null : Convert.ToInt32(value);
            }

            if (equipoId is null)
            {
                await tx.RollbackAsync();
                return null;
            }

            await using (var cmdDelete = new SqlCommand(
                "DELETE FROM dbo.SoftwareInstalado WHERE EquipoId = @EquipoId;", cn, (SqlTransaction)tx))
            {
                cmdDelete.Parameters.AddWithValue("@EquipoId", equipoId.Value);
                await cmdDelete.ExecuteNonQueryAsync();
            }

            const string insertSql = """
                INSERT INTO dbo.SoftwareInstalado
                    (EquipoId, Nombre, Version, Fabricante, FechaInstalacion, Arquitectura, FechaInventario)
                VALUES
                    (@EquipoId, @Nombre, @Version, @Fabricante, @FechaInstalacion, @Arquitectura, SYSDATETIME());
                """;

            foreach (var item in request.Software)
            {
                await using var cmdInsert = new SqlCommand(insertSql, cn, (SqlTransaction)tx);
                cmdInsert.Parameters.AddWithValue("@EquipoId", equipoId.Value);
                cmdInsert.Parameters.AddWithValue("@Nombre", item.Nombre.Trim());
                cmdInsert.Parameters.AddWithValue("@Version", DbValue(item.Version, 100));
                cmdInsert.Parameters.AddWithValue("@Fabricante", DbValue(item.Fabricante, 200));
                cmdInsert.Parameters.AddWithValue("@FechaInstalacion", item.FechaInstalacion?.Date ?? (object)DBNull.Value);
                cmdInsert.Parameters.AddWithValue("@Arquitectura", DbValue(item.Arquitectura, 20));
                await cmdInsert.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            return equipoId;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<SoftwareResumen> ObtenerSoftwareResumen()
    {
        await using var cn = new SqlConnection(ObtenerCadenaConexion());
        await cn.OpenAsync();

        var resumen = new SoftwareResumen();

        const string sql = """
            SELECT
                s.Nombre,
                s.Version,
                s.Fabricante,
                s.Arquitectura,
                COUNT(DISTINCT s.EquipoId) AS Equipos
            FROM dbo.SoftwareInstalado s
            GROUP BY s.Nombre, s.Version, s.Fabricante, s.Arquitectura
            ORDER BY Equipos DESC, s.Nombre, s.Version;
            """;

        await using (var cmd = new SqlCommand(sql, cn))
        await using (var dr = await cmd.ExecuteReaderAsync())
        {
            while (await dr.ReadAsync())
            {
                resumen.Items.Add(new SoftwareResumenItem
                {
                    Nombre = dr.GetString(0),
                    Version = LeerTexto(dr, 1),
                    Fabricante = LeerTexto(dr, 2),
                    Arquitectura = LeerTexto(dr, 3),
                    Equipos = dr.GetInt32(4)
                });
            }
        }

        const string totalesSql = """
            SELECT COUNT(1), COUNT(DISTINCT EquipoId)
            FROM dbo.SoftwareInstalado;
            """;

        await using (var cmdTotales = new SqlCommand(totalesSql, cn))
        await using (var drTotales = await cmdTotales.ExecuteReaderAsync())
        {
            if (await drTotales.ReadAsync())
            {
                resumen.Instalaciones = drTotales.IsDBNull(0)
                    ? 0
                    : Convert.ToInt64(drTotales.GetValue(0));
                resumen.EquiposConSoftware = drTotales.IsDBNull(1)
                    ? 0
                    : Convert.ToInt32(drTotales.GetValue(1));
            }
        }

        resumen.Programas = resumen.Items.Count;
        return resumen;
    }

    public async Task<List<SoftwareGrupoEquipo>> ObtenerEquiposConPrograma(
        string nombre,
        string? version,
        string? fabricante,
        string? arquitectura)
    {
        await using var cn = new SqlConnection(ObtenerCadenaConexion());
        await cn.OpenAsync();

        const string sql = """
            SELECT
                e.NombreEquipo,
                e.Dominio,
                e.DireccionIP,
                CASE
                    WHEN e.UltimoHeartbeat < DATEADD(MINUTE, -3, SYSDATETIME())
                        THEN 'Offline'
                    ELSE e.Estado
                END AS Estado
            FROM dbo.SoftwareInstalado s
            INNER JOIN dbo.Equipos e ON e.Id = s.EquipoId
            WHERE s.Nombre = @Nombre
              AND ((@Version IS NULL AND s.Version IS NULL) OR s.Version = @Version)
              AND ((@Fabricante IS NULL AND s.Fabricante IS NULL) OR s.Fabricante = @Fabricante)
              AND ((@Arquitectura IS NULL AND s.Arquitectura IS NULL) OR s.Arquitectura = @Arquitectura)
            ORDER BY e.NombreEquipo;
            """;

        var lista = new List<SoftwareGrupoEquipo>();

        await using (var cmd = new SqlCommand(sql, cn))
        {
            cmd.Parameters.Add("@Nombre", SqlDbType.NVarChar, 300).Value = nombre;
            cmd.Parameters.Add("@Version", SqlDbType.NVarChar, 100).Value =
                string.IsNullOrWhiteSpace(version) ? DBNull.Value : version.Trim();
            cmd.Parameters.Add("@Fabricante", SqlDbType.NVarChar, 200).Value =
                string.IsNullOrWhiteSpace(fabricante) ? DBNull.Value : fabricante.Trim();
            cmd.Parameters.Add("@Arquitectura", SqlDbType.NVarChar, 20).Value =
                string.IsNullOrWhiteSpace(arquitectura) ? DBNull.Value : arquitectura.Trim();

            await using var dr = await cmd.ExecuteReaderAsync();
            while (await dr.ReadAsync())
            {
                lista.Add(new SoftwareGrupoEquipo
                {
                    NombreEquipo = dr.GetString(0),
                    Dominio = LeerTexto(dr, 1),
                    DireccionIP = LeerTexto(dr, 2),
                    Estado = dr.GetString(3)
                });
            }
        }

        return lista;
    }

    public async Task<List<SoftwareEquipoItem>?> ObtenerSoftware(int equipoId)
    {
        await using var cn = new SqlConnection(ObtenerCadenaConexion());
        await cn.OpenAsync();

        await using (var cmdExists = new SqlCommand("SELECT COUNT(1) FROM dbo.Equipos WHERE Id = @EquipoId;", cn))
        {
            cmdExists.Parameters.AddWithValue("@EquipoId", equipoId);
            if (Convert.ToInt32(await cmdExists.ExecuteScalarAsync()) == 0)
            {
                return null;
            }
        }

        const string sql = """
            SELECT Id, Nombre, Version, Fabricante, FechaInstalacion, Arquitectura, FechaInventario
            FROM dbo.SoftwareInstalado
            WHERE EquipoId = @EquipoId
            ORDER BY Nombre, Version;
            """;

        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@EquipoId", equipoId);
        await using var dr = await cmd.ExecuteReaderAsync();

        var result = new List<SoftwareEquipoItem>();
        while (await dr.ReadAsync())
        {
            result.Add(new SoftwareEquipoItem
            {
                Id = dr.GetInt32(0),
                Nombre = dr.GetString(1),
                Version = dr.IsDBNull(2) ? null : dr.GetString(2),
                Fabricante = dr.IsDBNull(3) ? null : dr.GetString(3),
                FechaInstalacion = dr.IsDBNull(4) ? null : dr.GetDateTime(4),
                Arquitectura = dr.IsDBNull(5) ? null : dr.GetString(5),
                FechaInventario = dr.GetDateTime(6)
            });
        }

        return result;
    }

    private string ObtenerCadenaConexion()
    {
        return _configuration.GetConnectionString("EndpointManager")
            ?? throw new InvalidOperationException("No se ha configurado la conexión EndpointManager.");
    }

    private static object DbValue(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return DBNull.Value;
        var clean = value.Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    private static string LeerTexto(SqlDataReader dr, int ordinal) =>
        dr.IsDBNull(ordinal) ? "" : dr.GetString(ordinal);
}

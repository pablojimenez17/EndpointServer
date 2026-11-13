using Microsoft.Data.SqlClient;
using System.Data;
using Tradipres.EndpointServer.Models;

namespace Tradipres.EndpointServer.Repositories;

public class InventarioRepository
{
    private readonly IConfiguration _configuration;

    public InventarioRepository(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<int?> GuardarInventario(InventoryReportRequest request)
    {
        var conexion = ObtenerCadenaConexion();

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();
        await using var transaction = (SqlTransaction)await cn.BeginTransactionAsync();

        try
        {
            var equipoId = await ObtenerEquipoId(cn, transaction, request.Guid);

            if (equipoId is null)
            {
                await transaction.RollbackAsync();
                return null;
            }

            await GuardarCabecera(cn, transaction, equipoId.Value, request);
            await ReemplazarDiscos(cn, transaction, equipoId.Value, request.Discos);

            await transaction.CommitAsync();
            return equipoId;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task<int?> ObtenerEquipoId(
        SqlConnection cn,
        SqlTransaction transaction,
        Guid guid)
    {
        const string sql = "SELECT Id FROM dbo.Equipos WHERE Guid = @Guid;";

        await using var cmd = new SqlCommand(sql, cn, transaction);
        cmd.Parameters.Add("@Guid", SqlDbType.UniqueIdentifier).Value = guid;

        var resultado = await cmd.ExecuteScalarAsync();

        return resultado is null || resultado == DBNull.Value
            ? null
            : Convert.ToInt32(resultado);
    }

    private static async Task GuardarCabecera(
        SqlConnection cn,
        SqlTransaction transaction,
        int equipoId,
        InventoryReportRequest request)
    {
        const string sql = """
        UPDATE dbo.InventarioEquipos
        SET Fabricante = @Fabricante,
            Modelo = @Modelo,
            NumeroSerie = @NumeroSerie,
            Procesador = @Procesador,
            Nucleos = @Nucleos,
            MemoriaRamGB = @MemoriaRamGB,
            Arquitectura = @Arquitectura,
            VersionWindows = @VersionWindows,
            FechaInstalacion = @FechaInstalacion,
            IpPrincipal = @IpPrincipal,
            MacPrincipal = @MacPrincipal,
            FechaInventario = SYSDATETIME()
        WHERE EquipoId = @EquipoId;

        IF @@ROWCOUNT = 0
        BEGIN
            INSERT INTO dbo.InventarioEquipos
            (
                EquipoId,
                Fabricante,
                Modelo,
                NumeroSerie,
                Procesador,
                Nucleos,
                MemoriaRamGB,
                Arquitectura,
                VersionWindows,
                FechaInstalacion,
                IpPrincipal,
                MacPrincipal,
                FechaInventario
            )
            VALUES
            (
                @EquipoId,
                @Fabricante,
                @Modelo,
                @NumeroSerie,
                @Procesador,
                @Nucleos,
                @MemoriaRamGB,
                @Arquitectura,
                @VersionWindows,
                @FechaInstalacion,
                @IpPrincipal,
                @MacPrincipal,
                SYSDATETIME()
            );
        END;
        """;

        await using var cmd = new SqlCommand(sql, cn, transaction);
        cmd.Parameters.Add("@EquipoId", SqlDbType.Int).Value = equipoId;
        cmd.Parameters.Add("@Fabricante", SqlDbType.NVarChar, 100).Value = Limpiar(request.Fabricante);
        cmd.Parameters.Add("@Modelo", SqlDbType.NVarChar, 150).Value = Limpiar(request.Modelo);
        cmd.Parameters.Add("@NumeroSerie", SqlDbType.NVarChar, 100).Value = Limpiar(request.NumeroSerie);
        cmd.Parameters.Add("@Procesador", SqlDbType.NVarChar, 250).Value = Limpiar(request.Procesador);
        cmd.Parameters.Add("@Nucleos", SqlDbType.Int).Value = request.Nucleos;

        var ramParameter = cmd.Parameters.Add("@MemoriaRamGB", SqlDbType.Decimal);
        ramParameter.Precision = 10;
        ramParameter.Scale = 2;
        ramParameter.Value = request.MemoriaRamGB;

        cmd.Parameters.Add("@Arquitectura", SqlDbType.NVarChar, 50).Value = Limpiar(request.Arquitectura);
        cmd.Parameters.Add("@VersionWindows", SqlDbType.NVarChar, 150).Value = Limpiar(request.VersionWindows);
        cmd.Parameters.Add("@FechaInstalacion", SqlDbType.DateTime2).Value =
            request.FechaInstalacion.HasValue ? request.FechaInstalacion.Value : DBNull.Value;
        cmd.Parameters.Add("@IpPrincipal", SqlDbType.NVarChar, 64).Value = Limpiar(request.IpPrincipal);
        cmd.Parameters.Add("@MacPrincipal", SqlDbType.NVarChar, 32).Value = Limpiar(request.MacPrincipal);

        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ReemplazarDiscos(
        SqlConnection cn,
        SqlTransaction transaction,
        int equipoId,
        IReadOnlyCollection<InventoryDiskRequest> discos)
    {
        const string deleteSql = "DELETE FROM dbo.InventarioDiscos WHERE EquipoId = @EquipoId;";

        await using (var deleteCmd = new SqlCommand(deleteSql, cn, transaction))
        {
            deleteCmd.Parameters.Add("@EquipoId", SqlDbType.Int).Value = equipoId;
            await deleteCmd.ExecuteNonQueryAsync();
        }

        const string insertSql = """
        INSERT INTO dbo.InventarioDiscos
        (
            EquipoId,
            Unidad,
            Etiqueta,
            SistemaArchivos,
            TipoUnidad,
            TamanoBytes,
            LibreBytes
        )
        VALUES
        (
            @EquipoId,
            @Unidad,
            @Etiqueta,
            @SistemaArchivos,
            @TipoUnidad,
            @TamanoBytes,
            @LibreBytes
        );
        """;

        foreach (var disco in discos)
        {
            await using var insertCmd = new SqlCommand(insertSql, cn, transaction);
            insertCmd.Parameters.Add("@EquipoId", SqlDbType.Int).Value = equipoId;
            insertCmd.Parameters.Add("@Unidad", SqlDbType.NVarChar, 20).Value = Limpiar(disco.Unidad);
            insertCmd.Parameters.Add("@Etiqueta", SqlDbType.NVarChar, 100).Value = ValorOpcional(disco.Etiqueta);
            insertCmd.Parameters.Add("@SistemaArchivos", SqlDbType.NVarChar, 30).Value = ValorOpcional(disco.SistemaArchivos);
            insertCmd.Parameters.Add("@TipoUnidad", SqlDbType.NVarChar, 30).Value = ValorOpcional(disco.TipoUnidad);
            insertCmd.Parameters.Add("@TamanoBytes", SqlDbType.BigInt).Value = disco.TamanoBytes;
            insertCmd.Parameters.Add("@LibreBytes", SqlDbType.BigInt).Value = disco.LibreBytes;
            await insertCmd.ExecuteNonQueryAsync();
        }
    }

    public async Task<List<InventarioEquipoFila>> ObtenerInventarioEquipos()
    {
        var lista = new List<InventarioEquipoFila>();
        var conexion = ObtenerCadenaConexion();

        await using var cn = new SqlConnection(conexion);
        await cn.OpenAsync();

        const string sql = """
        SELECT
            e.Id, e.Guid, e.NombreEquipo, e.Dominio, e.Usuario, e.DireccionIP,
            e.VersionAgente, e.UltimoHeartbeat,
            CASE
                WHEN e.UltimoHeartbeat < DATEADD(MINUTE, -3, SYSDATETIME())
                    THEN 'Offline'
                ELSE e.Estado
            END AS Estado,
            i.Fabricante, i.Modelo, i.NumeroSerie, i.Procesador, i.Nucleos,
            i.MemoriaRamGB, i.Arquitectura, i.VersionWindows, i.FechaInstalacion,
            i.IpPrincipal, i.MacPrincipal, i.FechaInventario
        FROM dbo.Equipos e
        LEFT JOIN dbo.InventarioEquipos i ON i.EquipoId = e.Id
        ORDER BY e.NombreEquipo;
        """;

        await using var cmd = new SqlCommand(sql, cn);
        await using var dr = await cmd.ExecuteReaderAsync();

        while (await dr.ReadAsync())
        {
            bool tieneInventario = !dr.IsDBNull(9);

            lista.Add(new InventarioEquipoFila
            {
                Id = dr.GetInt32(0),
                Guid = dr.GetGuid(1),
                NombreEquipo = dr.GetString(2),
                Dominio = dr.IsDBNull(3) ? "" : dr.GetString(3),
                Usuario = dr.IsDBNull(4) ? "" : dr.GetString(4),
                DireccionIP = dr.IsDBNull(5) ? "" : dr.GetString(5),
                VersionAgente = dr.IsDBNull(6) ? "" : dr.GetString(6),
                UltimoHeartbeat = dr.GetDateTime(7),
                Estado = dr.GetString(8),
                TieneInventario = tieneInventario,
                Fabricante = tieneInventario ? LeerTexto(dr, 9) : "",
                Modelo = tieneInventario ? LeerTexto(dr, 10) : "",
                NumeroSerie = tieneInventario ? LeerTexto(dr, 11) : "",
                Procesador = tieneInventario ? LeerTexto(dr, 12) : "",
                Nucleos = tieneInventario && !dr.IsDBNull(13) ? dr.GetInt32(13) : 0,
                MemoriaRamGB = tieneInventario && !dr.IsDBNull(14) ? dr.GetDecimal(14) : 0,
                Arquitectura = tieneInventario ? LeerTexto(dr, 15) : "",
                VersionWindows = tieneInventario ? LeerTexto(dr, 16) : "",
                FechaInstalacion = tieneInventario && !dr.IsDBNull(17) ? dr.GetDateTime(17) : null,
                IpPrincipal = tieneInventario ? LeerTexto(dr, 18) : "",
                MacPrincipal = tieneInventario ? LeerTexto(dr, 19) : "",
                FechaInventario = tieneInventario && !dr.IsDBNull(20) ? dr.GetDateTime(20) : null
            });
        }

        return lista;
    }

    private static string LeerTexto(SqlDataReader dr, int ordinal) =>
        dr.IsDBNull(ordinal) ? "" : dr.GetString(ordinal);

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

    private static string Limpiar(string? valor) => valor?.Trim() ?? "";

    private static object ValorOpcional(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? DBNull.Value : valor.Trim();
}

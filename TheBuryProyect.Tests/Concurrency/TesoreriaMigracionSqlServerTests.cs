using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TheBuryProject.Data;
using Xunit;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// D-03: la migración D03_TesoreriaOrigenesAnticipos crea un índice ÚNICO por cheque vigente. El esquema anterior permitía repetir un
// cheque en varios pagos; si la base ya tenía datos así, el índice habría hecho fallar la actualización. La migración concilia de forma
// conservadora (el pago más antiguo conserva el cheque; los demás pierden solo la referencia, con una nota) y estas pruebas lo
// verifican contra SQL Server REAL partiendo del esquema anterior, con datos sintéticos y en un catálogo descartable propio.
//
// Mismo gate que los demás harness: QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL con catálogo "qa_fin_conc*" (el propio es "<catálogo>_d03mig").
// ---------------------------------------------------------------------------------------------

public class TesoreriaMigracionSqlServerTests
{
    private const string MigracionD03 = "D03_TesoreriaOrigenesAnticipos";

    private static async Task<int> InsertarAsync(SqlConnection cn, string tabla, Dictionary<string, object?> valores)
    {
        var columnas = new List<(string Nombre, string Tipo, bool Nulo, bool TieneDefault)>();
        await using (var cmd = new SqlCommand(
            "SELECT c.name, t.name, c.is_nullable, CASE WHEN c.default_object_id <> 0 THEN 1 ELSE 0 END FROM sys.columns c " +
            "JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID(@t) AND c.is_identity = 0 AND c.is_computed = 0 " +
            "AND t.name NOT IN ('timestamp','rowversion') ORDER BY c.column_id", cn))
        {
            cmd.Parameters.AddWithValue("@t", tabla);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                columnas.Add((r.GetString(0), r.GetString(1), r.GetBoolean(2), r.GetInt32(3) == 1));
        }

        var nombres = new List<string>();
        var parametros = new List<SqlParameter>();
        foreach (var (nombre, tipo, nulo, tieneDefault) in columnas)
        {
            object? valor;
            if (valores.TryGetValue(nombre, out var dado))
                valor = dado;
            else if (!nulo && !tieneDefault)
                valor = tipo switch
                {
                    "int" or "bigint" or "smallint" or "tinyint" or "decimal" or "numeric" or "float" or "money" => 0,
                    "bit" => false,
                    "datetime" or "datetime2" or "date" or "smalldatetime" or "datetimeoffset" => DateTime.UtcNow,
                    "uniqueidentifier" => Guid.NewGuid(),
                    "varbinary" or "binary" => new byte[] { 0 },
                    _ => "x"
                };
            else
                continue;

            nombres.Add(nombre);
            parametros.Add(new SqlParameter($"@p{parametros.Count}", valor ?? DBNull.Value));
        }

        var sql = $"INSERT INTO [{tabla}] ({string.Join(",", nombres.Select(n => $"[{n}]"))}) OUTPUT INSERTED.[Id] " +
                  $"VALUES ({string.Join(",", parametros.Select(p => p.ParameterName))})";
        await using var insert = new SqlCommand(sql, cn);
        insert.Parameters.AddRange(parametros.ToArray());
        return (int)(await insert.ExecuteScalarAsync())!;
    }

    private static async Task<T?> EscalarAsync<T>(SqlConnection cn, string sql)
    {
        await using var cmd = new SqlCommand(sql, cn);
        var r = await cmd.ExecuteScalarAsync();
        return r is null or DBNull ? default : (T)Convert.ChangeType(r, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    [SqlServerConcFact(DisplayName = "D03-MIG-01 la migracion concilia pagos que repetian un cheque (el mas antiguo lo conserva) sin perder importes ni historia")]
    public async Task La_Migracion_Concilia_Cheques_Repetidos_Y_Crea_El_Indice_Unico()
    {
        var baseCs = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("QA_FIN_CONC_SQL")!);
        var catalogo = baseCs.InitialCatalog + "_d03mig";
        Assert.StartsWith("qa_fin_conc", catalogo, StringComparison.OrdinalIgnoreCase);

        var master = new SqlConnectionStringBuilder(baseCs.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        await using (var cnMaster = new SqlConnection(master))
        {
            await cnMaster.OpenAsync();
            await using var cmd = new SqlCommand(
                $"IF DB_ID(N'{catalogo}') IS NOT NULL BEGIN ALTER DATABASE [{catalogo}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{catalogo}]; END; CREATE DATABASE [{catalogo}];", cnMaster);
            await cmd.ExecuteNonQueryAsync();
        }

        var cs = new SqlConnectionStringBuilder(baseCs.ConnectionString) { InitialCatalog = catalogo }.ConnectionString;
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options;
            await using var ctx = new AppDbContext(options);
            var migraciones = ctx.Database.GetMigrations().ToList();
            var d03 = migraciones.Single(m => m.EndsWith(MigracionD03, StringComparison.Ordinal));
            var anterior = migraciones[migraciones.IndexOf(d03) - 1];
            // D03 ya no tiene por qué ser la ÚLTIMA migración (las posteriores se aplican en el MigrateAsync() final); la prueba fija el
            // esquema ANTERIOR a D03 y migra hasta el final: lo que importa es que D03 exista una sola vez y no sea la primera.
            Assert.True(migraciones.IndexOf(d03) > 0);

            // 1) Esquema ANTERIOR + datos sintéticos con el defecto: tres pagos vigentes con el mismo cheque, uno anulado y un cheque sin repetir.
            await ctx.GetService<IMigrator>().MigrateAsync(anterior);
            int pagoViejo, pagoMedio, pagoNuevo, pagoLargo, pagoAnulado, pagoUnico, cheque, chequeUnico;
            var observacionLarga = new string('o', 1000);
            await using (var cn = new SqlConnection(cs))
            {
                await cn.OpenAsync();
                var prov = await InsertarAsync(cn, "Proveedores", new() { ["Cuit"] = "30700000009", ["RazonSocial"] = "Prov migración", ["Activo"] = true, ["IsDeleted"] = false });
                cheque = await InsertarAsync(cn, "Cheques", new() { ["ProveedorId"] = prov, ["Numero"] = "0001", ["Banco"] = "Banco", ["Monto"] = 500m, ["IsDeleted"] = false });
                chequeUnico = await InsertarAsync(cn, "Cheques", new() { ["ProveedorId"] = prov, ["Numero"] = "0002", ["Banco"] = "Banco", ["Monto"] = 300m, ["IsDeleted"] = false });
                var factura = await InsertarAsync(cn, "ComprobantesProveedor", new()
                {
                    ["ProveedorId"] = prov, ["Tipo"] = 1, ["NumeroComprobante"] = "F-MIG", ["ImporteTotal"] = 2000m, ["SaldoPendiente"] = 0m, ["Estado"] = 3, ["IsDeleted"] = false
                });

                async Task<int> Pago(int? chequeId, decimal monto, bool anulado, string? obs = null)
                {
                    var id = await InsertarAsync(cn, "PagosProveedor", new()
                    {
                        ["ProveedorId"] = prov, ["Medio"] = 3, ["Monto"] = monto, ["ChequeId"] = chequeId, ["Anulado"] = anulado, ["Observaciones"] = obs, ["IsDeleted"] = false
                    });
                    await InsertarAsync(cn, "PagoProveedorImputaciones", new() { ["PagoProveedorId"] = id, ["ComprobanteProveedorId"] = factura, ["Monto"] = monto, ["IsDeleted"] = false });
                    return id;
                }

                pagoViejo = await Pago(cheque, 500m, false);
                pagoMedio = await Pago(cheque, 500m, false, "pago cargado dos veces");
                pagoNuevo = await Pago(cheque, 500m, false);
                pagoLargo = await Pago(cheque, 500m, false, observacionLarga); // observación ya en el límite: no cabe la nota
                pagoAnulado = await Pago(cheque, 500m, true); // anulado: no cuenta para el índice y no se toca
                pagoUnico = await Pago(chequeUnico, 300m, false);
            }

            // 2) Se actualiza a la migración D-03: NO debe fallar por el índice único.
            await ctx.Database.MigrateAsync();

            await using (var cn = new SqlConnection(cs))
            {
                await cn.OpenAsync();
                async Task<int?> ChequeDe(int pago) => await EscalarAsync<int?>(cn, $"SELECT ChequeId FROM PagosProveedor WHERE Id = {pago}");

                Assert.Equal(cheque, await ChequeDe(pagoViejo)); // el más antiguo conserva el cheque
                Assert.Null(await ChequeDe(pagoMedio));
                Assert.Null(await ChequeDe(pagoNuevo));
                Assert.Null(await ChequeDe(pagoLargo));
                Assert.Equal(cheque, await ChequeDe(pagoAnulado)); // anulado: intacto
                Assert.Equal(chequeUnico, await ChequeDe(pagoUnico)); // sin repetir: intacto

                var nota = await EscalarAsync<string>(cn, $"SELECT Observaciones FROM PagosProveedor WHERE Id = {pagoMedio}");
                Assert.StartsWith("pago cargado dos veces [Migración D-03]", nota);
                Assert.Contains($"ya lo respalda el pago #{pagoViejo}", nota);
                Assert.Null(await EscalarAsync<string>(cn, $"SELECT Observaciones FROM PagosProveedor WHERE Id = {pagoViejo}"));

                // Observación en el límite: el texto original queda intacto (no se recorta) y la trazabilidad vive en la auditoría.
                Assert.Equal(observacionLarga, await EscalarAsync<string>(cn, $"SELECT Observaciones FROM PagosProveedor WHERE Id = {pagoLargo}"));
                foreach (var afectado in new[] { pagoMedio, pagoNuevo, pagoLargo })
                {
                    var detalle = await EscalarAsync<string>(cn, $"SELECT Detalle FROM SeguridadEventosAuditoria WHERE Modulo = 'CuentasPorPagar' AND Accion = 'ConciliarChequeRepetido' AND Entidad = 'Pago:{afectado}'");
                    Assert.NotNull(detalle);
                    Assert.Contains($"cheque Id {cheque}", detalle);
                    Assert.Contains($"del pago #{afectado}", detalle);
                    Assert.Contains($"lo conserva el pago #{pagoViejo}", detalle);
                }
                Assert.Equal(3, await EscalarAsync<int>(cn, "SELECT COUNT(*) FROM SeguridadEventosAuditoria WHERE Accion = 'ConciliarChequeRepetido'"));

                // Importes, imputaciones y cantidad de pagos: ninguno se pierde ni cambia.
                Assert.Equal(6, await EscalarAsync<int>(cn, "SELECT COUNT(*) FROM PagosProveedor"));
                Assert.Equal(2800m, await EscalarAsync<decimal>(cn, "SELECT SUM(Monto) FROM PagosProveedor"));
                Assert.Equal(6, await EscalarAsync<int>(cn, "SELECT COUNT(*) FROM PagoProveedorImputaciones"));
                Assert.Equal(1, await EscalarAsync<int>(cn, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_PagosProveedor_ChequeId_Vigente' AND is_unique = 1"));
            }

            // 3) Idempotente: el índice ya impide un duplicado vigente.
            await using (var cn = new SqlConnection(cs))
            {
                await cn.OpenAsync();
                var ex = await Assert.ThrowsAsync<SqlException>(() => InsertarAsync(cn, "PagosProveedor", new()
                {
                    ["ProveedorId"] = 1, ["Medio"] = 3, ["Monto"] = 1m, ["ChequeId"] = cheque, ["Anulado"] = false, ["IsDeleted"] = false
                }));
                Assert.Contains("IX_PagosProveedor_ChequeId_Vigente", ex.Message);
            }
        }
        finally
        {
            await using var cnMaster = new SqlConnection(master);
            await cnMaster.OpenAsync();
            await using var cmd = new SqlCommand(
                $"IF DB_ID(N'{catalogo}') IS NOT NULL BEGIN ALTER DATABASE [{catalogo}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{catalogo}]; END;", cnMaster);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

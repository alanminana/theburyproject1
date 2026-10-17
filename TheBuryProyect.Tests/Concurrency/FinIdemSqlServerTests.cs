using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// FIN-IDEM-P0 (H-IDEM-1) — idempotencia del movimiento manual de caja contra SQL Server REAL (efímero, solo CI).
//
// Misma intención + mismo token => un solo asiento y el MISMO movimiento devuelto, también con solicitudes simultáneas
// (compuertas deterministas sobre el lock de apertura de H-CAJA-1), reintento posterior al commit y reintento tras el cierre.
// Mismo token con datos distintos => rechazo. Tokens distintos => movimientos legítimos distintos.
//
// Mismo gate de seguridad que los demás harness (QA_RELEASE_TURNO=1 + QA_FIN_CONC_SQL con catálogo "qa_fin_conc*");
// el workflow corre la clase con READ_COMMITTED_SNAPSHOT apagado y encendido (QA_FIN_CONC_RCSI = 0 / 1).
// ---------------------------------------------------------------------------------------------

public sealed class FinIdemSqlHostFixture : ConcSqlHostFixture
{
    protected override string SufijoCatalogo => "_idem";
}

public class FinIdemSqlServerTests : IClassFixture<FinIdemSqlHostFixture>
{
    private static readonly TimeSpan EsperaCompuerta = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EsperaObservacion = TimeSpan.FromSeconds(7);
    private const decimal Monto = 25m;

    private readonly FinIdemSqlHostFixture _fx;
    private readonly ITestOutputHelper _out;

    public FinIdemSqlServerTests(FinIdemSqlHostFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _out = output;
    }

    private sealed record Escenario(string Usuario, int AperturaId);

    private async Task<Escenario> SembrarAsync()
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var usuario = "qi" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        return new Escenario(usuario, await ConcSeed.SembrarAperturaAsync(ctx, usuario));
    }

    private static MovimientoCajaViewModel Modelo(Escenario e, Guid? clave, decimal monto = Monto, string descripcion = "FIN-IDEM-P0") => new()
    {
        AperturaCajaId = e.AperturaId, Tipo = TipoMovimientoCaja.Egreso, Concepto = ConceptoMovimientoCaja.GastoOperativo,
        Descripcion = descripcion, Monto = monto, IdempotencyKey = clave
    };

    private Task<(string resultado, int? id)> Registrar(Escenario e, MovimientoCajaViewModel modelo, GatePlan? plan = null) =>
        Task.Run(async () =>
        {
            try
            {
                ConcGateInterceptor.Plan.Value = plan;
                using var scope = _fx.Services.CreateScope();
                var m = await scope.ServiceProvider.GetRequiredService<ICajaService>().RegistrarMovimientoAsync(modelo, e.Usuario);
                return ($"OK({m.Id})", (int?)m.Id);
            }
            catch (Exception ex)
            {
                return ($"{ex.GetType().Name}: {ex.Message}", (int?)null);
            }
            finally
            {
                ConcGateInterceptor.Plan.Value = null;
            }
        });

    private async Task CerrarAsync(Escenario e)
    {
        using var scope = _fx.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ICajaService>().CerrarCajaAsync(
            new CerrarCajaViewModel { AperturaCajaId = e.AperturaId, EfectivoContado = 0m, JustificacionDiferencia = "FIN-IDEM-P0" },
            e.Usuario);
    }

    private async Task<(int movimientos, decimal egresos)> LibroAsync(int aperturaId)
    {
        using var scope = _fx.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var movs = await ctx.MovimientosCaja.AsNoTracking()
            .Where(m => m.AperturaCajaId == aperturaId && !m.IsDeleted && m.Descripcion.StartsWith("FIN-IDEM-P0"))
            .ToListAsync();
        return (movs.Count, movs.Where(m => m.Tipo == TipoMovimientoCaja.Egreso).Sum(m => m.Monto));
    }

    private static GatePlan Compuerta(string nombre, string fragmentoSql) =>
        new(nombre, sql => sql.Contains(fragmentoSql, StringComparison.Ordinal));

    private async Task<bool> LlegoAsync(GatePlan plan, Task operacion) =>
        await Task.WhenAny(plan.Reached.Task, operacion, Task.Delay(EsperaCompuerta)) == plan.Reached.Task;

    // ================================================================== escenarios

    [SqlServerConcFact(DisplayName = "FIN-IDEM-P0 [IDEM-00] el catalogo corre con el modo de aislamiento pedido (RCSI)")]
    public async Task IDEM00_ModoDeAislamientoDelCatalogo_CoincideConElPedido()
    {
        var pedido = Environment.GetEnvironmentVariable("QA_FIN_CONC_RCSI") == "1" ? 1 : 0;
        await using var c = new SqlConnection(_fx.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE name = DB_NAME()", c);
        Assert.Equal(pedido, (int)(await cmd.ExecuteScalarAsync())!);
    }

    [SqlServerConcFact(DisplayName = "FIN-IDEM-P0 [IDEM-01] dos solicitudes simultaneas con el mismo token: un solo asiento, mismo movimiento")]
    public async Task IDEM01_DosSolicitudesSimultaneas_MismoToken_UnSoloAsiento()
    {
        var e = await SembrarAsync();
        var clave = Guid.NewGuid();

        // A queda detenida con el lock tomado, justo antes del INSERT; B llega, se bloquea en el lock y espera.
        var plan = Compuerta("con-lock/antes-del-insert", "INSERT INTO [MovimientosCaja]");
        var a = Registrar(e, Modelo(e, clave), plan);
        Assert.True(await LlegoAsync(plan, a), "A no llegó a la compuerta");

        var b = Registrar(e, Modelo(e, clave));
        Assert.True(await Task.WhenAny(b, Task.Delay(EsperaObservacion)) != b, "B debía quedar bloqueada por el lock de la apertura");

        plan.Release.TrySetResult();
        var (ra, ida) = await a;
        var (rb, idb) = await b;
        _out.WriteLine($"A={ra} B={rb}");

        Assert.NotNull(ida);
        Assert.Equal(ida, idb);
        var libro = await LibroAsync(e.AperturaId);
        Assert.Equal(1, libro.movimientos);
        Assert.Equal(Monto, libro.egresos);
    }

    [SqlServerConcFact(DisplayName = "FIN-IDEM-P0 [IDEM-02] solicitudes en carrera total (sin compuerta, 8 hilos): un solo asiento")]
    public async Task IDEM02_CarreraTotal_MismoToken_UnSoloAsiento()
    {
        var e = await SembrarAsync();
        var clave = Guid.NewGuid();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Registrar(e, Modelo(e, clave))));
        foreach (var r in resultados) _out.WriteLine(r.resultado);

        Assert.All(resultados, r => Assert.NotNull(r.id));
        Assert.Single(resultados.Select(r => r.id).Distinct());
        var libro = await LibroAsync(e.AperturaId);
        Assert.Equal(1, libro.movimientos);
        Assert.Equal(Monto, libro.egresos);
    }

    [SqlServerConcFact(DisplayName = "FIN-IDEM-P0 [IDEM-03] reintento posterior al commit: devuelve el mismo movimiento sin duplicar")]
    public async Task IDEM03_ReintentoPosteriorAlCommit_DevuelveElMismoMovimiento()
    {
        var e = await SembrarAsync();
        var clave = Guid.NewGuid();

        var (r1, id1) = await Registrar(e, Modelo(e, clave));
        var (r2, id2) = await Registrar(e, Modelo(e, clave));
        _out.WriteLine($"{r1} / {r2}");

        Assert.NotNull(id1);
        Assert.Equal(id1, id2);
        Assert.Equal(1, (await LibroAsync(e.AperturaId)).movimientos);
    }

    [SqlServerConcFact(DisplayName = "FIN-IDEM-P0 [IDEM-04] reintento despues del cierre de caja: mismo movimiento, sin escribir ni reabrir")]
    public async Task IDEM04_ReintentoTrasCierre_DevuelveElMismoMovimientoSinEscribir()
    {
        var e = await SembrarAsync();
        var clave = Guid.NewGuid();

        var (_, id1) = await Registrar(e, Modelo(e, clave));
        Assert.NotNull(id1);
        await CerrarAsync(e);

        var (r2, id2) = await Registrar(e, Modelo(e, clave));
        _out.WriteLine(r2);
        Assert.Equal(id1, id2);
        Assert.Equal(1, (await LibroAsync(e.AperturaId)).movimientos);

        // Una intención NUEVA (otro token) sobre la caja cerrada sigue rechazada por las protecciones de H-CAJA-1/2.
        var (r3, id3) = await Registrar(e, Modelo(e, Guid.NewGuid()));
        Assert.Null(id3);
        Assert.Contains("cerrada", r3, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, (await LibroAsync(e.AperturaId)).movimientos);
    }

    [SqlServerConcFact(DisplayName = "FIN-IDEM-P0 [IDEM-05] mismo token con datos distintos: se rechaza sin escribir")]
    public async Task IDEM05_MismoTokenDatosDistintos_SeRechaza()
    {
        var e = await SembrarAsync();
        var clave = Guid.NewGuid();

        var (_, id1) = await Registrar(e, Modelo(e, clave));
        Assert.NotNull(id1);

        var (r2, id2) = await Registrar(e, Modelo(e, clave, monto: 99m));
        _out.WriteLine(r2);
        Assert.Null(id2);
        Assert.Contains("InvalidOperationException", r2);

        var (r3, id3) = await Registrar(e, Modelo(e, clave, descripcion: "FIN-IDEM-P0 otra"));
        Assert.Null(id3);
        Assert.Contains("InvalidOperationException", r3);

        var libro = await LibroAsync(e.AperturaId);
        Assert.Equal(1, libro.movimientos);
        Assert.Equal(Monto, libro.egresos);
    }

    [SqlServerConcFact(DisplayName = "FIN-IDEM-P0 [IDEM-06] tokens distintos con los mismos datos: dos movimientos legitimos")]
    public async Task IDEM06_TokensDistintos_PermitenMovimientosLegitimosDistintos()
    {
        var e = await SembrarAsync();

        var (_, id1) = await Registrar(e, Modelo(e, Guid.NewGuid()));
        var (_, id2) = await Registrar(e, Modelo(e, Guid.NewGuid()));

        Assert.NotNull(id1);
        Assert.NotNull(id2);
        Assert.NotEqual(id1, id2);
        var libro = await LibroAsync(e.AperturaId);
        Assert.Equal(2, libro.movimientos);
        Assert.Equal(Monto * 2, libro.egresos);
    }

    [SqlServerConcFact(DisplayName = "FIN-IDEM-P0 [IDEM-07] el indice unico filtrado rechaza una clave repetida y admite muchos nulos")]
    public async Task IDEM07_IndiceUnicoFiltrado_RechazaDuplicadoYAdmiteNulos()
    {
        var e = await SembrarAsync();
        var clave = Guid.NewGuid();
        await Registrar(e, Modelo(e, clave));

        await using var c = new SqlConnection(_fx.ConnectionString);
        await c.OpenAsync();
        const string insert = "INSERT INTO [MovimientosCaja] ([AperturaCajaId],[FechaMovimiento],[Tipo],[Concepto],[Monto],[Descripcion],[Usuario],[IdempotencyKey],[CreatedAt],[IsDeleted]) " +
                              "VALUES (@a, SYSUTCDATETIME(), 1, 1, 1, N'FIN-IDEM-P0 raw', N'raw', @k, SYSUTCDATETIME(), 0)";

        for (var i = 0; i < 2; i++) // dos NULL conviven
        {
            await using var nulo = new SqlCommand(insert, c);
            nulo.Parameters.AddWithValue("@a", e.AperturaId);
            nulo.Parameters.AddWithValue("@k", DBNull.Value);
            await nulo.ExecuteNonQueryAsync();
        }

        await using var dup = new SqlCommand(insert, c);
        dup.Parameters.AddWithValue("@a", e.AperturaId);
        dup.Parameters.AddWithValue("@k", clave);
        var ex = await Assert.ThrowsAsync<SqlException>(() => dup.ExecuteNonQueryAsync());
        Assert.Contains(ex.Errors.Cast<SqlError>(), x => x.Number is 2601 or 2627);
    }
}

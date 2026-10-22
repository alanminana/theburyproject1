using AutoMapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Tests.Integration;
using Xunit;

namespace TheBuryProject.Tests.Concurrency;

// ---------------------------------------------------------------------------------------------
// PROMPT 90 — Credito/ConfigurarVenta ante pedidos CONCURRENTES.
//
// Ejecuta CreditoService.ConfigurarCreditoAsync REAL contra SQL Server real (SQLite serializa
// escritores y escondería la carrera). Invariantes:
//  - CV-01: configurar vs. pasar el crédito a Generado: nunca queda un crédito Generado "pisado"
//    por una configuración posterior (lost update); el perdedor recibe rechazo o conflicto de RowVersion.
//  - CV-02: dos configuraciones simultáneas del mismo circuito: el resultado es uno de los dos
//    planes completos (nunca una mezcla) y la venta queda en Presupuesto.
//  - CV-03: configuraciones cruzadas simultáneas (crédito A con venta B y crédito B con venta A):
//    ambas rechazadas; ningún circuito cambia.
//
// NO corre por defecto. Requiere QA_SEC_CONFIGVENTA_SQL con una cadena a una BD DESCARTABLE cuyo
// Initial Catalog empiece con "qa_sec_configventa" (se crea aplicando las migraciones y se elimina al final).
// Nunca toca la BD de desarrollo.
// ---------------------------------------------------------------------------------------------

public sealed class ConfigurarVentaSqlServerFactAttribute : FactAttribute
{
    public ConfigurarVentaSqlServerFactAttribute()
    {
        var skip = ConfigurarVentaSqlEnv.SkipReason();
        if (skip != null) Skip = skip;
    }
}

internal static class ConfigurarVentaSqlEnv
{
    public static string? ConnectionString => Environment.GetEnvironmentVariable("QA_SEC_CONFIGVENTA_SQL");

    public static string? SkipReason()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            return "BLOCKED: falta QA_SEC_CONFIGVENTA_SQL (BD SQL Server descartable).";
        var catalog = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;
        return catalog.StartsWith("qa_sec_configventa", StringComparison.OrdinalIgnoreCase)
            ? null
            : $"BLOCKED: Initial Catalog '{catalog}' no empieza con 'qa_sec_configventa' (protección de la BD de desarrollo).";
    }
}

public class ConfigurarVentaConcurrenciaSqlServerTests : IAsyncLifetime
{
    private const int Iteraciones = 10;
    private static int _secuencia = 5_000;

    public async Task InitializeAsync()
    {
        if (ConfigurarVentaSqlEnv.SkipReason() != null) return;

        var csb = new SqlConnectionStringBuilder(ConfigurarVentaSqlEnv.ConnectionString);
        var catalogo = csb.InitialCatalog;
        csb.InitialCatalog = "master";
        await using (var c = new SqlConnection(csb.ConnectionString))
        {
            await c.OpenAsync();
            await using var cmd = new SqlCommand(
                $"IF DB_ID(N'{catalogo}') IS NOT NULL BEGIN ALTER DATABASE [{catalogo}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{catalogo}]; END; " +
                $"CREATE DATABASE [{catalogo}];", c);
            await cmd.ExecuteNonQueryAsync();
        }

        // Modo de aislamiento pedido (QA_SEC_CONFIGVENTA_RCSI = 0 / 1), fijado de forma explícita en
        // ambos sentidos: no se confía en lo que herede de `model`. CV-00 lo verifica contra el catálogo real.
        var rcsi = Environment.GetEnvironmentVariable("QA_SEC_CONFIGVENTA_RCSI") == "1" ? "ON" : "OFF";
        csb.InitialCatalog = "master";
        await using (var c = new SqlConnection(csb.ConnectionString))
        {
            await c.OpenAsync();
            await using var cmd = new SqlCommand(
                $"ALTER DATABASE [{catalogo}] SET READ_COMMITTED_SNAPSHOT {rcsi} WITH ROLLBACK IMMEDIATE;", c);
            await cmd.ExecuteNonQueryAsync();
        }

        await using var context = NuevoContexto();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (ConfigurarVentaSqlEnv.SkipReason() != null) return;
        await using var context = NuevoContexto();
        await context.Database.EnsureDeletedAsync();
    }

    [ConfigurarVentaSqlServerFact(DisplayName = "PROMPT 93 [CV-00] el catálogo corre con el modo de aislamiento pedido (RCSI)")]
    public async Task CV00_ModoDeAislamientoDelCatalogo_CoincideConElPedido()
    {
        var pedido = Environment.GetEnvironmentVariable("QA_SEC_CONFIGVENTA_RCSI") == "1" ? 1 : 0;
        await using var c = new SqlConnection(ConfigurarVentaSqlEnv.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT CAST(is_read_committed_snapshot_on AS int) FROM sys.databases WHERE name = DB_NAME()", c);
        Assert.Equal(pedido, (int)(await cmd.ExecuteScalarAsync())!);
    }

    [ConfigurarVentaSqlServerFact(DisplayName = "PROMPT 93 [CV-04] un crédito ya Generado no se reconfigura: rechazo sin mutaciones")]
    public async Task CV04_CreditoYaGenerado_NoSeReconfigura_YNoSeMutaNada()
    {
        var circuito = await SembrarCircuitoAsync();
        await using (var ctx = NuevoContexto())
        {
            var credito = await ctx.Creditos.SingleAsync(c => c.Id == circuito.CreditoId);
            credito.Estado = EstadoCredito.Generado;
            await ctx.SaveChangesAsync();
        }

        var resultados = await Task.WhenAll(
            Capturar(() => NuevoServicio(NuevoContexto()).ConfigurarCreditoAsync(Comando(circuito, cuotas: 6))),
            Capturar(() => NuevoServicio(NuevoContexto()).ConfigurarCreditoAsync(Comando(circuito, cuotas: 12))));

        Assert.All(resultados, r => Assert.IsType<ConfiguracionCreditoRechazadaException>(r));
        await AssertSinMutacionAsync(circuito, EstadoCredito.Generado);
    }

    private static async Task AssertSinMutacionAsync(Circuito c, EstadoCredito estadoCredito)
    {
        await using var verif = NuevoContexto();
        var credito = await verif.Creditos.AsNoTracking().SingleAsync(x => x.Id == c.CreditoId);
        var venta = await verif.Ventas.AsNoTracking().SingleAsync(x => x.Id == c.VentaId);
        Assert.Equal(estadoCredito, credito.Estado);
        Assert.Equal(0, credito.CantidadCuotas);
        Assert.Equal(10_000m, credito.MontoAprobado);
        Assert.Equal(EstadoVenta.PendienteFinanciacion, venta.Estado);
        Assert.Null(venta.FechaConfiguracionCredito);
        Assert.Empty(await verif.Cuotas.AsNoTracking().Where(q => q.CreditoId == c.CreditoId).ToListAsync());
    }

    [ConfigurarVentaSqlServerFact(DisplayName = "PROMPT 90 [CV-01] configurar vs. pasar a Generado: no se pisa un crédito Generado")]
    public async Task CV01_ConfigurarVsGenerado_NoPisaUnCreditoGenerado()
    {
        for (var i = 0; i < Iteraciones; i++)
        {
            var circuito = await SembrarCircuitoAsync();

            var configurar = Capturar(() => NuevoServicio(NuevoContexto()).ConfigurarCreditoAsync(Comando(circuito)));
            var generar = Capturar(async () =>
            {
                await using var ctx = NuevoContexto();
                var credito = await ctx.Creditos.SingleAsync(c => c.Id == circuito.CreditoId);
                await Task.Delay(5);
                credito.Estado = EstadoCredito.Generado;
                await ctx.SaveChangesAsync();
            });

            var resultados = await Task.WhenAll(configurar, generar);

            await using var verif = NuevoContexto();
            var final = await verif.Creditos.AsNoTracking().SingleAsync(c => c.Id == circuito.CreditoId);

            // Si la generación se aplicó, el estado final es Generado; una configuración posterior
            // jamás puede devolverlo a Configurado.
            if (resultados[1] == null)
                Assert.Equal(EstadoCredito.Generado, final.Estado);

            // Si la configuración perdió, no dejó mutaciones parciales (venta, cuotas ni monto).
            if (resultados[0] != null)
            {
                var venta = await verif.Ventas.AsNoTracking().SingleAsync(v => v.Id == circuito.VentaId);
                Assert.Equal(EstadoVenta.PendienteFinanciacion, venta.Estado);
                Assert.Null(venta.FechaConfiguracionCredito);
                Assert.Equal(0, final.CantidadCuotas);
            }

            // Quien perdió falló por rechazo controlado o conflicto de concurrencia, nunca por otra causa.
            foreach (var ex in resultados.Where(r => r != null))
                Assert.True(ex is ConfiguracionCreditoRechazadaException or DbUpdateConcurrencyException,
                    $"Excepción inesperada: {ex}");
        }
    }

    [ConfigurarVentaSqlServerFact(DisplayName = "PROMPT 90 [CV-02] dos configuraciones simultáneas del mismo circuito: plan consistente")]
    public async Task CV02_DosConfiguracionesSimultaneas_DejanUnPlanCompleto()
    {
        for (var i = 0; i < Iteraciones; i++)
        {
            var circuito = await SembrarCircuitoAsync();

            var resultados = await Task.WhenAll(
                Capturar(() => NuevoServicio(NuevoContexto()).ConfigurarCreditoAsync(Comando(circuito, cuotas: 6, anticipo: 0m))),
                Capturar(() => NuevoServicio(NuevoContexto()).ConfigurarCreditoAsync(Comando(circuito, cuotas: 12, anticipo: 1_000m))));

            Assert.Contains(resultados, r => r == null); // al menos una se aplicó
            foreach (var ex in resultados.Where(r => r != null))
                Assert.True(ex is ConfiguracionCreditoRechazadaException or DbUpdateConcurrencyException, $"Excepción inesperada: {ex}");

            await using var verif = NuevoContexto();
            var credito = await verif.Creditos.AsNoTracking().SingleAsync(c => c.Id == circuito.CreditoId);
            var venta = await verif.Ventas.AsNoTracking().SingleAsync(v => v.Id == circuito.VentaId);

            Assert.Equal(EstadoCredito.Configurado, credito.Estado);
            Assert.Equal(EstadoVenta.Presupuesto, venta.Estado);
            // (cuotas, monto aprobado) pertenecen al mismo comando: sin mezcla entre los dos.
            Assert.Contains((credito.CantidadCuotas, credito.MontoAprobado), new[] { (6, 10_000m), (12, 9_000m) });
        }
    }

    [ConfigurarVentaSqlServerFact(DisplayName = "PROMPT 90 [CV-03] configuraciones cruzadas simultáneas: ambas rechazadas, nada cambia")]
    public async Task CV03_ConfiguracionesCruzadasSimultaneas_SeRechazanYNoCambiaNada()
    {
        for (var i = 0; i < Iteraciones; i++)
        {
            var a = await SembrarCircuitoAsync();
            var b = await SembrarCircuitoAsync();

            var resultados = await Task.WhenAll(
                Capturar(() => NuevoServicio(NuevoContexto()).ConfigurarCreditoAsync(Comando(a with { VentaId = b.VentaId }))),
                Capturar(() => NuevoServicio(NuevoContexto()).ConfigurarCreditoAsync(Comando(b with { VentaId = a.VentaId }))));

            Assert.All(resultados, r => Assert.IsType<ConfiguracionCreditoRechazadaException>(r));

            await using var verif = NuevoContexto();
            foreach (var c in new[] { a, b })
            {
                var credito = await verif.Creditos.AsNoTracking().SingleAsync(x => x.Id == c.CreditoId);
                var venta = await verif.Ventas.AsNoTracking().SingleAsync(x => x.Id == c.VentaId);
                Assert.Equal(EstadoCredito.PendienteConfiguracion, credito.Estado);
                Assert.Equal(0, credito.CantidadCuotas);
                Assert.Equal(EstadoVenta.PendienteFinanciacion, venta.Estado);
                Assert.Null(venta.FechaConfiguracionCredito);
            }
        }
    }

    private record Circuito(int ClienteId, int CreditoId, int VentaId);

    private static ConfiguracionCreditoComando Comando(Circuito c, int cuotas = 6, decimal anticipo = 0m) => new()
    {
        CreditoId = c.CreditoId,
        VentaId = c.VentaId,
        Monto = 10_000m,
        Anticipo = anticipo,
        CantidadCuotas = cuotas,
        TasaMensual = 3m,
        FechaPrimeraCuota = DateTime.UtcNow.AddMonths(1),
        MetodoCalculo = MetodoCalculoCredito.Global,
        FuenteConfiguracion = FuenteConfiguracionCredito.Global,
        CuotasMinPermitidas = 1,
        CuotasMaxPermitidas = 24
    };

    private static async Task<Exception?> Capturar(Func<Task> accion)
    {
        try
        {
            await Task.Yield();
            await accion();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task<Circuito> SembrarCircuitoAsync()
    {
        var n = Interlocked.Increment(ref _secuencia);
        await using var ctx = NuevoContexto();

        var cliente = new Cliente { Nombre = "Cliente", Apellido = $"CV{n}", NumeroDocumento = $"8{n:D7}", IsDeleted = false };
        ctx.Clientes.Add(cliente);
        await ctx.SaveChangesAsync();

        var credito = new Credito
        {
            ClienteId = cliente.Id,
            Numero = $"CRE-CV-{n}",
            Estado = EstadoCredito.PendienteConfiguracion,
            MontoSolicitado = 10_000m,
            MontoAprobado = 10_000m,
            SaldoPendiente = 10_000m,
            IsDeleted = false
        };
        ctx.Creditos.Add(credito);
        await ctx.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            CreditoId = credito.Id,
            Numero = $"VTA-CV-{n}",
            Estado = EstadoVenta.PendienteFinanciacion,
            Total = 10_000m,
            IsDeleted = false
        };
        ctx.Ventas.Add(venta);
        await ctx.SaveChangesAsync();

        return new Circuito(cliente.Id, credito.Id, venta.Id);
    }

    private static AppDbContext NuevoContexto() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConfigurarVentaSqlEnv.ConnectionString).Options);

    private static CreditoService NuevoServicio(AppDbContext context)
    {
        var mapper = new MapperConfiguration(cfg => { cfg.AddProfile<MappingProfile>(); }, NullLoggerFactory.Instance).CreateMapper();
        return new CreditoService(
            context,
            mapper,
            NullLogger<CreditoService>.Instance,
            new StubFinancialServiceConfig(),
            new StubCajaServiceConfiguracion(),
            new StubCreditoDisponibleServiceConfig(),
            new StubCurrentUserServiceConfig());
    }
}

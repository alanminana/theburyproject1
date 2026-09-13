using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

[Trait("Category", "CreditoUi")]
[Trait("Category", "PagosAbm")]
[Trait("Category", "CuotaCreditoPersonal")]
public sealed class CreditoPersonalCuotaConfigTests
{
    private static (AppDbContext ctx, SqliteConnection conn) CreateContext()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return (ctx, conn);
    }

    private static ConfiguracionPagoService CreateService(AppDbContext ctx) =>
        new(ctx, null!, NullLogger<ConfiguracionPagoService>.Instance);

    [Fact]
    public async Task GetCuotasCreditoPersonal_SinDatos_DevuelveListaVacia()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);

            var resultado = await service.GetCuotasCreditoPersonalAsync();

            Assert.Empty(resultado);
        }
    }

    [Fact]
    public async Task GuardarCuotasCreditoPersonal_ItemsValidos_PersisteTodos()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);

            var items = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 1, TasaMensual = 1m, Activo = true, Orden = 1 },
                new() { CantidadCuotas = 5, TasaMensual = 10m, Activo = true, Orden = 5 }
            };

            var (ok, errores) = await service.GuardarCuotasCreditoPersonalAsync(items, "test-user");

            Assert.True(ok, string.Join("; ", errores));

            var guardadas = await ctx.ConfiguracionCreditoPersonalCuotas.OrderBy(x => x.CantidadCuotas).ToListAsync();
            Assert.Equal(2, guardadas.Count);
            Assert.Equal(1m, guardadas.First(g => g.CantidadCuotas == 1).TasaMensual);
            Assert.Equal(10m, guardadas.First(g => g.CantidadCuotas == 5).TasaMensual);
        }
    }

    // Un plan ACTIVO sin porcentaje explícito se puede guardar: al resolverse contra una venta
    // hereda el recargo global legacy si existe (ver ResolverPlanesCreditoPersonalAsync). 0 %
    // sigue siendo válido y distinguible de null: se prueba junto en el mismo caso para dejar el
    // contraste explícito.
    [Fact]
    public async Task GuardarCuotasCreditoPersonal_ActivoSinPorcentaje_Persiste()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);

            var (ok, errores) = await service.GuardarCuotasCreditoPersonalAsync(
                new List<CuotaCreditoPersonalViewModel>
                {
                    new() { CantidadCuotas = 2, TasaMensual = null, Activo = true, Orden = 2 }, // sin porcentaje propio: valido, hereda al resolver
                    new() { CantidadCuotas = 3, TasaMensual = 0m, Activo = true, Orden = 3 }    // 0 % explicito: valido
                },
                "test");

            Assert.True(ok, string.Join("; ", errores));

            var guardadas = await ctx.ConfiguracionCreditoPersonalCuotas.OrderBy(x => x.CantidadCuotas).ToListAsync();
            Assert.Equal(2, guardadas.Count);
            Assert.Null(guardadas.First(g => g.CantidadCuotas == 2).TasaMensual);
            Assert.Equal(0m, guardadas.First(g => g.CantidadCuotas == 3).TasaMensual);
        }
    }

    // ML4 — Fase 2/9: un plan INACTIVO puede conservar un porcentaje historico null (no se
    // decide automaticamente que sea invalido; solo importa para venta, que ya filtra por Activo).
    [Fact]
    public async Task GuardarCuotasCreditoPersonal_InactivoSinPorcentaje_Persiste()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);

            var (ok, errores) = await service.GuardarCuotasCreditoPersonalAsync(
                new List<CuotaCreditoPersonalViewModel>
                {
                    new() { CantidadCuotas = 2, TasaMensual = null, Activo = false, Orden = 2 }
                },
                "test");

            Assert.True(ok, string.Join("; ", errores));

            var todas = await service.GetCuotasCreditoPersonalAsync();
            Assert.Null(todas.Single(c => c.CantidadCuotas == 2).TasaMensual);
        }
    }

    // Persistir un plan activo sin porcentaje propio se guarda tal cual (null); el recargo global
    // legacy configurado (10 %) recién se aplica al RESOLVER el plan contra una venta
    // (ResolverPlanesCreditoPersonalAsync), no al guardar.
    [Fact]
    public async Task GuardarCuotasCreditoPersonal_ConRecargoGlobalLegacyConfigurado_PersisteNullYLuegoHereda()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            ctx.ConfiguracionesPago.Add(new TheBuryProject.Models.Entities.ConfiguracionPago
            {
                TipoPago = TheBuryProject.Models.Enums.TipoPago.CreditoPersonal,
                Nombre = "credito personal",
                Activo = true,
                TasaInteresMensualCreditoPersonal = 10m
            });
            await ctx.SaveChangesAsync();

            var service = CreateService(ctx);

            var (ok, errores) = await service.GuardarCuotasCreditoPersonalAsync(
                new List<CuotaCreditoPersonalViewModel>
                {
                    new() { CantidadCuotas = 1, TasaMensual = null, Activo = true, Orden = 1 }
                },
                "test");

            Assert.True(ok, string.Join("; ", errores));
            var guardada = await ctx.ConfiguracionCreditoPersonalCuotas.SingleAsync();
            Assert.Null(guardada.TasaMensual);

            // Al resolver contra una venta (sin producto: solo tabla global), hereda el 10 % legacy.
            var planes = await service.ResolverPlanesCreditoPersonalAsync(Array.Empty<int>());
            Assert.True(planes.EsValido);
            Assert.Equal(10m, planes.BuscarPlan(1)!.TasaMensual);
        }
    }

    // ML4 — Fase 9 (test A/E): plan activo con porcentaje explicito guarda y, al recargar la
    // pantalla (GetCuotasCreditoPersonalAsync, el mismo metodo que usa el GET del controller),
    // devuelve exactamente el mismo valor — sin redondeos ni recalculo.
    [Fact]
    public async Task GuardarCuotasCreditoPersonal_ActivoConPorcentajeExplicito_PersisteYRecargaIgual()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);

            var (ok, errores) = await service.GuardarCuotasCreditoPersonalAsync(
                new List<CuotaCreditoPersonalViewModel>
                {
                    new() { CantidadCuotas = 6, TasaMensual = 8m, Activo = true, Orden = 6 }
                },
                "test");

            Assert.True(ok, string.Join("; ", errores));

            var recargado = await service.GetCuotasCreditoPersonalAsync();
            Assert.Equal(8m, recargado.Single(c => c.CantidadCuotas == 6).TasaMensual);
        }
    }

    [Fact]
    public async Task GuardarCuotasCreditoPersonal_ActualizaExistenteEnLugarDeDuplicar()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);
            await service.GuardarCuotasCreditoPersonalAsync(
                new List<CuotaCreditoPersonalViewModel> { new() { CantidadCuotas = 3, TasaMensual = 2m, Activo = true } },
                "test");

            var existente = await ctx.ConfiguracionCreditoPersonalCuotas.FirstAsync(x => x.CantidadCuotas == 3);

            var (ok, _) = await service.GuardarCuotasCreditoPersonalAsync(
                new List<CuotaCreditoPersonalViewModel> { new() { Id = existente.Id, CantidadCuotas = 3, TasaMensual = 4m, Activo = false } },
                "test");

            Assert.True(ok);
            var total = await ctx.ConfiguracionCreditoPersonalCuotas.CountAsync();
            Assert.Equal(1, total);
            var actualizada = await ctx.ConfiguracionCreditoPersonalCuotas.FindAsync(existente.Id);
            Assert.Equal(4m, actualizada!.TasaMensual);
            Assert.False(actualizada.Activo);
        }
    }

    [Fact]
    public async Task GuardarCuotasCreditoPersonal_CantidadesDuplicadas_Rechaza()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);
            var items = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 5m, Activo = true },
                new() { CantidadCuotas = 6, TasaMensual = 8m, Activo = true }
            };

            var (ok, errores) = await service.GuardarCuotasCreditoPersonalAsync(items, "test");

            Assert.False(ok);
            Assert.Contains(errores, e => e.Contains("duplicad", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task GuardarCuotasCreditoPersonal_FueraDeRango_Rechaza()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);
            var items = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 0, TasaMensual = 1m, Activo = true }
            };

            var (ok, errores) = await service.GuardarCuotasCreditoPersonalAsync(items, "test");

            Assert.False(ok);
            Assert.Contains(errores, e => e.Contains("rango", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task GuardarCuotasCreditoPersonal_ListaVacia_PersisteSinError()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);

            var (ok, errores) = await service.GuardarCuotasCreditoPersonalAsync(new List<CuotaCreditoPersonalViewModel>(), "test");

            Assert.True(ok, string.Join("; ", errores));
            Assert.Empty(await ctx.ConfiguracionCreditoPersonalCuotas.ToListAsync());
        }
    }

    [Fact]
    public async Task GetCuotasCreditoPersonalActivas_FiltraInactivas()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);
            await service.GuardarCuotasCreditoPersonalAsync(
                new List<CuotaCreditoPersonalViewModel>
                {
                    new() { CantidadCuotas = 1, TasaMensual = 1m, Activo = true },
                    new() { CantidadCuotas = 2, TasaMensual = 2m, Activo = false }
                },
                "test");

            var activas = await service.GetCuotasCreditoPersonalActivasAsync();

            Assert.Single(activas);
            Assert.Equal(1, activas[0].CantidadCuotas);
        }
    }

    // -------------------------------------------------------------------------
    // Micro-lote 5: 0 % es un recargo válido y distinguible de "inexistente"/"inactivo".
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetCuotasCreditoPersonal_PlanActivoDe3CuotasConCeroPorciento_EsRecuperable()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);
            await service.GuardarCuotasCreditoPersonalAsync(
                new List<CuotaCreditoPersonalViewModel>
                {
                    new() { CantidadCuotas = 3, TasaMensual = 0m, Activo = true, Orden = 3 }
                },
                "test");

            var activas = await service.GetCuotasCreditoPersonalActivasAsync();
            var plan3 = activas.SingleOrDefault(c => c.CantidadCuotas == 3);

            Assert.NotNull(plan3);
            Assert.True(plan3!.Activo);
            Assert.Equal(0m, plan3.TasaMensual);
        }
    }

    [Fact]
    public async Task GetCuotasCreditoPersonal_PlanInactivo_NoApareceEnActivasPeroSiEnListaCompleta()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);
            await service.GuardarCuotasCreditoPersonalAsync(
                new List<CuotaCreditoPersonalViewModel>
                {
                    new() { CantidadCuotas = 6, TasaMensual = 5m, Activo = false }
                },
                "test");

            var activas = await service.GetCuotasCreditoPersonalActivasAsync();
            var todas = await service.GetCuotasCreditoPersonalAsync();

            Assert.DoesNotContain(activas, c => c.CantidadCuotas == 6);
            Assert.Contains(todas, c => c.CantidadCuotas == 6 && !c.Activo);
        }
    }

    [Fact]
    public async Task GuardarCuotasCreditoPersonal_TasaNegativa_Rechaza()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);
            var items = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 4, TasaMensual = -1m, Activo = true }
            };

            var (ok, errores) = await service.GuardarCuotasCreditoPersonalAsync(items, "test");

            Assert.False(ok);
            Assert.Contains(errores, e => e.Contains("negativ", StringComparison.OrdinalIgnoreCase));

            var enDb = await ctx.ConfiguracionCreditoPersonalCuotas.ToListAsync();
            Assert.Empty(enDb);
        }
    }

    // -------------------------------------------------------------------------
    // CSR-ML5 — GetCuotasCreditoPersonalAsync (usado por el GET del admin) batch-carga
    // CuotasSinRecargo por plan (CSR-ML5.UI1/UI10): sin N+1 (misma query batch que ya usa
    // ResolverPlanesCreditoPersonalAsync desde CSR-ML4) y sin fallback cuando no hay selección.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetCuotasCreditoPersonal_PlanConSeleccionUnoTresCinco_DevuelveExactamenteEsos()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);
            await service.GuardarCuotasCreditoPersonalAsync(
                new List<CuotaCreditoPersonalViewModel>
                {
                    new() { CantidadCuotas = 10, TasaMensual = 10m, Activo = true, Orden = 10 }
                },
                "test");
            var plan = await ctx.ConfiguracionCreditoPersonalCuotas.SingleAsync(c => c.CantidadCuotas == 10);

            var (ok, errores) = await service.GuardarCuotasSinRecargoCreditoPersonalAsync(plan.Id, new[] { 1, 3, 5 }, "test");
            Assert.True(ok, string.Join("; ", errores));

            var todas = await service.GetCuotasCreditoPersonalAsync();

            Assert.Equal(new[] { 1, 3, 5 }, todas.Single(c => c.CantidadCuotas == 10).CuotasSinRecargo);
        }
    }

    [Fact]
    public async Task GetCuotasCreditoPersonal_PlanSinConfiguracionDeCuotasSinRecargo_DevuelveListaVaciaSinFallback()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);
            await service.GuardarCuotasCreditoPersonalAsync(
                new List<CuotaCreditoPersonalViewModel>
                {
                    new() { CantidadCuotas = 12, TasaMensual = 5m, Activo = true, Orden = 12 }
                },
                "test");

            var todas = await service.GetCuotasCreditoPersonalAsync();

            Assert.Empty(todas.Single(c => c.CantidadCuotas == 12).CuotasSinRecargo);
        }
    }

    [Fact]
    public async Task GetCuotasCreditoPersonal_VariosPlanes_CadaUnoConSuPropiaSeleccion_SinMezclarlas()
    {
        var (ctx, conn) = CreateContext();
        using (conn)
        {
            var service = CreateService(ctx);
            await service.GuardarCuotasCreditoPersonalAsync(
                new List<CuotaCreditoPersonalViewModel>
                {
                    new() { CantidadCuotas = 6, TasaMensual = 10m, Activo = true, Orden = 6 },
                    new() { CantidadCuotas = 10, TasaMensual = 10m, Activo = true, Orden = 10 }
                },
                "test");
            var plan6 = await ctx.ConfiguracionCreditoPersonalCuotas.SingleAsync(c => c.CantidadCuotas == 6);
            var plan10 = await ctx.ConfiguracionCreditoPersonalCuotas.SingleAsync(c => c.CantidadCuotas == 10);

            await service.GuardarCuotasSinRecargoCreditoPersonalAsync(plan6.Id, new[] { 1, 2 }, "test");
            await service.GuardarCuotasSinRecargoCreditoPersonalAsync(plan10.Id, new[] { 2, 10 }, "test");

            var todas = await service.GetCuotasCreditoPersonalAsync();

            Assert.Equal(new[] { 1, 2 }, todas.Single(c => c.CantidadCuotas == 6).CuotasSinRecargo);
            Assert.Equal(new[] { 2, 10 }, todas.Single(c => c.CantidadCuotas == 10).CuotasSinRecargo);
        }
    }
}

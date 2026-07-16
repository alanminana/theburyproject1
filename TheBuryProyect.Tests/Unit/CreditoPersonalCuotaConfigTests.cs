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
}

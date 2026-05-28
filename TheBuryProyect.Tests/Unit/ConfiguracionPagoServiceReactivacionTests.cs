using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

[Trait("Category", "PagosAbm")]
[Trait("Category", "ConfiguracionPago")]
public sealed class ConfiguracionPagoServiceReactivacionTests
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

    private static ConfiguracionPago MedioCredito(int id = 1) => new()
    {
        Id = id,
        TipoPago = TipoPago.TarjetaCredito,
        Nombre = "Tarjeta credito",
        Activo = true,
        RowVersion = new byte[8]
    };

    private static ConfiguracionPago MedioEfectivo(int id = 10) => new()
    {
        Id = id,
        TipoPago = TipoPago.Efectivo,
        Nombre = "Efectivo",
        Activo = true,
        RowVersion = new byte[8]
    };

    // -----------------------------------------------------------------------
    // Punto 3 — Tarjeta: reactivacion de inactiva equivalente
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CrearTarjeta_InactivaEquivalente_ReactivalaSinCrearNueva()
    {
        var (ctx, conn) = CreateContext();
        await using (conn)
        await using (ctx)
        {
            var medio = MedioCredito();
            ctx.ConfiguracionesPago.Add(medio);
            var inactiva = new ConfiguracionTarjeta
            {
                ConfiguracionPagoId = 1,
                NombreTarjeta = "Visa",
                TipoTarjeta = TipoTarjeta.Credito,
                Activa = false,
                RowVersion = new byte[8]
            };
            ctx.ConfiguracionesTarjeta.Add(inactiva);
            await ctx.SaveChangesAsync();

            var service = CreateService(ctx);
            var result = await service.CrearTarjetaGlobalAsync(new TarjetaGlobalCommandViewModel
            {
                ConfiguracionPagoId = 1,
                NombreTarjeta = "Visa",
                TipoTarjeta = TipoTarjeta.Credito,
                Activa = true
            });

            Assert.Equal(inactiva.Id, result.Id);
            Assert.True(result.Activa);

            var count = await ctx.ConfiguracionesTarjeta.CountAsync(t => t.ConfiguracionPagoId == 1 && !t.IsDeleted);
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task CrearTarjeta_InactivaEquivalente_ActualizaObservaciones()
    {
        var (ctx, conn) = CreateContext();
        await using (conn)
        await using (ctx)
        {
            var medio = MedioCredito();
            ctx.ConfiguracionesPago.Add(medio);
            ctx.ConfiguracionesTarjeta.Add(new ConfiguracionTarjeta
            {
                ConfiguracionPagoId = 1,
                NombreTarjeta = "Mastercard",
                TipoTarjeta = TipoTarjeta.Credito,
                Activa = false,
                Observaciones = "vieja",
                RowVersion = new byte[8]
            });
            await ctx.SaveChangesAsync();

            var service = CreateService(ctx);
            var result = await service.CrearTarjetaGlobalAsync(new TarjetaGlobalCommandViewModel
            {
                ConfiguracionPagoId = 1,
                NombreTarjeta = "Mastercard",
                TipoTarjeta = TipoTarjeta.Credito,
                Activa = true,
                Observaciones = "nueva"
            });

            Assert.Equal("nueva", result.Observaciones);
        }
    }

    [Fact]
    public async Task CrearTarjeta_ActivaEquivalente_LanzaExcepcionDuplicado()
    {
        var (ctx, conn) = CreateContext();
        await using (conn)
        await using (ctx)
        {
            var medio = MedioCredito();
            ctx.ConfiguracionesPago.Add(medio);
            ctx.ConfiguracionesTarjeta.Add(new ConfiguracionTarjeta
            {
                ConfiguracionPagoId = 1,
                NombreTarjeta = "Visa",
                TipoTarjeta = TipoTarjeta.Credito,
                Activa = true,
                RowVersion = new byte[8]
            });
            await ctx.SaveChangesAsync();

            var service = CreateService(ctx);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CrearTarjetaGlobalAsync(new TarjetaGlobalCommandViewModel
                {
                    ConfiguracionPagoId = 1,
                    NombreTarjeta = "Visa",
                    TipoTarjeta = TipoTarjeta.Credito,
                    Activa = true
                }));
        }
    }

    [Fact]
    public async Task CambiarEstadoTarjeta_Quitar_NoEliminaFisicamente()
    {
        var (ctx, conn) = CreateContext();
        await using (conn)
        await using (ctx)
        {
            var medio = MedioCredito();
            ctx.ConfiguracionesPago.Add(medio);
            var tarjeta = new ConfiguracionTarjeta
            {
                ConfiguracionPagoId = 1,
                NombreTarjeta = "Visa",
                TipoTarjeta = TipoTarjeta.Credito,
                Activa = true,
                RowVersion = new byte[8]
            };
            ctx.ConfiguracionesTarjeta.Add(tarjeta);
            await ctx.SaveChangesAsync();

            var service = CreateService(ctx);
            var ok = await service.CambiarEstadoTarjetaGlobalAsync(tarjeta.Id, activa: false);

            Assert.True(ok);
            var enBd = await ctx.ConfiguracionesTarjeta.FindAsync(tarjeta.Id);
            Assert.NotNull(enBd);
            Assert.False(enBd!.IsDeleted);
            Assert.False(enBd.Activa);
        }
    }

    // -----------------------------------------------------------------------
    // Punto 4 — Plan/cuota: reactivacion de inactivo equivalente
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CrearPlan_InactivoEquivalente_ReactivaYActualizaAjuste()
    {
        var (ctx, conn) = CreateContext();
        await using (conn)
        await using (ctx)
        {
            var medio = MedioEfectivo();
            ctx.ConfiguracionesPago.Add(medio);
            var planInactivo = new ConfiguracionPagoPlan
            {
                ConfiguracionPagoId = medio.Id,
                TipoPago = TipoPago.Efectivo,
                CantidadCuotas = 3,
                Activo = false,
                TipoAjuste = TipoAjustePagoPlan.Porcentaje,
                AjustePorcentaje = 0m,
                RowVersion = new byte[8]
            };
            ctx.ConfiguracionPagoPlanes.Add(planInactivo);
            await ctx.SaveChangesAsync();

            var service = CreateService(ctx);
            var result = await service.CrearPlanGlobalAsync(new PlanPagoGlobalCommandViewModel
            {
                ConfiguracionPagoId = medio.Id,
                CantidadCuotas = 3,
                AjustePorcentaje = 5m,
                TipoAjuste = TipoAjustePagoPlan.Porcentaje,
                Activo = true,
                Etiqueta = "3 cuotas con recargo"
            });

            Assert.Equal(planInactivo.Id, result.Id);
            Assert.True(result.Activo);
            Assert.Equal(5m, result.AjustePorcentaje);
            Assert.Equal("3 cuotas con recargo", result.Etiqueta);

            var count = await ctx.ConfiguracionPagoPlanes.CountAsync(p => p.ConfiguracionPagoId == medio.Id && !p.IsDeleted);
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task CrearPlan_ActivoEquivalente_LanzaExcepcionDuplicado()
    {
        var (ctx, conn) = CreateContext();
        await using (conn)
        await using (ctx)
        {
            var medio = MedioEfectivo();
            ctx.ConfiguracionesPago.Add(medio);
            ctx.ConfiguracionPagoPlanes.Add(new ConfiguracionPagoPlan
            {
                ConfiguracionPagoId = medio.Id,
                TipoPago = TipoPago.Efectivo,
                CantidadCuotas = 1,
                Activo = true,
                TipoAjuste = TipoAjustePagoPlan.Porcentaje,
                AjustePorcentaje = 0m,
                RowVersion = new byte[8]
            });
            await ctx.SaveChangesAsync();

            var service = CreateService(ctx);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CrearPlanGlobalAsync(new PlanPagoGlobalCommandViewModel
                {
                    ConfiguracionPagoId = medio.Id,
                    CantidadCuotas = 1,
                    AjustePorcentaje = 2m,
                    TipoAjuste = TipoAjustePagoPlan.Porcentaje,
                    Activo = true
                }));
        }
    }

    [Fact]
    public async Task CambiarEstadoPlan_Quitar_NoEliminaFisicamente()
    {
        var (ctx, conn) = CreateContext();
        await using (conn)
        await using (ctx)
        {
            var medio = MedioEfectivo();
            ctx.ConfiguracionesPago.Add(medio);
            var plan = new ConfiguracionPagoPlan
            {
                ConfiguracionPagoId = medio.Id,
                TipoPago = TipoPago.Efectivo,
                CantidadCuotas = 1,
                Activo = true,
                TipoAjuste = TipoAjustePagoPlan.Porcentaje,
                AjustePorcentaje = 0m,
                RowVersion = new byte[8]
            };
            ctx.ConfiguracionPagoPlanes.Add(plan);
            await ctx.SaveChangesAsync();

            var service = CreateService(ctx);
            var ok = await service.CambiarEstadoPlanGlobalAsync(plan.Id, activo: false);

            Assert.True(ok);
            var enBd = await ctx.ConfiguracionPagoPlanes.FindAsync(plan.Id);
            Assert.NotNull(enBd);
            Assert.False(enBd!.IsDeleted);
            Assert.False(enBd.Activo);
        }
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

public sealed class ConfiguracionPagoPlanEfTests
{
    [Fact]
    public async Task ConfiguracionPagoPlan_PersistePlanGeneralSinTarjeta()
    {
        await using var fixture = await ConfiguracionPagoPlanDbFixture.CreateAsync();
        var configuracion = await fixture.SeedConfiguracionPagoAsync(TipoPago.Efectivo);

        fixture.Context.ConfiguracionPagoPlanes.Add(new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = configuracion.Id,
            TipoPago = TipoPago.Efectivo,
            CantidadCuotas = 1,
            AjustePorcentaje = -5m,
            Etiqueta = "Efectivo -5%",
            Orden = 1
        });
        await fixture.Context.SaveChangesAsync();

        var plan = await fixture.Context.ConfiguracionPagoPlanes.SingleAsync();

        Assert.Equal(configuracion.Id, plan.ConfiguracionPagoId);
        Assert.Null(plan.ConfiguracionTarjetaId);
        Assert.Equal(TipoPago.Efectivo, plan.TipoPago);
        Assert.Equal("Efectivo -5%", plan.Etiqueta);
        Assert.NotEmpty(plan.RowVersion);
    }

    [Fact]
    public async Task ConfiguracionPagoPlan_PersistePlanAsociadoATarjeta()
    {
        await using var fixture = await ConfiguracionPagoPlanDbFixture.CreateAsync();
        var (configuracion, tarjeta) = await fixture.SeedTarjetaAsync();

        fixture.Context.ConfiguracionPagoPlanes.Add(new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = configuracion.Id,
            ConfiguracionTarjetaId = tarjeta.Id,
            TipoPago = TipoPago.TarjetaCredito,
            CantidadCuotas = 6,
            AjustePorcentaje = 10m,
            Etiqueta = "6 cuotas"
        });
        await fixture.Context.SaveChangesAsync();

        var plan = await fixture.Context.ConfiguracionPagoPlanes
            .Include(p => p.ConfiguracionTarjeta)
            .SingleAsync();

        Assert.Equal(tarjeta.Id, plan.ConfiguracionTarjetaId);
        Assert.Equal(tarjeta.NombreTarjeta, plan.ConfiguracionTarjeta!.NombreTarjeta);
    }

    [Fact]
    public async Task ConfiguracionPagoPlan_RespetaPrecisionDecimalDeAjustePorcentaje()
    {
        await using var fixture = await ConfiguracionPagoPlanDbFixture.CreateAsync();
        var configuracion = await fixture.SeedConfiguracionPagoAsync(TipoPago.Transferencia);

        fixture.Context.ConfiguracionPagoPlanes.Add(new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = configuracion.Id,
            TipoPago = TipoPago.Transferencia,
            CantidadCuotas = 1,
            AjustePorcentaje = 12.3456m
        });
        await fixture.Context.SaveChangesAsync();

        var plan = await fixture.Context.ConfiguracionPagoPlanes.SingleAsync();
        Assert.Equal(12.3456m, plan.AjustePorcentaje);
    }

    [Fact]
    public async Task ConfiguracionPagoPlan_EvitaDuplicadoActivoParaMismoMedioTarjetaYCuotas()
    {
        await using var fixture = await ConfiguracionPagoPlanDbFixture.CreateAsync();
        var (configuracion, tarjeta) = await fixture.SeedTarjetaAsync();

        fixture.Context.ConfiguracionPagoPlanes.AddRange(
            new ConfiguracionPagoPlan
            {
                ConfiguracionPagoId = configuracion.Id,
                ConfiguracionTarjetaId = tarjeta.Id,
                TipoPago = TipoPago.TarjetaCredito,
                CantidadCuotas = 6,
                AjustePorcentaje = 0m
            },
            new ConfiguracionPagoPlan
            {
                ConfiguracionPagoId = configuracion.Id,
                ConfiguracionTarjetaId = tarjeta.Id,
                TipoPago = TipoPago.TarjetaCredito,
                CantidadCuotas = 6,
                AjustePorcentaje = 5m
            });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ConfiguracionPagoPlan_PermiteMismasCuotasParaDistintaTarjeta()
    {
        await using var fixture = await ConfiguracionPagoPlanDbFixture.CreateAsync();
        var configuracion = await fixture.SeedConfiguracionPagoAsync(TipoPago.TarjetaCredito);
        var visa = await fixture.SeedTarjetaAsync(configuracion, "Visa");
        var master = await fixture.SeedTarjetaAsync(configuracion, "Mastercard");

        fixture.Context.ConfiguracionPagoPlanes.AddRange(
            new ConfiguracionPagoPlan
            {
                ConfiguracionPagoId = configuracion.Id,
                ConfiguracionTarjetaId = visa.Id,
                TipoPago = TipoPago.TarjetaCredito,
                CantidadCuotas = 6,
                AjustePorcentaje = 0m
            },
            new ConfiguracionPagoPlan
            {
                ConfiguracionPagoId = configuracion.Id,
                ConfiguracionTarjetaId = master.Id,
                TipoPago = TipoPago.TarjetaCredito,
                CantidadCuotas = 6,
                AjustePorcentaje = 5m
            });

        await fixture.Context.SaveChangesAsync();

        var planes = await fixture.Context.ConfiguracionPagoPlanes.ToListAsync();
        Assert.Equal(2, planes.Count);
    }

    [Fact]
    public async Task ConfiguracionPagoPlan_PermitePlanInactivoHistoricoConMismasCuotas()
    {
        await using var fixture = await ConfiguracionPagoPlanDbFixture.CreateAsync();
        var configuracion = await fixture.SeedConfiguracionPagoAsync(TipoPago.MercadoPago);

        fixture.Context.ConfiguracionPagoPlanes.AddRange(
            new ConfiguracionPagoPlan
            {
                ConfiguracionPagoId = configuracion.Id,
                TipoPago = TipoPago.MercadoPago,
                CantidadCuotas = 3,
                Activo = false,
                AjustePorcentaje = 0m
            },
            new ConfiguracionPagoPlan
            {
                ConfiguracionPagoId = configuracion.Id,
                TipoPago = TipoPago.MercadoPago,
                CantidadCuotas = 3,
                Activo = true,
                AjustePorcentaje = 8m
            });

        await fixture.Context.SaveChangesAsync();

        var planes = await fixture.Context.ConfiguracionPagoPlanes.ToListAsync();
        Assert.Equal(2, planes.Count);
    }

    [Fact]
    public async Task ConfiguracionPagoPlan_RelacionConConfiguracionPagoFunciona()
    {
        await using var fixture = await ConfiguracionPagoPlanDbFixture.CreateAsync();
        var configuracion = await fixture.SeedConfiguracionPagoAsync(TipoPago.Cheque);

        fixture.Context.ConfiguracionPagoPlanes.Add(new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = configuracion.Id,
            TipoPago = TipoPago.Cheque,
            CantidadCuotas = 1,
            AjustePorcentaje = 0m
        });
        await fixture.Context.SaveChangesAsync();

        var configRecargada = await fixture.Context.ConfiguracionesPago
            .Include(c => c.PlanesPago)
            .SingleAsync(c => c.Id == configuracion.Id);

        Assert.Single(configRecargada.PlanesPago);
    }

    [Fact]
    public async Task ConfiguracionPagoPlan_RelacionConConfiguracionTarjetaFunciona()
    {
        await using var fixture = await ConfiguracionPagoPlanDbFixture.CreateAsync();
        var (configuracion, tarjeta) = await fixture.SeedTarjetaAsync();

        fixture.Context.ConfiguracionPagoPlanes.Add(new ConfiguracionPagoPlan
        {
            ConfiguracionPagoId = configuracion.Id,
            ConfiguracionTarjetaId = tarjeta.Id,
            TipoPago = TipoPago.TarjetaCredito,
            CantidadCuotas = 12,
            AjustePorcentaje = 15m
        });
        await fixture.Context.SaveChangesAsync();

        var tarjetaRecargada = await fixture.Context.ConfiguracionesTarjeta
            .Include(t => t.PlanesPago)
            .SingleAsync(t => t.Id == tarjeta.Id);

        Assert.Single(tarjetaRecargada.PlanesPago);
    }

    private sealed class ConfiguracionPagoPlanDbFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ConfiguracionPagoPlanDbFixture(SqliteConnection connection, AppDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public AppDbContext Context { get; }

        public static async Task<ConfiguracionPagoPlanDbFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            return new ConfiguracionPagoPlanDbFixture(connection, context);
        }

        public async Task<ConfiguracionPago> SeedConfiguracionPagoAsync(TipoPago tipoPago)
        {
            var configuracion = new ConfiguracionPago
            {
                TipoPago = tipoPago,
                Nombre = $"{tipoPago}-{Guid.NewGuid():N}"[..32]
            };

            Context.ConfiguracionesPago.Add(configuracion);
            await Context.SaveChangesAsync();
            return configuracion;
        }

        public async Task<(ConfiguracionPago Configuracion, ConfiguracionTarjeta Tarjeta)> SeedTarjetaAsync()
        {
            var configuracion = await SeedConfiguracionPagoAsync(TipoPago.TarjetaCredito);
            var tarjeta = await SeedTarjetaAsync(configuracion, "Visa");
            return (configuracion, tarjeta);
        }

        public async Task<ConfiguracionTarjeta> SeedTarjetaAsync(ConfiguracionPago configuracion, string nombre)
        {
            var tarjeta = new ConfiguracionTarjeta
            {
                ConfiguracionPagoId = configuracion.Id,
                NombreTarjeta = $"{nombre}-{Guid.NewGuid():N}"[..32],
                TipoTarjeta = TipoTarjeta.Credito,
                Activa = true,
                PermiteCuotas = true,
                CantidadMaximaCuotas = 12
            };

            Context.ConfiguracionesTarjeta.Add(tarjeta);
            await Context.SaveChangesAsync();
            return tarjeta;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}

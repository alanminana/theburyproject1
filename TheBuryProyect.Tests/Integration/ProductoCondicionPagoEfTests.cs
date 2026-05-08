using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

public sealed class ProductoCondicionPagoEfTests
{
    [Fact]
    public async Task ProductoCondicionPago_PersisteReglaGeneralYEspecificaSinNormalizarTarjetaLegacy()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var tarjeta = await fixture.SeedTarjetaAsync();

        fixture.Context.ProductoCondicionesPago.Add(new ProductoCondicionPago
        {
            ProductoId = producto.Id,
            TipoPago = TipoPago.Tarjeta,
            Permitido = true,
            MaxCuotasSinInteres = 6,
            Observaciones = "Regla legacy explicitamente persistida",
            Tarjetas =
            {
                new ProductoCondicionPagoTarjeta
                {
                    ConfiguracionTarjetaId = null,
                    Permitido = true,
                    MaxCuotasSinInteres = 3
                },
                new ProductoCondicionPagoTarjeta
                {
                    ConfiguracionTarjetaId = tarjeta.Id,
                    Permitido = false,
                    PorcentajeRecargo = 5m
                }
            }
        });
        await fixture.Context.SaveChangesAsync();

        var condicion = await fixture.Context.ProductoCondicionesPago
            .Include(c => c.Tarjetas)
            .SingleAsync(c => c.ProductoId == producto.Id);

        Assert.Equal(TipoPago.Tarjeta, condicion.TipoPago);
        Assert.NotEmpty(condicion.RowVersion);
        Assert.Equal(2, condicion.Tarjetas.Count);
        Assert.Contains(condicion.Tarjetas, t => t.ConfiguracionTarjetaId is null);
        Assert.Contains(condicion.Tarjetas, t => t.ConfiguracionTarjetaId == tarjeta.Id && t.Permitido == false);
    }

    [Fact]
    public async Task ProductoCondicionPago_NoPermiteDosCondicionesActivasParaMismoProductoYTipoPago()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();

        fixture.Context.ProductoCondicionesPago.AddRange(
            new ProductoCondicionPago { ProductoId = producto.Id, TipoPago = TipoPago.CreditoPersonal },
            new ProductoCondicionPago { ProductoId = producto.Id, TipoPago = TipoPago.CreditoPersonal });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCondicionPagoTarjeta_NoPermiteDuplicarReglaGeneralActiva()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionesPagoTarjeta.AddRange(
            new ProductoCondicionPagoTarjeta { ProductoCondicionPagoId = condicion.Id, ConfiguracionTarjetaId = null },
            new ProductoCondicionPagoTarjeta { ProductoCondicionPagoId = condicion.Id, ConfiguracionTarjetaId = null });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCondicionPagoTarjeta_NoPermiteDuplicarReglaEspecificaActiva()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var tarjeta = await fixture.SeedTarjetaAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionesPagoTarjeta.AddRange(
            new ProductoCondicionPagoTarjeta { ProductoCondicionPagoId = condicion.Id, ConfiguracionTarjetaId = tarjeta.Id },
            new ProductoCondicionPagoTarjeta { ProductoCondicionPagoId = condicion.Id, ConfiguracionTarjetaId = tarjeta.Id });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCondicionPago_RechazaCuotasMenoresAUno()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();

        fixture.Context.ProductoCondicionesPago.Add(new ProductoCondicionPago
        {
            ProductoId = producto.Id,
            TipoPago = TipoPago.CreditoPersonal,
            MaxCuotasCredito = 0
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCondicionPago_RechazaPorcentajesFueraDeRango()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();

        fixture.Context.ProductoCondicionesPago.Add(new ProductoCondicionPago
        {
            ProductoId = producto.Id,
            TipoPago = TipoPago.Efectivo,
            PorcentajeDescuentoMaximo = 101m
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCondicionPago_RechazaPorcentajeNegativoActual()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();

        fixture.Context.ProductoCondicionesPago.Add(new ProductoCondicionPago
        {
            ProductoId = producto.Id,
            TipoPago = TipoPago.TarjetaCredito,
            PorcentajeRecargo = -1m
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCondicionPagoTarjeta_RechazaPorcentajeNegativoActual()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionesPagoTarjeta.Add(new ProductoCondicionPagoTarjeta
        {
            ProductoCondicionPagoId = condicion.Id,
            PorcentajeDescuentoMaximo = -1m
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    // ============================================================
    // ProductoCondicionPagoPlan — Fase 15.3
    // ============================================================

    [Fact]
    public async Task ProductoCondicionPagoPlan_PersistePlanActivoConAjustePositivo()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionPagoPlanes.Add(new ProductoCondicionPagoPlan
        {
            ProductoCondicionPagoId = condicion.Id,
            CantidadCuotas = 6,
            Activo = true,
            AjustePorcentaje = 5.5m
        });
        await fixture.Context.SaveChangesAsync();

        var plan = await fixture.Context.ProductoCondicionPagoPlanes.SingleAsync(p => p.ProductoCondicionPagoId == condicion.Id);
        Assert.Equal(6, plan.CantidadCuotas);
        Assert.True(plan.Activo);
        Assert.Equal(5.5m, plan.AjustePorcentaje);
        Assert.NotEmpty(plan.RowVersion);
    }

    [Fact]
    public async Task ProductoCondicionPagoPlan_PersistePlanActivoConAjusteCero()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.CreditoPersonal);

        fixture.Context.ProductoCondicionPagoPlanes.Add(new ProductoCondicionPagoPlan
        {
            ProductoCondicionPagoId = condicion.Id,
            CantidadCuotas = 3,
            Activo = true,
            AjustePorcentaje = 0m
        });
        await fixture.Context.SaveChangesAsync();

        var plan = await fixture.Context.ProductoCondicionPagoPlanes.SingleAsync(p => p.ProductoCondicionPagoId == condicion.Id);
        Assert.Equal(0m, plan.AjustePorcentaje);
    }

    [Fact]
    public async Task ProductoCondicionPagoPlan_PersistePlanActivoConAjusteNegativo()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionPagoPlanes.Add(new ProductoCondicionPagoPlan
        {
            ProductoCondicionPagoId = condicion.Id,
            CantidadCuotas = 1,
            Activo = true,
            AjustePorcentaje = -10m
        });
        await fixture.Context.SaveChangesAsync();

        var plan = await fixture.Context.ProductoCondicionPagoPlanes.SingleAsync(p => p.ProductoCondicionPagoId == condicion.Id);
        Assert.Equal(-10m, plan.AjustePorcentaje);
    }

    [Fact]
    public async Task ProductoCondicionPagoPlan_PermiteActivoFalsePersistido()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionPagoPlanes.Add(new ProductoCondicionPagoPlan
        {
            ProductoCondicionPagoId = condicion.Id,
            CantidadCuotas = 12,
            Activo = false,
            AjustePorcentaje = 0m
        });
        await fixture.Context.SaveChangesAsync();

        var plan = await fixture.Context.ProductoCondicionPagoPlanes
            .IgnoreQueryFilters()
            .SingleAsync(p => p.ProductoCondicionPagoId == condicion.Id);
        Assert.False(plan.Activo);
    }

    [Fact]
    public async Task ProductoCondicionPagoPlan_RechazaCantidadCuotasMenorAUno()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionPagoPlanes.Add(new ProductoCondicionPagoPlan
        {
            ProductoCondicionPagoId = condicion.Id,
            CantidadCuotas = 0,
            AjustePorcentaje = 0m
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCondicionPagoPlan_PermiteMultiplesCuotasDistintasEnMismaCondicion()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionPagoPlanes.AddRange(
            new ProductoCondicionPagoPlan { ProductoCondicionPagoId = condicion.Id, CantidadCuotas = 3, AjustePorcentaje = 0m },
            new ProductoCondicionPagoPlan { ProductoCondicionPagoId = condicion.Id, CantidadCuotas = 6, AjustePorcentaje = 5m },
            new ProductoCondicionPagoPlan { ProductoCondicionPagoId = condicion.Id, CantidadCuotas = 12, AjustePorcentaje = 10m });

        await fixture.Context.SaveChangesAsync();

        var planes = await fixture.Context.ProductoCondicionPagoPlanes
            .Where(p => p.ProductoCondicionPagoId == condicion.Id)
            .ToListAsync();
        Assert.Equal(3, planes.Count);
    }

    [Fact]
    public async Task ProductoCondicionPagoPlan_EvitaDuplicadoDeMismaCuotaEnMismaCondicionGeneral()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = await fixture.SeedCondicionAsync(producto.Id, TipoPago.TarjetaCredito);

        fixture.Context.ProductoCondicionPagoPlanes.AddRange(
            new ProductoCondicionPagoPlan { ProductoCondicionPagoId = condicion.Id, CantidadCuotas = 6, AjustePorcentaje = 0m },
            new ProductoCondicionPagoPlan { ProductoCondicionPagoId = condicion.Id, CantidadCuotas = 6, AjustePorcentaje = 5m });

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ProductoCondicionPagoPlan_MaximosEscalaresExistentesNoSonModificadosPorLaEntidad()
    {
        await using var fixture = await ProductoCondicionPagoDbFixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();

        var condicion = new ProductoCondicionPago
        {
            ProductoId = producto.Id,
            TipoPago = TipoPago.TarjetaCredito,
            MaxCuotasSinInteres = 6,
            MaxCuotasConInteres = 12
        };
        fixture.Context.ProductoCondicionesPago.Add(condicion);
        await fixture.Context.SaveChangesAsync();

        fixture.Context.ProductoCondicionPagoPlanes.Add(new ProductoCondicionPagoPlan
        {
            ProductoCondicionPagoId = condicion.Id,
            CantidadCuotas = 3,
            AjustePorcentaje = 0m
        });
        await fixture.Context.SaveChangesAsync();

        var condicionRecargada = await fixture.Context.ProductoCondicionesPago
            .SingleAsync(c => c.Id == condicion.Id);
        Assert.Equal(6, condicionRecargada.MaxCuotasSinInteres);
        Assert.Equal(12, condicionRecargada.MaxCuotasConInteres);
    }

    private sealed class ProductoCondicionPagoDbFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ProductoCondicionPagoDbFixture(SqliteConnection connection, AppDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public AppDbContext Context { get; }

        public static async Task<ProductoCondicionPagoDbFixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();

            return new ProductoCondicionPagoDbFixture(connection, context);
        }

        public async Task<Producto> SeedProductoAsync()
        {
            var categoria = new Categoria { Codigo = Guid.NewGuid().ToString("N")[..12], Nombre = "Categoria test" };
            var marca = new Marca { Codigo = Guid.NewGuid().ToString("N")[..12], Nombre = "Marca test" };
            var producto = new Producto
            {
                Codigo = Guid.NewGuid().ToString("N")[..12],
                Nombre = "Producto test",
                Categoria = categoria,
                Marca = marca,
                PrecioCompra = 100m,
                PrecioVenta = 150m,
                StockActual = 1m
            };

            Context.Productos.Add(producto);
            await Context.SaveChangesAsync();
            return producto;
        }

        public async Task<ConfiguracionTarjeta> SeedTarjetaAsync()
        {
            var configuracionPago = new ConfiguracionPago
            {
                TipoPago = TipoPago.TarjetaCredito,
                Nombre = Guid.NewGuid().ToString("N")[..12]
            };
            var tarjeta = new ConfiguracionTarjeta
            {
                ConfiguracionPago = configuracionPago,
                NombreTarjeta = Guid.NewGuid().ToString("N")[..12],
                TipoTarjeta = TipoTarjeta.Credito,
                PermiteCuotas = true,
                CantidadMaximaCuotas = 12
            };

            Context.ConfiguracionesTarjeta.Add(tarjeta);
            await Context.SaveChangesAsync();
            return tarjeta;
        }

        public async Task<ProductoCondicionPago> SeedCondicionAsync(int productoId, TipoPago tipoPago)
        {
            var condicion = new ProductoCondicionPago
            {
                ProductoId = productoId,
                TipoPago = tipoPago
            };

            Context.ProductoCondicionesPago.Add(condicion);
            await Context.SaveChangesAsync();
            return condicion;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}

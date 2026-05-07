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

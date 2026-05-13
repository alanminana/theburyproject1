using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

public sealed class ProductoCreditoRestriccionServiceTests
{
    [Fact]
    public async Task SinRestriccion_PermiteCreditoPersonalSinLimitePorProducto()
    {
        await using var fixture = await Fixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var service = new ProductoCreditoRestriccionService(fixture.Context);

        var resultado = await service.ResolverAsync(new[] { producto.Id });

        Assert.True(resultado.Permitido);
        Assert.Null(resultado.MaxCuotasCredito);
        Assert.Empty(resultado.ProductoIdsBloqueantes);
        Assert.Empty(resultado.ProductoIdsRestrictivos);
    }

    [Fact]
    public async Task PermitidoFalse_BloqueaCreditoPersonalParaProducto()
    {
        await using var fixture = await Fixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        fixture.Context.ProductoCreditoRestricciones.Add(new ProductoCreditoRestriccion
        {
            ProductoId = producto.Id,
            Permitido = false,
            Activo = true,
            IsDeleted = false
        });
        await fixture.Context.SaveChangesAsync();
        var service = new ProductoCreditoRestriccionService(fixture.Context);

        var resultado = await service.ResolverAsync(new[] { producto.Id });

        Assert.False(resultado.Permitido);
        Assert.Equal(new[] { producto.Id }, resultado.ProductoIdsBloqueantes);
        Assert.Null(resultado.MaxCuotasCredito);
    }

    [Fact]
    public async Task MaxCuotasCredito_UsaMenorRestriccionActiva()
    {
        await using var fixture = await Fixture.CreateAsync();
        var producto12 = await fixture.SeedProductoAsync();
        var producto6 = await fixture.SeedProductoAsync();
        fixture.Context.ProductoCreditoRestricciones.AddRange(
            new ProductoCreditoRestriccion
            {
                ProductoId = producto12.Id,
                MaxCuotasCredito = 12,
                Activo = true,
                IsDeleted = false
            },
            new ProductoCreditoRestriccion
            {
                ProductoId = producto6.Id,
                MaxCuotasCredito = 6,
                Activo = true,
                IsDeleted = false
            });
        await fixture.Context.SaveChangesAsync();
        var service = new ProductoCreditoRestriccionService(fixture.Context);

        var resultado = await service.ResolverAsync(new[] { producto12.Id, producto6.Id });

        Assert.True(resultado.Permitido);
        Assert.Equal(6, resultado.MaxCuotasCredito);
        Assert.Equal(producto6.Id, resultado.ProductoIdsRestrictivos[0]);
    }

    [Fact]
    public async Task RestriccionInactivaOSoftDeleted_NoAplica()
    {
        await using var fixture = await Fixture.CreateAsync();
        var inactivo = await fixture.SeedProductoAsync();
        var eliminado = await fixture.SeedProductoAsync();
        fixture.Context.ProductoCreditoRestricciones.AddRange(
            new ProductoCreditoRestriccion
            {
                ProductoId = inactivo.Id,
                Permitido = false,
                MaxCuotasCredito = 1,
                Activo = false,
                IsDeleted = false
            },
            new ProductoCreditoRestriccion
            {
                ProductoId = eliminado.Id,
                Permitido = false,
                MaxCuotasCredito = 1,
                Activo = true,
                IsDeleted = true
            });
        await fixture.Context.SaveChangesAsync();
        var service = new ProductoCreditoRestriccionService(fixture.Context);

        var resultado = await service.ResolverAsync(new[] { inactivo.Id, eliminado.Id });

        Assert.True(resultado.Permitido);
        Assert.Null(resultado.MaxCuotasCredito);
        Assert.Empty(resultado.ProductoIdsBloqueantes);
        Assert.Empty(resultado.ProductoIdsRestrictivos);
    }

    [Fact]
    public async Task ProductoCondicionPagoLegacy_NoAfectaCreditoPersonal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var producto = await fixture.SeedProductoAsync();
        var condicion = new ProductoCondicionPago
        {
            ProductoId = producto.Id,
            TipoPago = TipoPago.CreditoPersonal,
            Permitido = false,
            MaxCuotasCredito = 1,
            Activo = true,
            IsDeleted = false
        };
        fixture.Context.ProductoCondicionesPago.Add(condicion);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ProductoCondicionPagoPlanes.Add(new ProductoCondicionPagoPlan
        {
            ProductoCondicionPagoId = condicion.Id,
            CantidadCuotas = 1,
            Activo = true,
            IsDeleted = false
        });
        await fixture.Context.SaveChangesAsync();
        var service = new ProductoCreditoRestriccionService(fixture.Context);

        var resultado = await service.ResolverAsync(new[] { producto.Id });

        Assert.True(resultado.Permitido);
        Assert.Null(resultado.MaxCuotasCredito);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private int _counter;

        private Fixture(SqliteConnection connection, AppDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public AppDbContext Context { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new AppDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new Fixture(connection, context);
        }

        public async Task<Producto> SeedProductoAsync()
        {
            var suffix = Interlocked.Increment(ref _counter).ToString();
            var categoria = new Categoria { Codigo = $"C{suffix}", Nombre = $"Categoria {suffix}", Activo = true };
            var marca = new Marca { Codigo = $"M{suffix}", Nombre = $"Marca {suffix}", Activo = true };
            Context.Categorias.Add(categoria);
            Context.Marcas.Add(marca);
            await Context.SaveChangesAsync();

            var producto = new Producto
            {
                Codigo = $"P{suffix}",
                Nombre = $"Producto {suffix}",
                CategoriaId = categoria.Id,
                MarcaId = marca.Id,
                PrecioVenta = 1_000m,
                StockActual = 10m,
                Activo = true,
                IsDeleted = false
            };
            Context.Productos.Add(producto);
            await Context.SaveChangesAsync();
            return producto;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}

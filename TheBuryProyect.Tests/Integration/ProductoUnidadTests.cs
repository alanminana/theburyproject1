using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests EF para ProductoUnidad y ProductoUnidadMovimiento.
/// Usan SQLite in-memory (proveedor relacional) para validar constraints e índices filtrados.
/// </summary>
public class ProductoUnidadTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;

    public ProductoUnidadTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Producto> SeedProductoAsync(string? codigoOverride = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var marca = new Marca { Codigo = "M" + suffix, Nombre = "Marca-" + suffix, Activo = true };
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var cat = new Categoria { Codigo = "C" + suffix, Nombre = "Cat-" + suffix, Activo = true };
        _context.Categorias.Add(cat);
        await _context.SaveChangesAsync();

        var prod = new Producto
        {
            Codigo = codigoOverride ?? "P" + suffix,
            Nombre = "Prod-" + suffix,
            CategoriaId = cat.Id,
            MarcaId = marca.Id,
            PrecioCompra = 100m,
            PrecioVenta = 150m,
            PorcentajeIVA = 21m
        };
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();
        return prod;
    }

    private static ProductoUnidad BuildUnidad(int productoId, string codigoInterno, string? numeroSerie = null)
        => new()
        {
            ProductoId = productoId,
            CodigoInternoUnidad = codigoInterno,
            NumeroSerie = numeroSerie,
            Estado = EstadoUnidad.EnStock,
            FechaIngreso = DateTime.UtcNow
        };

    // -------------------------------------------------------------------------
    // 1. Persiste ProductoUnidad válida
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PersistProductoUnidadValida()
    {
        var prod = await SeedProductoAsync();

        var unidad = BuildUnidad(prod.Id, "CI-001", "SN-001");
        unidad.UbicacionActual = "Depósito A";
        unidad.Observaciones = "Observación de prueba";

        _context.ProductoUnidades.Add(unidad);
        await _context.SaveChangesAsync();

        var saved = await _context.ProductoUnidades
            .AsNoTracking()
            .SingleAsync(u => u.Id == unidad.Id);

        Assert.Equal(prod.Id, saved.ProductoId);
        Assert.Equal("CI-001", saved.CodigoInternoUnidad);
        Assert.Equal("SN-001", saved.NumeroSerie);
        Assert.Equal(EstadoUnidad.EnStock, saved.Estado);
        Assert.Equal("Depósito A", saved.UbicacionActual);
        Assert.Equal("Observación de prueba", saved.Observaciones);
        Assert.False(saved.IsDeleted);
    }

    // -------------------------------------------------------------------------
    // 2. CodigoInternoUnidad requerido
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CodigoInternoUnidadRequerido_Falla()
    {
        var prod = await SeedProductoAsync();

        var unidad = BuildUnidad(prod.Id, "dummy");
        unidad.CodigoInternoUnidad = null!;

        _context.ProductoUnidades.Add(unidad);

        await Assert.ThrowsAnyAsync<Exception>(() => _context.SaveChangesAsync());
    }

    // -------------------------------------------------------------------------
    // 3. NumeroSerie null permitido
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NumeroSerieNullPermitido()
    {
        var prod = await SeedProductoAsync();

        var unidad = BuildUnidad(prod.Id, "CI-NULL", numeroSerie: null);
        _context.ProductoUnidades.Add(unidad);
        await _context.SaveChangesAsync();

        var saved = await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.Id == unidad.Id);
        Assert.Null(saved.NumeroSerie);
    }

    // -------------------------------------------------------------------------
    // 4. NumeroSerie duplicado por producto rechazado si no es null
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NumeroSerieDuplicadoPorProducto_Rechazado()
    {
        var prod = await SeedProductoAsync();

        _context.ProductoUnidades.Add(BuildUnidad(prod.Id, "CI-A", "SN-DUP"));
        await _context.SaveChangesAsync();

        _context.ProductoUnidades.Add(BuildUnidad(prod.Id, "CI-B", "SN-DUP"));

        await Assert.ThrowsAnyAsync<Exception>(() => _context.SaveChangesAsync());
    }

    // -------------------------------------------------------------------------
    // 5. Mismo NumeroSerie permitido en productos distintos
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MismoNumeroSeriePermitidoEnProductosDistintos()
    {
        var prod1 = await SeedProductoAsync();
        var prod2 = await SeedProductoAsync();

        _context.ProductoUnidades.Add(BuildUnidad(prod1.Id, "CI-P1", "SN-SHARED"));
        _context.ProductoUnidades.Add(BuildUnidad(prod2.Id, "CI-P2", "SN-SHARED"));

        var ex = await Record.ExceptionAsync(() => _context.SaveChangesAsync());
        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // 6. CodigoInternoUnidad duplicado por producto rechazado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CodigoInternoUnidadDuplicadoPorProducto_Rechazado()
    {
        var prod = await SeedProductoAsync();

        _context.ProductoUnidades.Add(BuildUnidad(prod.Id, "CI-DUP", "SN-1"));
        await _context.SaveChangesAsync();

        _context.ProductoUnidades.Add(BuildUnidad(prod.Id, "CI-DUP", "SN-2"));

        await Assert.ThrowsAnyAsync<Exception>(() => _context.SaveChangesAsync());
    }

    // -------------------------------------------------------------------------
    // 7. Histórico soft-deleted permite reutilizar CodigoInternoUnidad
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SoftDeletedPermiteReutilizarCodigoInterno()
    {
        var prod = await SeedProductoAsync();

        var original = BuildUnidad(prod.Id, "CI-REUS");
        _context.ProductoUnidades.Add(original);
        await _context.SaveChangesAsync();

        original.IsDeleted = true;
        await _context.SaveChangesAsync();

        var nuevo = BuildUnidad(prod.Id, "CI-REUS");
        _context.ProductoUnidades.Add(nuevo);

        var ex = await Record.ExceptionAsync(() => _context.SaveChangesAsync());
        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // 8. Relación Producto funciona (navegación)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RelacionProductoFunciona()
    {
        var prod = await SeedProductoAsync();

        _context.ProductoUnidades.Add(BuildUnidad(prod.Id, "CI-NAV"));
        await _context.SaveChangesAsync();

        var unidad = await _context.ProductoUnidades
            .Include(u => u.Producto)
            .FirstAsync(u => u.ProductoId == prod.Id);

        Assert.NotNull(unidad.Producto);
        Assert.Equal(prod.Id, unidad.Producto.Id);
    }

    // -------------------------------------------------------------------------
    // 9. Relación VentaDetalle nullable funciona (guarda sin FK)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RelacionVentaDetalleNullablePermitida()
    {
        var prod = await SeedProductoAsync();

        var unidad = BuildUnidad(prod.Id, "CI-NODET");
        unidad.VentaDetalleId = null;
        unidad.ClienteId = null;
        unidad.FechaVenta = null;

        _context.ProductoUnidades.Add(unidad);
        var ex = await Record.ExceptionAsync(() => _context.SaveChangesAsync());
        Assert.Null(ex);

        var saved = await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.Id == unidad.Id);
        Assert.Null(saved.VentaDetalleId);
        Assert.Null(saved.ClienteId);
    }

    // -------------------------------------------------------------------------
    // 10. ProductoUnidadMovimiento persiste cambio de estado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProductoUnidadMovimientoPersisteCambioEstado()
    {
        var prod = await SeedProductoAsync();

        var unidad = BuildUnidad(prod.Id, "CI-MOV");
        _context.ProductoUnidades.Add(unidad);
        await _context.SaveChangesAsync();

        var movimiento = new ProductoUnidadMovimiento
        {
            ProductoUnidadId = unidad.Id,
            EstadoAnterior = EstadoUnidad.EnStock,
            EstadoNuevo = EstadoUnidad.Vendida,
            Motivo = "Venta registrada",
            OrigenReferencia = "Venta #V-001",
            UsuarioResponsable = "usuario@test.com",
            FechaCambio = DateTime.UtcNow
        };

        _context.ProductoUnidadMovimientos.Add(movimiento);
        await _context.SaveChangesAsync();

        var saved = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .SingleAsync(m => m.Id == movimiento.Id);

        Assert.Equal(unidad.Id, saved.ProductoUnidadId);
        Assert.Equal(EstadoUnidad.EnStock, saved.EstadoAnterior);
        Assert.Equal(EstadoUnidad.Vendida, saved.EstadoNuevo);
        Assert.Equal("Venta registrada", saved.Motivo);
        Assert.Equal("Venta #V-001", saved.OrigenReferencia);
        Assert.Equal("usuario@test.com", saved.UsuarioResponsable);
    }

    // -------------------------------------------------------------------------
    // 11. ProductoUnidadMovimiento requiere Motivo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProductoUnidadMovimientoRequiereMotivo_Falla()
    {
        var prod = await SeedProductoAsync();

        var unidad = BuildUnidad(prod.Id, "CI-MOTIVO");
        _context.ProductoUnidades.Add(unidad);
        await _context.SaveChangesAsync();

        var movimiento = new ProductoUnidadMovimiento
        {
            ProductoUnidadId = unidad.Id,
            EstadoAnterior = EstadoUnidad.EnStock,
            EstadoNuevo = EstadoUnidad.Vendida,
            Motivo = null!,
            FechaCambio = DateTime.UtcNow
        };

        _context.ProductoUnidadMovimientos.Add(movimiento);
        await Assert.ThrowsAnyAsync<Exception>(() => _context.SaveChangesAsync());
    }
}

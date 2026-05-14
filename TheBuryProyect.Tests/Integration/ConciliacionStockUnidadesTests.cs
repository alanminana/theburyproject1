using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests del flujo de ajuste asistido de stock desde conciliación de unidades.
/// Verifica la regla: RegistrarAjusteAsync(productoId, Ajuste, UnidadesEnStock)
/// iguala StockActual a las unidades físicas disponibles sin tocar ProductoUnidad ni ProductoUnidadMovimiento.
/// </summary>
public class ConciliacionStockUnidadesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly MovimientoStockService _movimientoStockService;

    public ConciliacionStockUnidadesTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _movimientoStockService = new MovimientoStockService(
            _context,
            NullLogger<MovimientoStockService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Producto> SeedProductoAsync(decimal stockActual, bool requiereNumeroSerie = true)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var marca = new Marca { Codigo = "M" + suffix, Nombre = "Marca-" + suffix, Activo = true };
        _context.Marcas.Add(marca);
        var cat = new Categoria { Codigo = "C" + suffix, Nombre = "Cat-" + suffix, Activo = true };
        _context.Categorias.Add(cat);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = suffix,
            Nombre = "Prod-" + suffix,
            CategoriaId = cat.Id,
            MarcaId = marca.Id,
            PrecioCompra = 100m,
            PrecioVenta = 150m,
            PorcentajeIVA = 21m,
            StockActual = stockActual,
            RequiereNumeroSerie = requiereNumeroSerie,
            Activo = true
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    private async Task SeedUnidadesEnStockAsync(int productoId, int cantidad)
    {
        for (var i = 0; i < cantidad; i++)
        {
            _context.Set<ProductoUnidad>().Add(new ProductoUnidad
            {
                ProductoId = productoId,
                CodigoInternoUnidad = $"U-{productoId}-{i}-{Guid.NewGuid():N}",
                Estado = EstadoUnidad.EnStock,
                FechaIngreso = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();
    }

    private async Task SeedUnidadConEstadoAsync(int productoId, EstadoUnidad estado)
    {
        _context.Set<ProductoUnidad>().Add(new ProductoUnidad
        {
            ProductoId = productoId,
            CodigoInternoUnidad = $"U-{productoId}-{estado}-{Guid.NewGuid():N}",
            Estado = estado,
            FechaIngreso = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Diferencia positiva: StockActual > UnidadesEnStock → ajuste negativo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DiferenciaPositiva_AjustaStockAlNumeroDeUnidadesEnStock()
    {
        // StockActual=5, UnidadesEnStock=3 → diferencia=+2 → ajuste a 3
        var producto = await SeedProductoAsync(stockActual: 5m);
        await SeedUnidadesEnStockAsync(producto.Id, 3);

        var referencia = $"ConciliacionUnidad:{producto.Id}";
        await _movimientoStockService.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Ajuste, 3m, referencia, "Recuento fisico", "tester");

        var productoActualizado = await _context.Productos.FindAsync(producto.Id);
        Assert.Equal(3m, productoActualizado!.StockActual);
    }

    [Fact]
    public async Task DiferenciaPositiva_MovimientoStockTieneCantidadNegativa()
    {
        // Delta = nuevoStock - stockAnterior = 3 - 5 = -2
        var producto = await SeedProductoAsync(stockActual: 5m);
        await SeedUnidadesEnStockAsync(producto.Id, 3);

        var referencia = $"ConciliacionUnidad:{producto.Id}";
        var movimiento = await _movimientoStockService.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Ajuste, 3m, referencia, "Recuento fisico", "tester");

        Assert.Equal(-2m, movimiento.Cantidad);
        Assert.Equal(5m, movimiento.StockAnterior);
        Assert.Equal(3m, movimiento.StockNuevo);
    }

    // -------------------------------------------------------------------------
    // Diferencia negativa: StockActual < UnidadesEnStock → ajuste positivo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DiferenciaNegativa_AjustaStockAlNumeroDeUnidadesEnStock()
    {
        // StockActual=3, UnidadesEnStock=5 → diferencia=-2 → ajuste a 5
        var producto = await SeedProductoAsync(stockActual: 3m);
        await SeedUnidadesEnStockAsync(producto.Id, 5);

        var referencia = $"ConciliacionUnidad:{producto.Id}";
        await _movimientoStockService.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Ajuste, 5m, referencia, "Carga corregida", "tester");

        var productoActualizado = await _context.Productos.FindAsync(producto.Id);
        Assert.Equal(5m, productoActualizado!.StockActual);
    }

    [Fact]
    public async Task DiferenciaNegativa_MovimientoStockTieneCantidadPositiva()
    {
        // Delta = 5 - 3 = +2
        var producto = await SeedProductoAsync(stockActual: 3m);
        await SeedUnidadesEnStockAsync(producto.Id, 5);

        var referencia = $"ConciliacionUnidad:{producto.Id}";
        var movimiento = await _movimientoStockService.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Ajuste, 5m, referencia, "Carga corregida", "tester");

        Assert.Equal(2m, movimiento.Cantidad);
        Assert.Equal(3m, movimiento.StockAnterior);
        Assert.Equal(5m, movimiento.StockNuevo);
    }

    // -------------------------------------------------------------------------
    // Referencia y auditoría
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MovimientoStock_TieneReferenciaConciliacionUnidad()
    {
        var producto = await SeedProductoAsync(stockActual: 5m);
        await SeedUnidadesEnStockAsync(producto.Id, 3);

        var referencia = $"ConciliacionUnidad:{producto.Id}";
        var movimiento = await _movimientoStockService.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Ajuste, 3m, referencia, "Recuento fisico", "tester");

        Assert.Equal($"ConciliacionUnidad:{producto.Id}", movimiento.Referencia);
        Assert.Equal(TipoMovimiento.Ajuste, movimiento.Tipo);
    }

    [Fact]
    public async Task MovimientoStock_TieneMotivoCorrecto()
    {
        var producto = await SeedProductoAsync(stockActual: 5m);
        await SeedUnidadesEnStockAsync(producto.Id, 3);

        var referencia = $"ConciliacionUnidad:{producto.Id}";
        var movimiento = await _movimientoStockService.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Ajuste, 3m, referencia, "Recuento fisico confirmado", "tester");

        Assert.Equal("Recuento fisico confirmado", movimiento.Motivo);
    }

    // -------------------------------------------------------------------------
    // Diferencia cero no debe intentarse (validación de controller)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DiferenciaCero_NoGeneraCambioSiSeInvocaConMismoStock()
    {
        // Cuando diferencia=0, el controller no llama a RegistrarAjusteAsync.
        // Aquí verificamos que si igual se llama con el mismo valor, el delta es 0.
        var producto = await SeedProductoAsync(stockActual: 3m);
        await SeedUnidadesEnStockAsync(producto.Id, 3);

        var referencia = $"ConciliacionUnidad:{producto.Id}";
        var movimiento = await _movimientoStockService.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Ajuste, 3m, referencia, "Sin diferencia", "tester");

        // Delta = 3 - 3 = 0, stock no cambia
        Assert.Equal(0m, movimiento.Cantidad);
        var productoActualizado = await _context.Productos.FindAsync(producto.Id);
        Assert.Equal(3m, productoActualizado!.StockActual);
    }

    // -------------------------------------------------------------------------
    // No toca ProductoUnidad ni ProductoUnidadMovimiento
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Ajuste_NoModificaEstadoDeProductoUnidad()
    {
        var producto = await SeedProductoAsync(stockActual: 5m);
        await SeedUnidadesEnStockAsync(producto.Id, 3);

        var referencia = $"ConciliacionUnidad:{producto.Id}";
        await _movimientoStockService.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Ajuste, 3m, referencia, "Recuento fisico", "tester");

        var unidades = _context.Set<ProductoUnidad>().Where(u => u.ProductoId == producto.Id).ToList();
        Assert.All(unidades, u => Assert.Equal(EstadoUnidad.EnStock, u.Estado));
    }

    [Fact]
    public async Task Ajuste_NoCreaNingunProductoUnidadMovimiento()
    {
        var producto = await SeedProductoAsync(stockActual: 5m);
        await SeedUnidadesEnStockAsync(producto.Id, 3);

        var unidadIds = _context.Set<ProductoUnidad>()
            .Where(u => u.ProductoId == producto.Id)
            .Select(u => u.Id)
            .ToList();

        var referencia = $"ConciliacionUnidad:{producto.Id}";
        await _movimientoStockService.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Ajuste, 3m, referencia, "Recuento fisico", "tester");

        var movimientosUnidad = _context.Set<ProductoUnidadMovimiento>()
            .Where(m => unidadIds.Contains(m.ProductoUnidadId))
            .ToList();

        Assert.Empty(movimientosUnidad);
    }

    // -------------------------------------------------------------------------
    // Unidades en otros estados no se ven afectadas
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Ajuste_NoModificaUnidadesEnOtrosEstados()
    {
        var producto = await SeedProductoAsync(stockActual: 5m);
        await SeedUnidadesEnStockAsync(producto.Id, 3);
        await SeedUnidadConEstadoAsync(producto.Id, EstadoUnidad.Vendida);
        await SeedUnidadConEstadoAsync(producto.Id, EstadoUnidad.Faltante);

        var referencia = $"ConciliacionUnidad:{producto.Id}";
        await _movimientoStockService.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Ajuste, 3m, referencia, "Recuento fisico", "tester");

        var vendidas = _context.Set<ProductoUnidad>()
            .Where(u => u.ProductoId == producto.Id && u.Estado == EstadoUnidad.Vendida)
            .ToList();
        var faltantes = _context.Set<ProductoUnidad>()
            .Where(u => u.ProductoId == producto.Id && u.Estado == EstadoUnidad.Faltante)
            .ToList();

        Assert.Single(vendidas);
        Assert.Single(faltantes);
    }

    // -------------------------------------------------------------------------
    // Validaciones de servicio
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProductoInexistente_LanzaInvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _movimientoStockService.RegistrarAjusteAsync(
                9999, TipoMovimiento.Ajuste, 3m,
                "ConciliacionUnidad:9999", "Motivo", "tester"));
    }

    [Fact]
    public async Task CantidadNegativa_LanzaInvalidOperationException()
    {
        var producto = await SeedProductoAsync(stockActual: 5m);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _movimientoStockService.RegistrarAjusteAsync(
                producto.Id, TipoMovimiento.Ajuste, -1m,
                $"ConciliacionUnidad:{producto.Id}", "Motivo", "tester"));
    }

    [Fact]
    public async Task AjusteACero_EstableceStoActualEnCero()
    {
        // Si todas las unidades están en otros estados (ninguna EnStock), UnidadesEnStock=0
        var producto = await SeedProductoAsync(stockActual: 5m);
        await SeedUnidadConEstadoAsync(producto.Id, EstadoUnidad.Vendida);

        // UnidadesEnStock = 0 → llamar con cantidad=0 debe aceptarse (ajuste a 0)
        var referencia = $"ConciliacionUnidad:{producto.Id}";
        await _movimientoStockService.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Ajuste, 0m, referencia, "Sin unidades disponibles", "tester");

        var productoActualizado = await _context.Productos.FindAsync(producto.Id);
        Assert.Equal(0m, productoActualizado!.StockActual);
    }
}

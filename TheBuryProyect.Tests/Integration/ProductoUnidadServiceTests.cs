using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para ProductoUnidadService.
/// Usan SQLite in-memory (proveedor relacional) para validar constraints e índices.
/// </summary>
public class ProductoUnidadServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ProductoUnidadService _service;

    public ProductoUnidadServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        _service = new ProductoUnidadService(_context, NullLogger<ProductoUnidadService>.Instance);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Producto> SeedProductoAsync(string? codigo = null)
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
            Codigo = codigo ?? "P" + suffix,
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

    // -------------------------------------------------------------------------
    // 1. Crear unidad sin número de serie
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_SinSerie_Persiste()
    {
        var prod = await SeedProductoAsync("TV55");

        var unidad = await _service.CrearUnidadAsync(prod.Id);

        Assert.NotEqual(0, unidad.Id);
        Assert.Equal(prod.Id, unidad.ProductoId);
        Assert.Null(unidad.NumeroSerie);
        Assert.Equal(EstadoUnidad.EnStock, unidad.Estado);
        Assert.False(unidad.IsDeleted);
    }

    // -------------------------------------------------------------------------
    // 2. Crear unidad con número de serie
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_ConSerie_Persiste()
    {
        var prod = await SeedProductoAsync("TV55");

        var unidad = await _service.CrearUnidadAsync(prod.Id, numeroSerie: "SN-001");

        Assert.Equal("SN-001", unidad.NumeroSerie);
        Assert.Equal(EstadoUnidad.EnStock, unidad.Estado);
    }

    // -------------------------------------------------------------------------
    // 3. Genera CodigoInternoUnidad automático con base en Producto.Codigo
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_GeneraCodigoInterno_UsandoProductoCodigo()
    {
        var prod = await SeedProductoAsync("TV55");

        var unidad = await _service.CrearUnidadAsync(prod.Id);

        Assert.StartsWith("TV55-U-", unidad.CodigoInternoUnidad);
    }

    // -------------------------------------------------------------------------
    // 4. Genera correlativo por producto (primer = 0001, segundo = 0002)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_GeneraCorrelativoSecuencial()
    {
        var prod = await SeedProductoAsync("TV55");

        var u1 = await _service.CrearUnidadAsync(prod.Id);
        var u2 = await _service.CrearUnidadAsync(prod.Id);

        Assert.Equal("TV55-U-0001", u1.CodigoInternoUnidad);
        Assert.Equal("TV55-U-0002", u2.CodigoInternoUnidad);
    }

    // -------------------------------------------------------------------------
    // 5. Correlativos por producto son independientes entre productos distintos
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_CorrelativosIndependientesPorProducto()
    {
        var prod1 = await SeedProductoAsync("TV55");
        var prod2 = await SeedProductoAsync("HELADERA");

        var u1 = await _service.CrearUnidadAsync(prod1.Id);
        var u2 = await _service.CrearUnidadAsync(prod2.Id);
        var u3 = await _service.CrearUnidadAsync(prod1.Id);

        Assert.Equal("TV55-U-0001", u1.CodigoInternoUnidad);
        Assert.Equal("HELADERA-U-0001", u2.CodigoInternoUnidad);
        Assert.Equal("TV55-U-0002", u3.CodigoInternoUnidad);
    }

    // -------------------------------------------------------------------------
    // 6. Rechaza producto inexistente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_ProductoInexistente_LanzaExcepcion()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CrearUnidadAsync(999999));
    }

    // -------------------------------------------------------------------------
    // 7. Rechaza producto eliminado (soft-deleted)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_ProductoEliminado_LanzaExcepcion()
    {
        var prod = await SeedProductoAsync();
        prod.IsDeleted = true;
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CrearUnidadAsync(prod.Id));
    }

    // -------------------------------------------------------------------------
    // 8. Rechaza NumeroSerie duplicado en mismo producto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_NumeroSerieDuplicadoMismoProducto_LanzaExcepcion()
    {
        var prod = await SeedProductoAsync();

        await _service.CrearUnidadAsync(prod.Id, numeroSerie: "SN-DUP");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.CrearUnidadAsync(prod.Id, numeroSerie: "SN-DUP"));
    }

    // -------------------------------------------------------------------------
    // 9. Permite misma serie en producto distinto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_MismaSerieProductoDistinto_Permitida()
    {
        var prod1 = await SeedProductoAsync();
        var prod2 = await SeedProductoAsync();

        await _service.CrearUnidadAsync(prod1.Id, numeroSerie: "SN-SHARED");
        var ex = await Record.ExceptionAsync(
            () => _service.CrearUnidadAsync(prod2.Id, numeroSerie: "SN-SHARED"));

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // 10. Crea movimiento inicial en historial
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_CreaMovimientoInicial()
    {
        var prod = await SeedProductoAsync();

        var unidad = await _service.CrearUnidadAsync(prod.Id, usuario: "admin@test.com");

        var historial = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .Where(m => m.ProductoUnidadId == unidad.Id)
            .ToListAsync();

        Assert.Single(historial);
        var mov = historial[0];
        Assert.Equal(EstadoUnidad.EnStock, mov.EstadoAnterior);
        Assert.Equal(EstadoUnidad.EnStock, mov.EstadoNuevo);
        Assert.Equal("Ingreso inicial de unidad", mov.Motivo);
        Assert.Equal("AltaUnidad", mov.OrigenReferencia);
        Assert.Equal("admin@test.com", mov.UsuarioResponsable);
    }

    // -------------------------------------------------------------------------
    // 11. ObtenerDisponiblesPorProductoAsync devuelve solo EnStock
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerDisponibles_DevuelveSoloEnStock()
    {
        var prod = await SeedProductoAsync();

        var u1 = await _service.CrearUnidadAsync(prod.Id);
        var u2 = await _service.CrearUnidadAsync(prod.Id);

        // Cambiamos estado de u2 a Vendida directamente
        var u2Entity = await _context.ProductoUnidades.FindAsync(u2.Id);
        u2Entity!.Estado = EstadoUnidad.Vendida;
        await _context.SaveChangesAsync();

        var disponibles = (await _service.ObtenerDisponiblesPorProductoAsync(prod.Id)).ToList();

        Assert.Single(disponibles);
        Assert.Equal(u1.Id, disponibles[0].Id);
    }

    // -------------------------------------------------------------------------
    // 12. ObtenerHistorialAsync ordena por FechaCambio ascendente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerHistorial_OrdenaPorFechaCambioAscendente()
    {
        var prod = await SeedProductoAsync();
        var unidad = await _service.CrearUnidadAsync(prod.Id);

        // Agrego movimientos adicionales con fechas controladas
        var mov2 = new ProductoUnidadMovimiento
        {
            ProductoUnidadId = unidad.Id,
            EstadoAnterior = EstadoUnidad.EnStock,
            EstadoNuevo = EstadoUnidad.Reservada,
            Motivo = "Reserva",
            FechaCambio = DateTime.UtcNow.AddSeconds(10)
        };
        var mov3 = new ProductoUnidadMovimiento
        {
            ProductoUnidadId = unidad.Id,
            EstadoAnterior = EstadoUnidad.Reservada,
            EstadoNuevo = EstadoUnidad.Vendida,
            Motivo = "Venta",
            FechaCambio = DateTime.UtcNow.AddSeconds(20)
        };
        _context.ProductoUnidadMovimientos.AddRange(mov2, mov3);
        await _context.SaveChangesAsync();

        var historial = (await _service.ObtenerHistorialAsync(unidad.Id)).ToList();

        Assert.Equal(3, historial.Count);
        Assert.True(historial[0].FechaCambio <= historial[1].FechaCambio);
        Assert.True(historial[1].FechaCambio <= historial[2].FechaCambio);
        Assert.Equal("Ingreso inicial de unidad", historial[0].Motivo);
        Assert.Equal("Reserva", historial[1].Motivo);
        Assert.Equal("Venta", historial[2].Motivo);
    }

    // -------------------------------------------------------------------------
    // 13. ObtenerPorProductoAsync excluye eliminados
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerPorProducto_ExcluyeEliminados()
    {
        var prod = await SeedProductoAsync();

        var u1 = await _service.CrearUnidadAsync(prod.Id);
        var u2 = await _service.CrearUnidadAsync(prod.Id);

        var u2Entity = await _context.ProductoUnidades.FindAsync(u2.Id);
        u2Entity!.IsDeleted = true;
        await _context.SaveChangesAsync();

        var resultado = (await _service.ObtenerPorProductoAsync(prod.Id)).ToList();

        Assert.Single(resultado);
        Assert.Equal(u1.Id, resultado[0].Id);
    }

    // -------------------------------------------------------------------------
    // 14. Fallback a ProductoId cuando Codigo está vacío
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CrearUnidad_CodigoProductoVacio_UsaProductoIdComoBase()
    {
        var prod = await SeedProductoAsync();

        // Forzar Codigo vacío directamente en DB para simular edge case
        var entity = await _context.Productos.FindAsync(prod.Id);
        entity!.Codigo = "";
        await _context.SaveChangesAsync();

        // El service recarga con AsNoTracking; necesitamos que la BD tenga el valor vacío
        var unidad = await _service.CrearUnidadAsync(prod.Id);

        Assert.StartsWith($"{prod.Id}-U-", unidad.CodigoInternoUnidad);
    }
}

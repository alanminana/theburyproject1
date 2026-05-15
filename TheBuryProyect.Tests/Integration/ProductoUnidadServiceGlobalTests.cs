using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para ProductoUnidadService.BuscarUnidadesGlobalAsync.
/// Validan el reporte global de unidades físicas (Fase 10.1).
/// </summary>
public class ProductoUnidadServiceGlobalTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ProductoUnidadService _service;

    public ProductoUnidadServiceGlobalTests()
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

    private async Task<Producto> SeedProductoAsync(string? codigo = null, string? nombre = null)
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
            Nombre = nombre ?? "Prod-" + suffix,
            CategoriaId = cat.Id,
            MarcaId = marca.Id,
            PrecioCompra = 100m,
            PrecioVenta = 150m,
            PorcentajeIVA = 21m,
            StockActual = 0m
        };
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();
        return prod;
    }

    // -------------------------------------------------------------------------
    // 1. Sin filtros devuelve todos los productos
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuscarUnidadesGlobal_SinFiltros_RetornaTodas()
    {
        var p1 = await SeedProductoAsync("PROD-A");
        var p2 = await SeedProductoAsync("PROD-B");

        await _service.CrearUnidadAsync(p1.Id);
        await _service.CrearUnidadAsync(p1.Id);
        await _service.CrearUnidadAsync(p2.Id);

        var resultado = await _service.BuscarUnidadesGlobalAsync(new ProductoUnidadesGlobalFiltros());

        Assert.Equal(3, resultado.TotalUnidades);
        Assert.Equal(3, resultado.Items.Count);
    }

    // -------------------------------------------------------------------------
    // 2. Excluye soft-deleted
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuscarUnidadesGlobal_ExcluyeSoftDeleted()
    {
        var prod = await SeedProductoAsync("PROD-C");
        var u1 = await _service.CrearUnidadAsync(prod.Id);
        await _service.CrearUnidadAsync(prod.Id);

        // Soft-delete manual de la primera unidad
        var unidad = await _context.ProductoUnidades.FindAsync(u1.Id);
        unidad!.IsDeleted = true;
        await _context.SaveChangesAsync();

        var resultado = await _service.BuscarUnidadesGlobalAsync(new ProductoUnidadesGlobalFiltros());

        Assert.Equal(1, resultado.TotalUnidades);
        Assert.DoesNotContain(resultado.Items, i => i.Id == u1.Id);
    }

    // -------------------------------------------------------------------------
    // 3. Filtra por producto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuscarUnidadesGlobal_FiltraPorProducto()
    {
        var p1 = await SeedProductoAsync("PROD-D");
        var p2 = await SeedProductoAsync("PROD-E");

        await _service.CrearUnidadAsync(p1.Id);
        await _service.CrearUnidadAsync(p2.Id);
        await _service.CrearUnidadAsync(p2.Id);

        var resultado = await _service.BuscarUnidadesGlobalAsync(
            new ProductoUnidadesGlobalFiltros { ProductoId = p2.Id });

        Assert.Equal(2, resultado.TotalUnidades);
        Assert.All(resultado.Items, i => Assert.Equal(p2.Id, i.ProductoId));
    }

    // -------------------------------------------------------------------------
    // 4. Filtra por estado
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuscarUnidadesGlobal_FiltraPorEstado()
    {
        var prod = await SeedProductoAsync("PROD-F");
        await _service.CrearUnidadAsync(prod.Id);
        var u2 = await _service.CrearUnidadAsync(prod.Id);

        await _service.MarcarFaltanteAsync(u2.Id, "Test faltante");

        var resultado = await _service.BuscarUnidadesGlobalAsync(
            new ProductoUnidadesGlobalFiltros { Estado = EstadoUnidad.Faltante });

        Assert.Equal(1, resultado.TotalUnidades);
        Assert.Equal(EstadoUnidad.Faltante, resultado.Items[0].Estado);
    }

    // -------------------------------------------------------------------------
    // 5. Filtra por texto en número de serie
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuscarUnidadesGlobal_FiltraPorTextoEnSerie()
    {
        var prod = await SeedProductoAsync("PROD-G");
        await _service.CrearUnidadAsync(prod.Id, numeroSerie: "SN-ESPECIAL-001");
        await _service.CrearUnidadAsync(prod.Id, numeroSerie: "SN-OTRO-002");

        var resultado = await _service.BuscarUnidadesGlobalAsync(
            new ProductoUnidadesGlobalFiltros { Texto = "ESPECIAL" });

        Assert.Equal(1, resultado.TotalUnidades);
        Assert.Equal("SN-ESPECIAL-001", resultado.Items[0].NumeroSerie);
    }

    // -------------------------------------------------------------------------
    // 6. Filtra por texto en código interno
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuscarUnidadesGlobal_FiltraPorTextoEnCodigoInterno()
    {
        var prod = await SeedProductoAsync("BUSCAR-H");
        await _service.CrearUnidadAsync(prod.Id);

        // El código interno generado sería "BUSCAR-H-U-0001"
        var resultado = await _service.BuscarUnidadesGlobalAsync(
            new ProductoUnidadesGlobalFiltros { Texto = "BUSCAR-H-U" });

        Assert.Equal(1, resultado.TotalUnidades);
        Assert.Contains("BUSCAR-H", resultado.Items[0].CodigoInternoUnidad);
    }

    // -------------------------------------------------------------------------
    // 7. Filtra por texto en nombre de producto
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuscarUnidadesGlobal_FiltraPorTextoEnNombreProducto()
    {
        var p1 = await SeedProductoAsync("P-I", nombre: "Television 55 pulgadas");
        var p2 = await SeedProductoAsync("P-J", nombre: "Heladera combi");

        await _service.CrearUnidadAsync(p1.Id);
        await _service.CrearUnidadAsync(p2.Id);

        var resultado = await _service.BuscarUnidadesGlobalAsync(
            new ProductoUnidadesGlobalFiltros { Texto = "Television" });

        Assert.Equal(1, resultado.TotalUnidades);
        Assert.Equal(p1.Id, resultado.Items[0].ProductoId);
    }

    // -------------------------------------------------------------------------
    // 8. Calcula resumen por estado correctamente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuscarUnidadesGlobal_CalculaResumenCorrecto()
    {
        var prod = await SeedProductoAsync("PROD-K");
        await _service.CrearUnidadAsync(prod.Id); // EnStock
        await _service.CrearUnidadAsync(prod.Id); // EnStock
        var uFaltante = await _service.CrearUnidadAsync(prod.Id);
        var uBaja = await _service.CrearUnidadAsync(prod.Id);

        await _service.MarcarFaltanteAsync(uFaltante.Id, "Motivo test");
        await _service.MarcarFaltanteAsync(uBaja.Id, "Motivo test");
        await _service.MarcarBajaAsync(uBaja.Id, "Motivo baja");

        var resultado = await _service.BuscarUnidadesGlobalAsync(new ProductoUnidadesGlobalFiltros());

        Assert.Equal(4, resultado.TotalUnidades);
        Assert.Equal(2, resultado.TotalEnStock);
        Assert.Equal(1, resultado.TotalFaltantes);
        Assert.Equal(1, resultado.TotalBaja);
    }

    // -------------------------------------------------------------------------
    // 9. SoloDisponibles devuelve solo EnStock
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuscarUnidadesGlobal_SoloDisponibles_DevuelveSoloEnStock()
    {
        var prod = await SeedProductoAsync("PROD-L");
        await _service.CrearUnidadAsync(prod.Id);
        var uFaltante = await _service.CrearUnidadAsync(prod.Id);
        await _service.MarcarFaltanteAsync(uFaltante.Id, "Test");

        var resultado = await _service.BuscarUnidadesGlobalAsync(
            new ProductoUnidadesGlobalFiltros { SoloDisponibles = true });

        Assert.All(resultado.Items, i => Assert.Equal(EstadoUnidad.EnStock, i.Estado));
        Assert.Equal(1, resultado.TotalUnidades);
    }

    // -------------------------------------------------------------------------
    // 10. Filtro ProductoId inexistente no rompe — devuelve vacío
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuscarUnidadesGlobal_ProductoInexistente_DevuelveVacio()
    {
        var prod = await SeedProductoAsync("PROD-M");
        await _service.CrearUnidadAsync(prod.Id);

        var resultado = await _service.BuscarUnidadesGlobalAsync(
            new ProductoUnidadesGlobalFiltros { ProductoId = 99999 });

        Assert.Equal(0, resultado.TotalUnidades);
        Assert.Empty(resultado.Items);
    }

    // -------------------------------------------------------------------------
    // 11. Producto no trazable con unidades operativas aparece correctamente
    // -------------------------------------------------------------------------

    [Fact]
    public async Task BuscarUnidadesGlobal_ProductoSinTrazabilidadRequerida_ConUnidades_Aparece()
    {
        // RequiereNumeroSerie = false (valor default en la entidad Producto)
        var prod = await SeedProductoAsync("PROD-N");
        await _service.CrearUnidadAsync(prod.Id);

        var resultado = await _service.BuscarUnidadesGlobalAsync(new ProductoUnidadesGlobalFiltros());

        Assert.Contains(resultado.Items, i => i.ProductoId == prod.Id);
    }
}

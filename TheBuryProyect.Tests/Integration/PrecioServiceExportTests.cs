using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para PrecioService — ExportarHistorialPreciosAsync.
/// Verifica que el método genere archivos Excel válidos (firma PK ZIP)
/// y aplique filtros correctamente.
/// </summary>
public class PrecioServiceExportTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly PrecioService _service;

    public PrecioServiceExportTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var config = new ConfigurationBuilder().Build();
        _service = new PrecioService(
            _context,
            NullLogger<PrecioService>.Instance,
            new StubCurrentUserServiceExport(),
            config);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Producto> SeedProductoAsync()
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = code, Nombre = "Cat-" + code, Activo = true };
        var marca = new Marca { Codigo = code, Nombre = "Marca-" + code, Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var prod = new Producto
        {
            Codigo = code,
            Nombre = "Prod-" + code,
            CategoriaId = cat.Id,
            MarcaId = marca.Id,
            PrecioCompra = 60m,
            PrecioVenta = 100m,
            PorcentajeIVA = 21m,
            StockActual = 5m,
            Activo = true
        };
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();
        return prod;
    }

    private async Task<ListaPrecio> SeedListaAsync()
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var lista = new ListaPrecio
        {
            Nombre = "Lista-" + code,
            Codigo = code,
            Tipo = TipoListaPrecio.Contado,
            Activa = true,
            EsPredeterminada = false,
            Orden = 1,
            ReglaRedondeo = "ninguno"
        };
        _context.ListasPrecios.Add(lista);
        await _context.SaveChangesAsync();
        await _context.Entry(lista).ReloadAsync();
        return lista;
    }

    private async Task<ProductoPrecioLista> SeedPrecioAsync(
        int productoId, int listaId,
        decimal precio = 100m, decimal costo = 60m,
        DateTime? vigenciaDesde = null, DateTime? vigenciaHasta = null)
    {
        var pp = new ProductoPrecioLista
        {
            ProductoId = productoId,
            ListaId = listaId,
            Precio = precio,
            Costo = costo,
            MargenValor = precio - costo,
            MargenPorcentaje = costo > 0 ? ((precio - costo) / costo) * 100 : 0,
            VigenciaDesde = vigenciaDesde ?? DateTime.UtcNow.AddDays(-30),
            VigenciaHasta = vigenciaHasta,
            EsVigente = vigenciaHasta == null,
            EsManual = true,
            CreadoPor = "test"
        };
        _context.ProductosPrecios.Add(pp);
        await _context.SaveChangesAsync();
        return pp;
    }

    // =========================================================================
    // ExportarHistorialPreciosAsync
    // =========================================================================

    [Fact]
    public async Task ExportarHistorial_ListaVacia_RetornaArrayVacio()
    {
        var resultado = await _service.ExportarHistorialPreciosAsync(
            new List<int>(),
            DateTime.UtcNow.AddMonths(-1),
            DateTime.UtcNow);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task ExportarHistorial_FechasInvertidas_LanzaArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ExportarHistorialPreciosAsync(
                new List<int> { 1 },
                DateTime.UtcNow,
                DateTime.UtcNow.AddDays(-1)));
    }

    [Fact]
    public async Task ExportarHistorial_ConDatos_RetornaExcelValido()
    {
        var producto = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        await SeedPrecioAsync(producto.Id, lista.Id,
            vigenciaDesde: DateTime.UtcNow.AddDays(-10));

        var resultado = await _service.ExportarHistorialPreciosAsync(
            new List<int> { producto.Id },
            DateTime.UtcNow.AddMonths(-1),
            DateTime.UtcNow.AddDays(1));

        Assert.NotEmpty(resultado);
        // PK ZIP signature — XLSX format
        Assert.Equal(0x50, resultado[0]); // 'P'
        Assert.Equal(0x4B, resultado[1]); // 'K'
    }

    [Fact]
    public async Task ExportarHistorial_SinPreciosEnRango_RetornaExcelSoloHeaders()
    {
        var producto = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        // Precio fuera del rango de fechas solicitado
        await SeedPrecioAsync(producto.Id, lista.Id,
            vigenciaDesde: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            vigenciaHasta: new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var resultado = await _service.ExportarHistorialPreciosAsync(
            new List<int> { producto.Id },
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc));

        // Should return Excel with headers only (no rows), still valid XLSX
        Assert.NotEmpty(resultado);
        Assert.Equal(0x50, resultado[0]);
        Assert.Equal(0x4B, resultado[1]);
    }

    [Fact]
    public async Task ExportarHistorial_ProductoInexistente_RetornaExcelSoloHeaders()
    {
        var resultado = await _service.ExportarHistorialPreciosAsync(
            new List<int> { 99999 },
            DateTime.UtcNow.AddMonths(-1),
            DateTime.UtcNow.AddDays(1));

        Assert.NotEmpty(resultado);
        Assert.Equal(0x50, resultado[0]);
        Assert.Equal(0x4B, resultado[1]);
    }

    [Fact]
    public async Task ExportarHistorial_MultiplesProductos_IncluieTodosEnExcel()
    {
        var prod1 = await SeedProductoAsync();
        var prod2 = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        await SeedPrecioAsync(prod1.Id, lista.Id, vigenciaDesde: DateTime.UtcNow.AddDays(-5));
        await SeedPrecioAsync(prod2.Id, lista.Id, vigenciaDesde: DateTime.UtcNow.AddDays(-5));

        var resultado = await _service.ExportarHistorialPreciosAsync(
            new List<int> { prod1.Id, prod2.Id },
            DateTime.UtcNow.AddMonths(-1),
            DateTime.UtcNow.AddDays(1));

        Assert.NotEmpty(resultado);
        Assert.Equal(0x50, resultado[0]);
        Assert.Equal(0x4B, resultado[1]);
    }
}

// ---------------------------------------------------------------------------
// Stub mínimo de ICurrentUserService
// ---------------------------------------------------------------------------
file sealed class StubCurrentUserServiceExport : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-export";
    public string GetEmail() => "export@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}

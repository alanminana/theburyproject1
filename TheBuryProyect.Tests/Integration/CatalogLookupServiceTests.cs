using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para CatalogLookupService.
/// El servicio delega en CategoriaService, MarcaService y ProductoService.
/// Se verifica que las combinaciones de datos devuelvan las colecciones correctas.
/// </summary>
public class CatalogLookupServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CatalogLookupService _service;

    public CatalogLookupServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var categoriaService = new CategoriaService(_context, NullLogger<CategoriaService>.Instance);
        var marcaService = new MarcaService(_context, NullLogger<MarcaService>.Instance);
        var precioHistorico = new PrecioHistoricoService(_context, NullLogger<PrecioHistoricoService>.Instance);
        var stubUser = new StubCurrentUserServiceLookup();
        var productoService = new ProductoService(_context, NullLogger<ProductoService>.Instance, precioHistorico, stubUser,
            new PrecioVigenteResolver(_context));

        _service = new CatalogLookupService(categoriaService, marcaService, productoService, _context);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Categoria> SeedCategoriaAsync()
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = code, Nombre = "Cat-" + code, Activo = true };
        _context.Categorias.Add(cat);
        await _context.SaveChangesAsync();
        return cat;
    }

    private async Task<Marca> SeedMarcaAsync()
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var marca = new Marca { Codigo = code, Nombre = "Marca-" + code, Activo = true };
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        return marca;
    }

    private async Task<Producto> SeedProductoAsync(int categoriaId, int marcaId)
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var prod = new Producto
        {
            Codigo = code, Nombre = "Prod-" + code,
            CategoriaId = categoriaId, MarcaId = marcaId,
            PrecioCompra = 10m, PrecioVenta = 20m,
            PorcentajeIVA = 21m, StockActual = 5m, Activo = true
        };
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();
        return prod;
    }

    // =========================================================================
    // GetCategoriasYMarcasAsync
    // =========================================================================

    [Fact]
    public async Task GetCategoriasYMarcas_DevuelveCategorias()
    {
        var baseCount = (await _service.GetCategoriasYMarcasAsync()).categorias.Count();
        await SeedCategoriaAsync();

        var (categorias, _) = await _service.GetCategoriasYMarcasAsync();

        Assert.True(categorias.Count() >= baseCount + 1);
    }

    [Fact]
    public async Task GetCategoriasYMarcas_DevuelveMarcas()
    {
        var baseMarcas = (await _service.GetCategoriasYMarcasAsync()).marcas.Count();
        await SeedMarcaAsync();

        var (_, marcas) = await _service.GetCategoriasYMarcasAsync();

        Assert.True(marcas.Count() >= baseMarcas + 1);
    }

    // =========================================================================
    // GetCategoriasMarcasYProductosAsync
    // =========================================================================

    [Fact]
    public async Task GetCategoriasMarcasYProductos_DevuelveTresCambios()
    {
        var cat = await SeedCategoriaAsync();
        var marca = await SeedMarcaAsync();
        var baseProd = (await _service.GetCategoriasMarcasYProductosAsync()).productos.Count();
        await SeedProductoAsync(cat.Id, marca.Id);

        var (_, _, productos) = await _service.GetCategoriasMarcasYProductosAsync();

        Assert.True(productos.Count() >= baseProd + 1);
    }

    // =========================================================================
    // GetSubcategoriasAsync
    // =========================================================================

    [Fact]
    public async Task GetSubcategorias_PadreSinHijos_RetornaVacio()
    {
        var padre = await SeedCategoriaAsync();

        var resultado = await _service.GetSubcategoriasAsync(padre.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetSubcategorias_PadreConHijos_RetornaHijos()
    {
        var padre = await SeedCategoriaAsync();
        var code1 = Guid.NewGuid().ToString("N")[..8];
        var code2 = Guid.NewGuid().ToString("N")[..8];
        _context.Categorias.AddRange(
            new Categoria { Codigo = code1, Nombre = "Hijo1-" + code1, ParentId = padre.Id, Activo = true },
            new Categoria { Codigo = code2, Nombre = "Hijo2-" + code2, ParentId = padre.Id, Activo = true });
        await _context.SaveChangesAsync();

        var resultado = await _service.GetSubcategoriasAsync(padre.Id);

        Assert.Equal(2, resultado.Count());
    }

    // =========================================================================
    // GetSubmarcasAsync
    // =========================================================================

    [Fact]
    public async Task GetSubmarcas_PadreSinHijos_RetornaVacio()
    {
        var padre = await SeedMarcaAsync();

        var resultado = await _service.GetSubmarcasAsync(padre.Id);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task GetSubmarcas_PadreConHijos_RetornaHijos()
    {
        var padre = await SeedMarcaAsync();
        var code1 = Guid.NewGuid().ToString("N")[..8];
        var code2 = Guid.NewGuid().ToString("N")[..8];
        _context.Marcas.AddRange(
            new Marca { Codigo = code1, Nombre = "Hijo1-" + code1, ParentId = padre.Id, Activo = true },
            new Marca { Codigo = code2, Nombre = "Hijo2-" + code2, ParentId = padre.Id, Activo = true });
        await _context.SaveChangesAsync();

        var resultado = await _service.GetSubmarcasAsync(padre.Id);

        Assert.Equal(2, resultado.Count());
    }

    // =========================================================================
    // ObtenerAlicuotasIVAParaFormAsync
    // =========================================================================

    [Fact]
    public async Task ObtenerAlicuotasIVAParaForm_DevuelveSoloActivas()
    {
        var code1 = Guid.NewGuid().ToString("N")[..8];
        var code2 = Guid.NewGuid().ToString("N")[..8];
        var activa = new AlicuotaIVA { Codigo = code1, Nombre = "IVA21-" + code1, Porcentaje = 21m, Activa = true };
        var inactiva = new AlicuotaIVA { Codigo = code2, Nombre = "IVA10-" + code2, Porcentaje = 10m, Activa = false };
        _context.AlicuotasIVA.AddRange(activa, inactiva);
        await _context.SaveChangesAsync();

        var resultado = await _service.ObtenerAlicuotasIVAParaFormAsync();

        Assert.Contains(resultado, a => a.Id == activa.Id);
        Assert.DoesNotContain(resultado, a => a.Id == inactiva.Id);
    }

    [Fact]
    public async Task ObtenerAlicuotasIVAParaForm_PredeterminadaPrimero()
    {
        var code1 = Guid.NewGuid().ToString("N")[..8];
        var code2 = Guid.NewGuid().ToString("N")[..8];
        var regular = new AlicuotaIVA { Codigo = code1, Nombre = "IVA21-" + code1, Porcentaje = 21m, Activa = true, EsPredeterminada = false };
        var predeterminada = new AlicuotaIVA { Codigo = code2, Nombre = "IVA10-" + code2, Porcentaje = 10m, Activa = true, EsPredeterminada = true };
        _context.AlicuotasIVA.AddRange(regular, predeterminada);
        await _context.SaveChangesAsync();

        var resultado = await _service.ObtenerAlicuotasIVAParaFormAsync();

        var idxPredeterminada = resultado.FindIndex(a => a.Id == predeterminada.Id);
        var idxRegular = resultado.FindIndex(a => a.Id == regular.Id);
        Assert.True(idxPredeterminada < idxRegular, "La predeterminada debe aparecer antes que la regular");
    }

    // =========================================================================
    // ObtenerPorcentajeAlicuotaAsync
    // =========================================================================

    [Fact]
    public async Task ObtenerPorcentajeAlicuota_IdValido_DevuelvePorcentaje()
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var alicuota = new AlicuotaIVA { Codigo = code, Nombre = "IVA21-" + code, Porcentaje = 21m, Activa = true };
        _context.AlicuotasIVA.Add(alicuota);
        await _context.SaveChangesAsync();

        var resultado = await _service.ObtenerPorcentajeAlicuotaAsync(alicuota.Id);

        Assert.Equal(21m, resultado);
    }

    [Fact]
    public async Task ObtenerPorcentajeAlicuota_IdInvalido_DevuelveNull()
    {
        var resultado = await _service.ObtenerPorcentajeAlicuotaAsync(int.MaxValue);

        Assert.Null(resultado);
    }
}

// ---------------------------------------------------------------------------
// Stub mínimo de ICurrentUserService
// ---------------------------------------------------------------------------
file sealed class StubCurrentUserServiceLookup : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-test";
    public string GetEmail() => "test@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}

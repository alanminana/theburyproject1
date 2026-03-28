using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para CatalogoService.
/// Cubren ObtenerFilaAsync (producto no existe, sin precio lista usa PrecioVenta base,
/// con precio lista usa precio lista, margen calculado, EstadoStock: Normal/StockBajo/SinStock),
/// ObtenerCatalogoAsync (base vacía, filtro por texto, lista predeterminada resuelve precios,
/// sin lista predeterminada retorna precio base),
/// SimularCambioPreciosAsync (crea batch con items).
/// </summary>
public class CatalogoServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CatalogoService _service;

    // Servicios reales para el grafo completo
    private readonly PrecioService _precioService;
    private readonly ProductoService _productoService;
    private readonly CategoriaService _categoriaService;
    private readonly MarcaService _marcaService;

    public CatalogoServiceTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var config = new ConfigurationBuilder().Build();
        var stubUser = new StubCurrentUserServiceCatalogo();

        _categoriaService = new CategoriaService(_context, NullLogger<CategoriaService>.Instance);
        _marcaService = new MarcaService(_context, NullLogger<MarcaService>.Instance);
        var precioHistorico = new PrecioHistoricoService(_context, NullLogger<PrecioHistoricoService>.Instance);
        _productoService = new ProductoService(_context, NullLogger<ProductoService>.Instance, precioHistorico, stubUser);
        _precioService = new PrecioService(_context, NullLogger<PrecioService>.Instance, stubUser, config);

        var catalogLookup = new CatalogLookupService(_categoriaService, _marcaService, _productoService);

        _service = new CatalogoService(
            catalogLookup,
            _productoService,
            _precioService,
            NullLogger<CatalogoService>.Instance,
            stubUser);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<(Categoria cat, Marca marca)> SeedCatMarcaAsync()
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = code, Nombre = "Cat-" + code, Activo = true };
        var marca = new Marca { Codigo = code, Nombre = "Marca-" + code, Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        return (cat, marca);
    }

    private async Task<Producto> SeedProductoAsync(
        decimal precioCompra = 60m, decimal precioVenta = 100m,
        decimal stockActual = 10m, decimal stockMinimo = 5m,
        bool activo = true)
    {
        var (cat, marca) = await SeedCatMarcaAsync();
        var code = Guid.NewGuid().ToString("N")[..8];
        var prod = new Producto
        {
            Codigo = code, Nombre = "Prod-" + code,
            CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = precioCompra, PrecioVenta = precioVenta,
            PorcentajeIVA = 21m, StockActual = stockActual,
            StockMinimo = stockMinimo, Activo = activo
        };
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();
        return prod;
    }

    private async Task<ListaPrecio> SeedListaAsync(bool esPredeterminada = false)
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var lista = new ListaPrecio
        {
            Nombre = "Lista-" + code, Codigo = code,
            Tipo = TipoListaPrecio.Contado,
            Activa = true, EsPredeterminada = esPredeterminada,
            Orden = 1, ReglaRedondeo = "ninguno"
        };
        _context.ListasPrecios.Add(lista);
        await _context.SaveChangesAsync();
        await _context.Entry(lista).ReloadAsync();
        return lista;
    }

    private async Task<ProductoPrecioLista> SeedPrecioVigenteAsync(
        int productoId, int listaId, decimal precio, decimal costo = 60m)
    {
        var pp = new ProductoPrecioLista
        {
            ProductoId = productoId, ListaId = listaId,
            Precio = precio, Costo = costo,
            MargenValor = precio - costo,
            MargenPorcentaje = costo > 0 ? ((precio - costo) / costo) * 100 : 0,
            VigenciaDesde = DateTime.UtcNow.AddDays(-1),
            EsVigente = true, EsManual = true, CreadoPor = "test"
        };
        _context.ProductosPrecios.Add(pp);
        await _context.SaveChangesAsync();
        return pp;
    }

    // -------------------------------------------------------------------------
    // ObtenerFilaAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerFila_ProductoNoExiste_RetornaNull()
    {
        var resultado = await _service.ObtenerFilaAsync(99999);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task ObtenerFila_SinPrecioLista_UsaPrecioVentaBase()
    {
        var prod = await SeedProductoAsync(precioVenta: 100m);

        var fila = await _service.ObtenerFilaAsync(prod.Id);

        Assert.NotNull(fila);
        Assert.Equal(100m, fila!.PrecioActual);
        Assert.Equal(100m, fila.PrecioBase);
        Assert.False(fila.TienePrecioLista);
    }

    [Fact]
    public async Task ObtenerFila_ConPrecioLista_UsaPrecioLista()
    {
        var prod = await SeedProductoAsync(precioVenta: 100m);
        var lista = await SeedListaAsync(esPredeterminada: true);
        await SeedPrecioVigenteAsync(prod.Id, lista.Id, precio: 120m);

        var fila = await _service.ObtenerFilaAsync(prod.Id);

        Assert.NotNull(fila);
        Assert.Equal(120m, fila!.PrecioActual);  // precio de lista
        Assert.Equal(100m, fila.PrecioBase);     // precio base del producto
        Assert.True(fila.TienePrecioLista);
    }

    [Fact]
    public async Task ObtenerFila_CalculaMargenConPrecioBase()
    {
        // sin precio lista → usa PrecioVenta, margen = (100-60)/60*100 = 66.67%
        var prod = await SeedProductoAsync(precioCompra: 60m, precioVenta: 100m);

        var fila = await _service.ObtenerFilaAsync(prod.Id);

        Assert.NotNull(fila);
        Assert.Equal(Math.Round((100m - 60m) / 60m * 100m, 2), fila!.MargenPorcentaje);
    }

    [Fact]
    public async Task ObtenerFila_StockNormal_EstadoNormal()
    {
        var prod = await SeedProductoAsync(stockActual: 10m, stockMinimo: 5m);

        var fila = await _service.ObtenerFilaAsync(prod.Id);

        Assert.Equal("Normal", fila!.EstadoStock);
    }

    [Fact]
    public async Task ObtenerFila_StockBajo_EstadoStockBajo()
    {
        var prod = await SeedProductoAsync(stockActual: 3m, stockMinimo: 5m);

        var fila = await _service.ObtenerFilaAsync(prod.Id);

        Assert.Equal("Stock Bajo", fila!.EstadoStock);
    }

    [Fact]
    public async Task ObtenerFila_SinStock_EstadoSinStock()
    {
        var prod = await SeedProductoAsync(stockActual: 0m, stockMinimo: 5m);

        var fila = await _service.ObtenerFilaAsync(prod.Id);

        Assert.Equal("Sin Stock", fila!.EstadoStock);
    }

    [Fact]
    public async Task ObtenerFila_StockExactoMinimo_EstadoStockBajo()
    {
        // stockActual == stockMinimo → "Stock Bajo" (condición <=)
        var prod = await SeedProductoAsync(stockActual: 5m, stockMinimo: 5m);

        var fila = await _service.ObtenerFilaAsync(prod.Id);

        Assert.Equal("Stock Bajo", fila!.EstadoStock);
    }

    [Fact]
    public async Task ObtenerFila_ConListaPrecioEspecifica_UsaEsaLista()
    {
        var prod = await SeedProductoAsync(precioVenta: 100m);
        var lista1 = await SeedListaAsync();
        var lista2 = await SeedListaAsync();
        await SeedPrecioVigenteAsync(prod.Id, lista1.Id, precio: 110m);
        await SeedPrecioVigenteAsync(prod.Id, lista2.Id, precio: 130m);

        var fila = await _service.ObtenerFilaAsync(prod.Id, lista2.Id);

        Assert.NotNull(fila);
        Assert.Equal(130m, fila!.PrecioActual);
    }

    // -------------------------------------------------------------------------
    // ObtenerCatalogoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ObtenerCatalogo_BaseVacia_RetornaResultadoVacio()
    {
        var resultado = await _service.ObtenerCatalogoAsync(new FiltrosCatalogo());

        Assert.NotNull(resultado);
        Assert.Empty(resultado.Filas);
        Assert.Equal(0, resultado.TotalResultados);
    }

    [Fact]
    public async Task ObtenerCatalogo_ConProductos_RetornaFilas()
    {
        await SeedProductoAsync();
        await SeedProductoAsync();

        var resultado = await _service.ObtenerCatalogoAsync(new FiltrosCatalogo());

        Assert.Equal(2, resultado.TotalResultados);
        Assert.Equal(2, resultado.Filas.Count());
    }

    [Fact]
    public async Task ObtenerCatalogo_ConListaPredeterminada_ResuelvePrecios()
    {
        var prod = await SeedProductoAsync(precioVenta: 100m);
        var lista = await SeedListaAsync(esPredeterminada: true);
        await SeedPrecioVigenteAsync(prod.Id, lista.Id, precio: 150m);

        var resultado = await _service.ObtenerCatalogoAsync(new FiltrosCatalogo());

        var fila = resultado.Filas.FirstOrDefault(f => f.ProductoId == prod.Id);
        Assert.NotNull(fila);
        Assert.Equal(150m, fila!.PrecioActual);
        Assert.True(fila.TienePrecioLista);
    }

    [Fact]
    public async Task ObtenerCatalogo_SinListaPredeterminada_UsaPrecioBase()
    {
        var prod = await SeedProductoAsync(precioVenta: 80m);
        // No listas predeterminadas

        var resultado = await _service.ObtenerCatalogoAsync(new FiltrosCatalogo());

        var fila = resultado.Filas.FirstOrDefault(f => f.ProductoId == prod.Id);
        Assert.NotNull(fila);
        Assert.Equal(80m, fila!.PrecioActual);
        Assert.False(fila.TienePrecioLista);
    }

    [Fact]
    public async Task ObtenerCatalogo_ConListaEspecifica_UsaEsaLista()
    {
        var prod = await SeedProductoAsync(precioVenta: 100m);
        var lista = await SeedListaAsync(esPredeterminada: false);
        await SeedPrecioVigenteAsync(prod.Id, lista.Id, precio: 200m);

        var resultado = await _service.ObtenerCatalogoAsync(
            new FiltrosCatalogo { ListaPrecioId = lista.Id });

        var fila = resultado.Filas.FirstOrDefault(f => f.ProductoId == prod.Id);
        Assert.NotNull(fila);
        Assert.Equal(200m, fila!.PrecioActual);
    }

    [Fact]
    public async Task ObtenerCatalogo_IncluyCategoriasMarcasEnDropdowns()
    {
        await SeedProductoAsync(); // crea cat + marca internamente

        var resultado = await _service.ObtenerCatalogoAsync(new FiltrosCatalogo());

        Assert.True(resultado.TotalCategorias >= 1);
        Assert.True(resultado.TotalMarcas >= 1);
        Assert.NotEmpty(resultado.Categorias);
        Assert.NotEmpty(resultado.Marcas);
    }

    // -------------------------------------------------------------------------
    // SimularCambioPreciosAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SimularCambios_ConProductoYPrecio_CreasBatchConFilas()
    {
        var prod = await SeedProductoAsync(precioVenta: 100m);
        var lista = await SeedListaAsync();
        await SeedPrecioVigenteAsync(prod.Id, lista.Id, precio: 100m);

        var solicitud = new SolicitudSimulacionPrecios
        {
            Nombre = "Test simulación",
            TipoCambio = "porcentaje",
            Valor = 10m,
            ListasIds = new List<int> { lista.Id },
            ProductosIds = new List<int> { prod.Id }
        };

        var resultado = await _service.SimularCambioPreciosAsync(solicitud);

        Assert.True(resultado.BatchId > 0);
        Assert.Single(resultado.Filas);
        Assert.Equal(100m, resultado.Filas[0].PrecioActual);
        Assert.Equal(110m, resultado.Filas[0].PrecioNuevo);
    }

    [Fact]
    public async Task SimularCambios_ValorNegativo_EsDisminucion()
    {
        var prod = await SeedProductoAsync(precioVenta: 200m);
        var lista = await SeedListaAsync();
        await SeedPrecioVigenteAsync(prod.Id, lista.Id, precio: 200m);

        var solicitud = new SolicitudSimulacionPrecios
        {
            Nombre = "Baja 10%",
            TipoCambio = "porcentaje",
            Valor = -10m,  // negativo = disminución
            ListasIds = new List<int> { lista.Id },
            ProductosIds = new List<int> { prod.Id }
        };

        var resultado = await _service.SimularCambioPreciosAsync(solicitud);

        Assert.Single(resultado.Filas);
        Assert.Equal(180m, resultado.Filas[0].PrecioNuevo); // 200 * 0.9
    }
}

file sealed class StubCurrentUserServiceCatalogo : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-test";
    public string GetEmail() => "test@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}

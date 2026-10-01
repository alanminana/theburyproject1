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
/// Tests de integración para PrecioService.AplicarCambioPrecioDirectoAsync.
/// Cubren guards de entrada (model null, porcentaje cero, alcance inválido, sin
/// productos en filtro, filtros JSON inválido), happy path con alcance "seleccionados",
/// actualización de Producto.PrecioVenta, creación de PrecioHistorico y CambioPrecioEvento,
/// y precio resultante negativo.
/// </summary>
public class PrecioServiceCambioDirectoTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly PrecioService _service;

    public PrecioServiceCambioDirectoTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var config = new ConfigurationBuilder().Build();
        _service = new PrecioService(_context, NullLogger<PrecioService>.Instance,
            new StubCurrentUserServiceCambioDirecto(), config);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Producto> SeedProductoAsync(decimal precioVenta = 100m)
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = code, Nombre = "Cat-" + code, Activo = true };
        var marca = new Marca { Codigo = code, Nombre = "Marca-" + code, Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var prod = new Producto
        {
            Codigo = code, Nombre = "Prod-" + code,
            CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = 60m, PrecioVenta = precioVenta,
            PorcentajeIVA = 21m, StockActual = 5m, Activo = true
        };
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();
        return prod;
    }

    // -------------------------------------------------------------------------
    // Guards de entrada
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AplicarCambioDirecto_ModelNull_RetornaNoExitoso()
    {
        var resultado = await _service.AplicarCambioPrecioDirectoAsync(null!);

        Assert.False(resultado.Exitoso);
    }

    [Fact]
    public async Task AplicarCambioDirecto_PorcentajeCero_RetornaNoExitoso()
    {
        var resultado = await _service.AplicarCambioPrecioDirectoAsync(
            new AplicarCambioPrecioDirectoViewModel
            {
                Alcance = "seleccionados",
                ValorPorcentaje = 0,
                ProductoIdsText = "1"
            });

        Assert.False(resultado.Exitoso);
    }

    [Fact]
    public async Task AplicarCambioDirecto_AlcanceInvalido_RetornaNoExitoso()
    {
        var resultado = await _service.AplicarCambioPrecioDirectoAsync(
            new AplicarCambioPrecioDirectoViewModel
            {
                Alcance = "invalido",
                ValorPorcentaje = 10
            });

        Assert.False(resultado.Exitoso);
    }

    [Fact]
    public async Task AplicarCambioDirecto_SeleccionadosSinIds_RetornaNoExitoso()
    {
        var resultado = await _service.AplicarCambioPrecioDirectoAsync(
            new AplicarCambioPrecioDirectoViewModel
            {
                Alcance = "seleccionados",
                ValorPorcentaje = 10,
                ProductoIdsText = ""
            });

        Assert.False(resultado.Exitoso);
    }

    [Fact]
    public async Task AplicarCambioDirecto_FiltradosSinFiltrosJson_RetornaNoExitoso()
    {
        var resultado = await _service.AplicarCambioPrecioDirectoAsync(
            new AplicarCambioPrecioDirectoViewModel
            {
                Alcance = "filtrados",
                ValorPorcentaje = 10,
                FiltrosJson = null
            });

        Assert.False(resultado.Exitoso);
    }

    [Fact]
    public async Task AplicarCambioDirecto_FiltradosFiltrosJsonInvalido_RetornaNoExitoso()
    {
        var resultado = await _service.AplicarCambioPrecioDirectoAsync(
            new AplicarCambioPrecioDirectoViewModel
            {
                Alcance = "filtrados",
                ValorPorcentaje = 10,
                FiltrosJson = "{ invalid json"
            });

        Assert.False(resultado.Exitoso);
    }

    [Fact]
    public async Task AplicarCambioDirecto_ProductoIdInexistente_RetornaNoExitoso()
    {
        var resultado = await _service.AplicarCambioPrecioDirectoAsync(
            new AplicarCambioPrecioDirectoViewModel
            {
                Alcance = "seleccionados",
                ValorPorcentaje = 10,
                ProductoIdsText = "99999"
            });

        Assert.False(resultado.Exitoso);
    }

    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AplicarCambioDirecto_Seleccionados10Pct_ActualizaPrecioVenta()
    {
        var prod = await SeedProductoAsync(precioVenta: 100m);

        var resultado = await _service.AplicarCambioPrecioDirectoAsync(
            new AplicarCambioPrecioDirectoViewModel
            {
                Alcance = "seleccionados",
                ValorPorcentaje = 10m,
                ProductoIdsText = prod.Id.ToString(),
                Motivo = "test aumento"
            });

        Assert.True(resultado.Exitoso);
        Assert.Equal(1, resultado.ProductosActualizados);

        _context.ChangeTracker.Clear();
        var prodBd = await _context.Productos.FirstAsync(p => p.Id == prod.Id);
        Assert.Equal(110m, prodBd.PrecioVenta); // 100 * 1.10
    }

    [Fact]
    public async Task AplicarCambioDirecto_CreaHistoricoYEvento()
    {
        var prod = await SeedProductoAsync(precioVenta: 100m);

        await _service.AplicarCambioPrecioDirectoAsync(
            new AplicarCambioPrecioDirectoViewModel
            {
                Alcance = "seleccionados",
                ValorPorcentaje = 10m,
                ProductoIdsText = prod.Id.ToString()
            });

        var historico = await _context.PreciosHistoricos
            .FirstOrDefaultAsync(h => h.ProductoId == prod.Id);
        Assert.NotNull(historico);
        Assert.Equal(100m, historico!.PrecioVentaAnterior);
        Assert.Equal(110m, historico.PrecioVentaNuevo);

        var evento = await _context.CambioPrecioEventos.FirstOrDefaultAsync();
        Assert.NotNull(evento);
        Assert.Equal(1, evento!.CantidadProductos);
    }

    [Fact]
    public async Task AplicarCambioDirecto_PrecioNegativo_RetornaNoExitoso()
    {
        var prod = await SeedProductoAsync(precioVenta: 100m);

        // -200% → precio negativo
        var resultado = await _service.AplicarCambioPrecioDirectoAsync(
            new AplicarCambioPrecioDirectoViewModel
            {
                Alcance = "seleccionados",
                ValorPorcentaje = -200m,
                ProductoIdsText = prod.Id.ToString()
            });

        Assert.False(resultado.Exitoso);
    }

    [Fact]
    public async Task AplicarCambioDirecto_Disminucion_ActualizaCorrectamente()
    {
        var prod = await SeedProductoAsync(precioVenta: 200m);

        var resultado = await _service.AplicarCambioPrecioDirectoAsync(
            new AplicarCambioPrecioDirectoViewModel
            {
                Alcance = "seleccionados",
                ValorPorcentaje = -10m,
                ProductoIdsText = prod.Id.ToString()
            });

        Assert.True(resultado.Exitoso);
        _context.ChangeTracker.Clear();
        var prodBd = await _context.Productos.FirstAsync(p => p.Id == prod.Id);
        Assert.Equal(180m, prodBd.PrecioVenta); // 200 * (1 + (-10/100)) = 200 * 0.9 = 180
    }

    [Theory]
    [InlineData("porcentaje", 10, "none", 100, 110)]
    [InlineData("porcentaje", 10, "entero", 103.4, 114)]       // 113.74 -> 114
    [InlineData("porcentaje", 10, "99", 100, 110.99)]            // 110.00 -> 110.99
    [InlineData("montofijo", 50, "none", 100, 150)]
    [InlineData("MontoFijo", 100, "99", 12500, 12600.99)]
    public void CalcularPrecioBase_AplicaTipoYRedondeo(string tipo, decimal valor, string redondeo, decimal anterior, decimal esperado)
    {
        Assert.Equal(esperado, PrecioService.CalcularPrecioBase(anterior, tipo, valor, redondeo));
    }

    [Fact]
    public async Task AplicarCambioDirecto_MontoFijoConRedondeo_CoincideConLaVistaPrevia()
    {
        var prod = await SeedProductoAsync(precioVenta: 12500m);

        var filas = await _service.SimularCambioPrecioBaseAsync("montofijo", 100m, "99", productoIds: new List<int> { prod.Id });
        var fila = Assert.Single(filas);
        Assert.Equal(12600.99m, fila.PrecioNuevo);

        var resultado = await _service.AplicarCambioPrecioDirectoAsync(
            new AplicarCambioPrecioDirectoViewModel
            {
                Alcance = "seleccionados",
                ValorPorcentaje = 100m,
                TipoCambio = "montofijo",
                Redondeo = "99",
                ProductoIdsText = prod.Id.ToString()
            });

        Assert.True(resultado.Exitoso);
        _context.ChangeTracker.Clear();
        var prodBd = await _context.Productos.FirstAsync(p => p.Id == prod.Id);
        Assert.Equal(fila.PrecioNuevo, prodBd.PrecioVenta);

        // El evento no guarda "100" como si fuera un porcentaje: guarda el % efectivo.
        var evento = await _context.CambioPrecioEventos.FirstAsync();
        Assert.Equal(Math.Round((12600.99m - 12500m) / 12500m * 100m, 2), evento.ValorPorcentaje);
    }

    [Fact]
    public async Task SimularCambioPrecioBase_PorMarca_SoloActivosDeEsaMarca()
    {
        var a = await SeedProductoAsync(precioVenta: 100m);
        var b = await SeedProductoAsync(precioVenta: 200m);
        var inactivo = await SeedProductoAsync(precioVenta: 300m);
        inactivo.MarcaId = a.MarcaId;
        inactivo.Activo = false;
        await _context.SaveChangesAsync();

        var filas = await _service.SimularCambioPrecioBaseAsync("porcentaje", 10m, "none",
            marcaIds: new List<int> { a.MarcaId });

        var fila = Assert.Single(filas);
        Assert.Equal(a.Id, fila.ProductoId);
        Assert.Equal(110m, fila.PrecioNuevo);
        Assert.DoesNotContain(filas, f => f.ProductoId == b.Id || f.ProductoId == inactivo.Id);
    }
}

file sealed class StubCurrentUserServiceCambioDirecto : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-test";
    public string GetEmail() => "test@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}

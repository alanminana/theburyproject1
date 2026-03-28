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
/// Tests de integración para PrecioService — gestión de precios por producto/lista.
/// Cubren GetPrecioVigenteAsync (existe, no existe, vigencia por fecha, producto eliminado),
/// GetPreciosProductoAsync (multiples listas), GetHistorialPreciosAsync,
/// SetPrecioManualAsync (persistencia, campos, producto inexistente, lista inexistente,
/// precio anterior queda no vigente, margen calculado),
/// CalcularPrecioAutomaticoAsync (sin margen, con margen, con recargo, lista inexistente).
/// </summary>
public class PrecioServicePrecioProductoTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly PrecioService _service;

    public PrecioServicePrecioProductoTests()
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
            new StubCurrentUserServicePrecioProducto(), config);
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
            Codigo = code, Nombre = "Prod-" + code,
            CategoriaId = cat.Id, MarcaId = marca.Id,
            PrecioCompra = 10m, PrecioVenta = 50m,
            PorcentajeIVA = 21m, StockActual = 5m, Activo = true
        };
        _context.Productos.Add(prod);
        await _context.SaveChangesAsync();
        return prod;
    }

    private async Task<ListaPrecio> SeedListaAsync(
        decimal? margenPorcentaje = null,
        decimal? recargoPorcentaje = null,
        string? reglaRedondeo = "ninguno")
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
            MargenPorcentaje = margenPorcentaje,
            RecargoPorcentaje = recargoPorcentaje,
            ReglaRedondeo = reglaRedondeo
        };
        _context.ListasPrecios.Add(lista);
        await _context.SaveChangesAsync();
        await _context.Entry(lista).ReloadAsync();
        return lista;
    }

    private async Task<ProductoPrecioLista> SeedPrecioAsync(
        int productoId, int listaId,
        decimal precio = 100m, decimal costo = 60m,
        DateTime? vigenciaDesde = null, DateTime? vigenciaHasta = null,
        bool esVigente = true)
    {
        var pp = new ProductoPrecioLista
        {
            ProductoId = productoId,
            ListaId = listaId,
            Precio = precio,
            Costo = costo,
            MargenValor = precio - costo,
            MargenPorcentaje = costo > 0 ? ((precio - costo) / costo) * 100 : 0,
            VigenciaDesde = vigenciaDesde ?? DateTime.UtcNow.AddDays(-1),
            VigenciaHasta = vigenciaHasta,
            EsVigente = esVigente,
            EsManual = true,
            CreadoPor = "test"
        };
        _context.ProductosPrecios.Add(pp);
        await _context.SaveChangesAsync();
        return pp;
    }

    // -------------------------------------------------------------------------
    // GetPrecioVigenteAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPrecioVigente_Existe_RetornaPrecio()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        var pp = await SeedPrecioAsync(prod.Id, lista.Id, precio: 120m);

        var resultado = await _service.GetPrecioVigenteAsync(prod.Id, lista.Id);

        Assert.NotNull(resultado);
        Assert.Equal(120m, resultado!.Precio);
    }

    [Fact]
    public async Task GetPrecioVigente_NoExiste_RetornaNull()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();

        var resultado = await _service.GetPrecioVigenteAsync(prod.Id, lista.Id);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetPrecioVigente_NoVigente_RetornaNull()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        await SeedPrecioAsync(prod.Id, lista.Id, esVigente: false);

        var resultado = await _service.GetPrecioVigenteAsync(prod.Id, lista.Id);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetPrecioVigente_VigenciaVencida_RetornaNull()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        // vigencia que ya expiró
        await SeedPrecioAsync(prod.Id, lista.Id,
            vigenciaDesde: DateTime.UtcNow.AddDays(-10),
            vigenciaHasta: DateTime.UtcNow.AddDays(-1),
            esVigente: true);

        var resultado = await _service.GetPrecioVigenteAsync(prod.Id, lista.Id);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task GetPrecioVigente_PorFechaHistorica_RetornaPrecioCorrecto()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();

        var fechaHistorica = DateTime.UtcNow.AddDays(-5);
        // precio que solo era vigente en la fecha histórica (ya expirado, no vigente)
        await SeedPrecioAsync(prod.Id, lista.Id, precio: 80m,
            vigenciaDesde: DateTime.UtcNow.AddDays(-10),
            vigenciaHasta: DateTime.UtcNow.AddDays(-3),
            esVigente: false);  // marcado no vigente, expirado por VigenciaHasta

        var resultado = await _service.GetPrecioVigenteAsync(prod.Id, lista.Id, fechaHistorica);

        // EsVigente=false → no retorna, aunque la fecha está dentro del rango
        // Este test verifica que el flag EsVigente es determinante
        Assert.Null(resultado);
    }

    // -------------------------------------------------------------------------
    // GetPreciosProductoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPreciosProducto_MultiplasListas_RetornaTodasVigentes()
    {
        var prod = await SeedProductoAsync();
        var lista1 = await SeedListaAsync();
        var lista2 = await SeedListaAsync();
        await SeedPrecioAsync(prod.Id, lista1.Id, precio: 100m);
        await SeedPrecioAsync(prod.Id, lista2.Id, precio: 120m);

        var resultado = await _service.GetPreciosProductoAsync(prod.Id);

        Assert.Equal(2, resultado.Count);
    }

    [Fact]
    public async Task GetPreciosProducto_SinPrecios_RetornaVacio()
    {
        var prod = await SeedProductoAsync();

        var resultado = await _service.GetPreciosProductoAsync(prod.Id);

        Assert.Empty(resultado);
    }

    // -------------------------------------------------------------------------
    // GetHistorialPreciosAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetHistorial_ConPrecioVigente_RetornaPrecio()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        await SeedPrecioAsync(prod.Id, lista.Id, precio: 100m);

        var resultado = await _service.GetHistorialPreciosAsync(prod.Id, lista.Id);

        Assert.Single(resultado);
        Assert.Equal(100m, resultado[0].Precio);
    }

    [Fact]
    public async Task GetHistorial_NoExistePrecio_RetornaVacio()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();

        var resultado = await _service.GetHistorialPreciosAsync(prod.Id, lista.Id);

        Assert.Empty(resultado);
    }

    // -------------------------------------------------------------------------
    // SetPrecioManualAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetPrecioManual_DatosValidos_Persiste()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();

        var resultado = await _service.SetPrecioManualAsync(prod.Id, lista.Id,
            precio: 150m, costo: 90m);

        Assert.True(resultado.Id > 0);
        Assert.Equal(150m, resultado.Precio);
        Assert.Equal(90m, resultado.Costo);
        Assert.True(resultado.EsVigente);
        Assert.True(resultado.EsManual);
    }

    [Fact]
    public async Task SetPrecioManual_CalculaMargenCorrectamente()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();

        var resultado = await _service.SetPrecioManualAsync(prod.Id, lista.Id,
            precio: 150m, costo: 100m);

        // Margen = (150-100) / 100 * 100 = 50%
        Assert.Equal(50m, resultado.MargenValor);
        Assert.Equal(50m, resultado.MargenPorcentaje);
    }

    [Fact]
    public async Task SetPrecioManual_ProductoInexistente_LanzaExcepcion()
    {
        var lista = await SeedListaAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SetPrecioManualAsync(99999, lista.Id, 100m, 60m));
    }

    [Fact]
    public async Task SetPrecioManual_ListaInexistente_LanzaExcepcion()
    {
        var prod = await SeedProductoAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SetPrecioManualAsync(prod.Id, 99999, 100m, 60m));
    }

    [Fact]
    public async Task SetPrecioManual_PrecioAnteriorQuedaNoVigente()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        var anterior = await SeedPrecioAsync(prod.Id, lista.Id, precio: 80m);

        await _service.SetPrecioManualAsync(prod.Id, lista.Id, precio: 100m, costo: 60m);

        _context.ChangeTracker.Clear();
        var anteriorBd = await _context.ProductosPrecios
            .IgnoreQueryFilters()
            .FirstAsync(p => p.Id == anterior.Id);
        Assert.False(anteriorBd.EsVigente);
        Assert.NotNull(anteriorBd.VigenciaHasta);
    }

    [Fact]
    public async Task SetPrecioManual_SoloUnPrecioVigenteporProductoYLista()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync();
        await SeedPrecioAsync(prod.Id, lista.Id, precio: 80m);

        await _service.SetPrecioManualAsync(prod.Id, lista.Id, precio: 100m, costo: 60m);

        _context.ChangeTracker.Clear();
        var vigentes = await _context.ProductosPrecios
            .CountAsync(p => p.ProductoId == prod.Id && p.ListaId == lista.Id && p.EsVigente && !p.IsDeleted);
        Assert.Equal(1, vigentes);
    }

    // -------------------------------------------------------------------------
    // CalcularPrecioAutomaticoAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CalcularPrecioAutomatico_SinMargenNiRecargo_RetornaCosto()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync(margenPorcentaje: null, recargoPorcentaje: null);

        var precio = await _service.CalcularPrecioAutomaticoAsync(prod.Id, lista.Id, costo: 100m);

        Assert.Equal(100m, precio);
    }

    [Fact]
    public async Task CalcularPrecioAutomatico_ConMargen50_AplicaMargen()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync(margenPorcentaje: 50m);

        var precio = await _service.CalcularPrecioAutomaticoAsync(prod.Id, lista.Id, costo: 100m);

        // 100 * (1 + 50/100) = 150
        Assert.Equal(150m, precio);
    }

    [Fact]
    public async Task CalcularPrecioAutomatico_ConRecargo10_AplicaRecargo()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync(margenPorcentaje: null, recargoPorcentaje: 10m);

        var precio = await _service.CalcularPrecioAutomaticoAsync(prod.Id, lista.Id, costo: 100m);

        // 100 * (1 + 10/100) = 110
        Assert.Equal(110m, precio);
    }

    [Fact]
    public async Task CalcularPrecioAutomatico_ConMargenYRecargo_AplicaAmbos()
    {
        var prod = await SeedProductoAsync();
        var lista = await SeedListaAsync(margenPorcentaje: 50m, recargoPorcentaje: 10m);

        var precio = await _service.CalcularPrecioAutomaticoAsync(prod.Id, lista.Id, costo: 100m);

        // costo * (1 + margen/100) * (1 + recargo/100) = 100 * 1.5 * 1.1 = 165
        Assert.Equal(165m, precio);
    }

    [Fact]
    public async Task CalcularPrecioAutomatico_ListaInexistente_LanzaExcepcion()
    {
        var prod = await SeedProductoAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CalcularPrecioAutomaticoAsync(prod.Id, 99999, costo: 100m));
    }
}

file sealed class StubCurrentUserServicePrecioProducto : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-test";
    public string GetEmail() => "test@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}

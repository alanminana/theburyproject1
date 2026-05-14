using System.Text.Json;
using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración para MovimientoStockController.
/// Verifican que Index, Create (GET) y Kardex devuelven las vistas correctas
/// y que el flujo Create POST sigue funcionando.
/// </summary>
public class MovimientoStockControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly MovimientoStockController _controller;
    private readonly MovimientoStockService _movimientoService;
    private readonly ProductoService _productoService;

    public MovimientoStockControllerTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var currentUser = new StubCurrentUserMovCtrl();
        var resolver = new PrecioVigenteResolver(_context);

        _movimientoService = new MovimientoStockService(
            _context,
            NullLogger<MovimientoStockService>.Instance);

        _productoService = new ProductoService(
            _context,
            NullLogger<ProductoService>.Instance,
            new StubPrecioHistoricoMovCtrl(),
            currentUser,
            resolver);

        var mapper = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance).CreateMapper();

        _controller = new MovimientoStockController(
            _movimientoService,
            _productoService,
            mapper,
            NullLogger<MovimientoStockController>.Instance,
            currentUser);

        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        _controller.TempData = new StubTempDataMovCtrl();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<Producto> SeedProductoAsync(decimal stockActual = 50m)
    {
        var codigo = Guid.NewGuid().ToString("N")[..10];

        var categoria = new Categoria { Codigo = codigo, Nombre = "Cat-" + codigo, Activo = true };
        _context.Set<Categoria>().Add(categoria);
        await _context.SaveChangesAsync();

        var marca = new Marca { Codigo = codigo, Nombre = "Marca-" + codigo, Activo = true };
        _context.Set<Marca>().Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = codigo,
            Nombre = "Prod-" + codigo,
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = 10m,
            PrecioVenta = 15m,
            PorcentajeIVA = 21m,
            StockActual = stockActual,
            Activo = true
        };
        _context.Set<Producto>().Add(producto);
        await _context.SaveChangesAsync();

        return producto;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Index
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Index_SinFiltros_DevuelveVistaIndexTw()
    {
        var result = await _controller.Index(new MovimientoStockFilterViewModel()) as ViewResult;

        Assert.NotNull(result);
        Assert.Equal("Index_tw", result!.ViewName);
    }

    [Fact]
    public async Task Index_SinMovimientos_DevuelveModelConListaVacia()
    {
        var result = await _controller.Index(new MovimientoStockFilterViewModel()) as ViewResult;

        var model = Assert.IsType<MovimientoStockFilterViewModel>(result!.Model);
        Assert.Empty(model.Movimientos);
        Assert.Equal(0, model.TotalResultados);
    }

    [Fact]
    public async Task Index_ConMovimientosRegistrados_DevuelveMovimientosEnModel()
    {
        var producto = await SeedProductoAsync();
        await _movimientoService.RegistrarAjusteAsync(producto.Id, TipoMovimiento.Entrada, 10m, null, "Test", "user");

        var result = await _controller.Index(new MovimientoStockFilterViewModel()) as ViewResult;

        var model = Assert.IsType<MovimientoStockFilterViewModel>(result!.Model);
        Assert.Equal(1, model.TotalResultados);
        Assert.Single(model.Movimientos);
    }

    [Fact]
    public async Task Index_ConFiltroProductoId_FiltraResultados()
    {
        var p1 = await SeedProductoAsync();
        var p2 = await SeedProductoAsync();
        await _movimientoService.RegistrarAjusteAsync(p1.Id, TipoMovimiento.Entrada, 5m, null, "T", "u");
        await _movimientoService.RegistrarAjusteAsync(p2.Id, TipoMovimiento.Entrada, 5m, null, "T", "u");

        var filter = new MovimientoStockFilterViewModel { ProductoId = p1.Id };
        var result = await _controller.Index(filter) as ViewResult;

        var model = Assert.IsType<MovimientoStockFilterViewModel>(result!.Model);
        Assert.Equal(1, model.TotalResultados);
        Assert.All(model.Movimientos, m => Assert.Equal(p1.Id, m.ProductoId));
    }

    [Fact]
    public async Task Index_PoblacionViewBagProductos_NoEsNulo()
    {
        var result = await _controller.Index(new MovimientoStockFilterViewModel()) as ViewResult;

        Assert.NotNull(_controller.ViewBag.Productos);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Create — GET
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Get_SinProductoId_DevuelveVistaCreateTw()
    {
        var result = await _controller.Create(productoId: null) as ViewResult;

        Assert.NotNull(result);
        Assert.Equal("Create_tw", result!.ViewName);
    }

    [Fact]
    public async Task Create_Get_SinProductoId_ViewModelVacio()
    {
        var result = await _controller.Create(productoId: null) as ViewResult;

        var model = Assert.IsType<AjusteStockViewModel>(result!.Model);
        Assert.Equal(0, model.ProductoId);
        Assert.Null(model.ProductoNombre);
    }

    [Fact]
    public async Task Create_Get_ConProductoIdExistente_PreseleccionaProducto()
    {
        var producto = await SeedProductoAsync(stockActual: 42m);

        var result = await _controller.Create(productoId: producto.Id) as ViewResult;

        Assert.NotNull(result);
        Assert.Equal("Create_tw", result!.ViewName);

        var model = Assert.IsType<AjusteStockViewModel>(result.Model);
        Assert.Equal(producto.Id, model.ProductoId);
        Assert.Equal(producto.Nombre, model.ProductoNombre);
        Assert.Equal(producto.Codigo, model.ProductoCodigo);
        Assert.Equal(42m, model.StockActual);
    }

    [Fact]
    public async Task Create_Get_ConProductoIdInexistente_DevuelveVistaConViewModelVacio()
    {
        var result = await _controller.Create(productoId: 99999) as ViewResult;

        Assert.NotNull(result);
        Assert.Equal("Create_tw", result!.ViewName);

        var model = Assert.IsType<AjusteStockViewModel>(result!.Model);
        Assert.Equal(0, model.ProductoId);
        Assert.Null(model.ProductoNombre);
    }

    [Fact]
    public async Task Create_Get_PoblacionViewBagProductos_SoloActivos()
    {
        var activo = await SeedProductoAsync();
        var inactivo = new Producto
        {
            Codigo = "INACT",
            Nombre = "Inactivo",
            CategoriaId = activo.CategoriaId,
            MarcaId = activo.MarcaId,
            PrecioCompra = 1m,
            PrecioVenta = 2m,
            PorcentajeIVA = 21m,
            Activo = false
        };
        _context.Set<Producto>().Add(inactivo);
        await _context.SaveChangesAsync();

        await _controller.Create(productoId: null);

        Assert.NotNull(_controller.ViewBag.Productos);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Kardex
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Kardex_ProductoExistente_DevuelveVistaKardexTw()
    {
        var producto = await SeedProductoAsync();

        var result = await _controller.Kardex(producto.Id) as ViewResult;

        Assert.NotNull(result);
        Assert.Equal("Kardex_tw", result!.ViewName);
    }

    [Fact]
    public async Task Kardex_ProductoExistente_ViewBagProductoAsignado()
    {
        var producto = await SeedProductoAsync();

        await _controller.Kardex(producto.Id);

        var productoVb = _controller.ViewBag.Producto as Producto;
        Assert.NotNull(productoVb);
        Assert.Equal(producto.Id, productoVb!.Id);
    }

    [Fact]
    public async Task Kardex_ProductoExistente_DevuelveMovimientosDelProducto()
    {
        var producto = await SeedProductoAsync();
        await _movimientoService.RegistrarAjusteAsync(producto.Id, TipoMovimiento.Entrada, 10m, null, "T", "u");
        await _movimientoService.RegistrarAjusteAsync(producto.Id, TipoMovimiento.Salida, 3m, null, "T", "u");

        var result = await _controller.Kardex(producto.Id) as ViewResult;

        var model = Assert.IsAssignableFrom<IEnumerable<MovimientoStockViewModel>>(result!.Model);
        Assert.Equal(2, model.Count());
    }

    [Fact]
    public async Task Kardex_ProductoInexistente_RedirigueAProductoIndex()
    {
        var result = await _controller.Kardex(99999) as RedirectToActionResult;

        Assert.NotNull(result);
        Assert.Equal("Index", result!.ActionName);
        Assert.Equal("Producto", result.ControllerName);
    }

    [Fact]
    public async Task Kardex_MovimientoConMotivo_MotivoPresente()
    {
        var producto = await SeedProductoAsync();
        await _movimientoService.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Entrada, 10m, null, "Ajuste por conciliación de stock", "user");

        var result = await _controller.Kardex(producto.Id) as ViewResult;
        var model = Assert.IsAssignableFrom<IEnumerable<MovimientoStockViewModel>>(result!.Model).ToList();

        Assert.Single(model);
        Assert.Equal("Ajuste por conciliación de stock", model[0].Motivo);
    }

    [Fact]
    public async Task Kardex_MovimientoSinMotivo_MotivoNullEnModel()
    {
        var producto = await SeedProductoAsync();
        var movimiento = new MovimientoStock
        {
            ProductoId = producto.Id,
            Tipo = TipoMovimiento.Entrada,
            Cantidad = 5m,
            StockAnterior = producto.StockActual,
            StockNuevo = producto.StockActual + 5m,
            Motivo = null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "testuser"
        };
        await _movimientoService.CreateAsync(movimiento);

        var result = await _controller.Kardex(producto.Id) as ViewResult;
        var model = Assert.IsAssignableFrom<IEnumerable<MovimientoStockViewModel>>(result!.Model).ToList();

        Assert.Single(model);
        Assert.Null(model[0].Motivo);
    }

    [Fact]
    public async Task Kardex_AjusteConciliacion_MotivoYReferenciaPresentes()
    {
        var producto = await SeedProductoAsync(stockActual: 20m);
        var referencia = $"ConciliacionUnidad:{producto.Id}";
        var motivo = "Conciliación de unidades físicas";
        await _movimientoService.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Ajuste, 15m, referencia, motivo, "user");

        var result = await _controller.Kardex(producto.Id) as ViewResult;
        var model = Assert.IsAssignableFrom<IEnumerable<MovimientoStockViewModel>>(result!.Model).ToList();

        Assert.Single(model);
        Assert.Equal(motivo, model[0].Motivo);
        Assert.Equal(referencia, model[0].Referencia);
    }

    [Fact]
    public async Task Kardex_AjusteNegativo_CantidadEnModelEsNegativa()
    {
        var producto = await SeedProductoAsync(stockActual: 20m);
        await _movimientoService.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Ajuste, 15m, null, "Corrección baja", "user");

        var result = await _controller.Kardex(producto.Id) as ViewResult;
        var model = Assert.IsAssignableFrom<IEnumerable<MovimientoStockViewModel>>(result!.Model).ToList();

        Assert.Single(model);
        Assert.Equal(-5m, model[0].Cantidad);
        Assert.Equal(20m, model[0].StockAnterior);
        Assert.Equal(15m, model[0].StockNuevo);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ListJson
    // ─────────────────────────────────────────────────────────────────────────

    private static JsonElement ParseListJsonResult(IActionResult result)
    {
        var json = Assert.IsType<JsonResult>(result);
        var raw = JsonSerializer.Serialize(json.Value);
        return JsonDocument.Parse(raw).RootElement;
    }

    [Fact]
    public async Task ListJson_ConMotivo_DevuelveMotivoEnItems()
    {
        var producto = await SeedProductoAsync();
        await _movimientoService.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Entrada, 10m, null, "Ajuste por conciliación", "user");

        var result = await _controller.ListJson(null, null, null, null, null);
        var doc = ParseListJsonResult(result);
        var items = doc.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal("Ajuste por conciliación", items[0].GetProperty("motivo").GetString());
    }

    [Fact]
    public async Task ListJson_SinMotivo_DevuelveMotivoNullEnItems()
    {
        var producto = await SeedProductoAsync();
        var movimiento = new MovimientoStock
        {
            ProductoId = producto.Id,
            Tipo = TipoMovimiento.Entrada,
            Cantidad = 5m,
            StockAnterior = producto.StockActual,
            StockNuevo = producto.StockActual + 5m,
            Motivo = null,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "testuser"
        };
        await _movimientoService.CreateAsync(movimiento);

        var result = await _controller.ListJson(null, null, null, null, null);
        var doc = ParseListJsonResult(result);
        var items = doc.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("motivo").ValueKind);
    }

    [Fact]
    public async Task ListJson_ConReferencia_DevuelveReferenciaEnItems()
    {
        var producto = await SeedProductoAsync();
        var referencia = $"ConciliacionUnidad:{producto.Id}";
        await _movimientoService.RegistrarAjusteAsync(
            producto.Id, TipoMovimiento.Ajuste, 15m, referencia, "Conciliación física", "user");

        var result = await _controller.ListJson(null, null, null, null, null);
        var doc = ParseListJsonResult(result);
        var items = doc.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal(referencia, items[0].GetProperty("referencia").GetString());
        Assert.Equal("Conciliación física", items[0].GetProperty("motivo").GetString());
    }

    [Fact]
    public async Task ListJson_FiltradoPorProductoId_DevuelveSoloMovimientosDelProducto()
    {
        var p1 = await SeedProductoAsync();
        var p2 = await SeedProductoAsync();
        await _movimientoService.RegistrarAjusteAsync(p1.Id, TipoMovimiento.Entrada, 5m, null, "Motivo P1", "u");
        await _movimientoService.RegistrarAjusteAsync(p2.Id, TipoMovimiento.Entrada, 5m, null, "Motivo P2", "u");

        var result = await _controller.ListJson(p1.Id, null, null, null, null);
        var doc = ParseListJsonResult(result);
        var items = doc.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal(p1.Id, items[0].GetProperty("productoId").GetInt32());
        Assert.Equal("Motivo P1", items[0].GetProperty("motivo").GetString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Create — POST (validación básica)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_Post_ModeloInvalido_DevuelveVistaConErrores()
    {
        _controller.ModelState.AddModelError("Motivo", "El motivo es requerido");

        var vm = new AjusteStockViewModel { ProductoId = 1, Cantidad = 5m };
        var result = await _controller.Create(vm) as ViewResult;

        Assert.NotNull(result);
        Assert.Equal("Create_tw", result!.ViewName);
    }

    [Fact]
    public async Task Create_Post_ProductoInexistente_DevuelveVistaConError()
    {
        var vm = new AjusteStockViewModel
        {
            ProductoId = 99999,
            Tipo = TipoMovimiento.Entrada,
            Cantidad = 5m,
            Motivo = "Test"
        };

        var result = await _controller.Create(vm) as ViewResult;

        Assert.NotNull(result);
        Assert.Equal("Create_tw", result!.ViewName);
        Assert.False(_controller.ModelState.IsValid);
    }

    [Fact]
    public async Task Create_Post_Exitoso_RedirigueAKardex()
    {
        var producto = await SeedProductoAsync();
        var vm = new AjusteStockViewModel
        {
            ProductoId = producto.Id,
            Tipo = TipoMovimiento.Entrada,
            Cantidad = 10m,
            Motivo = "Compra test"
        };

        var result = await _controller.Create(vm) as RedirectToActionResult;

        Assert.NotNull(result);
        Assert.Equal("Kardex", result!.ActionName);
        Assert.Equal(producto.Id, result.RouteValues?["id"]);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Stubs de dependencias
// ─────────────────────────────────────────────────────────────────────────────

file sealed class StubCurrentUserMovCtrl : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-test";
    public string GetEmail() => "test@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}

file sealed class StubPrecioHistoricoMovCtrl : IPrecioHistoricoService
{
    public Task<PrecioHistorico> RegistrarCambioAsync(
        int productoId, decimal precioCompraAnterior, decimal precioCompraNuevo,
        decimal precioVentaAnterior, decimal precioVentaNuevo,
        string? motivoCambio, string usuarioModificacion)
        => Task.FromResult(new PrecioHistorico
        {
            ProductoId = productoId,
            PrecioCompraAnterior = precioCompraAnterior,
            PrecioCompraNuevo = precioCompraNuevo,
            PrecioVentaAnterior = precioVentaAnterior,
            PrecioVentaNuevo = precioVentaNuevo,
            UsuarioModificacion = usuarioModificacion
        });

    public Task<List<PrecioHistorico>> GetHistorialByProductoIdAsync(int productoId)
        => Task.FromResult(new List<PrecioHistorico>());

    public Task<PrecioHistorico?> GetUltimoCambioAsync(int productoId)
        => Task.FromResult<PrecioHistorico?>(null);

    public Task<bool> RevertirCambioAsync(int historialId)
        => Task.FromResult(false);

    public Task<PrecioHistoricoEstadisticasViewModel> GetEstadisticasAsync(DateTime? fechaDesde, DateTime? fechaHasta)
        => Task.FromResult(new PrecioHistoricoEstadisticasViewModel());

    public Task<PaginatedResult<PrecioHistoricoViewModel>> BuscarAsync(PrecioHistoricoFiltroViewModel filtro)
        => Task.FromResult(new PaginatedResult<PrecioHistoricoViewModel>());

    public Task<PrecioSimulacionViewModel> SimularCambioAsync(int productoId, decimal precioCompraNuevo, decimal precioVentaNuevo)
        => Task.FromResult(new PrecioSimulacionViewModel());

    public Task MarcarComoNoReversibleAsync(int historialId)
        => Task.CompletedTask;
}

file sealed class StubTempDataMovCtrl : Dictionary<string, object?>, ITempDataDictionary
{
    public void Keep() { }
    public void Keep(string key) { }
    public void Load() { }
    public object? Peek(string key) => TryGetValue(key, out var v) ? v : null;
    public void Save() { }
}

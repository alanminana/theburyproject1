using System.Security.Claims;
using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
/// Tests de integración para las pestañas Alertas / Movimientos embebidas en
/// CatalogoController.Index (Fase 7 del rework de Categoría/Inventario).
///
/// Cubren: cada pestaña respeta su propio permiso real (stock.viewalerts /
/// movimientos.view, distinto de cotizaciones.view que ya protege Catálogo), sus datos
/// se arman solo cuando corresponde, y `tab` no queda en un valor que el usuario no
/// puede ver.
/// </summary>
public class CatalogoControllerTabsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly CatalogoController _controller;

    public CatalogoControllerTabsTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var config = new ConfigurationBuilder().Build();
        var stubUser = new StubCurrentUserCatalogoTabs();

        var categoriaService = new CategoriaService(_context, NullLogger<CategoriaService>.Instance);
        var marcaService = new MarcaService(_context, NullLogger<MarcaService>.Instance);
        var precioHistorico = new PrecioHistoricoService(_context, NullLogger<PrecioHistoricoService>.Instance);
        var productoService = new ProductoService(_context, NullLogger<ProductoService>.Instance, precioHistorico, stubUser,
            new PrecioVigenteResolver(_context));
        var precioService = new PrecioService(_context, NullLogger<PrecioService>.Instance, stubUser, config);
        var catalogLookup = new CatalogLookupService(categoriaService, marcaService, productoService, _context);
        var catalogoService = new CatalogoService(
            catalogLookup,
            productoService,
            precioService,
            new PrecioVigenteResolver(_context),
            NullLogger<CatalogoService>.Instance,
            stubUser);

        var alertaStockService = new AlertaStockService(_context, NullLogger<AlertaStockService>.Instance);
        var movimientoStockService = new MovimientoStockService(_context, NullLogger<MovimientoStockService>.Instance);
        var movimientoReferenciaResolver = new MovimientoStockReferenciaResolver(_context);

        var mapper = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance).CreateMapper();

        _controller = new CatalogoController(
            catalogoService,
            catalogLookup,
            alertaStockService,
            movimientoStockService,
            productoService,
            movimientoReferenciaResolver,
            NullLogger<CatalogoController>.Instance,
            mapper);

        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        _controller.TempData = new StubTempDataCatalogoTabs();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SetUser(params string[] permisos)
    {
        var claims = permisos.Select(p => new Claim("Permission", p)).ToList();
        var identity = new ClaimsIdentity(claims, "TestAuth");
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(identity);
    }

    private async Task<Producto> SeedProductoAsync(decimal stockActual = 5m, decimal stockMinimo = 10m)
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = code, Nombre = "Cat-" + code, Activo = true };
        var marca = new Marca { Codigo = code, Nombre = "Marca-" + code, Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = code,
            Nombre = "Prod-" + code,
            CategoriaId = cat.Id,
            MarcaId = marca.Id,
            PrecioCompra = 60m,
            PrecioVenta = 100m,
            PorcentajeIVA = 21m,
            StockActual = stockActual,
            StockMinimo = stockMinimo,
            Activo = true
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    private async Task SeedAlertaAsync(int productoId, EstadoAlerta estado = EstadoAlerta.Pendiente)
    {
        _context.Set<AlertaStock>().Add(new AlertaStock
        {
            ProductoId = productoId,
            Tipo = TipoAlertaStock.StockBajo,
            Prioridad = PrioridadAlerta.Media,
            Estado = estado,
            Mensaje = "Alerta de test",
            StockActual = 5m,
            StockMinimo = 10m,
            FechaAlerta = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    private async Task SeedMovimientoAsync(int productoId)
    {
        _context.Set<MovimientoStock>().Add(new MovimientoStock
        {
            ProductoId = productoId,
            Tipo = TipoMovimiento.Entrada,
            Cantidad = 5m,
            Motivo = "Recepción de test"
        });
        await _context.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Sin permisos de módulo: las pestañas no se arman ni se pueden forzar por URL
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Index_SinPermisosDeStock_NoMuestraNiArmaLasPestanasNuevas()
    {
        SetUser("cotizaciones.view");

        var result = await _controller.Index() as ViewResult;
        var model = Assert.IsType<CatalogoUnificadoViewModel>(result!.Model);

        Assert.False(model.MostrarTabAlertas);
        Assert.False(model.MostrarTabMovimientos);
        Assert.Null(model.AlertasPartial);
        Assert.Null(model.MovimientosPartial);
    }

    [Fact]
    public async Task Index_SinPermisoAlertas_TabForzadoPorUrlCaeAProductos()
    {
        SetUser("cotizaciones.view", "movimientos.view");

        var result = await _controller.Index(tab: "alertas") as ViewResult;
        var model = Assert.IsType<CatalogoUnificadoViewModel>(result!.Model);

        // No tiene stock.viewalerts: no puede quedar en "alertas" aunque lo pida por URL.
        Assert.Equal("productos", model.TabActiva);
        Assert.False(model.MostrarTabAlertas);
    }

    // -------------------------------------------------------------------------
    // Con permiso de Alertas
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Index_ConPermisoAlertas_ArmaElPartialConLaAlertaPendiente()
    {
        SetUser("cotizaciones.view", "stock.viewalerts");
        var producto = await SeedProductoAsync();
        await SeedAlertaAsync(producto.Id);

        var result = await _controller.Index(tab: "alertas") as ViewResult;
        var model = Assert.IsType<CatalogoUnificadoViewModel>(result!.Model);

        Assert.True(model.MostrarTabAlertas);
        Assert.Equal("alertas", model.TabActiva);
        Assert.NotNull(model.AlertasPartial);
        Assert.True(model.AlertasPartial!.Embed);
        Assert.Single(model.AlertasPartial.Resultado.Items);
        Assert.Equal(1, model.AlertasPartial.TotalPendientes);
    }

    [Fact]
    public async Task Index_ConPermisoAlertas_PeroSinPermisoMovimientos_MovimientosQuedaOculta()
    {
        SetUser("cotizaciones.view", "stock.viewalerts");

        var result = await _controller.Index() as ViewResult;
        var model = Assert.IsType<CatalogoUnificadoViewModel>(result!.Model);

        Assert.True(model.MostrarTabAlertas);
        Assert.False(model.MostrarTabMovimientos);
        Assert.Null(model.MovimientosPartial);
    }

    // -------------------------------------------------------------------------
    // Con permiso de Movimientos
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Index_ConPermisoMovimientos_ArmaElPartialConElMovimientoRegistrado()
    {
        SetUser("cotizaciones.view", "movimientos.view");
        var producto = await SeedProductoAsync();
        await SeedMovimientoAsync(producto.Id);

        var result = await _controller.Index(tab: "movimientos") as ViewResult;
        var model = Assert.IsType<CatalogoUnificadoViewModel>(result!.Model);

        Assert.True(model.MostrarTabMovimientos);
        Assert.Equal("movimientos", model.TabActiva);
        Assert.NotNull(model.MovimientosPartial);
        Assert.True(model.MovimientosPartial!.Embed);
        Assert.Equal(1, model.MovimientosPartial.Filtro.TotalResultados);
        Assert.Single(model.MovimientosPartial.Filtro.Movimientos);
    }

    // -------------------------------------------------------------------------
    // SuperAdmin: bypass de permisos (TienePermiso ya lo resuelve, se confirma acá
    // porque es el camino real que va a usar el rol con más acceso del sistema).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Index_SuperAdmin_VeAmbasPestanasSinClaimsDePermisoExplicitos()
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "SuperAdmin") }, "TestAuth");
        _controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(identity);

        var result = await _controller.Index() as ViewResult;
        var model = Assert.IsType<CatalogoUnificadoViewModel>(result!.Model);

        Assert.True(model.MostrarTabAlertas);
        Assert.True(model.MostrarTabMovimientos);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Stubs de dependencias
// ─────────────────────────────────────────────────────────────────────────────

file sealed class StubCurrentUserCatalogoTabs : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-test";
    public string GetEmail() => "test@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}

file sealed class StubTempDataCatalogoTabs : Dictionary<string, object?>, ITempDataDictionary
{
    public void Keep() { }
    public void Keep(string key) { }
    public void Load() { }
    public object? Peek(string key) => TryGetValue(key, out var v) ? v : null;
    public void Save() { }
}

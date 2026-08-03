using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using System.Text.Json;
using TheBuryProject.Controllers;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Tests de integración de ML6: paridad Crear/Editar de Crédito Personal por producto,
/// autoridad del backend ante payloads manipulados y precarga completa en Editar.
/// No duplica lo ya cubierto por <see cref="ProductoCreditoPersonalConfigServiceTests"/>
/// (validación pura, herencia global, Id ajeno): acá se prueba a través del controller real
/// (CreateAjax/EditAjax/GetJson), que es lo que efectivamente ejecuta la UI.
/// </summary>
public class ProductoCreditoPersonalControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ProductoController _controller;
    private readonly IProductoService _productoService;
    private readonly IProductoUnidadService _productoUnidadService;
    private readonly IMovimientoStockService _movimientoStockService;
    private readonly ICatalogLookupService _catalogLookup;
    private readonly ICatalogoService _catalogoService;
    private readonly IMapper _mapper;

    public ProductoCreditoPersonalControllerTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var resolver = new PrecioVigenteResolver(_context);
        var stubUser = new StubCurrentUserCpCtrlTest();
        var config = new ConfigurationBuilder().Build();

        var productoService = new ProductoService(
            _context,
            NullLogger<ProductoService>.Instance,
            new StubHistoricoPrecioCpCtrlTest(),
            stubUser,
            resolver);
        var productoUnidadService = new ProductoUnidadService(
            _context,
            NullLogger<ProductoUnidadService>.Instance);

        var precioService = new PrecioService(_context, NullLogger<PrecioService>.Instance, stubUser, config);
        var catalogLookup = new StubCatalogLookupCpCtrlTest();
        var catalogoService = new CatalogoService(
            catalogLookup, productoService, precioService, resolver,
            NullLogger<CatalogoService>.Instance, stubUser);

        var movimientoStockService = new MovimientoStockService(
            _context,
            NullLogger<MovimientoStockService>.Instance);

        var mapper = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance).CreateMapper();

        var configuracionPagoService = new ConfiguracionPagoService(
            _context,
            mapper,
            NullLogger<ConfiguracionPagoService>.Instance);
        var productoCreditoPersonalConfigService = new ProductoCreditoPersonalConfigService(
            _context,
            configuracionPagoService);

        _productoService = productoService;
        _productoUnidadService = productoUnidadService;
        _movimientoStockService = movimientoStockService;
        _catalogLookup = catalogLookup;
        _catalogoService = catalogoService;
        _mapper = mapper;

        _controller = new ProductoController(
            productoService,
            productoUnidadService,
            movimientoStockService,
            catalogLookup,
            catalogoService,
            productoCreditoPersonalConfigService,
            NullLogger<ProductoController>.Instance,
            mapper,
            _context);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Name, "operador@test.com") },
                    "TestAuth"))
            }
        };
        _controller.TempData = new StubTempDataDictionaryCpCtrlTest();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ─────────────────────────────────────────────────────────────
    // Crear: hereda global (comportamiento por defecto, sin tocar nada)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAjax_SinTocarCreditoPersonal_HeredaGlobalSinRestriccionNiCuotas()
    {
        var vm = await ProductoViewModelParaCrearAsync();
        // vm.CreditoPersonal queda con los defaults: Modo=HeredaGlobal, AdmiteCreditoPersonal=true, Cuotas=[]

        var result = await _controller.CreateAjax(vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        var productoId = doc.RootElement.GetProperty("entity").GetProperty("id").GetInt32();

        Assert.Null(await _context.ProductoCreditoRestricciones.FirstOrDefaultAsync(r => r.ProductoId == productoId));
        Assert.Empty(await _context.ProductoCreditoPersonalCuotas.Where(c => c.ProductoId == productoId).ToListAsync());
    }

    // ─────────────────────────────────────────────────────────────
    // Crear con planes propios (cierra I9: antes CreateAjax no persistía nada de crédito)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAjax_ConPlanesPropios_PersisteLosPlanes()
    {
        var vm = await ProductoViewModelParaCrearAsync();
        vm.CreditoPersonal = new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            AdmiteCreditoPersonal = true,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 0m, Activo = true, Orden = 6 },
                new() { CantidadCuotas = 12, TasaMensual = 10m, Activo = true, Orden = 12 }
            }
        };

        var result = await _controller.CreateAjax(vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        var productoId = doc.RootElement.GetProperty("entity").GetProperty("id").GetInt32();

        var cuotas = await _context.ProductoCreditoPersonalCuotas
            .Where(c => c.ProductoId == productoId)
            .OrderBy(c => c.CantidadCuotas)
            .ToListAsync();
        Assert.Equal(2, cuotas.Count);
        Assert.Equal(0m, cuotas[0].TasaMensual);
        Assert.Equal(10m, cuotas[1].TasaMensual);
    }

    // ─────────────────────────────────────────────────────────────
    // Producto bloqueado para Crédito Personal (Crear y Editar persisten el mismo estado)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAjax_Bloqueado_PersisteRestriccionNoPermitido()
    {
        var vm = await ProductoViewModelParaCrearAsync();
        vm.CreditoPersonal = new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.NoDisponible,
            AdmiteCreditoPersonal = false
        };

        var result = await _controller.CreateAjax(vm) as JsonResult;

        var doc = ParseJson(result!.Value);
        var productoId = doc.RootElement.GetProperty("entity").GetProperty("id").GetInt32();

        var restriccion = await _context.ProductoCreditoRestricciones
            .SingleAsync(r => r.ProductoId == productoId);
        Assert.False(restriccion.Permitido);
    }

    [Fact]
    public async Task EditAjax_Bloqueado_PersisteElMismoEstadoQueCrear()
    {
        var producto = await SeedProductoAsync();
        var vm = ProductoViewModelParaEditar(producto);
        vm.CreditoPersonal = new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.NoDisponible,
            AdmiteCreditoPersonal = false
        };

        var result = await _controller.EditAjax(producto.Id, vm) as JsonResult;
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());

        var restriccion = await _context.ProductoCreditoRestricciones
            .SingleAsync(r => r.ProductoId == producto.Id);
        Assert.False(restriccion.Permitido);

        var json = await _controller.GetJson(producto.Id) as JsonResult;
        var getDoc = ParseJson(json!.Value);
        var cp = getDoc.RootElement.GetProperty("creditoPersonal");
        Assert.Equal("NoDisponible", cp.GetProperty("modo").GetString());
        Assert.False(cp.GetProperty("admiteCreditoPersonal").GetBoolean());
    }

    // ─────────────────────────────────────────────────────────────
    // Editar: precarga completa de lo guardado (GetJson tras EditAjax)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EditAjax_ConPlanesPropios_GetJsonPrecargaTodoElEstadoGuardado()
    {
        var producto = await SeedProductoAsync();
        var vm = ProductoViewModelParaEditar(producto);
        vm.CreditoPersonal = new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            AdmiteCreditoPersonal = true,
            MaxCuotasCredito = 18,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 3, TasaMensual = 0m, Activo = true, Orden = 3 },
                new() { CantidadCuotas = 9, TasaMensual = 15m, Activo = true, Orden = 9 }
            }
        };

        var editResult = await _controller.EditAjax(producto.Id, vm) as JsonResult;
        Assert.True(ParseJson(editResult!.Value).RootElement.GetProperty("success").GetBoolean());

        var json = await _controller.GetJson(producto.Id) as JsonResult;
        var doc = ParseJson(json!.Value);
        var cp = doc.RootElement.GetProperty("creditoPersonal");

        Assert.Equal("ConfiguracionPropia", cp.GetProperty("modo").GetString());
        Assert.True(cp.GetProperty("admiteCreditoPersonal").GetBoolean());
        Assert.Equal(18, cp.GetProperty("maxCuotasCredito").GetInt32());

        var cuotasJson = cp.GetProperty("cuotas").EnumerateArray().ToList();
        var plan3 = cuotasJson.Single(c => c.GetProperty("cantidadCuotas").GetInt32() == 3);
        var plan9 = cuotasJson.Single(c => c.GetProperty("cantidadCuotas").GetInt32() == 9);
        Assert.True(plan3.GetProperty("activo").GetBoolean());
        Assert.Equal(0m, plan3.GetProperty("tasaMensual").GetDecimal());
        Assert.True(plan9.GetProperty("activo").GetBoolean());
        Assert.Equal(15m, plan9.GetProperty("tasaMensual").GetDecimal());
    }

    // ─────────────────────────────────────────────────────────────
    // Paridad de persistencia y de contrato financiero entre Crear y Editar
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrearYEditar_ConElMismoPayloadDeCredito_PersistenElMismoEstado()
    {
        ProductoCreditoPersonalConfigViewModel Config() => new()
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            AdmiteCreditoPersonal = true,
            MaxCuotasCredito = 24,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 8m, Activo = true, Orden = 6 }
            }
        };

        var vmCrear = await ProductoViewModelParaCrearAsync();
        vmCrear.CreditoPersonal = Config();
        var creado = await _controller.CreateAjax(vmCrear) as JsonResult;
        var idCreado = ParseJson(creado!.Value).RootElement.GetProperty("entity").GetProperty("id").GetInt32();

        var productoEditado = await SeedProductoAsync();
        var vmEditar = ProductoViewModelParaEditar(productoEditado);
        vmEditar.CreditoPersonal = Config();
        await _controller.EditAjax(productoEditado.Id, vmEditar);

        var jsonCreado = ParseJson((await _controller.GetJson(idCreado) as JsonResult)!.Value)
            .RootElement.GetProperty("creditoPersonal");
        var jsonEditado = ParseJson((await _controller.GetJson(productoEditado.Id) as JsonResult)!.Value)
            .RootElement.GetProperty("creditoPersonal");

        Assert.Equal(jsonCreado.GetProperty("modo").GetString(), jsonEditado.GetProperty("modo").GetString());
        Assert.Equal(jsonCreado.GetProperty("admiteCreditoPersonal").GetBoolean(), jsonEditado.GetProperty("admiteCreditoPersonal").GetBoolean());
        Assert.Equal(jsonCreado.GetProperty("maxCuotasCredito").GetInt32(), jsonEditado.GetProperty("maxCuotasCredito").GetInt32());

        var cuotaCreado = jsonCreado.GetProperty("cuotas").EnumerateArray().Single(c => c.GetProperty("cantidadCuotas").GetInt32() == 6);
        var cuotaEditado = jsonEditado.GetProperty("cuotas").EnumerateArray().Single(c => c.GetProperty("cantidadCuotas").GetInt32() == 6);
        Assert.Equal(cuotaCreado.GetProperty("tasaMensual").GetDecimal(), cuotaEditado.GetProperty("tasaMensual").GetDecimal());
        Assert.Equal(cuotaCreado.GetProperty("activo").GetBoolean(), cuotaEditado.GetProperty("activo").GetBoolean());
    }

    // ─────────────────────────────────────────────────────────────
    // Backend autoritativo: payload manipulado no guarda nada, ni de crédito ni del producto
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EditAjax_CreditoPersonalManipuladoConCombinacionContradictoria_NoGuardaNiCreditoNiCambiosDelProducto()
    {
        var producto = await SeedProductoAsync();
        var nombreOriginal = producto.Nombre;
        var vm = ProductoViewModelParaEditar(producto);
        vm.Nombre = "Nombre nunca deberia persistir";
        // DevTools: radio "Hereda global" pero con un plan propio activo colado en el payload.
        vm.CreditoPersonal = new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.HeredaGlobal,
            AdmiteCreditoPersonal = true,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 5m, Activo = true }
            }
        };

        var result = await _controller.EditAjax(producto.Id, vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty(nameof(ProductoViewModel.CreditoPersonal), out _));

        Assert.Empty(await _context.ProductoCreditoPersonalCuotas.Where(c => c.ProductoId == producto.Id).ToListAsync());
        var sinCambios = await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == producto.Id);
        Assert.Equal(nombreOriginal, sinCambios.Nombre); // el nombre manipulado tampoco se guardó
    }

    [Fact]
    public async Task CreateAjax_CreditoPersonalManipuladoConPorcentajeNegativo_NoCreaElProducto()
    {
        var vm = await ProductoViewModelParaCrearAsync();
        vm.CreditoPersonal = new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = -5m, Activo = true }
            }
        };

        var result = await _controller.CreateAjax(vm) as JsonResult;

        var doc = ParseJson(result!.Value);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.False(await _context.Productos.AnyAsync(p => p.Codigo == vm.Codigo));
    }

    // ─────────────────────────────────────────────────────────────
    // Regresión: campos generales del producto no se ven afectados
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EditAjax_ConCreditoPersonalValido_ActualizaPrecioYNoRompeElResguardoDeStock()
    {
        var producto = await SeedProductoAsync();
        var stockOriginal = producto.StockActual;
        var vm = ProductoViewModelParaEditar(producto);
        vm.StockActual = 42m; // UpdateAsync lo ignora deliberadamente (protección existente, no de ML6)
        vm.PrecioVenta = 555m;
        vm.CreditoPersonal = new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 3, TasaMensual = 5m, Activo = true }
            }
        };

        var result = await _controller.EditAjax(producto.Id, vm) as JsonResult;
        Assert.True(ParseJson(result!.Value).RootElement.GetProperty("success").GetBoolean());

        var actualizado = await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == producto.Id);
        Assert.Equal(stockOriginal, actualizado.StockActual); // sigue protegido tras el reordenamiento de ML6
        Assert.Equal(555m, actualizado.PrecioVenta);
    }

    // ─────────────────────────────────────────────────────────────
    // Atomicidad: Producto general + Crédito Personal son una única unidad de trabajo.
    // Si cualquiera de los dos falla — validación tardía o excepción real —, ninguno de
    // los dos queda parcialmente persistido. Todos estos tests corren contra SQLite real
    // (no InMemory), que sí soporta transacciones, y verifican el estado final leyendo la
    // base — no mocks que simulen el resultado.
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EditAjax_RowVersionInvalidaConCreditoValido_RevierteCreditoYProductoJuntos()
    {
        var producto = await SeedProductoAsync();
        var nombreOriginal = producto.Nombre;
        var precioOriginal = producto.PrecioVenta;

        var vm = ProductoViewModelParaEditar(producto);
        vm.PrecioVenta = 999m;
        vm.RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }; // deliberadamente distinto del real: concurrencia real
        vm.CreditoPersonal = new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 8m, Activo = true }
            }
        };

        var result = await _controller.EditAjax(producto.Id, vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());

        // El crédito personal era válido y hubiera persistido de no ser por el rollback
        // provocado por el DbUpdateConcurrencyException real al guardar el producto después.
        Assert.Empty(await _context.ProductoCreditoPersonalCuotas.Where(c => c.ProductoId == producto.Id).ToListAsync());

        var sinCambios = await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == producto.Id);
        Assert.Equal(nombreOriginal, sinCambios.Nombre);
        Assert.Equal(precioOriginal, sinCambios.PrecioVenta);
    }

    [Fact]
    public async Task CreateAjax_ExcepcionAlGuardarCreditoDespuesDeCrearElProducto_RevierteAmbos()
    {
        // No existe un camino público para provocar una violación de constraint real en
        // ProductoCreditoPersonalCuotas una vez que Validar() ya aprobó el payload (los
        // check constraints de la base coinciden exactamente con las reglas de Validar()).
        // Para probar el caso "excepción después de escribir crédito, con el producto ya
        // insertado en la misma transacción" se usa un decorator que ejecuta el guardado
        // real (SaveChangesAsync real contra SQLite) y luego lanza, simulando una falla
        // inesperada aguas abajo — es el mecanismo más representativo disponible con el
        // proveedor de este proyecto.
        var configReal = new ProductoCreditoPersonalConfigService(
            _context,
            new ConfiguracionPagoService(_context, _mapper, NullLogger<ConfiguracionPagoService>.Instance));
        var creditoQueFalla = new ThrowingCreditoPersonalConfigServiceDecorator(configReal);

        var controllerConFalla = new ProductoController(
            _productoService,
            _productoUnidadService,
            _movimientoStockService,
            _catalogLookup,
            _catalogoService,
            creditoQueFalla,
            NullLogger<ProductoController>.Instance,
            _mapper,
            _context)
        {
            ControllerContext = _controller.ControllerContext,
            TempData = _controller.TempData
        };

        var vm = await ProductoViewModelParaCrearAsync();
        vm.CreditoPersonal = new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 8m, Activo = true }
            }
        };

        var result = await controllerConFalla.CreateAjax(vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());

        // El producto se había insertado (con SaveChangesAsync real) antes de que crédito
        // fallara; el rollback de la transacción ambiente debe revertir esa inserción también.
        Assert.False(await _context.Productos.AnyAsync(p => p.Codigo == vm.Codigo));
        Assert.Empty(await _context.ProductoCreditoPersonalCuotas
            .Where(c => c.CantidadCuotas == 6 && c.TasaMensual == 8m).ToListAsync());
    }

    [Fact]
    public async Task CreateAjax_CreditoConCantidadesDuplicadas_NoQuedaProductoNiCreditoHuerfano()
    {
        var vm = await ProductoViewModelParaCrearAsync();
        vm.CreditoPersonal = new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 5m, Activo = true },
                new() { CantidadCuotas = 6, TasaMensual = 8m, Activo = true }
            }
        };

        var result = await _controller.CreateAjax(vm) as JsonResult;

        var doc = ParseJson(result!.Value);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.False(await _context.Productos.AnyAsync(p => p.Codigo == vm.Codigo));
        Assert.Empty(await _context.ProductoCreditoPersonalCuotas
            .Where(c => c.TasaMensual == 5m || c.TasaMensual == 8m).ToListAsync());
    }

    [Fact]
    public async Task CreateAjax_ProductoYCreditoValidos_AmbosQuedanCommiteadosParaOtraConexion()
    {
        var vm = await ProductoViewModelParaCrearAsync();
        vm.CreditoPersonal = new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 8m, Activo = true }
            }
        };

        var result = await _controller.CreateAjax(vm) as JsonResult;
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        var productoId = doc.RootElement.GetProperty("entity").GetProperty("id").GetInt32();

        // Lee desde una conexión SQLite completamente nueva (mismo :memory: con Cache=Shared)
        // para probar que el commit fue real y no solo visible dentro de la misma conexión.
        await using var otraConexion = new SqliteConnection(_connection.ConnectionString);
        await otraConexion.OpenAsync();
        var otrasOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(otraConexion).Options;
        await using var otroContext = new AppDbContext(otrasOptions);

        Assert.True(await otroContext.Productos.AnyAsync(p => p.Id == productoId));
        Assert.Equal(1, await otroContext.ProductoCreditoPersonalCuotas
            .CountAsync(c => c.ProductoId == productoId && c.CantidadCuotas == 6 && c.TasaMensual == 8m));
    }

    [Fact]
    public async Task EditAjax_ProductoYCreditoValidos_AmbosQuedanCommiteadosParaOtraConexion()
    {
        var producto = await SeedProductoAsync();
        var vm = ProductoViewModelParaEditar(producto);
        vm.PrecioVenta = 777m;
        vm.CreditoPersonal = new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 9, TasaMensual = 12m, Activo = true }
            }
        };

        var result = await _controller.EditAjax(producto.Id, vm) as JsonResult;
        Assert.True(ParseJson(result!.Value).RootElement.GetProperty("success").GetBoolean());

        await using var otraConexion = new SqliteConnection(_connection.ConnectionString);
        await otraConexion.OpenAsync();
        var otrasOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(otraConexion).Options;
        await using var otroContext = new AppDbContext(otrasOptions);

        var productoDesdeOtraConexion = await otroContext.Productos.AsNoTracking().SingleAsync(p => p.Id == producto.Id);
        Assert.Equal(777m, productoDesdeOtraConexion.PrecioVenta);
        Assert.Equal(1, await otroContext.ProductoCreditoPersonalCuotas
            .CountAsync(c => c.ProductoId == producto.Id && c.CantidadCuotas == 9 && c.TasaMensual == 12m));
    }

    [Fact]
    public async Task EditAjax_GuardarDosVecesConElMismoPayload_EsInvariante()
    {
        var producto = await SeedProductoAsync();
        var vm = ProductoViewModelParaEditar(producto);
        vm.CreditoPersonal = new ProductoCreditoPersonalConfigViewModel
        {
            Modo = ModoCreditoPersonalProducto.ConfiguracionPropia,
            Cuotas = new List<CuotaCreditoPersonalViewModel>
            {
                new() { CantidadCuotas = 6, TasaMensual = 8m, Activo = true }
            }
        };

        var primerResultado = await _controller.EditAjax(producto.Id, vm) as JsonResult;
        Assert.True(ParseJson(primerResultado!.Value).RootElement.GetProperty("success").GetBoolean());

        var cuotasTrasPrimerGuardado = await _context.ProductoCreditoPersonalCuotas
            .AsNoTracking().Where(c => c.ProductoId == producto.Id).ToListAsync();
        Assert.Single(cuotasTrasPrimerGuardado);

        // Recarga RowVersion como haría la UI real antes de un segundo guardado (evita
        // depender de si el proveedor rota o no el RowVersion en UPDATE).
        var jsonTrasPrimerGuardado = await _controller.GetJson(producto.Id) as JsonResult;
        var rowVersionActual = ParseJson(jsonTrasPrimerGuardado!.Value).RootElement.GetProperty("rowVersion").GetString();
        vm.RowVersion = Convert.FromBase64String(rowVersionActual!);

        var segundoResultado = await _controller.EditAjax(producto.Id, vm) as JsonResult;
        Assert.True(ParseJson(segundoResultado!.Value).RootElement.GetProperty("success").GetBoolean());

        var cuotasTrasSegundoGuardado = await _context.ProductoCreditoPersonalCuotas
            .AsNoTracking().Where(c => c.ProductoId == producto.Id).ToListAsync();
        Assert.Single(cuotasTrasSegundoGuardado); // no se duplicó la fila
        Assert.Equal(cuotasTrasPrimerGuardado[0].Id, cuotasTrasSegundoGuardado[0].Id);
        Assert.Equal(8m, cuotasTrasSegundoGuardado[0].TasaMensual);
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    private static JsonDocument ParseJson(object? value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonDocument.Parse(json);
    }

    private async Task<ProductoViewModel> ProductoViewModelParaCrearAsync()
    {
        var (categoria, marca) = await SeedCategoriaYMarcaAsync();
        return new ProductoViewModel
        {
            Codigo = "P" + Guid.NewGuid().ToString("N")[..8],
            Nombre = "Producto credito personal test",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = 80m,
            PrecioVenta = 100m,
            PorcentajeIVA = 21m,
            StockActual = 3m,
            StockMinimo = 1m,
            Activo = true
        };
    }

    private static ProductoViewModel ProductoViewModelParaEditar(Producto producto)
        => new()
        {
            Id = producto.Id,
            Codigo = producto.Codigo,
            Nombre = producto.Nombre,
            CategoriaId = producto.CategoriaId,
            MarcaId = producto.MarcaId,
            PrecioCompra = 60m,
            PrecioVenta = 80m,
            PorcentajeIVA = 21m,
            StockActual = producto.StockActual,
            StockMinimo = producto.StockMinimo,
            Activo = true,
            RowVersion = producto.RowVersion
        };

    private async Task<(Categoria Categoria, Marca Marca)> SeedCategoriaYMarcaAsync()
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var cat = new Categoria { Codigo = "C" + code, Nombre = "Cat-" + code, Activo = true };
        var marca = new Marca { Codigo = "M" + code, Nombre = "Marca-" + code, Activo = true };
        _context.Categorias.Add(cat);
        _context.Marcas.Add(marca);
        await _context.SaveChangesAsync();
        return (cat, marca);
    }

    private async Task<Producto> SeedProductoAsync(decimal precioVenta = 100m)
    {
        var (cat, marca) = await SeedCategoriaYMarcaAsync();
        var code = Guid.NewGuid().ToString("N")[..8];

        var producto = new Producto
        {
            Codigo = "P" + code,
            Nombre = "Prod-" + code,
            CategoriaId = cat.Id,
            MarcaId = marca.Id,
            PrecioCompra = 60m,
            PrecioVenta = precioVenta,
            PorcentajeIVA = 21m,
            StockActual = 10m,
            Activo = true
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }
}

// ─────────────────────────────────────────────────────────────────
// Stubs de dependencias para tests de controller (scope de archivo)
// ─────────────────────────────────────────────────────────────────

/// <summary>
/// Decorator que delega en una <see cref="ProductoCreditoPersonalConfigService"/> real (con
/// SaveChangesAsync real contra SQLite) y luego lanza, simulando una falla inesperada
/// inmediatamente después de persistir Crédito Personal. Usado solo en
/// <see cref="ProductoCreditoPersonalControllerTests.CreateAjax_ExcepcionAlGuardarCreditoDespuesDeCrearElProducto_RevierteAmbos"/>
/// para probar rollback real de transacción sin depender de un constraint de base
/// explotable a través de la API pública (Validar() ya cubre exactamente los mismos casos
/// que los check constraints de la base).
/// </summary>
file sealed class ThrowingCreditoPersonalConfigServiceDecorator : IProductoCreditoPersonalConfigService
{
    private readonly IProductoCreditoPersonalConfigService _inner;

    public ThrowingCreditoPersonalConfigServiceDecorator(IProductoCreditoPersonalConfigService inner)
        => _inner = inner;

    public Task<ProductoCreditoPersonalConfigViewModel> ObtenerAsync(int productoId) => _inner.ObtenerAsync(productoId);

    public List<string> Validar(ProductoCreditoPersonalConfigViewModel config) => _inner.Validar(config);

    public async Task<(bool Ok, List<string> Errores)> GuardarAsync(int productoId, ProductoCreditoPersonalConfigViewModel config, string usuario)
    {
        await _inner.GuardarAsync(productoId, config, usuario);
        throw new InvalidOperationException("Falla simulada después de persistir Crédito Personal (prueba de rollback).");
    }
}

file sealed class StubCurrentUserCpCtrlTest : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-test";
    public string GetEmail() => "test@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}

file sealed class StubHistoricoPrecioCpCtrlTest : IPrecioHistoricoService
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

file sealed class StubTempDataDictionaryCpCtrlTest : Dictionary<string, object?>, ITempDataDictionary
{
    public void Keep() { }
    public void Keep(string key) { }
    public void Load() { }
    public object? Peek(string key) => TryGetValue(key, out var v) ? v : null;
    public void Save() { }
}

file sealed class StubCatalogLookupCpCtrlTest : ICatalogLookupService
{
    public Task<(IEnumerable<Categoria>, IEnumerable<Marca>)> GetCategoriasYMarcasAsync()
        => Task.FromResult<(IEnumerable<Categoria>, IEnumerable<Marca>)>(([], []));

    public Task<(IEnumerable<Categoria>, IEnumerable<Marca>, IEnumerable<Producto>)> GetCategoriasMarcasYProductosAsync()
        => Task.FromResult<(IEnumerable<Categoria>, IEnumerable<Marca>, IEnumerable<Producto>)>(([], [], []));

    public Task<IEnumerable<Categoria>> GetSubcategoriasAsync(int categoriaId)
        => Task.FromResult<IEnumerable<Categoria>>([]);

    public Task<IEnumerable<Marca>> GetSubmarcasAsync(int marcaId)
        => Task.FromResult<IEnumerable<Marca>>([]);

    public Task<List<AlicuotaIVAFormItem>> ObtenerAlicuotasIVAParaFormAsync()
        => Task.FromResult(new List<AlicuotaIVAFormItem>());

    public Task<decimal?> ObtenerPorcentajeAlicuotaAsync(int alicuotaIVAId)
        => Task.FromResult<decimal?>(null);
}

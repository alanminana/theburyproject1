using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using System.Text.Json;
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
/// Tests de integración para ProductoController.GetJson y EditAjax.
/// Verifican que el contexto de precio vigente (precioActual, precioBase,
/// tienePrecioLista, listaPrecioActualNombre) se resuelve y devuelve correctamente.
/// </summary>
public class ProductoControllerPrecioTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly ProductoController _controller;

    public ProductoControllerPrecioTests()
    {
        _connection = new SqliteConnection($"DataSource={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();

        var resolver = new PrecioVigenteResolver(_context);
        var stubUser = new StubCurrentUserCtrlTest();
        var config = new ConfigurationBuilder().Build();

        var productoService = new ProductoService(
            _context,
            NullLogger<ProductoService>.Instance,
            new StubHistoricoPrecioCtrlTest(),
            stubUser,
            resolver);
        var productoUnidadService = new ProductoUnidadService(
            _context,
            NullLogger<ProductoUnidadService>.Instance);

        var precioService = new PrecioService(_context, NullLogger<PrecioService>.Instance, stubUser, config);
        var catalogLookup = new StubCatalogLookupCtrlTest();
        var catalogoService = new CatalogoService(
            catalogLookup, productoService, precioService, resolver,
            NullLogger<CatalogoService>.Instance, stubUser);

        var movimientoStockService = new MovimientoStockService(
            _context,
            NullLogger<MovimientoStockService>.Instance);

        var mapper = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance).CreateMapper();

        _controller = new ProductoController(
            productoService,
            productoUnidadService,
            movimientoStockService,
            catalogLookup,
            catalogoService,
            NullLogger<ProductoController>.Instance,
            mapper);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Name, "operador@test.com") },
                    "TestAuth"))
            }
        };
        _controller.TempData = new StubTempDataDictionaryCtrlTest();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    // ─────────────────────────────────────────────────────────────
    // GetJson
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetJson_ConPrecioLista_DevuelvePrecioActualDeListaYPrecioBase()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);
        var lista = await SeedListaAsync(esPredeterminada: true, nombre: "Contado");
        await SeedPrecioListaAsync(producto.Id, lista.Id, precio: 150m);

        var result = await _controller.GetJson(producto.Id) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.Equal(150m, doc.RootElement.GetProperty("precioActual").GetDecimal());
        Assert.Equal(100m, doc.RootElement.GetProperty("precioBase").GetDecimal());
        Assert.True(doc.RootElement.GetProperty("tienePrecioLista").GetBoolean());
        Assert.Equal("Contado", doc.RootElement.GetProperty("listaPrecioActualNombre").GetString());
    }

    [Fact]
    public async Task GetJson_SinPrecioLista_DevuelvePrecioActualIgualAPrecioBase()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);

        var result = await _controller.GetJson(producto.Id) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.Equal(100m, doc.RootElement.GetProperty("precioActual").GetDecimal());
        Assert.Equal(100m, doc.RootElement.GetProperty("precioBase").GetDecimal());
        Assert.False(doc.RootElement.GetProperty("tienePrecioLista").GetBoolean());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("listaPrecioActualNombre").ValueKind);
    }

    // ─────────────────────────────────────────────────────────────
    // EditAjax
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EditAjax_ConPrecioLista_MantienePreccioActualDeListaTrasEditarBase()
    {
        var producto = await SeedProductoAsync(precioVenta: 121m);
        var lista = await SeedListaAsync(esPredeterminada: true, nombre: "Mayorista");
        await SeedPrecioListaAsync(producto.Id, lista.Id, precio: 150m);

        var productoActualizado = await _context.Productos.FindAsync(producto.Id);
        var vm = new ProductoViewModel
        {
            Id = producto.Id,
            Codigo = productoActualizado!.Codigo,
            Nombre = productoActualizado.Nombre,
            CategoriaId = productoActualizado.CategoriaId,
            MarcaId = productoActualizado.MarcaId,
            PrecioCompra = 60m,
            PrecioVenta = 80m,   // sin IVA → almacenará 96.80 con 21%
            PorcentajeIVA = 21m,
            StockActual = productoActualizado.StockActual,
            StockMinimo = productoActualizado.StockMinimo,
            Activo = true,
            RowVersion = productoActualizado.RowVersion
        };

        var result = await _controller.EditAjax(producto.Id, vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        var entity = doc.RootElement.GetProperty("entity");
        Assert.Equal(150m, entity.GetProperty("precioActual").GetDecimal());
        Assert.Equal(96.8m, entity.GetProperty("precioBase").GetDecimal());
        Assert.True(entity.GetProperty("tienePrecioLista").GetBoolean());
        Assert.Equal("Mayorista", entity.GetProperty("listaPrecioActualNombre").GetString());
    }

    [Fact]
    public async Task EditAjax_SinPrecioLista_ActualizaPrecioActualConNuevoPrecioBase()
    {
        var producto = await SeedProductoAsync(precioVenta: 121m);

        var productoActualizado = await _context.Productos.FindAsync(producto.Id);
        var vm = new ProductoViewModel
        {
            Id = producto.Id,
            Codigo = productoActualizado!.Codigo,
            Nombre = productoActualizado.Nombre,
            CategoriaId = productoActualizado.CategoriaId,
            MarcaId = productoActualizado.MarcaId,
            PrecioCompra = 60m,
            PrecioVenta = 90m,   // sin IVA → almacenará 108.90 con 21%
            PorcentajeIVA = 21m,
            StockActual = productoActualizado.StockActual,
            StockMinimo = productoActualizado.StockMinimo,
            Activo = true,
            RowVersion = productoActualizado.RowVersion
        };

        var result = await _controller.EditAjax(producto.Id, vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        var entity = doc.RootElement.GetProperty("entity");
        Assert.Equal(108.9m, entity.GetProperty("precioActual").GetDecimal());
        Assert.Equal(108.9m, entity.GetProperty("precioBase").GetDecimal());
        Assert.False(entity.GetProperty("tienePrecioLista").GetBoolean());
        Assert.Equal(JsonValueKind.Null, entity.GetProperty("listaPrecioActualNombre").ValueKind);
    }

    [Fact]
    public async Task EditAjax_DevuelveMargenBasadoEnPrecioCompraActualizado()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);

        var productoActualizado = await _context.Productos.FindAsync(producto.Id);
        var vm = new ProductoViewModel
        {
            Id = producto.Id,
            Codigo = productoActualizado!.Codigo,
            Nombre = productoActualizado.Nombre,
            CategoriaId = productoActualizado.CategoriaId,
            MarcaId = productoActualizado.MarcaId,
            PrecioCompra = 50m,
            PrecioVenta = 90m,   // sin IVA → 108.90 con 21%
            PorcentajeIVA = 21m,
            StockActual = productoActualizado.StockActual,
            StockMinimo = productoActualizado.StockMinimo,
            Activo = true,
            RowVersion = productoActualizado.RowVersion
        };

        var result = await _controller.EditAjax(producto.Id, vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        var entity = doc.RootElement.GetProperty("entity");
        // (108.9 - 50) / 50 * 100 = 117.80
        Assert.Equal(117.8m, entity.GetProperty("margenPorcentaje").GetDecimal());
    }

    [Theory]
    [InlineData("12,5", 12.5)]
    [InlineData("12.5", 12.5)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public async Task EditAjax_ComisionPorcentajeBindeada_PersisteValor(string? rawComision, double esperado)
    {
        var producto = await SeedProductoAsync(precioVenta: 121m);
        var productoActualizado = await _context.Productos.FindAsync(producto.Id);
        var vm = CrearProductoViewModelParaEditar(productoActualizado!);
        vm.ComisionPorcentaje = BindComisionOrDefault(rawComision);

        var result = await _controller.EditAjax(producto.Id, vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());

        var esperadoDecimal = Convert.ToDecimal(esperado);
        var entity = doc.RootElement.GetProperty("entity");
        Assert.Equal(esperadoDecimal, entity.GetProperty("comisionPorcentaje").GetDecimal());

        var actualizado = await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == producto.Id);
        Assert.Equal(esperadoDecimal, actualizado.ComisionPorcentaje);
    }

    [Fact]
    public async Task EditAjax_ComisionPorcentajeInvalida_MantieneErrorModelState()
    {
        var producto = await SeedProductoAsync(precioVenta: 121m);
        var productoActualizado = await _context.Productos.FindAsync(producto.Id);
        var vm = CrearProductoViewModelParaEditar(productoActualizado!);
        _controller.ModelState.Clear();
        _controller.ModelState.AddModelError(nameof(ProductoViewModel.ComisionPorcentaje), "Comision invalida");

        var result = await _controller.EditAjax(producto.Id, vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty(nameof(ProductoViewModel.ComisionPorcentaje), out _));

        var sinCambios = await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == producto.Id);
        Assert.Equal(0m, sinCambios.ComisionPorcentaje);
    }

    [Fact]
    public void ProductoViewModel_ComisionPorcentaje_UsaDecimalModelBinder()
    {
        var property = typeof(ProductoViewModel).GetProperty(nameof(ProductoViewModel.ComisionPorcentaje));
        var attribute = property?.GetCustomAttributes(typeof(ModelBinderAttribute), inherit: false)
            .Cast<ModelBinderAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal(typeof(DecimalModelBinder), attribute!.BinderType);
    }

    [Fact]
    public void ProductoController_NoUsaRequestFormParaNormalizarComision()
    {
        var sourcePath = Path.Combine(FindRepoRoot(), "Controllers", "ProductoController.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("NormalizarComisionPorcentaje", source);
        Assert.DoesNotContain("Request.Form", source);
    }

    [Fact]
    public void ProductoController_NoDuplicaNormalizacionDeCaracteristicas()
    {
        var sourcePath = Path.Combine(FindRepoRoot(), "Controllers", "ProductoController.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("NormalizarCaracteristicas", source);
    }

    // ─────────────────────────────────────────────────────────────
    // MaxCuotasSinInteresPermitidas
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetJson_DevuelveMaxCuotasSinInteresPermitidas_CuandoEstaConfigurado()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m, maxCuotas: 6);

        var result = await _controller.GetJson(producto.Id) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.Equal(6, doc.RootElement.GetProperty("maxCuotasSinInteresPermitidas").GetInt32());
    }

    [Fact]
    public async Task GetJson_DevuelveMaxCuotasSinInteresPermitidas_Null_CuandoNoConfigurado()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);

        var result = await _controller.GetJson(producto.Id) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("maxCuotasSinInteresPermitidas").ValueKind);
    }

    [Fact]
    public async Task EditAjax_PersistMaxCuotasSinInteresPermitidas()
    {
        var producto = await SeedProductoAsync(precioVenta: 121m);
        var p = await _context.Productos.FindAsync(producto.Id);
        var vm = new ProductoViewModel
        {
            Id = p!.Id, Codigo = p.Codigo, Nombre = p.Nombre,
            CategoriaId = p.CategoriaId, MarcaId = p.MarcaId,
            PrecioCompra = 60m, PrecioVenta = 80m, PorcentajeIVA = 21m,
            StockActual = p.StockActual, StockMinimo = p.StockMinimo,
            Activo = true, RowVersion = p.RowVersion,
            MaxCuotasSinInteresPermitidas = 6
        };

        await _controller.EditAjax(p.Id, vm);

        var updated = await _context.Productos.AsNoTracking().FirstAsync(x => x.Id == producto.Id);
        Assert.Equal(6, updated.MaxCuotasSinInteresPermitidas);
    }

    [Fact]
    public async Task EditAjax_PermiteLimpiarMaxCuotasSinInteresPermitidas_A_Null()
    {
        var producto = await SeedProductoAsync(precioVenta: 121m, maxCuotas: 6);
        var p = await _context.Productos.FindAsync(producto.Id);
        var vm = new ProductoViewModel
        {
            Id = p!.Id, Codigo = p.Codigo, Nombre = p.Nombre,
            CategoriaId = p.CategoriaId, MarcaId = p.MarcaId,
            PrecioCompra = 60m, PrecioVenta = 80m, PorcentajeIVA = 21m,
            StockActual = p.StockActual, StockMinimo = p.StockMinimo,
            Activo = true, RowVersion = p.RowVersion,
            MaxCuotasSinInteresPermitidas = null
        };

        await _controller.EditAjax(p.Id, vm);

        var updated = await _context.Productos.AsNoTracking().FirstAsync(x => x.Id == producto.Id);
        Assert.Null(updated.MaxCuotasSinInteresPermitidas);
    }

    // CreateAjax

    [Fact]
    public async Task CreateAjax_ProductoValido_CreaProducto()
    {
        var (categoria, marca) = await SeedCategoriaYMarcaAsync();
        var codigo = "P" + Guid.NewGuid().ToString("N")[..8];
        var vm = new ProductoViewModel
        {
            Codigo = codigo,
            Nombre = "Producto ajax valido",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = 80m,
            PrecioVenta = 100m,
            PorcentajeIVA = 21m,
            StockActual = 3m,
            StockMinimo = 1m,
            Activo = true,
            MaxCuotasSinInteresPermitidas = 6
        };

        var result = await _controller.CreateAjax(vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        var entity = doc.RootElement.GetProperty("entity");
        Assert.Equal(codigo, entity.GetProperty("codigo").GetString());
        Assert.Equal("Producto ajax valido", entity.GetProperty("nombre").GetString());
        Assert.Equal(121m, entity.GetProperty("precioVenta").GetDecimal());

        var creado = await _context.Productos.AsNoTracking().SingleAsync(p => p.Codigo == codigo);
        Assert.Equal(121m, creado.PrecioVenta);
        Assert.Equal(6, creado.MaxCuotasSinInteresPermitidas);
    }

    [Fact]
    public async Task CreateAjax_ConAlicuotaIVAId_PersistePrecioFinalConPorcentajeDeAlicuota()
    {
        var (categoria, marca) = await SeedCategoriaYMarcaAsync();
        var alicuota = await SeedAlicuotaIVAAsync(10.5m);
        var codigo = "P" + Guid.NewGuid().ToString("N")[..8];
        var vm = new ProductoViewModel
        {
            Codigo = codigo,
            Nombre = "Producto ajax con alicuota",
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = 80m,
            PrecioVenta = 100m,
            PorcentajeIVA = 21m,
            AlicuotaIVAId = alicuota.Id,
            StockActual = 3m,
            StockMinimo = 1m,
            Activo = true
        };

        var result = await _controller.CreateAjax(vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        var entity = doc.RootElement.GetProperty("entity");
        Assert.Equal(110.50m, entity.GetProperty("precioVenta").GetDecimal());

        var creado = await _context.Productos.AsNoTracking().SingleAsync(p => p.Codigo == codigo);
        Assert.Equal(10.5m, creado.PorcentajeIVA);
        Assert.Equal(110.50m, creado.PrecioVenta);
        Assert.Equal(alicuota.Id, creado.AlicuotaIVAId);
    }

    [Fact]
    public async Task CreateAjax_CaracteristicasVacias_NoPersisteCaracteristicasVacias()
    {
        var vm = await CrearProductoViewModelParaCrearAsync();
        vm.Caracteristicas =
        [
            new ProductoCaracteristicaViewModel { Nombre = "   ", Valor = "Color" },
            new ProductoCaracteristicaViewModel { Nombre = "Memoria", Valor = "" },
            new ProductoCaracteristicaViewModel { Nombre = "", Valor = "   " }
        ];

        var result = await _controller.CreateAjax(vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());

        var creado = await _context.Productos
            .AsNoTracking()
            .Include(p => p.Caracteristicas)
            .SingleAsync(p => p.Codigo == vm.Codigo);
        Assert.Empty(creado.Caracteristicas);
    }

    [Fact]
    public async Task CreateAjax_CaracteristicasConEspacios_AplicaTrimYConservaValidas()
    {
        var vm = await CrearProductoViewModelParaCrearAsync();
        vm.Caracteristicas =
        [
            new ProductoCaracteristicaViewModel { Nombre = "  Color  ", Valor = "  Negro  " },
            new ProductoCaracteristicaViewModel { Nombre = " Memoria ", Valor = " 16GB " },
            new ProductoCaracteristicaViewModel { Nombre = "   ", Valor = "No persiste" }
        ];

        var result = await _controller.CreateAjax(vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());

        var caracteristicas = await _context.ProductosCaracteristicas
            .AsNoTracking()
            .Where(c => c.Producto.Codigo == vm.Codigo && !c.IsDeleted)
            .OrderBy(c => c.Nombre)
            .ToListAsync();
        Assert.Equal(2, caracteristicas.Count);
        Assert.Collection(
            caracteristicas,
            c =>
            {
                Assert.Equal("Color", c.Nombre);
                Assert.Equal("Negro", c.Valor);
            },
            c =>
            {
                Assert.Equal("Memoria", c.Nombre);
                Assert.Equal("16GB", c.Valor);
            });
    }

    [Fact]
    public async Task EditAjax_CaracteristicasVacias_NoPersisteCaracteristicasVacias()
    {
        var producto = await SeedProductoAsync(precioVenta: 121m);
        var p = await _context.Productos.FindAsync(producto.Id);
        var vm = CrearProductoViewModelParaEditar(p!);
        vm.Caracteristicas =
        [
            new ProductoCaracteristicaViewModel { Nombre = "  ", Valor = "Color" },
            new ProductoCaracteristicaViewModel { Nombre = "Memoria", Valor = "   " }
        ];

        var result = await _controller.EditAjax(producto.Id, vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());

        var caracteristicas = await _context.ProductosCaracteristicas
            .AsNoTracking()
            .Where(c => c.ProductoId == producto.Id && !c.IsDeleted)
            .ToListAsync();
        Assert.Empty(caracteristicas);
    }

    [Fact]
    public async Task EditAjax_CaracteristicasConEspacios_AplicaTrimYConservaValidas()
    {
        var producto = await SeedProductoAsync(precioVenta: 121m);
        var p = await _context.Productos.FindAsync(producto.Id);
        var vm = CrearProductoViewModelParaEditar(p!);
        vm.Caracteristicas =
        [
            new ProductoCaracteristicaViewModel { Nombre = "  Color  ", Valor = "  Blanco  " },
            new ProductoCaracteristicaViewModel { Nombre = " Almacenamiento ", Valor = " 512GB " },
            new ProductoCaracteristicaViewModel { Nombre = "", Valor = "No persiste" }
        ];

        var result = await _controller.EditAjax(producto.Id, vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());

        var caracteristicas = await _context.ProductosCaracteristicas
            .AsNoTracking()
            .Where(c => c.ProductoId == producto.Id && !c.IsDeleted)
            .OrderBy(c => c.Nombre)
            .ToListAsync();
        Assert.Equal(2, caracteristicas.Count);
        Assert.Collection(
            caracteristicas,
            c =>
            {
                Assert.Equal("Almacenamiento", c.Nombre);
                Assert.Equal("512GB", c.Valor);
            },
            c =>
            {
                Assert.Equal("Color", c.Nombre);
                Assert.Equal("Blanco", c.Valor);
            });
    }

    [Fact]
    public async Task CreateAjax_CodigoDuplicado_DevuelveError()
    {
        var existente = await SeedProductoAsync();
        var vm = new ProductoViewModel
        {
            Codigo = existente.Codigo,
            Nombre = "Producto duplicado",
            CategoriaId = existente.CategoriaId,
            MarcaId = existente.MarcaId,
            PrecioCompra = 50m,
            PrecioVenta = 100m,
            PorcentajeIVA = 21m,
            StockActual = 1m,
            Activo = true
        };

        var result = await _controller.CreateAjax(vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        // El service lanza InvalidOperationException; el controller lo captura bajo clave ""
        var globalErrors = doc.RootElement.GetProperty("errors").GetProperty("");
        Assert.Contains("Ya existe un producto con el codigo", Normalize(globalErrors[0].GetString()));
        Assert.Equal(1, await _context.Productos.CountAsync(p => p.Codigo == existente.Codigo));
    }

    [Fact]
    public async Task EditAjax_CodigoDuplicado_DevuelveError()
    {
        var producto1 = await SeedProductoAsync();
        var producto2 = await SeedProductoAsync();
        var p2 = await _context.Productos.FindAsync(producto2.Id);
        var vm = new ProductoViewModel
        {
            Id = producto2.Id,
            Codigo = producto1.Codigo, // código de otro producto existente
            Nombre = p2!.Nombre,
            CategoriaId = p2.CategoriaId,
            MarcaId = p2.MarcaId,
            PrecioCompra = p2.PrecioCompra,
            PrecioVenta = 80m,
            PorcentajeIVA = 21m,
            StockActual = p2.StockActual,
            StockMinimo = p2.StockMinimo,
            Activo = true,
            RowVersion = p2.RowVersion
        };

        var result = await _controller.EditAjax(producto2.Id, vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        // El service lanza InvalidOperationException; el controller lo captura bajo clave ""
        var globalErrors = doc.RootElement.GetProperty("errors").GetProperty("");
        Assert.Contains("Ya existe otro producto con el codigo", Normalize(globalErrors[0].GetString()));
        // El código del producto2 no debe haber cambiado
        var sinCambios = await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == producto2.Id);
        Assert.Equal(p2.Codigo, sinCambios.Codigo);
    }

    [Fact]
    public async Task CreateAjax_ModelStateInvalido_DevuelveError()
    {
        var vm = new ProductoViewModel
        {
            Codigo = "",
            Nombre = "",
            PrecioCompra = 10m,
            PrecioVenta = 20m,
            PorcentajeIVA = 21m
        };
        _controller.ModelState.AddModelError(nameof(ProductoViewModel.Codigo), "El codigo es obligatorio");

        var result = await _controller.CreateAjax(vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty(nameof(ProductoViewModel.Codigo), out var codigoErrors));
        Assert.Equal("El codigo es obligatorio", codigoErrors[0].GetString());
    }

    [Theory]
    [InlineData("12,5", 12.5)]
    [InlineData("12.5", 12.5)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public async Task CreateAjax_ComisionPorcentajeBindeada_PersisteValor(string? rawComision, double esperado)
    {
        var vm = await CrearProductoViewModelParaCrearAsync();
        vm.ComisionPorcentaje = BindComisionOrDefault(rawComision);

        var result = await _controller.CreateAjax(vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());

        var esperadoDecimal = Convert.ToDecimal(esperado);
        var creado = await _context.Productos.AsNoTracking().SingleAsync(p => p.Codigo == vm.Codigo);
        Assert.Equal(esperadoDecimal, creado.ComisionPorcentaje);
    }

    [Fact]
    public async Task CreateAjax_ComisionPorcentajeInvalida_MantieneErrorModelState()
    {
        var vm = await CrearProductoViewModelParaCrearAsync();
        _controller.ModelState.Clear();
        _controller.ModelState.AddModelError(nameof(ProductoViewModel.ComisionPorcentaje), "Comision invalida");

        var result = await _controller.CreateAjax(vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty(nameof(ProductoViewModel.ComisionPorcentaje), out _));
        Assert.False(await _context.Productos.AnyAsync(p => p.Codigo == vm.Codigo));
    }

    // ActualizarComisionVendedor

    [Theory]
    [InlineData("12,5", 12.5)]
    [InlineData("12.5", 12.5)]
    [InlineData("", 0)]
    public async Task ActualizarComisionVendedor_ValorValido_ActualizaComision(string porcentajeComision, double esperado)
    {
        var producto = await SeedProductoAsync();

        var result = await _controller.ActualizarComisionVendedor(producto.Id, porcentajeComision) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());

        var esperadoDecimal = Convert.ToDecimal(esperado);
        Assert.Equal(esperadoDecimal, doc.RootElement.GetProperty("comisionPorcentaje").GetDecimal());

        var actualizado = await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == producto.Id);
        Assert.Equal(esperadoDecimal, actualizado.ComisionPorcentaje);
    }

    [Fact]
    public async Task ActualizarComisionVendedor_ValorInvalido_DevuelveErrorSinActualizar()
    {
        var producto = await SeedProductoAsync();

        var result = await _controller.ActualizarComisionVendedor(producto.Id, "abc") as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("El porcentaje ingresado no es válido.", Normalize(doc.RootElement.GetProperty("message").GetString()));

        var sinCambios = await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == producto.Id);
        Assert.Equal(0m, sinCambios.ComisionPorcentaje);
    }

    [Fact]
    public async Task ActualizarComisionVendedor_ValorFueraDeRango_DevuelveErrorSinActualizar()
    {
        var producto = await SeedProductoAsync();

        var result = await _controller.ActualizarComisionVendedor(producto.Id, "101") as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("El porcentaje de comision debe estar entre 0 y 100.", Normalize(doc.RootElement.GetProperty("message").GetString()));

        var sinCambios = await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == producto.Id);
        Assert.Equal(0m, sinCambios.ComisionPorcentaje);
    }

    [Fact]
    public void ProductoCondicionPagoCatalogo_NoExponeAdministracionLegacy()
    {
        var html = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Catalogo", "Index_tw.cshtml"));

        Assert.DoesNotContain("data-condiciones-pago-producto-id", html);
        Assert.DoesNotContain("data-condiciones-pago-producto-nombre", html);
        Assert.DoesNotContain("row-action__label\">Condiciones de pago</span>", html);
        Assert.DoesNotContain("modal-condiciones-pago-producto", html);
        Assert.DoesNotContain("producto-condiciones-pago-modal.js", html);
    }

    [Fact]
    public void Catalogo_MuestraBotonUnidades()
    {
        var html = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Catalogo", "Index_tw.cshtml"));

        Assert.Contains("asp-action=\"Unidades\"", html);
        Assert.Contains("asp-route-productoId=\"@p.ProductoId\"", html);
        Assert.Contains("row-action__label\">Unidades</span>", html);
    }

    [Fact]
    public void UnidadesView_MuestraFormularioAgregarUnidad()
    {
        var html = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Producto", "Unidades.cshtml"));

        Assert.Contains("Agregar unidad", html);
        Assert.Contains("asp-action=\"CrearUnidad\"", html);
        Assert.Contains("asp-for=\"CrearUnidad.NumeroSerie\"", html);
        Assert.Contains("El codigo interno se genera automaticamente", html);
        Assert.Contains("El numero de serie es opcional", html);
        Assert.Contains("La unidad se creara en estado EnStock", html);
        Assert.Contains("Crear una unidad fisica no ajusta el stock agregado", html);
        Assert.DoesNotContain("CodigoInternoUnidad\" name=\"CrearUnidad", html);
    }

    [Fact]
    public void UnidadesView_MuestraFormularioCargaMasivaConPreview()
    {
        var html = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Producto", "Unidades.cshtml"));

        Assert.Contains("Carga masiva de unidades", html);
        Assert.Contains("asp-action=\"CrearUnidadesMasivas\"", html);
        Assert.Contains("asp-for=\"CargaMasiva.CantidadSinSerie\"", html);
        Assert.Contains("asp-for=\"CargaMasiva.NumerosSerieTexto\"", html);
        Assert.Contains("Previsualizar", html);
        Assert.Contains("Confirmar carga", html);
        Assert.Contains("La carga masiva no ajusta el stock agregado", html);
    }

    [Fact]
    public void UnidadesView_MuestraAccionesDeAjusteConMotivoObligatorio()
    {
        var html = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Producto", "Unidades.cshtml"));

        Assert.Contains("asp-action=\"MarcarUnidadFaltante\"", html);
        Assert.Contains("asp-action=\"DarUnidadBaja\"", html);
        Assert.Contains("asp-action=\"ReintegrarUnidadAStock\"", html);
        Assert.Contains("Marcar faltante", html);
        Assert.Contains("Dar de baja", html);
        Assert.Contains("Reintegrar a stock", html);
        Assert.Contains("name=\"Motivo\"", html);
        Assert.Contains("required", html);
    }

    [Fact]
    public void UnidadesView_MuestraPanelConciliacionSinBotonFuncional()
    {
        var html = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Producto", "Unidades.cshtml"));

        Assert.Contains("Conciliacion stock vs unidades fisicas", html);
        Assert.Contains("Stock agregado actual", html);
        Assert.Contains("Unidades disponibles", html);
        Assert.Contains("Unidades registradas", html);
        Assert.Contains("Diferencia", html);
        Assert.Contains("Vendidas", html);
        Assert.Contains("Faltantes", html);
        Assert.Contains("Baja", html);
        Assert.Contains("Devueltas", html);
        Assert.Contains("Reservadas", html);
        Assert.Contains("En reparacion", html);
        Assert.Contains("Conciliado", html);
        Assert.Contains("Diferencia detectada", html);
        Assert.Contains("Este panel compara el stock agregado del SKU contra las unidades fisicas disponibles. No realiza ajustes automaticamente.", html);
        Assert.Contains("Unidades registradas incluye todas las unidades no eliminadas, aunque esten vendidas, faltantes, anuladas o dadas de baja.", html);
        Assert.Contains("Unidades disponibles corresponde a unidades en estado EnStock.", html);
        Assert.Contains("Este producto no requiere trazabilidad individual. Las unidades cargadas son trazabilidad operativa opcional.", html);
        Assert.DoesNotContain("Total unidades activas", html);
        Assert.Contains("Ver Kardex SKU", html);
        Assert.Contains("Ver historial/listado de unidades", html);
        Assert.Contains("ConciliarStockUnidades", html);
        Assert.Contains("Conciliar stock agregado", html);
        Assert.Contains("ajuste-asistido", html);
    }

    [Fact]
    public async Task Unidades_ProductoExistente_DevuelveVistaConUnidadesDelProducto()
    {
        var producto = await SeedProductoAsync();
        var otroProducto = await SeedProductoAsync();
        await SeedProductoUnidadAsync(producto.Id, "UNI-OK", "SN-OK", EstadoUnidad.EnStock);
        await SeedProductoUnidadAsync(otroProducto.Id, "UNI-OTRA", "SN-OTRA", EstadoUnidad.EnStock);

        var result = await _controller.Unidades(producto.Id);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Unidades", view.ViewName);
        var model = Assert.IsType<ProductoUnidadesViewModel>(view.Model);
        var unidad = Assert.Single(model.Unidades);
        Assert.Equal("UNI-OK", unidad.CodigoInternoUnidad);
        Assert.DoesNotContain(model.Unidades, u => u.CodigoInternoUnidad == "UNI-OTRA");
        Assert.Equal(producto.StockActual, model.Conciliacion.StockActual);
        Assert.Equal(1, model.Conciliacion.UnidadesEnStock);
    }

    [Fact]
    public async Task Unidades_MuestraPanelConciliacionConStockUnidadesYDiferencia()
    {
        var producto = await SeedProductoAsync(requiereNumeroSerie: true);
        await SeedProductoUnidadAsync(producto.Id, "UNI-1", "SN-1", EstadoUnidad.EnStock);
        await SeedProductoUnidadAsync(producto.Id, "UNI-2", "SN-2", EstadoUnidad.EnStock);

        var result = await _controller.Unidades(producto.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductoUnidadesViewModel>(view.Model);
        Assert.Equal(10m, model.Conciliacion.StockActual);
        Assert.Equal(2, model.Conciliacion.UnidadesEnStock);
        Assert.Equal(8m, model.Conciliacion.DiferenciaStockVsUnidadesEnStock);
        Assert.True(model.Conciliacion.HayDiferencia);
    }

    [Fact]
    public async Task Unidades_ConciliacionSinDiferencia_MarcaConciliado()
    {
        var producto = await SeedProductoAsync(requiereNumeroSerie: true);
        var entity = await _context.Productos.FindAsync(producto.Id);
        entity!.StockActual = 2m;
        await _context.SaveChangesAsync();
        await SeedProductoUnidadAsync(producto.Id, "UNI-1", "SN-1", EstadoUnidad.EnStock);
        await SeedProductoUnidadAsync(producto.Id, "UNI-2", "SN-2", EstadoUnidad.EnStock);

        var result = await _controller.Unidades(producto.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductoUnidadesViewModel>(view.Model);
        Assert.False(model.Conciliacion.HayDiferencia);
        Assert.Equal(0m, model.Conciliacion.DiferenciaStockVsUnidadesEnStock);
    }

    [Fact]
    public async Task Unidades_ProductoInexistente_DevuelveNotFound()
    {
        var result = await _controller.Unidades(999999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Unidades_FiltroPorEstado_FiltraUnidades()
    {
        var producto = await SeedProductoAsync();
        await SeedProductoUnidadAsync(producto.Id, "UNI-STOCK", "SN-STOCK", EstadoUnidad.EnStock);
        await SeedProductoUnidadAsync(producto.Id, "UNI-VENDIDA", "SN-VENDIDA", EstadoUnidad.Vendida);

        var result = await _controller.Unidades(producto.Id, estado: EstadoUnidad.Vendida);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductoUnidadesViewModel>(view.Model);
        var unidad = Assert.Single(model.Unidades);
        Assert.Equal(EstadoUnidad.Vendida, unidad.Estado);
        Assert.Equal("UNI-VENDIDA", unidad.CodigoInternoUnidad);
    }

    [Fact]
    public async Task Unidades_FiltroTexto_BuscaCodigoInternoONumeroSerie()
    {
        var producto = await SeedProductoAsync();
        await SeedProductoUnidadAsync(producto.Id, "COD-123", "SERIE-ABC", EstadoUnidad.EnStock);
        await SeedProductoUnidadAsync(producto.Id, "COD-456", "SERIE-XYZ", EstadoUnidad.EnStock);

        var result = await _controller.Unidades(producto.Id, texto: "XYZ");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductoUnidadesViewModel>(view.Model);
        var unidad = Assert.Single(model.Unidades);
        Assert.Equal("COD-456", unidad.CodigoInternoUnidad);
    }

    [Fact]
    public async Task Unidades_DefineAccionesValidasPorEstado()
    {
        var producto = await SeedProductoAsync();
        await SeedProductoUnidadAsync(producto.Id, "UNI-STOCK", "SN-STOCK", EstadoUnidad.EnStock);
        await SeedProductoUnidadAsync(producto.Id, "UNI-FALT", "SN-FALT", EstadoUnidad.Faltante);
        await SeedProductoUnidadAsync(producto.Id, "UNI-DEV", "SN-DEV", EstadoUnidad.Devuelta);
        await SeedProductoUnidadAsync(producto.Id, "UNI-VEND", "SN-VEND", EstadoUnidad.Vendida);

        var result = await _controller.Unidades(producto.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductoUnidadesViewModel>(view.Model);
        var enStock = Assert.Single(model.Unidades, u => u.Estado == EstadoUnidad.EnStock);
        var faltante = Assert.Single(model.Unidades, u => u.Estado == EstadoUnidad.Faltante);
        var devuelta = Assert.Single(model.Unidades, u => u.Estado == EstadoUnidad.Devuelta);
        var vendida = Assert.Single(model.Unidades, u => u.Estado == EstadoUnidad.Vendida);

        Assert.True(enStock.PuedeMarcarFaltante);
        Assert.True(enStock.PuedeDarBaja);
        Assert.False(enStock.PuedeReintegrarAStock);

        Assert.False(faltante.PuedeMarcarFaltante);
        Assert.True(faltante.PuedeDarBaja);
        Assert.True(faltante.PuedeReintegrarAStock);

        Assert.False(devuelta.PuedeMarcarFaltante);
        Assert.True(devuelta.PuedeDarBaja);
        Assert.True(devuelta.PuedeReintegrarAStock);

        Assert.False(vendida.PuedeMarcarFaltante);
        Assert.False(vendida.PuedeDarBaja);
        Assert.False(vendida.PuedeReintegrarAStock);
    }

    [Fact]
    public async Task UnidadHistorial_UnidadExistente_MuestraMovimientos()
    {
        var producto = await SeedProductoAsync();
        var unidad = await SeedProductoUnidadAsync(producto.Id, "UNI-HIST", "SN-HIST", EstadoUnidad.Vendida);
        await SeedMovimientoUnidadAsync(unidad.Id, EstadoUnidad.EnStock, EstadoUnidad.Vendida, "Venta de unidad");

        var result = await _controller.UnidadHistorial(unidad.Id);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("UnidadHistorial", view.ViewName);
        var model = Assert.IsType<ProductoUnidadHistorialViewModel>(view.Model);
        Assert.Equal("UNI-HIST", model.CodigoInternoUnidad);
        Assert.Single(model.Movimientos);
        Assert.Equal("Venta de unidad", model.Movimientos[0].Motivo);
    }

    [Fact]
    public async Task UnidadHistorial_UnidadInexistente_DevuelveNotFound()
    {
        var result = await _controller.UnidadHistorial(999999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Unidades_ProductoNoTrazable_MuestraAvisoYNoRompe()
    {
        var producto = await SeedProductoAsync(requiereNumeroSerie: false);

        var result = await _controller.Unidades(producto.Id);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ProductoUnidadesViewModel>(view.Model);
        Assert.False(model.RequiereNumeroSerie);
        Assert.False(model.Conciliacion.RequiereNumeroSerie);
        Assert.Empty(model.Unidades);
    }

    [Fact]
    public async Task CrearUnidad_PostSinSerie_CreaUnidadYRedirigeAUnidades()
    {
        var producto = await SeedProductoAsync();

        var result = await _controller.CrearUnidad(new ProductoUnidadCrearViewModel
        {
            ProductoId = producto.Id,
            UbicacionActual = "Deposito A",
            Observaciones = "Alta manual"
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProductoController.Unidades), redirect.ActionName);
        Assert.Equal(producto.Id, redirect.RouteValues!["productoId"]);

        var unidad = await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.ProductoId == producto.Id);
        Assert.Null(unidad.NumeroSerie);
        Assert.Equal("Deposito A", unidad.UbicacionActual);
        Assert.Equal("Alta manual", unidad.Observaciones);
        Assert.Equal(EstadoUnidad.EnStock, unidad.Estado);
        Assert.StartsWith(producto.Codigo + "-U-", unidad.CodigoInternoUnidad);
    }

    [Fact]
    public async Task CrearUnidad_PostConSerie_CreaUnidadConSerieYCodigoAutomatico()
    {
        var producto = await SeedProductoAsync();

        await _controller.CrearUnidad(new ProductoUnidadCrearViewModel
        {
            ProductoId = producto.Id,
            NumeroSerie = "SN-MANUAL-001"
        });

        var unidad = await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.ProductoId == producto.Id);
        Assert.Equal("SN-MANUAL-001", unidad.NumeroSerie);
        Assert.StartsWith(producto.Codigo + "-U-", unidad.CodigoInternoUnidad);
        Assert.False(string.IsNullOrWhiteSpace(unidad.CodigoInternoUnidad));
    }

    [Fact]
    public async Task CrearUnidad_PostProductoInexistente_DevuelveNotFound()
    {
        var result = await _controller.CrearUnidad(new ProductoUnidadCrearViewModel
        {
            ProductoId = 999999,
            NumeroSerie = "SN-X"
        });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task CrearUnidad_PostSerieDuplicada_MuestraErrorClaroSinCrearOtraUnidad()
    {
        var producto = await SeedProductoAsync();
        await _controller.CrearUnidad(new ProductoUnidadCrearViewModel
        {
            ProductoId = producto.Id,
            NumeroSerie = "SN-DUP"
        });

        var result = await _controller.CrearUnidad(new ProductoUnidadCrearViewModel
        {
            ProductoId = producto.Id,
            NumeroSerie = "SN-DUP"
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Unidades", view.ViewName);
        Assert.False(_controller.ModelState.IsValid);
        var error = Assert.Single(_controller.ModelState["CrearUnidad.NumeroSerie"]!.Errors);
        Assert.Contains("unidad activa", Normalize(error.ErrorMessage));

        var total = await _context.ProductoUnidades.CountAsync(u => u.ProductoId == producto.Id);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task CrearUnidad_PostCreaHistorialInicialYNoModificaStockActual()
    {
        var producto = await SeedProductoAsync();
        var stockInicial = producto.StockActual;

        await _controller.CrearUnidad(new ProductoUnidadCrearViewModel
        {
            ProductoId = producto.Id,
            NumeroSerie = "SN-HIST-001"
        });

        var unidad = await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.ProductoId == producto.Id);
        var historial = await _context.ProductoUnidadMovimientos
            .AsNoTracking()
            .Where(m => m.ProductoUnidadId == unidad.Id)
            .ToListAsync();
        var productoActualizado = await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == producto.Id);

        var movimiento = Assert.Single(historial);
        Assert.Equal(EstadoUnidad.EnStock, movimiento.EstadoNuevo);
        Assert.Equal("Ingreso inicial de unidad", movimiento.Motivo);
        Assert.Equal(stockInicial, productoActualizado.StockActual);
    }

    [Fact]
    public async Task CrearUnidadesMasivas_PreviewValido_NoCreaUnidades()
    {
        var producto = await SeedProductoAsync(requiereNumeroSerie: true);

        var result = await _controller.CrearUnidadesMasivas(new ProductoUnidadCargaMasivaViewModel
        {
            ProductoId = producto.Id,
            CantidadSinSerie = 2,
            NumerosSerieTexto = "SN-BULK-001\r\nSN-BULK-002",
            UbicacionActual = "Deposito masivo",
            Observaciones = "Preview"
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Unidades", view.ViewName);
        var model = Assert.IsType<ProductoUnidadesViewModel>(view.Model);
        Assert.True(model.CargaMasiva.PreviewListo);
        Assert.Equal(4, model.CargaMasiva.Preview.Count);
        Assert.Equal(2, model.CargaMasiva.Preview.Count(p => !p.TieneNumeroSerie));
        Assert.Contains(model.CargaMasiva.Preview, p => p.NumeroSerie == "SN-BULK-001");
        Assert.Contains(model.CargaMasiva.Preview, p => p.NumeroSerie == "SN-BULK-002");

        var total = await _context.ProductoUnidades.CountAsync(u => u.ProductoId == producto.Id);
        Assert.Equal(0, total);
    }

    [Fact]
    public async Task CrearUnidadesMasivas_Confirmar_CreaUnidadesConHistorialYNoModificaStock()
    {
        var producto = await SeedProductoAsync(requiereNumeroSerie: true);
        var stockInicial = producto.StockActual;

        var result = await _controller.CrearUnidadesMasivas(new ProductoUnidadCargaMasivaViewModel
        {
            ProductoId = producto.Id,
            CantidadSinSerie = 1,
            NumerosSerieTexto = "SN-BULK-010\nSN-BULK-011",
            UbicacionActual = "Deposito masivo",
            Observaciones = "Alta masiva",
            Confirmar = true
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProductoController.Unidades), redirect.ActionName);
        Assert.Equal(producto.Id, redirect.RouteValues!["productoId"]);

        var unidades = await _context.ProductoUnidades
            .AsNoTracking()
            .Where(u => u.ProductoId == producto.Id)
            .OrderBy(u => u.CodigoInternoUnidad)
            .ToListAsync();
        var productoActualizado = await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == producto.Id);
        var movimientos = await _context.ProductoUnidadMovimientos.AsNoTracking().ToListAsync();

        Assert.Equal(3, unidades.Count);
        Assert.Single(unidades.Where(u => u.NumeroSerie == null));
        Assert.Contains(unidades, u => u.NumeroSerie == "SN-BULK-010");
        Assert.Contains(unidades, u => u.NumeroSerie == "SN-BULK-011");
        Assert.All(unidades, u =>
        {
            Assert.Equal(EstadoUnidad.EnStock, u.Estado);
            Assert.Equal("Deposito masivo", u.UbicacionActual);
            Assert.Equal("Alta masiva", u.Observaciones);
            Assert.StartsWith(producto.Codigo + "-U-", u.CodigoInternoUnidad);
        });
        Assert.Equal(stockInicial, productoActualizado.StockActual);
        Assert.Equal(3, movimientos.Count);
        Assert.All(movimientos, m => Assert.Equal("Ingreso inicial de unidad", m.Motivo));
    }

    [Fact]
    public async Task CrearUnidadesMasivas_SeriesDuplicadasEnInput_MuestraErrorYNoCrea()
    {
        var producto = await SeedProductoAsync(requiereNumeroSerie: true);

        var result = await _controller.CrearUnidadesMasivas(new ProductoUnidadCargaMasivaViewModel
        {
            ProductoId = producto.Id,
            NumerosSerieTexto = "SN-DUP-MASIVA\nsn-dup-masiva"
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Unidades", view.ViewName);
        Assert.False(_controller.ModelState.IsValid);
        var error = Assert.Single(_controller.ModelState["CargaMasiva.NumerosSerieTexto"]!.Errors);
        Assert.Contains("repetidos", Normalize(error.ErrorMessage));

        var total = await _context.ProductoUnidades.CountAsync(u => u.ProductoId == producto.Id);
        Assert.Equal(0, total);
    }

    [Fact]
    public async Task CrearUnidadesMasivas_SerieExistente_MuestraErrorYNoCrea()
    {
        var producto = await SeedProductoAsync(requiereNumeroSerie: true);
        await SeedProductoUnidadAsync(producto.Id, "UNI-EXISTENTE", "SN-EXISTE", EstadoUnidad.EnStock);

        var result = await _controller.CrearUnidadesMasivas(new ProductoUnidadCargaMasivaViewModel
        {
            ProductoId = producto.Id,
            NumerosSerieTexto = "SN-EXISTE\nSN-NUEVA"
        });

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Unidades", view.ViewName);
        Assert.False(_controller.ModelState.IsValid);
        var error = Assert.Single(_controller.ModelState["CargaMasiva.NumerosSerieTexto"]!.Errors);
        Assert.Contains("Ya existen unidades activas", Normalize(error.ErrorMessage));

        var total = await _context.ProductoUnidades.CountAsync(u => u.ProductoId == producto.Id);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task MarcarUnidadFaltante_PostCambiaEstadoCreaHistorialYNoModificaStock()
    {
        var producto = await SeedProductoAsync();
        var stockInicial = producto.StockActual;
        var unidad = await SeedProductoUnidadAsync(producto.Id, "UNI-FALTANTE", "SN-FALTANTE", EstadoUnidad.EnStock);

        var result = await _controller.MarcarUnidadFaltante(new ProductoUnidadAjusteViewModel
        {
            ProductoUnidadId = unidad.Id,
            Motivo = "No encontrada en deposito"
        });

        AssertRedirectUnidades(result, producto.Id);
        var unidadActualizada = await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.Id == unidad.Id);
        var productoActualizado = await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == producto.Id);
        var movimiento = await _context.ProductoUnidadMovimientos.AsNoTracking().SingleAsync(m => m.ProductoUnidadId == unidad.Id);

        Assert.Equal(EstadoUnidad.Faltante, unidadActualizada.Estado);
        Assert.Equal(stockInicial, productoActualizado.StockActual);
        Assert.Equal(EstadoUnidad.EnStock, movimiento.EstadoAnterior);
        Assert.Equal(EstadoUnidad.Faltante, movimiento.EstadoNuevo);
        Assert.Equal("No encontrada en deposito", movimiento.Motivo);
        Assert.Equal("AjusteUnidad:Faltante", movimiento.OrigenReferencia);
        Assert.Equal("operador@test.com", movimiento.UsuarioResponsable);
    }

    [Fact]
    public async Task DarUnidadBaja_PostCambiaEstadoCreaHistorialYNoModificaStock()
    {
        var producto = await SeedProductoAsync();
        var stockInicial = producto.StockActual;
        var unidad = await SeedProductoUnidadAsync(producto.Id, "UNI-BAJA", "SN-BAJA", EstadoUnidad.EnStock);

        var result = await _controller.DarUnidadBaja(new ProductoUnidadAjusteViewModel
        {
            ProductoUnidadId = unidad.Id,
            Motivo = "Rotura irreparable"
        });

        AssertRedirectUnidades(result, producto.Id);
        var unidadActualizada = await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.Id == unidad.Id);
        var productoActualizado = await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == producto.Id);
        var movimiento = await _context.ProductoUnidadMovimientos.AsNoTracking().SingleAsync(m => m.ProductoUnidadId == unidad.Id);

        Assert.Equal(EstadoUnidad.Baja, unidadActualizada.Estado);
        Assert.Equal(stockInicial, productoActualizado.StockActual);
        Assert.Equal(EstadoUnidad.Baja, movimiento.EstadoNuevo);
        Assert.Equal("AjusteUnidad:Baja", movimiento.OrigenReferencia);
    }

    [Fact]
    public async Task ReintegrarUnidadAStock_PostCambiaEstadoCreaHistorialYNoModificaStock()
    {
        var producto = await SeedProductoAsync();
        var stockInicial = producto.StockActual;
        var unidad = await SeedProductoUnidadAsync(producto.Id, "UNI-REINT", "SN-REINT", EstadoUnidad.Faltante);

        var result = await _controller.ReintegrarUnidadAStock(new ProductoUnidadAjusteViewModel
        {
            ProductoUnidadId = unidad.Id,
            Motivo = "Recuperada en deposito"
        });

        AssertRedirectUnidades(result, producto.Id);
        var unidadActualizada = await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.Id == unidad.Id);
        var productoActualizado = await _context.Productos.AsNoTracking().SingleAsync(p => p.Id == producto.Id);
        var movimiento = await _context.ProductoUnidadMovimientos.AsNoTracking().SingleAsync(m => m.ProductoUnidadId == unidad.Id);

        Assert.Equal(EstadoUnidad.EnStock, unidadActualizada.Estado);
        Assert.Equal(stockInicial, productoActualizado.StockActual);
        Assert.Equal(EstadoUnidad.Faltante, movimiento.EstadoAnterior);
        Assert.Equal(EstadoUnidad.EnStock, movimiento.EstadoNuevo);
        Assert.Equal("AjusteUnidad:Reintegro", movimiento.OrigenReferencia);
    }

    [Fact]
    public async Task AjusteUnidad_MotivoVacio_RedireccionaConErrorClaroSinCambiarEstado()
    {
        var producto = await SeedProductoAsync();
        var unidad = await SeedProductoUnidadAsync(producto.Id, "UNI-SIN-MOTIVO", "SN-SIN-MOTIVO", EstadoUnidad.EnStock);

        var result = await _controller.MarcarUnidadFaltante(new ProductoUnidadAjusteViewModel
        {
            ProductoUnidadId = unidad.Id,
            Motivo = " "
        });

        AssertRedirectUnidades(result, producto.Id);
        Assert.Equal("El motivo es obligatorio para ajustar la unidad.", _controller.TempData["Error"]);
        var unidadActualizada = await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.Id == unidad.Id);
        var movimientos = await _context.ProductoUnidadMovimientos.CountAsync(m => m.ProductoUnidadId == unidad.Id);
        Assert.Equal(EstadoUnidad.EnStock, unidadActualizada.Estado);
        Assert.Equal(0, movimientos);
    }

    [Fact]
    public async Task AjusteUnidad_TransicionInvalida_RedireccionaConErrorClaroSinCambiarEstado()
    {
        var producto = await SeedProductoAsync();
        var unidad = await SeedProductoUnidadAsync(producto.Id, "UNI-VENDIDA", "SN-VENDIDA", EstadoUnidad.Vendida);

        var result = await _controller.MarcarUnidadFaltante(new ProductoUnidadAjusteViewModel
        {
            ProductoUnidadId = unidad.Id,
            Motivo = "Intento invalido"
        });

        AssertRedirectUnidades(result, producto.Id);
        Assert.Contains("no permite la transicion", Normalize(_controller.TempData["Error"]?.ToString() ?? string.Empty));
        var unidadActualizada = await _context.ProductoUnidades.AsNoTracking().SingleAsync(u => u.Id == unidad.Id);
        var movimientos = await _context.ProductoUnidadMovimientos.CountAsync(m => m.ProductoUnidadId == unidad.Id);
        Assert.Equal(EstadoUnidad.Vendida, unidadActualizada.Estado);
        Assert.Equal(0, movimientos);
    }

    [Fact]
    public async Task EditAjax_RowVersionNula_DevuelveErrorControlado()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);
        var p = await _context.Productos.FindAsync(producto.Id);
        var vm = new ProductoViewModel
        {
            Id = p!.Id, Codigo = p.Codigo, Nombre = p.Nombre,
            CategoriaId = p.CategoriaId, MarcaId = p.MarcaId,
            PrecioCompra = 60m, PrecioVenta = 80m, PorcentajeIVA = 21m,
            StockActual = p.StockActual, StockMinimo = p.StockMinimo,
            Activo = true,
            RowVersion = null
        };

        var result = await _controller.EditAjax(producto.Id, vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        var globalErrors = doc.RootElement.GetProperty("errors").GetProperty("");
        Assert.True(globalErrors.GetArrayLength() > 0);
        Assert.Contains("RowVersion", globalErrors[0].GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditAjax_RowVersionVacia_DevuelveErrorControlado()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);
        var p = await _context.Productos.FindAsync(producto.Id);
        var vm = new ProductoViewModel
        {
            Id = p!.Id, Codigo = p.Codigo, Nombre = p.Nombre,
            CategoriaId = p.CategoriaId, MarcaId = p.MarcaId,
            PrecioCompra = 60m, PrecioVenta = 80m, PorcentajeIVA = 21m,
            StockActual = p.StockActual, StockMinimo = p.StockMinimo,
            Activo = true,
            RowVersion = Array.Empty<byte>()
        };

        var result = await _controller.EditAjax(producto.Id, vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        var globalErrors = doc.RootElement.GetProperty("errors").GetProperty("");
        Assert.True(globalErrors.GetArrayLength() > 0);
    }

    // ─────────────────────────────────────────────────────────────
    // Edit POST (ruta MVC tradicional)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Edit_Post_RowVersionNula_VuelveAVistaConErrorControlado()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);
        var p = await _context.Productos.FindAsync(producto.Id);
        var vm = new ProductoViewModel
        {
            Id = p!.Id, Codigo = p.Codigo, Nombre = p.Nombre,
            CategoriaId = p.CategoriaId, MarcaId = p.MarcaId,
            PrecioCompra = 60m, PrecioVenta = 80m, PorcentajeIVA = 21m,
            StockActual = p.StockActual, StockMinimo = p.StockMinimo,
            Activo = true,
            RowVersion = null
        };

        var result = await _controller.Edit(p.Id, vm);

        Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
        Assert.True(_controller.ModelState.ContainsKey(""));
        var mensajes = _controller.ModelState[""]!.Errors.Select(e => e.ErrorMessage);
        Assert.Contains(mensajes, m => m.Contains("RowVersion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Edit_Post_RowVersionVacia_VuelveAVistaConErrorControlado()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);
        var p = await _context.Productos.FindAsync(producto.Id);
        var vm = new ProductoViewModel
        {
            Id = p!.Id, Codigo = p.Codigo, Nombre = p.Nombre,
            CategoriaId = p.CategoriaId, MarcaId = p.MarcaId,
            PrecioCompra = 60m, PrecioVenta = 80m, PorcentajeIVA = 21m,
            StockActual = p.StockActual, StockMinimo = p.StockMinimo,
            Activo = true,
            RowVersion = Array.Empty<byte>()
        };

        var result = await _controller.Edit(p.Id, vm);

        Assert.IsType<ViewResult>(result);
        Assert.False(_controller.ModelState.IsValid);
        Assert.True(_controller.ModelState.ContainsKey(""));
    }

    [Fact]
    public async Task Edit_Post_Valido_RedireccionaAIndexYPersisteCambios()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);
        var p = await _context.Productos.FindAsync(producto.Id);
        var vm = new ProductoViewModel
        {
            Id = p!.Id, Codigo = p.Codigo, Nombre = "Nombre Actualizado por Test",
            CategoriaId = p.CategoriaId, MarcaId = p.MarcaId,
            PrecioCompra = 60m, PrecioVenta = 80m, PorcentajeIVA = 21m,
            StockActual = p.StockActual, StockMinimo = p.StockMinimo,
            Activo = true,
            RowVersion = p.RowVersion
        };
        _controller.TempData = new StubTempDataDictionaryCtrlTest();

        var result = await _controller.Edit(p.Id, vm);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProductoController.Index), redirect.ActionName);
        var actualizado = await _context.Productos.AsNoTracking().SingleAsync(x => x.Id == p.Id);
        Assert.Equal("Nombre Actualizado por Test", actualizado.Nombre);
    }

    [Fact]
    public async Task EditAjax_ConAlicuotaIVAId_PersistePrecioFinalConPorcentajeDeAlicuota()
    {
        var producto = await SeedProductoAsync(precioVenta: 121m);
        var alicuota = await SeedAlicuotaIVAAsync(10.5m);
        var p = await _context.Productos.FindAsync(producto.Id);
        var vm = new ProductoViewModel
        {
            Id = p!.Id,
            Codigo = p.Codigo,
            Nombre = p.Nombre,
            CategoriaId = p.CategoriaId,
            MarcaId = p.MarcaId,
            PrecioCompra = 60m,
            PrecioVenta = 100m,
            PorcentajeIVA = 21m,
            AlicuotaIVAId = alicuota.Id,
            StockActual = p.StockActual,
            StockMinimo = p.StockMinimo,
            Activo = true,
            RowVersion = p.RowVersion
        };

        var result = await _controller.EditAjax(producto.Id, vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        var entity = doc.RootElement.GetProperty("entity");
        Assert.Equal(110.50m, entity.GetProperty("precioBase").GetDecimal());

        var actualizado = await _context.Productos.AsNoTracking().SingleAsync(x => x.Id == producto.Id);
        Assert.Equal(10.5m, actualizado.PorcentajeIVA);
        Assert.Equal(110.50m, actualizado.PrecioVenta);
        Assert.Equal(alicuota.Id, actualizado.AlicuotaIVAId);
    }

    [Fact]
    public async Task EditAjax_ConPrecioLista_DevuelveMargenBasadoEnPrecioLista()
    {
        var producto = await SeedProductoAsync(precioVenta: 100m);
        var lista = await SeedListaAsync(esPredeterminada: true, nombre: "Contado");
        await SeedPrecioListaAsync(producto.Id, lista.Id, precio: 180m);

        var productoActualizado = await _context.Productos.FindAsync(producto.Id);
        var vm = new ProductoViewModel
        {
            Id = producto.Id,
            Codigo = productoActualizado!.Codigo,
            Nombre = productoActualizado.Nombre,
            CategoriaId = productoActualizado.CategoriaId,
            MarcaId = productoActualizado.MarcaId,
            PrecioCompra = 60m,
            PrecioVenta = 80m,   // sin IVA → 96.80 con 21%
            PorcentajeIVA = 21m,
            StockActual = productoActualizado.StockActual,
            StockMinimo = productoActualizado.StockMinimo,
            Activo = true,
            RowVersion = productoActualizado.RowVersion
        };

        var result = await _controller.EditAjax(producto.Id, vm) as JsonResult;

        Assert.NotNull(result);
        var doc = ParseJson(result!.Value);
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        var entity = doc.RootElement.GetProperty("entity");
        Assert.Equal(180m, entity.GetProperty("precioActual").GetDecimal());
        // (180 - 60) / 60 * 100 = 200.0
        Assert.Equal(200m, entity.GetProperty("margenPorcentaje").GetDecimal());
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    private static JsonDocument ParseJson(object? value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonDocument.Parse(json);
    }

    private static string? Normalize(string? value)
        => value?
            .Replace("ó", "o")
            .Replace("í", "i");

    private static void AssertRedirectUnidades(IActionResult result, int productoId)
    {
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(ProductoController.Unidades), redirect.ActionName);
        Assert.Equal(productoId, redirect.RouteValues!["productoId"]);
    }

    private static decimal BindComisionOrDefault(string? rawComision)
        => DecimalParsingHelper.TryParseFlexibleDecimal(
            rawComision,
            out var value,
            allowMixedSeparators: true)
            ? value
            : 0m;

    private async Task<ProductoViewModel> CrearProductoViewModelParaCrearAsync()
    {
        var (categoria, marca) = await SeedCategoriaYMarcaAsync();
        return new ProductoViewModel
        {
            Codigo = "P" + Guid.NewGuid().ToString("N")[..8],
            Nombre = "Producto ajax comision",
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

    private static ProductoViewModel CrearProductoViewModelParaEditar(Producto producto)
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

    private async Task<Producto> SeedProductoAsync(decimal precioVenta = 100m, int? maxCuotas = null, bool requiereNumeroSerie = false)
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
            Activo = true,
            RequiereNumeroSerie = requiereNumeroSerie,
            MaxCuotasSinInteresPermitidas = maxCuotas
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
    }

    private async Task<ProductoUnidad> SeedProductoUnidadAsync(
        int productoId,
        string codigoInterno,
        string? numeroSerie,
        EstadoUnidad estado)
    {
        var unidad = new ProductoUnidad
        {
            ProductoId = productoId,
            CodigoInternoUnidad = codigoInterno,
            NumeroSerie = numeroSerie,
            Estado = estado,
            UbicacionActual = "Deposito",
            FechaIngreso = DateTime.UtcNow.AddDays(-2),
            FechaVenta = estado == EstadoUnidad.Vendida ? DateTime.UtcNow.AddDays(-1) : null,
            Observaciones = "Obs test"
        };

        _context.ProductoUnidades.Add(unidad);
        await _context.SaveChangesAsync();
        return unidad;
    }

    private async Task SeedMovimientoUnidadAsync(
        int unidadId,
        EstadoUnidad anterior,
        EstadoUnidad nuevo,
        string motivo)
    {
        _context.ProductoUnidadMovimientos.Add(new ProductoUnidadMovimiento
        {
            ProductoUnidadId = unidadId,
            EstadoAnterior = anterior,
            EstadoNuevo = nuevo,
            Motivo = motivo,
            OrigenReferencia = "Test",
            UsuarioResponsable = "testuser",
            FechaCambio = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();
    }

    private async Task<AlicuotaIVA> SeedAlicuotaIVAAsync(decimal porcentaje)
    {
        var code = Guid.NewGuid().ToString("N")[..8];
        var alicuota = new AlicuotaIVA
        {
            Codigo = "IVA" + code,
            Nombre = $"IVA {porcentaje}",
            Porcentaje = porcentaje,
            Activa = true,
            IsDeleted = false
        };

        _context.AlicuotasIVA.Add(alicuota);
        await _context.SaveChangesAsync();
        return alicuota;
    }

    private async Task<ListaPrecio> SeedListaAsync(bool esPredeterminada, string nombre = "Lista-Test")
    {
        var lista = new ListaPrecio
        {
            Codigo = Guid.NewGuid().ToString("N")[..8],
            Nombre = nombre,
            Tipo = TipoListaPrecio.Contado,
            Activa = true,
            EsPredeterminada = esPredeterminada,
            Orden = 1
        };
        _context.ListasPrecios.Add(lista);
        await _context.SaveChangesAsync();
        return lista;
    }

    private async Task SeedPrecioListaAsync(int productoId, int listaId, decimal precio)
    {
        var precioLista = new ProductoPrecioLista
        {
            ProductoId = productoId,
            ListaId = listaId,
            Precio = precio,
            Costo = 60m,
            MargenValor = precio - 60m,
            MargenPorcentaje = ((precio - 60m) / 60m) * 100,
            VigenciaDesde = DateTime.UtcNow.AddDays(-1),
            VigenciaHasta = null,
            EsVigente = true,
            EsManual = true,
            CreadoPor = "test"
        };
        _context.ProductosPrecios.Add(precioLista);
        await _context.SaveChangesAsync();
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TheBuryProyect.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}

// ─────────────────────────────────────────────────────────────────
// Stubs de dependencias para tests de controller
// ─────────────────────────────────────────────────────────────────

file sealed class StubCurrentUserCtrlTest : ICurrentUserService
{
    public string GetUsername() => "testuser";
    public string GetUserId() => "user-test";
    public string GetEmail() => "test@test.com";
    public bool IsAuthenticated() => true;
    public bool IsInRole(string role) => false;
    public bool HasPermission(string modulo, string accion) => true;
    public string GetIpAddress() => "127.0.0.1";
}

file sealed class StubHistoricoPrecioCtrlTest : IPrecioHistoricoService
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

file sealed class StubTempDataDictionaryCtrlTest : Dictionary<string, object?>, ITempDataDictionary
{
    public void Keep() { }
    public void Keep(string key) { }
    public void Load() { }
    public object? Peek(string key) => TryGetValue(key, out var v) ? v : null;
    public void Save() { }
}

file sealed class StubCatalogLookupCtrlTest : ICatalogLookupService
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

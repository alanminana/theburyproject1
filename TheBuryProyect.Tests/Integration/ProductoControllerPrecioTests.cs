using AutoMapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using TheBuryProject.Controllers;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
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

        var precioService = new PrecioService(_context, NullLogger<PrecioService>.Instance, stubUser, config);
        var catalogLookup = new StubCatalogLookupCtrlTest();
        var catalogoService = new CatalogoService(
            catalogLookup, productoService, precioService, resolver,
            NullLogger<CatalogoService>.Instance, stubUser);

        var mapper = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance).CreateMapper();

        _controller = new ProductoController(
            productoService,
            catalogLookup,
            catalogoService,
            NullLogger<ProductoController>.Instance,
            mapper);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            [nameof(ProductoViewModel.ComisionPorcentaje)] = "0"
        });
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
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
    public async Task EditAjax_NormalizaComisionPorcentaje_DesdeRequestForm(string? rawComision, double esperado)
    {
        var producto = await SeedProductoAsync(precioVenta: 121m);
        var productoActualizado = await _context.Productos.FindAsync(producto.Id);
        var vm = CrearProductoViewModelParaEditar(productoActualizado!);
        SetComisionForm(rawComision, agregarErrorModelState: true);

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
        SetComisionForm("abc", agregarErrorModelState: true);

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
        var codigoErrors = doc.RootElement.GetProperty("errors").GetProperty("Codigo");
        Assert.Contains("Ya existe un producto con este codigo", Normalize(codigoErrors[0].GetString()));
        Assert.Equal(1, await _context.Productos.CountAsync(p => p.Codigo == existente.Codigo));
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
    public async Task CreateAjax_NormalizaComisionPorcentaje_DesdeRequestForm(string? rawComision, double esperado)
    {
        var vm = await CrearProductoViewModelParaCrearAsync();
        SetComisionForm(rawComision, agregarErrorModelState: true);

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
        SetComisionForm("abc", agregarErrorModelState: true);

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

    private void SetComisionForm(string? rawComision, bool agregarErrorModelState = false)
    {
        var form = new Dictionary<string, StringValues>();
        if (rawComision is not null)
            form[nameof(ProductoViewModel.ComisionPorcentaje)] = rawComision;

        _controller.ControllerContext.HttpContext.Request.Form = new FormCollection(form);
        _controller.ModelState.Clear();

        if (agregarErrorModelState)
            _controller.ModelState.AddModelError(nameof(ProductoViewModel.ComisionPorcentaje), "Comision invalida");
    }

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

    private async Task<Producto> SeedProductoAsync(decimal precioVenta = 100m, int? maxCuotas = null)
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
            MaxCuotasSinInteresPermitidas = maxCuotas
        };
        _context.Productos.Add(producto);
        await _context.SaveChangesAsync();
        return producto;
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

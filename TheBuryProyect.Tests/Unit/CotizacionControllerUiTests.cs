using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Filters;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public sealed class CotizacionControllerUiTests
{
    [Fact]
    public void CotizacionController_RequiereAutorizacionYPermisoVentasView()
    {
        Assert.Contains(
            typeof(CotizacionController).GetCustomAttributes<AuthorizeAttribute>(),
            a => a.GetType() == typeof(AuthorizeAttribute));

        var permiso = typeof(CotizacionController).GetCustomAttribute<PermisoRequeridoAttribute>();
        Assert.NotNull(permiso);
        Assert.Equal("cotizaciones", permiso.Modulo);
        Assert.Equal("view", permiso.Accion);
    }

    [Fact]
    public void Index_RetornaVistaTwSeparada()
    {
        var controller = CreateController();

        var result = controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Index_tw", view.ViewName);
    }

    [Fact]
    public void Controller_NoDependeDeVentaServiceNiCajaNiStock()
    {
        var constructor = Assert.Single(typeof(CotizacionController).GetConstructors());
        var parameterTypes = constructor.GetParameters().Select(p => p.ParameterType).ToArray();

        Assert.Contains(typeof(ICotizacionService), parameterTypes);
        Assert.Contains(typeof(IProductoService), parameterTypes);
        Assert.Contains(typeof(IClienteService), parameterTypes);
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Venta", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Caja", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(parameterTypes, t => t.Name.Contains("Stock", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void View_ConsumeApiCotizacionYScriptPropio()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Index_tw.cshtml"));

        Assert.Contains("data-simular-url=\"@Url.Content(\"~/api/cotizacion/simular\")\"", view);
        Assert.Contains("data-guardar-url=\"@Url.Content(\"~/api/cotizacion/guardar\")\"", view);
        Assert.Contains("~/js/cotizacion-simulador.js", view);
        Assert.Contains("data-productos-url=\"@Url.Action(\"BuscarProductos\", \"Cotizacion\")\"", view);
        Assert.Contains("data-clientes-url=\"@Url.Action(\"BuscarClientes\", \"Cotizacion\")\"", view);
        Assert.DoesNotContain("venta-create.js", view);
        Assert.DoesNotContain("asp-controller=\"Venta\"", view);
    }

    [Fact]
    public void Script_PosteaCotizacionReadOnlyYNoDependeDeVentaCreate()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-simulador.js"));

        Assert.Contains("JSON.stringify(buildRequest())", script);
        Assert.Contains("opcionSeleccionada: state.opcionSeleccionada", script);
        Assert.Contains("urls.guardar", script);
        Assert.Contains("method: 'POST'", script);
        Assert.Contains("data-cotizacion-medio", script);
        Assert.Contains("productos: state.productos.map", script);
        Assert.DoesNotContain("venta-create", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/api/ventas/BuscarProductos", script);
        Assert.DoesNotContain("/api/ventas/BuscarClientes", script);
    }

    [Fact]
    public void Layout_TieneAccesoSeparadoACotizacion()
    {
        var layout = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("asp-controller=\"Cotizacion\"", layout);
        Assert.Contains("Cotizaciones", layout);
        Assert.Contains("IsActive(\"Cotizacion\")", layout);
    }

    [Fact]
    public void DetallesView_ContieneBotonConversionYScriptConversion()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Detalles_tw.cshtml"));

        Assert.Contains("cotizacion-btn-convertir", view);
        Assert.Contains("data-cotizacion-conversion", view);
        Assert.Contains("data-preview-url=", view);
        Assert.Contains("data-convertir-url=", view);
        Assert.Contains("data-clientes-url=", view);
        Assert.Contains("data-venta-edit-url=", view);
        Assert.Contains("~/js/cotizacion-conversion.js", view);
        Assert.Contains("cotizacion-conversion-modal", view);
        Assert.Contains("cotizacion-btn-confirmar-conversion", view);
        Assert.DoesNotContain("venta-create.js", view);
        Assert.DoesNotContain("asp-controller=\"Venta\"", view);
    }

    [Fact]
    public void DetallesView_BotonConvertir_VerificaPermisoConvert()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Detalles_tw.cshtml"));

        Assert.Contains("TienePermiso(\"cotizaciones\", \"convert\")", view);
    }

    [Fact]
    public void DetallesView_MuestraBadgeParaEstadosNoConvertibles()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Detalles_tw.cshtml"));

        Assert.Contains("ConvertidaAVenta", view);
        Assert.Contains("Cancelada", view);
        Assert.Contains("ya fue convertida a venta", view);
        Assert.Contains("cancelada y no puede convertirse", view);
    }

    [Fact]
    public void ScriptConversion_NoDependeDeVentaCreateNiApiVentas()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-conversion.js"));

        Assert.Contains("data-cotizacion-conversion", script);
        Assert.Contains("urls.preview", script);
        Assert.Contains("urls.convertir", script);
        Assert.Contains("urls.ventaEdit", script);
        Assert.Contains("clienteFaltante", script);
        Assert.Contains("usarPrecioCotizado", script);
        Assert.Contains("confirmarAdvertencias", script);
        Assert.Contains("clienteIdOverride", script);
        Assert.DoesNotContain("venta-create", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/api/ventas/", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VentaService", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScriptConversion_UsaTextContentParaDatosExternos()
    {
        var script = File.ReadAllText(Path.Combine(FindRepoRoot(), "wwwroot", "js", "cotizacion-conversion.js"));

        // Verificar que los datos del servidor se aplican via textContent (no via string interpolacion insegura)
        Assert.Contains("li.textContent = texto", script);
        Assert.Contains("clearChildren", script);
        Assert.Contains("appendItems", script);
    }

    [Fact]
    public async Task Imprimir_CotizacionExistente_DevuelveVistaImprimir_tw()
    {
        var controller = CreateController();

        var result = await controller.Imprimir(42);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Imprimir_tw", view.ViewName);
        var model = Assert.IsType<CotizacionResultado>(view.Model);
        Assert.Equal(42, model.Id);
    }

    [Fact]
    public async Task Imprimir_CotizacionInexistente_DevuelveNotFound()
    {
        var controller = new CotizacionController(
            new StubCotizacionServiceVacio(), new StubProductoService(), new StubClienteService(),
            NullLogger<CotizacionController>.Instance);

        var result = await controller.Imprimir(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void ImprimirView_ContieneBotonImprimirYWindowPrint()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Imprimir_tw.cshtml"));

        Assert.Contains("window.print()", view);
        Assert.Contains("Imprimir", view);
        Assert.Contains("history.back()", view);
    }

    [Fact]
    public void ImprimirView_TieneLayoutNullYNoDependeDeLayout()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Imprimir_tw.cshtml"));

        Assert.Contains("Layout = null", view);
        Assert.DoesNotContain("_Layout", view);
    }

    [Fact]
    public void ImprimirView_NoContieneBotonesOperativos()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Imprimir_tw.cshtml"));

        Assert.DoesNotContain("btn-convertir", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("btn-cancelar", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("asp-action", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CotizacionConversion", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImprimirView_ContieneDisclaimerDeCotizacion()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Imprimir_tw.cshtml"));

        Assert.Contains("sujeta a disponibilidad", view);
        Assert.Contains("Guardar como PDF", view);
    }

    [Fact]
    public void ImprimirView_UsaModeloCotizacionResultado()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Imprimir_tw.cshtml"));

        Assert.Contains("@model CotizacionResultado", view);
        Assert.Contains("Model.Numero", view);
        Assert.Contains("Model.Detalles", view);
        Assert.Contains("Model.TotalBase", view);
    }

    [Fact]
    public void DetallesView_ContieneEnlaceImprimir()
    {
        var view = File.ReadAllText(Path.Combine(FindRepoRoot(), "Views", "Cotizacion", "Detalles_tw.cshtml"));

        Assert.Contains("Imprimir", view);
        Assert.Contains("\"Imprimir\", \"Cotizacion\"", view);
        Assert.Contains("target=\"_blank\"", view);
    }

    private static CotizacionController CreateController() =>
        new(new StubCotizacionService(), new StubProductoService(), new StubClienteService(), NullLogger<CotizacionController>.Instance);

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "TheBuryProyect.csproj")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return dir ?? throw new DirectoryNotFoundException("No se encontro la raiz del repo.");
    }

    private sealed class StubProductoService : IProductoService
    {
        public Task<IEnumerable<Producto>> GetAllAsync() => Task.FromResult<IEnumerable<Producto>>(Array.Empty<Producto>());
        public Task<Producto?> GetByIdAsync(int id) => Task.FromResult<Producto?>(null);
        public Task<IEnumerable<Producto>> GetByCategoriaAsync(int categoriaId) => Task.FromResult<IEnumerable<Producto>>(Array.Empty<Producto>());
        public Task<IEnumerable<Producto>> GetByMarcaAsync(int marcaId) => Task.FromResult<IEnumerable<Producto>>(Array.Empty<Producto>());
        public Task<IEnumerable<Producto>> GetProductosConStockBajoAsync() => Task.FromResult<IEnumerable<Producto>>(Array.Empty<Producto>());
        public Task<Producto> CreateAsync(Producto producto) => Task.FromResult(producto);
        public Task<Producto> UpdateAsync(Producto producto) => Task.FromResult(producto);
        public Task<bool> DeleteAsync(int id) => Task.FromResult(false);
        public Task PrepararPrecioVentaConIvaAsync(Producto producto) => Task.CompletedTask;
        public decimal ObtenerPrecioVentaSinIva(decimal precioVentaConIva, decimal porcentajeIVA) => precioVentaConIva;
        public Task<IEnumerable<Producto>> SearchAsync(string? searchTerm = null, int? categoriaId = null, int? marcaId = null, bool stockBajo = false, bool soloActivos = false, string? orderBy = null, string? orderDirection = "asc") => Task.FromResult<IEnumerable<Producto>>(Array.Empty<Producto>());
        public Task<List<int>> SearchIdsAsync(string? searchTerm = null, int? categoriaId = null, int? marcaId = null, bool stockBajo = false, bool soloActivos = false) => Task.FromResult(new List<int>());
        public Task<IEnumerable<ProductoVentaDto>> BuscarParaVentaAsync(string term, int take = 20, int? categoriaId = null, int? marcaId = null, bool soloConStock = true, decimal? precioMin = null, decimal? precioMax = null) => Task.FromResult<IEnumerable<ProductoVentaDto>>(Array.Empty<ProductoVentaDto>());
        public Task<ProductoPrecioVentaResultado?> ObtenerPrecioVigenteParaVentaAsync(int productoId) => Task.FromResult<ProductoPrecioVentaResultado?>(null);
        public Task<Producto> ActualizarStockAsync(int id, decimal cantidad) => Task.FromResult(new Producto { Id = id });
        public Task<Producto> ActualizarComisionAsync(int id, decimal porcentaje) => Task.FromResult(new Producto { Id = id });
        public Task<bool> ToggleDestacadoAsync(int id) => Task.FromResult(false);
        public Task CambiarTrazabilidadIndividualAsync(int productoId, bool requiereTrazabilidad) => Task.CompletedTask;
        public Task<bool> ExistsCodigoAsync(string codigo, int? excludeId = null) => Task.FromResult(false);
    }

    private sealed class StubCotizacionServiceVacio : ICotizacionService
    {
        public Task<CotizacionResultado> CrearAsync(CotizacionCrearRequest request, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionResultado());

        public Task<CotizacionResultado?> ObtenerAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<CotizacionResultado?>(null);

        public Task<CotizacionListadoResultado> ListarAsync(CotizacionFiltros filtros, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionListadoResultado());

        public Task<CotizacionCancelacionResultado> CancelarAsync(int id, CotizacionCancelacionRequest request, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionCancelacionResultado { CotizacionId = id });

        public Task<CotizacionVencimientoResultado> VencerEmitidasAsync(DateTime fechaReferenciaUtc, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionVencimientoResultado());
    }

    private sealed class StubCotizacionService : ICotizacionService
    {
        public Task<CotizacionResultado> CrearAsync(CotizacionCrearRequest request, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionResultado { Id = 1, Numero = "COT-TEST" });

        public Task<CotizacionResultado?> ObtenerAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<CotizacionResultado?>(new CotizacionResultado { Id = id, Numero = "COT-TEST" });

        public Task<CotizacionListadoResultado> ListarAsync(CotizacionFiltros filtros, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionListadoResultado());

        public Task<CotizacionCancelacionResultado> CancelarAsync(int id, CotizacionCancelacionRequest request, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionCancelacionResultado { Exitoso = true, CotizacionId = id });

        public Task<CotizacionVencimientoResultado> VencerEmitidasAsync(DateTime fechaReferenciaUtc, string usuario, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CotizacionVencimientoResultado { Exitoso = true });
    }

    private sealed class StubClienteService : IClienteService
    {
        public Task<IEnumerable<Cliente>> GetAllAsync() => Task.FromResult<IEnumerable<Cliente>>(Array.Empty<Cliente>());
        public Task<Cliente?> GetByIdAsync(int id) => Task.FromResult<Cliente?>(null);
        public Task<Cliente> CreateAsync(Cliente cliente) => Task.FromResult(cliente);
        public Task<Cliente> UpdateAsync(Cliente cliente) => Task.FromResult(cliente);
        public Task<bool> DeleteAsync(int id) => Task.FromResult(false);
        public Task<IEnumerable<Cliente>> SearchAsync(string? searchTerm = null, string? tipoDocumento = null, bool? soloActivos = null, bool? conCreditosActivos = null, decimal? puntajeMinimo = null, string? orderBy = null, string? orderDirection = null) => Task.FromResult<IEnumerable<Cliente>>(Array.Empty<Cliente>());
        public Task<bool> ExisteDocumentoAsync(string tipoDocumento, string numeroDocumento, int? excludeId = null) => Task.FromResult(false);
        public Task<Cliente?> GetByDocumentoAsync(string tipoDocumento, string numeroDocumento) => Task.FromResult<Cliente?>(null);
        public Task ActualizarPuntajeRiesgoAsync(int clienteId, decimal nuevoPuntaje, string motivo) => Task.CompletedTask;
    }
}

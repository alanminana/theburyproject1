using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// VENTA-UI-04B: paginación server-rendered en memoria de la pestaña Operaciones de
/// Venta/Index. GetAllAsync sigue devolviendo el conjunto filtrado completo (KPIs y
/// tabs lo necesitan); el controller calcula metadata y recorta solo lo que se expone
/// vía ViewBag.Paginacion para Operaciones.
/// </summary>
public class VentaControllerIndexPaginacionTests
{
    [Fact]
    public async Task Index_Pagina1_DevuelvePrimeras20DeOperaciones_YModeloCompleto()
    {
        var ventas = CrearVentas(111);
        var controller = CreateController(new StubVentaService { Ventas = ventas });

        var result = await controller.Index(new VentaFilterViewModel());

        var view = Assert.IsType<ViewResult>(result);
        var modelo = Assert.IsAssignableFrom<IEnumerable<VentaViewModel>>(view.Model);
        Assert.Equal(111, modelo.Count()); // el conjunto completo sigue disponible para KPIs/tabs

        var paginacion = Assert.IsType<PaginatedResult<VentaViewModel>>((object)controller.ViewBag.Paginacion);
        Assert.Equal(111, paginacion.TotalRecords);
        Assert.Equal(1, paginacion.PageNumber);
        Assert.Equal(20, paginacion.PageSize);
        Assert.Equal(6, paginacion.TotalPages);
        Assert.Equal(20, paginacion.Items.Count);
        Assert.Equal(ventas.Take(20).Select(v => v.Id), paginacion.Items.Select(v => v.Id));
        Assert.False(paginacion.HasPreviousPage);
        Assert.True(paginacion.HasNextPage);
    }

    [Fact]
    public async Task Index_Pagina2_DevuelveSiguientes20_SinSolaparConPagina1()
    {
        var ventas = CrearVentas(111);
        var controller = CreateController(new StubVentaService { Ventas = ventas });

        var result = await controller.Index(new VentaFilterViewModel { PageNumber = 2 });

        var view = Assert.IsType<ViewResult>(result);
        var modelo = Assert.IsAssignableFrom<IEnumerable<VentaViewModel>>(view.Model);
        Assert.Equal(111, modelo.Count());

        var paginacion = Assert.IsType<PaginatedResult<VentaViewModel>>((object)controller.ViewBag.Paginacion);
        Assert.Equal(2, paginacion.PageNumber);
        Assert.Equal(20, paginacion.Items.Count);
        Assert.Equal(ventas.Skip(20).Take(20).Select(v => v.Id), paginacion.Items.Select(v => v.Id));

        var idsPagina1 = ventas.Take(20).Select(v => v.Id).ToHashSet();
        Assert.Empty(paginacion.Items.Select(v => v.Id).Where(id => idsPagina1.Contains(id)));
    }

    [Fact]
    public async Task Index_FiltroYPagina_RecortaSobreElConjuntoYaFiltrado()
    {
        // Simula lo que ya hizo VentaService.AplicarFiltros: GetAllAsync devuelve un
        // subconjunto de 25 (no 111). El controller solo debe recortar sobre eso.
        var ventasFiltradas = CrearVentas(25);
        var controller = CreateController(new StubVentaService { Ventas = ventasFiltradas });

        var result = await controller.Index(new VentaFilterViewModel { Estado = EstadoVenta.Confirmada, PageNumber = 2 });

        var view = Assert.IsType<ViewResult>(result);
        var modelo = Assert.IsAssignableFrom<IEnumerable<VentaViewModel>>(view.Model);
        Assert.Equal(25, modelo.Count());

        var paginacion = Assert.IsType<PaginatedResult<VentaViewModel>>((object)controller.ViewBag.Paginacion);
        Assert.Equal(25, paginacion.TotalRecords);
        Assert.Equal(2, paginacion.TotalPages);
        Assert.Equal(2, paginacion.PageNumber);
        Assert.Equal(5, paginacion.Items.Count); // 25 - 20 de la página 1
        Assert.False(paginacion.HasNextPage);
    }

    [Fact]
    public async Task Index_PageNumber999_ClampeaAUltimaPagina()
    {
        var ventas = CrearVentas(111);
        var controller = CreateController(new StubVentaService { Ventas = ventas });

        var result = await controller.Index(new VentaFilterViewModel { PageNumber = 999 });

        Assert.IsType<ViewResult>(result);
        var paginacion = Assert.IsType<PaginatedResult<VentaViewModel>>((object)controller.ViewBag.Paginacion);
        Assert.Equal(6, paginacion.PageNumber); // 111 / 20 => 6 páginas, clampeado desde 999
        Assert.Equal(11, paginacion.Items.Count); // última página: 101-111
        Assert.Equal(ventas.Skip(100).Take(20).Select(v => v.Id), paginacion.Items.Select(v => v.Id));
        Assert.False(paginacion.HasNextPage);
    }

    [Fact]
    public async Task Index_PageNumberMenorA1_ClampeaA1()
    {
        // El clamp del límite inferior lo hace el setter de PaginationViewModel
        // (heredado por VentaFilterViewModel); esta prueba confirma que Index no lo pisa.
        var ventas = CrearVentas(111);
        var controller = CreateController(new StubVentaService { Ventas = ventas });
        var filter = new VentaFilterViewModel { PageNumber = -5 };

        var result = await controller.Index(filter);

        Assert.IsType<ViewResult>(result);
        var paginacion = Assert.IsType<PaginatedResult<VentaViewModel>>((object)controller.ViewBag.Paginacion);
        Assert.Equal(1, paginacion.PageNumber);
        Assert.Equal(ventas.Take(20).Select(v => v.Id), paginacion.Items.Select(v => v.Id));
    }

    [Fact]
    public async Task Index_CeroResultados_NoRompeYPaginaQuedaEn1()
    {
        var controller = CreateController(new StubVentaService { Ventas = new List<VentaViewModel>() });

        var result = await controller.Index(new VentaFilterViewModel());

        var view = Assert.IsType<ViewResult>(result);
        var modelo = Assert.IsAssignableFrom<IEnumerable<VentaViewModel>>(view.Model);
        Assert.Empty(modelo);

        var paginacion = Assert.IsType<PaginatedResult<VentaViewModel>>((object)controller.ViewBag.Paginacion);
        Assert.Equal(0, paginacion.TotalRecords);
        Assert.Equal(1, paginacion.PageNumber);
        Assert.Empty(paginacion.Items);
    }

    [Fact]
    public async Task Index_PageSizeDeQuerystring_SeNormalizaA20()
    {
        // "No exponer selector": aunque llegue otro PageSize por querystring, el
        // controller lo fuerza a 20 antes de paginar.
        var ventas = CrearVentas(111);
        var controller = CreateController(new StubVentaService { Ventas = ventas });

        var result = await controller.Index(new VentaFilterViewModel { PageSize = 50 });

        Assert.IsType<ViewResult>(result);
        var paginacion = Assert.IsType<PaginatedResult<VentaViewModel>>((object)controller.ViewBag.Paginacion);
        Assert.Equal(20, paginacion.PageSize);
        Assert.Equal(20, paginacion.Items.Count);
    }

    private static List<VentaViewModel> CrearVentas(int cantidad)
    {
        var lista = new List<VentaViewModel>();
        for (var i = 1; i <= cantidad; i++)
        {
            lista.Add(new VentaViewModel
            {
                Id = i,
                Numero = $"V-{i:D5}",
                FechaVenta = DateTime.Today.AddDays(-i),
            });
        }
        return lista;
    }

    private static VentaController CreateController(StubVentaService ventaService)
    {
        var httpContext = new DefaultHttpContext();
        var controller = new VentaController(
            ventaService,
            NullLogger<VentaController>.Instance,
            null!, // IFinancialCalculationService — no usado por Index
            null!, // IPrequalificationService — no usado por Index
            null!, // ICreditoService — no usado por Index
            null!, // IDocumentacionService — no usado por Index
            new StubClienteLookupService(),
            null!, // IValidacionVentaService — no usado por Index
            new StubCurrentUserService(),
            new StubCajaService(), // sin apertura activa -> CargarViewBags no se invoca
            null!, // VentaViewBagBuilder — solo se usa con caja abierta
            null!, // IContratoVentaCreditoService — no usado por Index
            null!, // AppDbContext — no usado por Index
            null!, // IRelojComercial — no usado por Index
            null!); // IVentaEnvioService — no usado por Index

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new StubTempDataProvider());
        controller.Url = new StubUrlHelper();
        return controller;
    }

    private sealed class StubVentaService : IVentaService
    {
        public List<VentaViewModel> Ventas { get; set; } = new();

        public Task<List<VentaViewModel>> GetAllAsync(VentaFilterViewModel? filter = null) => Task.FromResult(Ventas);

        public Task<VentaViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<VentaViewModel?> UpdateAsync(int id, VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<VentaViewModel> CreateAsync(VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<bool> ConfirmarVentaAsync(int id) => throw new NotImplementedException();
        public Task<bool> ConfirmarVentaCreditoAsync(int id) => throw new NotImplementedException();
        public Task<bool> CancelarVentaAsync(int id, string motivo) => throw new NotImplementedException();
        public Task AsociarCreditoAVentaAsync(int ventaId, int creditoId) => throw new NotImplementedException();
        public Task<bool> FacturarVentaAsync(int id, FacturaViewModel facturaViewModel) => throw new NotImplementedException();
        public Task<int?> AnularFacturaAsync(int facturaId, string motivo) => throw new NotImplementedException();
        public Task<bool> ValidarStockAsync(int ventaId) => throw new NotImplementedException();
        public Task<bool> SolicitarAutorizacionAsync(int id, string usuarioSolicita, string motivo) => throw new NotImplementedException();
        public Task<bool> AutorizarVentaAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
        public Task<bool> RechazarVentaAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
        public Task<bool> RegistrarExcepcionDocumentalAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
        public Task<bool> RequiereAutorizacionAsync(VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> GuardarDatosTarjetaAsync(int ventaId, DatosTarjetaViewModel datosTarjeta) => throw new NotImplementedException();
        public Task<bool> GuardarDatosChequeAsync(int ventaId, DatosChequeViewModel datosCheque) => throw new NotImplementedException();
        public Task<DatosTarjetaViewModel> CalcularCuotasTarjetaAsync(int tarjetaId, decimal monto, int cuotas) => throw new NotImplementedException();
        public Task<DatosCreditoPersonallViewModel?> ObtenerDatosCreditoVentaAsync(int ventaId) => throw new NotImplementedException();
        public Task<bool> ValidarDisponibilidadCreditoAsync(int creditoId, decimal monto) => throw new NotImplementedException();
        public TheBuryProject.ViewModels.Responses.CalculoTotalesVentaResponse CalcularTotalesPreview(List<TheBuryProject.ViewModels.Requests.DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => throw new NotImplementedException();
        public Task<TheBuryProject.ViewModels.Responses.CalculoTotalesVentaResponse> CalcularTotalesPreviewAsync(List<TheBuryProject.ViewModels.Requests.DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => throw new NotImplementedException();
        public Task<decimal?> GetTotalVentaAsync(int ventaId) => throw new NotImplementedException();
        public Task<bool> PrepararVentaDesdeCotizacionAsync(int id) => throw new NotImplementedException();
    }

    private sealed class StubClienteLookupService : IClienteLookupService
    {
        public Task<List<SelectListItem>> GetClientesSelectListAsync(int? selectedId = null, bool limitarACliente = false)
            => Task.FromResult(new List<SelectListItem>());

        public Task<string?> GetClienteDisplayNameAsync(int clienteId) => Task.FromResult<string?>(null);
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public string GetUsername() => "testuser";
        public string GetUserId() => "user-1";
        public bool IsAuthenticated() => true;
        public string? GetEmail() => "test@example.test";
        public bool IsInRole(string role) => false;
        public bool HasPermission(string modulo, string accion) => false;
        public string? GetIpAddress() => "127.0.0.1";
    }

    private sealed class StubCajaService : ICajaService
    {
        public Task<decimal?> ObtenerUltimoEfectivoCierreAsync(int cajaId) => Task.FromResult<decimal?>(null);

        // Sin caja abierta: Index no debería intentar CargarViewBags (que requiere
        // VentaViewBagBuilder, no stubeado acá).
        public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario) => Task.FromResult<AperturaCaja?>(null);

        public Task<List<Caja>> ObtenerTodasCajasAsync() => throw new NotImplementedException();
        public Task<Caja?> ObtenerCajaPorIdAsync(int id) => throw new NotImplementedException();
        public Task<Caja> CrearCajaAsync(CajaViewModel model) => throw new NotImplementedException();
        public Task<Caja> ActualizarCajaAsync(int id, CajaViewModel model) => throw new NotImplementedException();
        public Task EliminarCajaAsync(int id, byte[]? rowVersion = null) => throw new NotImplementedException();
        public Task<bool> ExisteCodigoCajaAsync(string codigo, int? cajaIdExcluir = null) => throw new NotImplementedException();
        public Task<AperturaCaja> AbrirCajaAsync(AbrirCajaViewModel model, string usuario) => throw new NotImplementedException();
        public Task<AperturaCaja?> ObtenerAperturaActivaAsync(int cajaId) => throw new NotImplementedException();
        public Task<AperturaCaja?> ObtenerAperturaPorIdAsync(int id) => throw new NotImplementedException();
        public Task<List<AperturaCaja>> ObtenerAperturasAbiertasAsync() => throw new NotImplementedException();
        public Task<bool> TieneCajaAbiertaAsync(int cajaId) => throw new NotImplementedException();
        public Task<bool> ExisteAlgunaCajaAbiertaAsync() => throw new NotImplementedException();
        public Task<MovimientoCaja> RegistrarMovimientoAsync(MovimientoCajaViewModel model, string usuario) => throw new NotImplementedException();
        public Task<List<MovimientoCaja>> ObtenerMovimientosDeAperturaAsync(int aperturaId) => throw new NotImplementedException();
        public Task<decimal> CalcularSaldoActualAsync(int aperturaId) => throw new NotImplementedException();
        public Task<decimal> CalcularSaldoRealAsync(int aperturaId) => throw new NotImplementedException();
        public Task<MovimientoCaja> AcreditarMovimientoAsync(int movimientoId, string usuario) => throw new NotImplementedException();
        public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario) => throw new NotImplementedException();
        public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario) => throw new NotImplementedException();
        public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => throw new NotImplementedException();
        public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(int cuotaId, string creditoNumero, int numeroCuota, decimal monto, string medioPago, string usuario) => throw new NotImplementedException();
        public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario) => throw new NotImplementedException();
        public Task<MovimientoCaja?> RegistrarContramovimientoVentaAsync(int ventaId, string ventaNumero, string motivo, string usuario) => throw new NotImplementedException();
        public Task<CierreCaja> CerrarCajaAsync(CerrarCajaViewModel model, string usuario) => throw new NotImplementedException();
        public Task<CierreCaja?> ObtenerCierrePorIdAsync(int id) => throw new NotImplementedException();
        public Task<List<CierreCaja>> ObtenerHistorialCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
        public Task<DetallesAperturaViewModel> ObtenerDetallesAperturaAsync(int aperturaId) => throw new NotImplementedException();
        public Task<ReporteCajaViewModel> GenerarReporteCajaAsync(DateTime fechaDesde, DateTime fechaHasta, int? cajaId = null) => throw new NotImplementedException();
        public Task<HistorialCierresViewModel> ObtenerEstadisticasCierresAsync(int? cajaId = null, DateTime? fechaDesde = null, DateTime? fechaHasta = null) => throw new NotImplementedException();
    }

    private sealed class StubTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext) => $"/Venta/{actionContext.Action}";
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => routeName;
        public string? RouteUrl(UrlRouteContext routeContext) => routeContext.RouteName;
    }
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
/// Bug reportado en vivo: en el paso "Envío" del wizard de edición, con el checkbox
/// "Esta venta tiene envío a domicilio" (TieneEnvio) destildado, el submit igual fallaba
/// con "El domicilio de entrega es requerido" / "El destinatario es requerido". Causa:
/// el panel "envio-datos" sólo se oculta con CSS ("hidden"), los &lt;input&gt;
/// Envio.Destinatario/Envio.Domicilio siguen en el DOM y se postean vacíos, así que el
/// model binder de ASP.NET Core igual construye un VentaEnvioViewModel y dispara sus
/// [Required] aunque TieneEnvio sea false. Fix: VentaController.LimpiarModelStateSegunEnvio
/// limpia esos errores de ModelState (y anula viewModel.Envio) cuando TieneEnvio es false,
/// mismo patrón que LimpiarModelStateSegunTipoPago con DatosTarjeta/DatosCheque.
/// </summary>
public class VentaControllerEditEnvioModelStateTests
{
    [Fact]
    public async Task Edit_TieneEnvioFalse_LimpiaErroresDeEnvioYGuarda()
    {
        var ventaAntes = CreateVentaBase();
        var resultado = CreateVentaBase();
        var ventaService = new StubVentaService { VentaById = ventaAntes, UpdateResult = resultado };
        var controller = CreateController(ventaService);

        var viewModel = CreateViewModelConDetalle(ventaAntes.Id);
        viewModel.TieneEnvio = false;
        viewModel.Envio = new VentaEnvioViewModel { Destinatario = string.Empty, Domicilio = string.Empty };

        // Simula lo que el model binder real agrega al ModelState al bindear los <input>
        // Envio.Destinatario/Envio.Domicilio vacíos (el panel está oculto por CSS, no ausente).
        controller.ModelState.AddModelError("Envio.Destinatario", "El destinatario es requerido");
        controller.ModelState.AddModelError("Envio.Domicilio", "El domicilio de entrega es requerido");

        var result = await controller.Edit(ventaAntes.Id, viewModel);

        // Antes del fix, esto devolvía la vista de edición con esos dos errores en vez de guardar.
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.True(controller.ModelState.IsValid);
        Assert.Null(viewModel.Envio);
    }

    private static VentaViewModel CreateVentaBase() => new()
    {
        Id = 4191,
        ClienteId = 1,
        Estado = EstadoVenta.PendienteRequisitos,
        TipoPago = TipoPago.Efectivo,
        RowVersion = new byte[] { 1, 2, 3 },
    };

    private static VentaViewModel CreateViewModelConDetalle(int id) => new()
    {
        Id = id,
        ClienteId = 1,
        TipoPago = TipoPago.Efectivo,
        RowVersion = new byte[] { 1, 2, 3 },
        Detalles = new List<VentaDetalleViewModel>
        {
            new() { ProductoId = 1, Cantidad = 1, PrecioUnitario = 178_770.90m }
        }
    };

    private static VentaController CreateController(StubVentaService ventaService)
    {
        var httpContext = new DefaultHttpContext();
        var controller = new VentaController(
            ventaService,
            NullLogger<VentaController>.Instance,
            null!, // IFinancialCalculationService — no usado (TipoPago Efectivo)
            null!, // IPrequalificationService — no usado
            null!, // ICreditoService — no usado (no es crédito personal)
            null!, // IDocumentacionService — no usado
            null!, // IClienteLookupService — no usado
            null!, // IValidacionVentaService — no usado
            new StubCurrentUserService(),
            new StubCajaService(),
            null!, // VentaViewBagBuilder — no usado
            null!, // IContratoVentaCreditoService — no usado
            null!, // AppDbContext — no usado
            new TheBuryProject.Tests.Helpers.RelojComercialFake(DateOnly.FromDateTime(DateTime.Today)),
            null!); // IVentaEnvioService — no usado

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new StubTempDataProvider());
        controller.Url = new StubUrlHelper();
        return controller;
    }

    private sealed class StubVentaService : IVentaService
    {
        public VentaViewModel? VentaById { get; set; }
        public VentaViewModel? UpdateResult { get; set; }

        public Task<VentaViewModel?> GetByIdAsync(int id) => Task.FromResult(VentaById);
        public Task<VentaViewModel?> UpdateAsync(int id, VentaViewModel viewModel) => Task.FromResult(UpdateResult);

        public Task<VentaViewModel> CreateAsync(VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<List<VentaViewModel>> GetAllAsync(TheBuryProject.ViewModels.VentaFilterViewModel? filter = null) => throw new NotImplementedException();
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
        public Task<AperturaCaja?> ObtenerAperturaActivaParaUsuarioAsync(string usuario)
            => Task.FromResult<AperturaCaja?>(new AperturaCaja { Id = 1, UsuarioApertura = usuario });

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

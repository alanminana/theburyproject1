using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;
using TheBuryProject.ViewModels.Responses;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Bug reportado: con una excepción documental ya autorizada (motivo cargado en el paso
/// Crédito del wizard, crédito configurado y contrato de venta generado), el submit final
/// "Confirmar operación" pega en VentaController.Edit(POST) — que volvía a exigir
/// documentación real del cliente (DocumentoClienteService) ignorando por completo la
/// excepción ya auditada en Venta.MotivoAutorizacion ("EXCEPCION_DOC|..."), mandando al
/// operador a /DocumentoCliente en vez de a los detalles de la venta.
///
/// Reproducido en vivo con Playwright (venta con excepción documental, crédito configurado
/// y contrato generado, cliente sin documentación real cargada) antes del fix.
/// </summary>
public class VentaControllerEditExcepcionDocumentalTests
{
    [Fact]
    public async Task Edit_ExcepcionDocumentalYaRegistrada_NoRedirigeADocumentoCliente()
    {
        var ventaAntes = CreateVentaCreditoPersonalBase();
        ventaAntes.CreditoId = 55; // ya tenía crédito asociado antes de este edit

        var resultado = CreateVentaCreditoPersonalBase();
        resultado.CreditoId = 55;
        resultado.CreditoEstado = EstadoCredito.Configurado; // -> CreditoConfigurado = true
        resultado.MotivoAutorizacion = $"EXCEPCION_DOC|{DateTime.UtcNow:O}|supervisor|Aprobado por gerencia";

        var documentacionService = new StubDocumentacionService
        {
            Resultado = new DocumentacionCreditoResultado
            {
                DocumentacionCompleta = false,
                MensajeFaltantes = "Faltan documentos: DNI, ReciboSueldo, Servicio"
            }
        };
        var ventaService = new StubVentaService { VentaById = ventaAntes, UpdateResult = resultado };
        var controller = CreateController(ventaService, documentacionService);
        var viewModel = CreateViewModelConDetalle(ventaAntes.Id);

        var result = await controller.Edit(ventaAntes.Id, viewModel);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.NotEqual("DocumentoCliente", redirect.ControllerName);
        Assert.Equal(0, documentacionService.CallCount);
    }

    [Fact]
    public async Task Edit_SinExcepcionDocumental_DocumentacionIncompleta_RedirigeADocumentoCliente()
    {
        // Regresión inversa: sin excepción autorizada, el gate original debe seguir vigente.
        var ventaAntes = CreateVentaCreditoPersonalBase();
        ventaAntes.CreditoId = 55;

        var resultado = CreateVentaCreditoPersonalBase();
        resultado.CreditoId = 55;
        resultado.MotivoAutorizacion = null;

        var documentacionService = new StubDocumentacionService
        {
            Resultado = new DocumentacionCreditoResultado
            {
                DocumentacionCompleta = false,
                MensajeFaltantes = "Faltan documentos: DNI, ReciboSueldo, Servicio"
            }
        };
        var ventaService = new StubVentaService { VentaById = ventaAntes, UpdateResult = resultado };
        var controller = CreateController(ventaService, documentacionService);
        var viewModel = CreateViewModelConDetalle(ventaAntes.Id);

        var result = await controller.Edit(ventaAntes.Id, viewModel);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("DocumentoCliente", redirect.ControllerName);
        Assert.Equal(1, documentacionService.CallCount);
    }

    private static VentaViewModel CreateVentaCreditoPersonalBase() => new()
    {
        Id = 2098,
        ClienteId = 1,
        Estado = EstadoVenta.PendienteFinanciacion,
        TipoPago = TipoPago.CreditoPersonal,
        RowVersion = new byte[] { 1, 2, 3 },
    };

    private static VentaViewModel CreateViewModelConDetalle(int id) => new()
    {
        Id = id,
        ClienteId = 1,
        TipoPago = TipoPago.CreditoPersonal,
        RowVersion = new byte[] { 1, 2, 3 },
        Detalles = new List<VentaDetalleViewModel>
        {
            new() { ProductoId = 1, Cantidad = 1, PrecioUnitario = 178_770.90m }
        }
    };

    private static VentaController CreateController(
        StubVentaService ventaService,
        StubDocumentacionService documentacionService)
    {
        var httpContext = new DefaultHttpContext();
        var controller = new VentaController(
            ventaService,
            NullLogger<VentaController>.Instance,
            null!,
            null!,
            new StubCreditoService(),
            documentacionService,
            null!,
            new StubValidacionVentaService(),
            new StubCurrentUserService(),
            new StubCajaService(),
            null!,
            new StubContratoVentaCreditoService(),
            null!,
            new TheBuryProject.Tests.Helpers.RelojComercialFake(DateOnly.FromDateTime(DateTime.Today)));

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new StubTempDataProvider());
        controller.Url = new StubUrlHelper();
        return controller;
    }

    private sealed class StubDocumentacionService : IDocumentacionService
    {
        public DocumentacionCreditoResultado Resultado { get; set; } = new() { DocumentacionCompleta = true };
        public int CallCount { get; private set; }

        public Task<DocumentacionCreditoResultado> ProcesarDocumentacionVentaAsync(int ventaId, bool crearCreditoSiCompleta = true)
        {
            CallCount++;
            return Task.FromResult(Resultado);
        }
    }

    private sealed class StubVentaService : IVentaService
    {
        public VentaViewModel? VentaById { get; set; }
        public VentaViewModel? UpdateResult { get; set; }

        public Task<VentaViewModel?> GetByIdAsync(int id) => Task.FromResult(VentaById);
        public Task<VentaViewModel?> UpdateAsync(int id, VentaViewModel viewModel) => Task.FromResult(UpdateResult);

        public Task<VentaViewModel> CreateAsync(VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<List<VentaViewModel>> GetAllAsync(VentaFilterViewModel? filter = null) => throw new NotImplementedException();
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
        public CalculoTotalesVentaResponse CalcularTotalesPreview(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => throw new NotImplementedException();
        public Task<CalculoTotalesVentaResponse> CalcularTotalesPreviewAsync(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => throw new NotImplementedException();
        public Task<decimal?> GetTotalVentaAsync(int ventaId) => throw new NotImplementedException();
        public Task<bool> PrepararVentaDesdeCotizacionAsync(int id) => throw new NotImplementedException();
    }

    private sealed class StubValidacionVentaService : IValidacionVentaService
    {
        public Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId) => Task.FromResult(new ValidacionVentaResult());
        public Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto) => throw new NotImplementedException();
        public Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(int clienteId, decimal montoVenta, int? creditoId = null) => throw new NotImplementedException();
        public Task<bool> ClientePuedeRecibirCreditoAsync(int clienteId, decimal montoSolicitado) => throw new NotImplementedException();
        public Task<ResumenCrediticioClienteViewModel> ObtenerResumenCrediticioAsync(int clienteId) => throw new NotImplementedException();
    }

    private sealed class StubCreditoService : ICreditoService
    {
        public Task<CreditoViewModel?> GetByIdAsync(int id) => Task.FromResult<CreditoViewModel?>(
            new CreditoViewModel { Id = id, Estado = EstadoCredito.Configurado });

        public Task<CobroPrimeraCuotaResultado> CobrarPrimeraCuotaAlGenerarAsync(
            int creditoId, string? medioPago = null, string? comprobante = null, string? observaciones = null)
            => throw new NotImplementedException();

        public Task ConfigurarCreditoAsync(ConfiguracionCreditoComando comando) => throw new NotImplementedException();

        public Task<List<CreditoViewModel>> GetAllAsync(CreditoFilterViewModel? filter = null) => throw new NotImplementedException();
        public Task<List<CreditoViewModel>> GetByClienteIdAsync(int clienteId) => throw new NotImplementedException();
        public Task<CreditoViewModel> CreateAsync(CreditoViewModel viewModel) => throw new NotImplementedException();
        public Task<CreditoViewModel> CreatePendienteConfiguracionAsync(int clienteId, decimal montoTotal) => throw new NotImplementedException();
        public Task<bool> UpdateAsync(CreditoViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<bool> AprobarCreditoAsync(int creditoId, string aprobadoPor) => throw new NotImplementedException();
        public Task<bool> RechazarCreditoAsync(int creditoId, string motivo) => throw new NotImplementedException();
        public Task<bool> CancelarCreditoAsync(int creditoId, string motivo) => throw new NotImplementedException();
        public Task<List<CuotaViewModel>> GetCuotasByCreditoAsync(int creditoId) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetCuotaByIdAsync(int cuotaId) => throw new NotImplementedException();
        public Task<bool> PagarCuotaAsync(PagarCuotaViewModel pago) => throw new NotImplementedException();
        public Task<PagoMultipleCuotasResult> PagarCuotasAsync(PagoMultipleCuotasRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> AdelantarCuotaAsync(PagarCuotaViewModel pago) => throw new NotImplementedException();
        public Task<PagoCuotaContextoResultado?> ObtenerContextoAdelantoAsync(int creditoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagoCuotaPreviewResultado?> PrevisualizarAdelantoAsync(AdelantoCuotaComando comando, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagoCuotaResultado?> RegistrarAdelantoAsync(AdelantoCuotaComando comando, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagoMultiplePreviewResultado> PrevisualizarPagoMultipleAsync(int clienteId, List<int> cuotaIds, string medioPago, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetPrimeraCuotaPendienteAsync(int creditoId) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetUltimaCuotaPendienteAsync(int creditoId) => throw new NotImplementedException();
        public Task<List<CuotaViewModel>> GetCuotasVencidasAsync() => throw new NotImplementedException();
        public Task ActualizarEstadoCuotasAsync() => throw new NotImplementedException();
        public Task<bool> RecalcularSaldoCreditoAsync(int creditoId) => throw new NotImplementedException();
        public Task<PagoCuotaContextoResultado?> ObtenerContextoPagoCuotaAsync(int cuotaId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagoCuotaPreviewResultado?> PrevisualizarPagoCuotaAsync(PagoCuotaIndividualComando comando, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PagoCuotaResultado?> RegistrarPagoCuotaIndividualAsync(PagoCuotaIndividualComando comando, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class StubContratoVentaCreditoService : IContratoVentaCreditoService
    {
        public Task<bool> ExisteContratoGeneradoAsync(int ventaId) => Task.FromResult(true);
        public Task<ContratoVentaCredito?> ObtenerContratoPorVentaAsync(int ventaId) => Task.FromResult<ContratoVentaCredito?>(null);
        public Task<ContratoVentaCreditoValidacionResult> ValidarDatosParaGenerarAsync(int ventaId) => throw new NotImplementedException();
        public Task<ContratoVentaCredito> GenerarAsync(int ventaId, string usuario) => throw new NotImplementedException();
        public Task<ContratoVentaCredito> GenerarPdfAsync(int ventaId, string usuario) => throw new NotImplementedException();
        public Task<ContratoVentaCreditoPdfArchivo?> ObtenerPdfAsync(int ventaId) => throw new NotImplementedException();
        public Task<bool> ExistePlantillaActivaAsync() => throw new NotImplementedException();
        public Task<ContratoVentaCredito?> ObtenerContratoPorCreditoAsync(int creditoId) => throw new NotImplementedException();
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
        public Task<Dictionary<int, DateTime>> ObtenerUltimosCierresPorCajaAsync() => throw new NotImplementedException();
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

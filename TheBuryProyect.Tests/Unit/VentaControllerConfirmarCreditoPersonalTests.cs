using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
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
/// Cubre REGLA 4 de VentaController.Confirmar (rama CreditoPersonal): reconocimiento
/// de excepción documental ya autorizada, sea por traza de confirmación
/// ("EXCEPCION_DOC|") o por autorización formal otorgada al crear la venta.
/// </summary>
public class VentaControllerConfirmarCreditoPersonalTests
{
    [Fact]
    public async Task Confirmar_AutorizacionFormalDeCreacionPorDocumentacion_PermiteConfirmar()
    {
        var venta = CreateVentaCreditoPersonalBase();
        venta.RequiereAutorizacion = true;
        venta.EstadoAutorizacion = EstadoAutorizacionVenta.Autorizada;
        venta.MotivoAutorizacion = "autorizado";
        venta.RazonesAutorizacionJson = SerializarRazon(TipoRazonAutorizacion.DocumentacionVencida);

        var ventaService = new StubVentaService { VentaById = venta, ConfirmarCreditoResult = true };
        var validacionService = new StubValidacionVentaService
        {
            Resultado = CrearValidacionSoloDocumentacionFaltante()
        };
        var controller = CreateController(ventaService, validacionService);

        var result = await controller.Confirmar(venta.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(1, ventaService.ConfirmarVentaCreditoCallCount);
        Assert.Equal(
            "Venta confirmada por excepción documental autorizada. Crédito generado con cuotas.",
            controller.TempData["Warning"]);
        Assert.Null(controller.TempData["Error"]);
    }

    [Fact]
    public async Task Confirmar_TrazaExcepcionDocumentalDeConfirmacionPrevia_PermiteConfirmar()
    {
        var venta = CreateVentaCreditoPersonalBase();
        venta.RequiereAutorizacion = false;
        venta.EstadoAutorizacion = EstadoAutorizacionVenta.NoRequiere;
        venta.MotivoAutorizacion = $"EXCEPCION_DOC|{DateTime.Today:yyyy-MM-dd}|otro_usuario|xq quiero";

        var ventaService = new StubVentaService { VentaById = venta, ConfirmarCreditoResult = true };
        var validacionService = new StubValidacionVentaService
        {
            Resultado = CrearValidacionSoloDocumentacionFaltante()
        };
        var controller = CreateController(ventaService, validacionService);

        var result = await controller.Confirmar(venta.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(1, ventaService.ConfirmarVentaCreditoCallCount);
        Assert.Equal(
            "Venta confirmada por excepción documental autorizada. Crédito generado con cuotas.",
            controller.TempData["Warning"]);
    }

    [Fact]
    public async Task Confirmar_SinExcepcionRegistrada_BloqueaConMensajeDeRequisitos()
    {
        var venta = CreateVentaCreditoPersonalBase();
        venta.RequiereAutorizacion = false;
        venta.EstadoAutorizacion = EstadoAutorizacionVenta.NoRequiere;
        venta.MotivoAutorizacion = null;

        var ventaService = new StubVentaService { VentaById = venta };
        var validacionService = new StubValidacionVentaService
        {
            Resultado = CrearValidacionSoloDocumentacionFaltante()
        };
        var controller = CreateController(ventaService, validacionService);

        var result = await controller.Confirmar(venta.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(0, ventaService.ConfirmarVentaCreditoCallCount);
        var error = Assert.IsType<string>(controller.TempData["Error"]);
        Assert.Contains("Faltan documentos", error);
    }

    [Fact]
    public async Task Confirmar_AutorizacionFormalPorOtraRazon_NoBypasseaRequisitosDocumentales()
    {
        // Autorizado, pero por ExcedeCupo (no por documentación) -> no debe habilitar el bypass.
        var venta = CreateVentaCreditoPersonalBase();
        venta.RequiereAutorizacion = true;
        venta.EstadoAutorizacion = EstadoAutorizacionVenta.Autorizada;
        venta.MotivoAutorizacion = "autorizado por excede cupo";
        venta.RazonesAutorizacionJson = SerializarRazon(TipoRazonAutorizacion.ExcedeCupo);

        var ventaService = new StubVentaService { VentaById = venta };
        var validacionService = new StubValidacionVentaService
        {
            Resultado = CrearValidacionSoloDocumentacionFaltante()
        };
        var controller = CreateController(ventaService, validacionService);

        var result = await controller.Confirmar(venta.Id);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(0, ventaService.ConfirmarVentaCreditoCallCount);
        var error = Assert.IsType<string>(controller.TempData["Error"]);
        Assert.Contains("Faltan documentos", error);
    }

    private static VentaViewModel CreateVentaCreditoPersonalBase() => new()
    {
        Id = 1017,
        Estado = EstadoVenta.PendienteRequisitos,
        TipoPago = TipoPago.CreditoPersonal,
        CreditoId = 55,
        FechaConfiguracionCredito = DateTime.Today,
    };

    private static ValidacionVentaResult CrearValidacionSoloDocumentacionFaltante() => new()
    {
        NoViable = true,
        PendienteRequisitos = true,
        RequisitosPendientes = new List<RequisitoPendiente>
        {
            new()
            {
                Tipo = TipoRequisitoPendiente.DocumentacionFaltante,
                Descripcion = "Faltan documentos: DNI, ReciboSueldo, Servicio"
            }
        }
    };

    private static string SerializarRazon(TipoRazonAutorizacion tipo) =>
        System.Text.Json.JsonSerializer.Serialize(new List<RazonAutorizacion>
        {
            new() { Tipo = tipo, Descripcion = "Excepción solicitada en creación: xq quiero" }
        });

    private static VentaController CreateController(
        StubVentaService ventaService,
        StubValidacionVentaService validacionVentaService)
    {
        var httpContext = new DefaultHttpContext();
        var controller = new VentaController(
            ventaService,
            NullLogger<VentaController>.Instance,
            null!,
            null!,
            new StubCreditoService(),
            null!,
            null!,
            validacionVentaService,
            new StubCurrentUserService(),
            new StubCajaService(),
            null!,
            new StubContratoVentaCreditoService(),
            null!);

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new StubTempDataProvider());
        controller.Url = new StubUrlHelper();
        return controller;
    }

    private sealed class StubVentaService : IVentaService
    {
        public VentaViewModel? VentaById { get; set; }
        public bool ConfirmarCreditoResult { get; set; }
        public int ConfirmarVentaCreditoCallCount { get; private set; }

        public Task<VentaViewModel?> GetByIdAsync(int id) => Task.FromResult(VentaById);

        public Task<bool> ConfirmarVentaCreditoAsync(int id)
        {
            ConfirmarVentaCreditoCallCount++;
            return Task.FromResult(ConfirmarCreditoResult);
        }

        public Task<VentaViewModel> CreateAsync(VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<List<VentaViewModel>> GetAllAsync(VentaFilterViewModel? filter = null) => throw new NotImplementedException();
        public Task<VentaViewModel?> UpdateAsync(int id, VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<bool> ConfirmarVentaAsync(int id) => throw new NotImplementedException();
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
        public Task<DatosCreditoPersonallViewModel> CalcularCreditoPersonallAsync(int creditoId, decimal montoAFinanciar, int cuotas, DateTime fechaPrimeraCuota) => throw new NotImplementedException();
        public Task<DatosCreditoPersonallViewModel?> ObtenerDatosCreditoVentaAsync(int ventaId) => throw new NotImplementedException();
        public Task<bool> ValidarDisponibilidadCreditoAsync(int creditoId, decimal monto) => throw new NotImplementedException();
        public CalculoTotalesVentaResponse CalcularTotalesPreview(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => throw new NotImplementedException();
        public Task<CalculoTotalesVentaResponse> CalcularTotalesPreviewAsync(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => throw new NotImplementedException();
        public Task<decimal?> GetTotalVentaAsync(int ventaId) => throw new NotImplementedException();
        public Task<bool> PrepararVentaDesdeCotizacionAsync(int id) => throw new NotImplementedException();
    }

    private sealed class StubValidacionVentaService : IValidacionVentaService
    {
        public ValidacionVentaResult Resultado { get; set; } = new();

        public Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId) => Task.FromResult(Resultado);

        public Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto) => throw new NotImplementedException();
        public Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(int clienteId, decimal montoVenta, int? creditoId = null) => throw new NotImplementedException();
        public Task<bool> ClientePuedeRecibirCreditoAsync(int clienteId, decimal montoSolicitado) => throw new NotImplementedException();
        public Task<ResumenCrediticioClienteViewModel> ObtenerResumenCrediticioAsync(int clienteId) => throw new NotImplementedException();
    }

    private sealed class StubCreditoService : ICreditoService
    {
        public Task<CreditoViewModel?> GetByIdAsync(int id) => Task.FromResult<CreditoViewModel?>(
            new CreditoViewModel { Id = id, Estado = EstadoCredito.Configurado });

        public Task<List<CreditoViewModel>> GetAllAsync(CreditoFilterViewModel? filter = null) => throw new NotImplementedException();
        public Task<List<CreditoViewModel>> GetByClienteIdAsync(int clienteId) => throw new NotImplementedException();
        public Task<CreditoViewModel> CreateAsync(CreditoViewModel viewModel) => throw new NotImplementedException();
        public Task<CreditoViewModel> CreatePendienteConfiguracionAsync(int clienteId, decimal montoTotal) => throw new NotImplementedException();
        public Task<bool> UpdateAsync(CreditoViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<SimularCreditoViewModel> SimularCreditoAsync(SimularCreditoViewModel modelo) => throw new NotImplementedException();
        public Task<bool> AprobarCreditoAsync(int creditoId, string aprobadoPor) => throw new NotImplementedException();
        public Task<bool> RechazarCreditoAsync(int creditoId, string motivo) => throw new NotImplementedException();
        public Task<bool> CancelarCreditoAsync(int creditoId, string motivo) => throw new NotImplementedException();
        public Task<List<CuotaViewModel>> GetCuotasByCreditoAsync(int creditoId) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetCuotaByIdAsync(int cuotaId) => throw new NotImplementedException();
        public Task<bool> PagarCuotaAsync(PagarCuotaViewModel pago) => throw new NotImplementedException();
        public Task<PagoMultipleCuotasResult> PagarCuotasAsync(PagoMultipleCuotasRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> AdelantarCuotaAsync(PagarCuotaViewModel pago) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetPrimeraCuotaPendienteAsync(int creditoId) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetUltimaCuotaPendienteAsync(int creditoId) => throw new NotImplementedException();
        public Task<List<CuotaViewModel>> GetCuotasVencidasAsync() => throw new NotImplementedException();
        public Task ActualizarEstadoCuotasAsync() => throw new NotImplementedException();
        public Task<bool> RecalcularSaldoCreditoAsync(int creditoId) => throw new NotImplementedException();
        public Task ConfigurarCreditoAsync(ConfiguracionCreditoComando comando) => throw new NotImplementedException();
    }

    private sealed class StubContratoVentaCreditoService : IContratoVentaCreditoService
    {
        public Task<bool> ExisteContratoGeneradoAsync(int ventaId) => Task.FromResult(true);

        public Task<ContratoVentaCreditoValidacionResult> ValidarDatosParaGenerarAsync(int ventaId) => throw new NotImplementedException();
        public Task<ContratoVentaCredito> GenerarAsync(int ventaId, string usuario) => throw new NotImplementedException();
        public Task<ContratoVentaCredito> GenerarPdfAsync(int ventaId, string usuario) => throw new NotImplementedException();
        public Task<ContratoVentaCreditoPdfArchivo?> ObtenerPdfAsync(int ventaId) => throw new NotImplementedException();
        public Task<bool> ExistePlantillaActivaAsync() => throw new NotImplementedException();
        public Task<ContratoVentaCredito?> ObtenerContratoPorVentaAsync(int ventaId) => throw new NotImplementedException();
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

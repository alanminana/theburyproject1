using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;
using TheBuryProject.ViewModels.Responses;

namespace TheBuryProject.Tests.Unit;

public class VentaControllerCondicionesPagoErrorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Theory]
    [InlineData("No se puede crear o confirmar la venta con Transferencia. Producto A: El producto bloquea el medio de pago seleccionado.", "Transferencia")]
    [InlineData("No se puede crear o confirmar la venta con TarjetaCredito. Producto B: La tarjeta seleccionada esta bloqueada para este producto.", "tarjeta")]
    [InlineData("Las cuotas seleccionadas (6) superan el maximo permitido para cuotas sin interes: 3. Restriccion efectiva: Producto C.", "cuotas")]
    public async Task CreateAjax_ErrorCondicionesPago_DevuelveJsonControlado(string backendMessage, string expectedText)
    {
        var ventaService = new StubVentaService
        {
            CreateException = new CondicionesPagoVentaException(backendMessage)
        };
        var controller = CreateController(ventaService);

        var result = await controller.CreateAjax(CreateVentaValida());

        var jsonResult = Assert.IsType<JsonResult>(result);
        using var json = ToJson(jsonResult.Value);
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        var message = json.RootElement.GetProperty("message").GetString();
        Assert.Contains("Condiciones de pago del producto", message);
        Assert.Contains(expectedText, message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            message,
            json.RootElement.GetProperty("errors").GetProperty("").EnumerateArray().Single().GetString());
    }

    [Fact]
    public async Task CreateAjax_VentaValida_ConservaFlujoActual()
    {
        var ventaService = new StubVentaService
        {
            CreatedVenta = new VentaViewModel
            {
                Id = 10,
                Numero = "V-10",
                TipoPago = TipoPago.Efectivo
            }
        };
        var controller = CreateController(ventaService);

        var result = await controller.CreateAjax(CreateVentaValida());

        var jsonResult = Assert.IsType<JsonResult>(result);
        using var json = ToJson(jsonResult.Value);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.True(json.RootElement.GetProperty("requiresRedirect").GetBoolean());
        Assert.Contains("V-10", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Confirmar_ErrorCondicionesPago_MuestraMensajeClaro()
    {
        const string backendMessage = "No se puede crear o confirmar la venta con Efectivo. Producto A: El producto bloquea el medio de pago seleccionado.";
        var ventaService = new StubVentaService
        {
            VentaById = new VentaViewModel
            {
                Id = 7,
                Estado = EstadoVenta.Presupuesto,
                TipoPago = TipoPago.Efectivo
            },
            ConfirmarException = new CondicionesPagoVentaException(backendMessage)
        };
        var controller = CreateController(ventaService);

        var result = await controller.Confirmar(7);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        var message = Assert.IsType<string>(controller.TempData["Error"]);
        Assert.Contains("Condiciones de pago del producto", message);
        Assert.Contains("Efectivo", message);
        Assert.Equal(1, ventaService.ConfirmarCallCount);
    }

    [Fact]
    public async Task Confirmar_VentaValida_ConservaFlujoActual()
    {
        var ventaService = new StubVentaService
        {
            VentaById = new VentaViewModel
            {
                Id = 8,
                Estado = EstadoVenta.Presupuesto,
                TipoPago = TipoPago.Efectivo
            },
            ConfirmarResult = true
        };
        var controller = CreateController(ventaService);

        var result = await controller.Confirmar(8);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal("Venta confirmada exitosamente. El stock ha sido descontado.", controller.TempData["Success"]);
    }

    private static VentaViewModel CreateVentaValida() => new()
    {
        ClienteId = 1,
        FechaVenta = DateTime.Today,
        Estado = EstadoVenta.Presupuesto,
        TipoPago = TipoPago.Efectivo,
        Detalles =
        {
            new VentaDetalleViewModel
            {
                ProductoId = 1,
                Cantidad = 1,
                PrecioUnitario = 100m
            }
        }
    };

    private static VentaController CreateController(StubVentaService ventaService)
    {
        var httpContext = new DefaultHttpContext();
        var controller = new VentaController(
            ventaService,
            NullLogger<VentaController>.Instance,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            new StubCurrentUserService(),
            new StubCajaService(),
            null!,
            null!,
            null!);

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        controller.TempData = new TempDataDictionary(httpContext, new StubTempDataProvider());
        controller.Url = new StubUrlHelper();
        return controller;
    }

    private static JsonDocument ToJson(object? value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonDocument.Parse(json);
    }

    private sealed class StubVentaService : IVentaService
    {
        public VentaViewModel? CreatedVenta { get; set; }
        public VentaViewModel? VentaById { get; set; }
        public Exception? CreateException { get; set; }
        public Exception? ConfirmarException { get; set; }
        public bool ConfirmarResult { get; set; }
        public int ConfirmarCallCount { get; private set; }

        public Task<VentaViewModel> CreateAsync(VentaViewModel viewModel)
        {
            if (CreateException != null) throw CreateException;
            return Task.FromResult(CreatedVenta ?? new VentaViewModel { Id = 1, Numero = "V-1", TipoPago = viewModel.TipoPago });
        }

        public Task<VentaViewModel?> GetByIdAsync(int id) => Task.FromResult(VentaById);

        public Task<bool> ConfirmarVentaAsync(int id)
        {
            ConfirmarCallCount++;
            if (ConfirmarException != null) throw ConfirmarException;
            return Task.FromResult(ConfirmarResult);
        }

        public Task<List<VentaViewModel>> GetAllAsync(VentaFilterViewModel? filter = null) => throw new NotImplementedException();
        public Task<VentaViewModel?> UpdateAsync(int id, VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
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
        public Task<DatosCreditoPersonallViewModel> CalcularCreditoPersonallAsync(int creditoId, decimal montoAFinanciar, int cuotas, DateTime fechaPrimeraCuota) => throw new NotImplementedException();
        public Task<DatosCreditoPersonallViewModel?> ObtenerDatosCreditoVentaAsync(int ventaId) => throw new NotImplementedException();
        public Task<bool> ValidarDisponibilidadCreditoAsync(int creditoId, decimal monto) => throw new NotImplementedException();
        public CalculoTotalesVentaResponse CalcularTotalesPreview(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => throw new NotImplementedException();
        public Task<CalculoTotalesVentaResponse> CalcularTotalesPreviewAsync(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => throw new NotImplementedException();
        public Task<decimal?> GetTotalVentaAsync(int ventaId) => throw new NotImplementedException();
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
        public Task<MovimientoCaja?> RegistrarMovimientoVentaAsync(int ventaId, string ventaNumero, decimal monto, TipoPago tipoPago, string usuario) => throw new NotImplementedException();
        public Task<MovimientoCaja?> RegistrarMovimientoAnticipoAsync(int creditoId, string creditoNumero, decimal montoAnticipo, string usuario) => throw new NotImplementedException();
        public Task<AperturaCaja?> ObtenerAperturaActivaParaVentaAsync() => throw new NotImplementedException();
        public Task<MovimientoCaja?> RegistrarMovimientoCuotaAsync(int cuotaId, string creditoNumero, int numeroCuota, decimal monto, string medioPago, string usuario) => throw new NotImplementedException();
        public Task<MovimientoCaja> RegistrarMovimientoDevolucionAsync(int devolucionId, int ventaId, string ventaNumero, string devolucionNumero, decimal monto, string usuario) => throw new NotImplementedException();
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

using Microsoft.AspNetCore.Mvc;
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

public class CreditoControllerSimularPlanVentaTests
{
    [Fact]
    public async Task SimularPlanVenta_UsaSemaforoConfiguradoYMantieneContratoJson()
    {
        var financial = new RecordingFinancialCalculationService();
        var aptitud = new SemaforoOnlyClienteAptitudService(new SemaforoFinancieroViewModel
        {
            RatioVerdeMax = 0.12m,
            RatioAmarilloMax = 0.20m
        });
        var venta = new VentaViewModel { Id = 77, ClienteId = 5, Total = 10_000m, Detalles = new List<VentaDetalleViewModel>() };

        var controller = new CreditoController(
            creditoService: null!,
            financialService: financial,
            configuracionPagoService: new TasaCreditoPersonalConfigService(5m),
            configuracionMoraService: null!,
            ventaService: new StubVentaService(venta),
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            aptitudService: aptitud);

        // ML6.1: con ventaId, el % lo resuelve la tasa única global configurada arriba (ver
        // TasaCreditoPersonalConfigService) — tasaMensual/metodoCalculo/fuenteConfiguracion ya no
        // tienen autoridad, se omiten porque son irrelevantes para lo que este test verifica.
        // ML8: ventaId es obligatorio — sin venta ni productos el service ya no resuelve el
        // escalar legacy, devuelve "contexto insuficiente" (ver CreditoSimulacionVentaService).
        var result = await controller.SimularPlanVenta(
            totalVenta: 10_000m,
            anticipo: 0m,
            cuotas: 10,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: "2026-01-01",
            tasaMensual: null,
            ventaId: venta.Id);

        var json = Assert.IsType<JsonResult>(result);
        var value = json.Value;
        Assert.NotNull(value);

        Assert.Equal(0.12m, financial.ReceivedVerdeMax);
        Assert.Equal(0.20m, financial.ReceivedAmarilloMax);
        Assert.NotNull(value.GetType().GetProperty("semaforoEstado"));
        Assert.NotNull(value.GetType().GetProperty("semaforoMensaje"));
        Assert.NotNull(value.GetType().GetProperty("mostrarMsgIngreso"));
        Assert.NotNull(value.GetType().GetProperty("mostrarMsgAntiguedad"));
    }

    [Fact]
    public async Task SimularPlanVenta_SinTasaRequestYSinConfiguracionGlobal_RetornaBadRequest()
    {
        var venta = new VentaViewModel { Id = 77, ClienteId = 5, Total = 10_000m, Detalles = new List<VentaDetalleViewModel>() };
        var controller = new CreditoController(
            creditoService: null!,
            financialService: new RecordingFinancialCalculationService(),
            configuracionPagoService: new TasaCreditoPersonalConfigService(null),
            configuracionMoraService: null!,
            ventaService: new StubVentaService(venta),
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            aptitudService: null);

        var result = await controller.SimularPlanVenta(
            totalVenta: 10_000m,
            anticipo: 0m,
            cuotas: 10,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: "2026-01-01",
            tasaMensual: null,
            ventaId: venta.Id);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequest.Value);
        var value = badRequest.Value!;
        var error = value.GetType().GetProperty("error")?.GetValue(value)?.ToString();
        Assert.Contains("tasa de inter", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cr", error);
    }

    [Fact]
    public async Task SimularPlanVenta_ConservaNombresJsonUsadosPorJs()
    {
        var venta = new VentaViewModel { Id = 77, ClienteId = 5, Total = 10_000m, Detalles = new List<VentaDetalleViewModel>() };
        var controller = new CreditoController(
            creditoService: null!,
            financialService: new RecordingFinancialCalculationService(),
            configuracionPagoService: new TasaCreditoPersonalConfigService(4.25m),
            configuracionMoraService: null!,
            ventaService: new StubVentaService(venta),
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            aptitudService: null);

        // ML6.1: tasaMensual/metodoCalculo/fuenteConfiguracion ya no tienen autoridad; el 4.25%
        // sale de la tasa única global configurada arriba (este test solo verifica los nombres
        // JSON, no la resolución del %). ML8: ventaId da el contexto obligatorio.
        var result = await controller.SimularPlanVenta(
            totalVenta: 10_000m,
            anticipo: 1_000m,
            cuotas: 6,
            gastosAdministrativos: 250m,
            fechaPrimeraCuota: "2026-07-15",
            tasaMensual: null,
            ventaId: venta.Id);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotNull(json.Value);
        var value = json.Value!;

        Assert.NotNull(value.GetType().GetProperty("montoFinanciado"));
        Assert.NotNull(value.GetType().GetProperty("cuotaEstimada"));
        Assert.NotNull(value.GetType().GetProperty("tasaAplicada"));
        Assert.NotNull(value.GetType().GetProperty("interesTotal"));
        Assert.NotNull(value.GetType().GetProperty("totalAPagar"));
        Assert.NotNull(value.GetType().GetProperty("gastosAdministrativos"));
        Assert.NotNull(value.GetType().GetProperty("totalPlan"));
        Assert.NotNull(value.GetType().GetProperty("fechaPrimerPago"));
        Assert.NotNull(value.GetType().GetProperty("semaforoEstado"));
        Assert.NotNull(value.GetType().GetProperty("semaforoMensaje"));
        Assert.NotNull(value.GetType().GetProperty("mostrarMsgIngreso"));
        Assert.NotNull(value.GetType().GetProperty("mostrarMsgAntiguedad"));

        // ML4: campos nuevos, aditivos (test obligatorio #10 — no se quitó ningún nombre existente).
        Assert.NotNull(value.GetType().GetProperty("totalVenta"));
        Assert.NotNull(value.GetType().GetProperty("anticipo"));
        Assert.NotNull(value.GetType().GetProperty("fuentePorcentaje"));
        Assert.NotNull(value.GetType().GetProperty("cuotas"));
    }

    [Fact]
    public async Task SimularPlanVenta_FechaInvalida_UsaFallbackAlMesSiguiente()
    {
        var financial = new RecordingFinancialCalculationService();
        var antes = DateTime.Today.AddMonths(1).Date;
        var venta = new VentaViewModel { Id = 77, ClienteId = 5, Total = 10_000m, Detalles = new List<VentaDetalleViewModel>() };
        var controller = new CreditoController(
            creditoService: null!,
            financialService: financial,
            configuracionPagoService: new TasaCreditoPersonalConfigService(5m),
            configuracionMoraService: null!,
            ventaService: new StubVentaService(venta),
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            aptitudService: null);

        // ML6.1: tasaMensual ya no tiene autoridad; el 5% sale de la tasa única global configurada
        // arriba (este test solo verifica el fallback de fecha). ML8: ventaId da el contexto
        // obligatorio.
        var result = await controller.SimularPlanVenta(
            totalVenta: 10_000m,
            anticipo: 0m,
            cuotas: 6,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: "fecha-invalida",
            tasaMensual: null,
            ventaId: venta.Id);
        var despues = DateTime.Today.AddMonths(1).Date;

        Assert.IsType<JsonResult>(result);
        Assert.NotNull(financial.ReceivedFechaPrimeraCuota);
        Assert.InRange(financial.ReceivedFechaPrimeraCuota!.Value.Date, antes, despues);
    }

    [Theory]
    [InlineData(-1, 0, 5, "anticipo no puede ser negativo")]
    [InlineData(0, -1, 5, "gastos administrativos no pueden ser negativos")]
    [InlineData(0, 0, -1, "tasa mensual no puede ser negativa")]
    public async Task SimularPlanVenta_RechazaValoresNegativos(
        decimal anticipo, decimal gastos, decimal tasaGlobalConfigurada, string mensaje)
    {
        // ML6.1: tasaMensual del request ya no tiene autoridad, así que el caso "tasa negativa" se
        // ejercita configurando una tasa única global negativa (dato mal cargado), el único camino
        // legítimo que hoy puede producir un tasaVal < 0. ML8: ventaId da el contexto obligatorio
        // (los casos anticipo/gastos negativos rechazan antes de resolverlo, así que no les afecta).
        var venta = new VentaViewModel { Id = 77, ClienteId = 5, Total = 10_000m, Detalles = new List<VentaDetalleViewModel>() };
        var controller = new CreditoController(
            creditoService: null!,
            financialService: new RecordingFinancialCalculationService(),
            configuracionPagoService: new TasaCreditoPersonalConfigService(tasaGlobalConfigurada),
            configuracionMoraService: null!,
            ventaService: new StubVentaService(venta),
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            aptitudService: null);

        var result = await controller.SimularPlanVenta(
            totalVenta: 10_000m,
            anticipo: anticipo,
            cuotas: 6,
            gastosAdministrativos: gastos,
            fechaPrimeraCuota: "2026-07-15",
            tasaMensual: null,
            ventaId: venta.Id);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = badRequest.Value!.GetType().GetProperty("error")?.GetValue(badRequest.Value)?.ToString();
        Assert.Contains(mensaje, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SimularPlanVenta_TasaRequestSinFuenteManualValida_EsIgnoradaYUsaTasaGlobal()
    {
        // I6/ML4: sin FuenteConfiguracion + MetodoCalculo ambos Manual, una tasa mandada por el
        // navegador (ej. DevTools) ya no tiene prioridad — el servidor resuelve la global.
        // ML8: ventaId da el contexto obligatorio.
        var financial = new RecordingFinancialCalculationService();
        var venta = new VentaViewModel { Id = 77, ClienteId = 5, Total = 10_000m, Detalles = new List<VentaDetalleViewModel>() };
        var controller = new CreditoController(
            creditoService: null!,
            financialService: financial,
            configuracionPagoService: new TasaCreditoPersonalConfigService(9m),
            configuracionMoraService: null!,
            ventaService: new StubVentaService(venta),
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            aptitudService: null);

        var result = await controller.SimularPlanVenta(
            totalVenta: 10_000m,
            anticipo: 0m,
            cuotas: 6,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: "2026-07-15",
            tasaMensual: 3.5m,
            ventaId: venta.Id);

        Assert.IsType<JsonResult>(result);
        Assert.Equal(9m, financial.ReceivedTasaMensual);
    }

    [Fact]
    public async Task SimularPlanVenta_TasaManualConFuenteYMetodoManual_YaNoSeHonra_UsaTasaGlobal()
    {
        // ML6.1 cierra el "Test obligatorio #9" anterior: FuenteConfiguracion + MetodoCalculo
        // ambos Manual ya NO conservan la tasa que mandó el operador (rama eliminada del service).
        // ML8: ventaId da el contexto obligatorio.
        var financial = new RecordingFinancialCalculationService();
        var venta = new VentaViewModel { Id = 77, ClienteId = 5, Total = 10_000m, Detalles = new List<VentaDetalleViewModel>() };
        var controller = new CreditoController(
            creditoService: null!,
            financialService: financial,
            configuracionPagoService: new TasaCreditoPersonalConfigService(9m),
            configuracionMoraService: null!,
            ventaService: new StubVentaService(venta),
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            aptitudService: null);

        var result = await controller.SimularPlanVenta(
            totalVenta: 10_000m,
            anticipo: 0m,
            cuotas: 6,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: "2026-07-15",
            tasaMensual: 3.5m,
            metodoCalculo: MetodoCalculoCredito.Manual,
            fuenteConfiguracion: FuenteConfiguracionCredito.Manual,
            ventaId: venta.Id);

        Assert.IsType<JsonResult>(result);
        Assert.Equal(9m, financial.ReceivedTasaMensual);
        Assert.NotEqual(3.5m, financial.ReceivedTasaMensual);
    }

    [Fact]
    public async Task SimularPlanVenta_SinTasaRequest_UsaTasaGlobal()
    {
        var financial = new RecordingFinancialCalculationService();
        // ML8: ventaId da el contexto obligatorio.
        var venta = new VentaViewModel { Id = 77, ClienteId = 5, Total = 10_000m, Detalles = new List<VentaDetalleViewModel>() };
        var controller = new CreditoController(
            creditoService: null!,
            financialService: financial,
            configuracionPagoService: new TasaCreditoPersonalConfigService(8m),
            configuracionMoraService: null!,
            ventaService: new StubVentaService(venta),
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            aptitudService: null);

        var result = await controller.SimularPlanVenta(
            totalVenta: 10_000m,
            anticipo: 0m,
            cuotas: 6,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: "2026-07-15",
            tasaMensual: null,
            ventaId: venta.Id);

        Assert.IsType<JsonResult>(result);
        Assert.Equal(8m, financial.ReceivedTasaMensual);
    }

    [Fact]
    public async Task SimularPlanVenta_SinVentaId_RetornaBadRequestPorContextoInsuficiente()
    {
        // ML8 — Fase 1/2: sin ventaId (y esta acción no expone ProductoIds) no hay contexto contra
        // el cual resolver un plan. Auditado: ningún caller productivo llega acá sin ventaId (toda
        // navegación real de Configurar Venta lo incluye); el service ya no cae al escalar global
        // legacy, devuelve "contexto insuficiente".
        var financial = new RecordingFinancialCalculationService();
        var controller = new CreditoController(
            creditoService: null!,
            financialService: financial,
            configuracionPagoService: new TasaCreditoPersonalConfigService(8m),
            configuracionMoraService: null!,
            ventaService: null!,
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            aptitudService: null);

        var result = await controller.SimularPlanVenta(
            totalVenta: 10_000m,
            anticipo: 0m,
            cuotas: 6,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: "2026-07-15",
            tasaMensual: null);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = badRequest.Value!.GetType().GetProperty("error")?.GetValue(badRequest.Value)?.ToString();
        Assert.Contains("contexto", error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(financial.ReceivedTasaMensual);
    }

    [Fact]
    public async Task SimularPlanVenta_ConVentaId_IgnoraTotalVentaManipuladoYUsaElTotalReal()
    {
        // Tests obligatorios #1/#2/#4 a través del endpoint HTTP completo.
        var financial = new RecordingFinancialCalculationService();
        var venta = new VentaViewModel
        {
            Id = 77,
            ClienteId = 5,
            Total = 20_000m,
            Detalles = new List<VentaDetalleViewModel>()
        };
        var controller = new CreditoController(
            creditoService: null!,
            financialService: financial,
            configuracionPagoService: new TasaCreditoPersonalConfigService(6m),
            configuracionMoraService: null!,
            ventaService: new StubVentaService(venta),
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            aptitudService: null);

        var result = await controller.SimularPlanVenta(
            totalVenta: 999_999m,
            anticipo: 0m,
            cuotas: 6,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: "2026-07-15",
            tasaMensual: null,
            ventaId: 77);

        var json = Assert.IsType<JsonResult>(result);
        var value = json.Value!;
        Assert.Equal(20_000m, value.GetType().GetProperty("totalVenta")!.GetValue(value));
        Assert.Equal(6m, financial.ReceivedTasaMensual);
    }

    // ML6/ML6.1 — Fase 8/Fase 6, test obligatorio: con ventaId (el único contexto real de
    // Configurar Venta) un payload manipulado con metodoCalculo=Manual&fuenteConfiguracion=Manual&
    // tasaMensual=99 no puede pisar el porcentaje del plan, y el JSON debe reportar
    // fuentePorcentaje="Plan" (nunca "Producto"/"Cliente"/"Manual"/"Global").
    [Fact]
    public async Task SimularPlanVenta_ConVentaIdYPayloadManipuladoMetodoFuenteTasa_LosIgnoraYUsaElPlan()
    {
        var financial = new RecordingFinancialCalculationService();
        var venta = new VentaViewModel
        {
            Id = 77,
            ClienteId = 5,
            Total = 10_000m,
            Detalles = new List<VentaDetalleViewModel>()
        };
        var controller = new CreditoController(
            creditoService: null!,
            financialService: financial,
            configuracionPagoService: new TasaCreditoPersonalConfigService(6m),
            configuracionMoraService: null!,
            ventaService: new StubVentaService(venta),
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            aptitudService: null);

        var result = await controller.SimularPlanVenta(
            totalVenta: 999_999m,
            anticipo: 0m,
            cuotas: 6,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: "2026-07-15",
            tasaMensual: 99m,
            ventaId: 77,
            metodoCalculo: MetodoCalculoCredito.Manual,
            fuenteConfiguracion: FuenteConfiguracionCredito.Manual);

        var json = Assert.IsType<JsonResult>(result);
        Assert.NotEqual(99m, financial.ReceivedTasaMensual);
        Assert.Equal(6m, financial.ReceivedTasaMensual);
        var fuente = json.Value!.GetType().GetProperty("fuentePorcentaje")!.GetValue(json.Value);
        Assert.Equal("Plan", fuente);
    }

    private sealed class StubVentaService : IVentaService
    {
        private readonly VentaViewModel? _venta;

        public StubVentaService(VentaViewModel? venta)
        {
            _venta = venta;
        }

        public Task<VentaViewModel?> GetByIdAsync(int id) => Task.FromResult(_venta);

        public Task<decimal?> GetTotalVentaAsync(int ventaId) => throw new NotImplementedException();
        public Task<List<VentaViewModel>> GetAllAsync(VentaFilterViewModel? filter = null) => throw new NotImplementedException();
        public Task<VentaViewModel> CreateAsync(VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<VentaViewModel?> UpdateAsync(int id, VentaViewModel viewModel) => throw new NotImplementedException();
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
    }

    private sealed class RecordingFinancialCalculationService : IFinancialCalculationService
    {
        public decimal? ReceivedVerdeMax { get; private set; }
        public decimal? ReceivedAmarilloMax { get; private set; }
        public decimal? ReceivedTasaMensual { get; private set; }
        public DateTime? ReceivedFechaPrimeraCuota { get; private set; }

        public decimal CalcularCuotaSistemaFrances(decimal monto, decimal tasaMensual, int cuotas) => throw new NotImplementedException();
        public decimal CalcularTotalConInteres(decimal monto, decimal tasaMensual, int cuotas) => throw new NotImplementedException();
        public decimal CalcularCFTEA(decimal totalAPagar, decimal montoInicial, int cuotas) => throw new NotImplementedException();
        public decimal CalcularInteresTotal(decimal monto, decimal tasaMensual, int cuotas) => throw new NotImplementedException();
        public decimal ComputePmt(decimal tasaMensual, int cuotas, decimal monto) => throw new NotImplementedException();
        public decimal ComputeFinancedAmount(decimal total, decimal anticipo) => throw new NotImplementedException();
        public decimal CalcularCFTEADesdeTasa(decimal tasaMensual) => throw new NotImplementedException();

        public SimulacionPlanCreditoDto SimularPlanCredito(
            decimal totalVenta,
            decimal anticipo,
            int cuotas,
            decimal tasaMensual,
            decimal gastosAdministrativos,
            DateTime fechaPrimeraCuota,
            decimal semaforoRatioVerdeMax = 0.08m,
            decimal semaforoRatioAmarilloMax = 0.15m)
        {
            ReceivedVerdeMax = semaforoRatioVerdeMax;
            ReceivedAmarilloMax = semaforoRatioAmarilloMax;
            ReceivedTasaMensual = tasaMensual;
            ReceivedFechaPrimeraCuota = fechaPrimeraCuota;

            return new SimulacionPlanCreditoDto
            {
                MontoFinanciado = totalVenta - anticipo,
                CuotaEstimada = 1_000m,
                TasaAplicada = tasaMensual,
                InteresTotal = 0m,
                TotalAPagar = 10_000m,
                GastosAdministrativos = gastosAdministrativos,
                TotalPlan = 10_000m + gastosAdministrativos,
                FechaPrimerPago = fechaPrimeraCuota,
                SemaforoEstado = "verde",
                SemaforoMensaje = "Condiciones preliminares saludables.",
                MostrarMsgIngreso = false,
                MostrarMsgAntiguedad = false
            };
        }
    }

    private sealed class SemaforoOnlyClienteAptitudService : IClienteAptitudService
    {
        private readonly SemaforoFinancieroViewModel _semaforo;

        public SemaforoOnlyClienteAptitudService(SemaforoFinancieroViewModel semaforo)
        {
            _semaforo = semaforo;
        }

        public Task<SemaforoFinancieroViewModel> GetSemaforoFinancieroAsync() => Task.FromResult(_semaforo);
        public Task UpdateSemaforoFinancieroAsync(SemaforoFinancieroViewModel model) => Task.CompletedTask;

        public Task<AptitudCrediticiaViewModel> EvaluarAptitudAsync(int clienteId, bool guardarResultado = true) => throw new NotImplementedException();
        public Task<AptitudCrediticiaViewModel> EvaluarAptitudSinGuardarAsync(int clienteId) => throw new NotImplementedException();
        public Task<AptitudCrediticiaViewModel?> GetUltimaEvaluacionAsync(int clienteId) => throw new NotImplementedException();
        public Task<(bool EsApto, string? Motivo)> VerificarAptitudParaMontoAsync(int clienteId, decimal monto) => throw new NotImplementedException();
        public Task<AptitudDocumentacionDetalle> EvaluarDocumentacionAsync(int clienteId) => throw new NotImplementedException();
        public Task<AptitudCupoDetalle> EvaluarCupoAsync(int clienteId) => throw new NotImplementedException();
        public Task<AptitudMoraDetalle> EvaluarMoraAsync(int clienteId) => throw new NotImplementedException();
        public Task<ConfiguracionCredito> GetConfiguracionAsync() => throw new NotImplementedException();
        public Task<ConfiguracionCredito> UpdateConfiguracionAsync(ConfiguracionCreditoViewModel viewModel) => throw new NotImplementedException();
        public Task<(bool EstaConfigurando, string? Mensaje)> VerificarConfiguracionAsync() => throw new NotImplementedException();
        public Task<bool> AsignarLimiteCreditoAsync(int clienteId, decimal limite, string? motivo = null) => throw new NotImplementedException();
        public Task<decimal> GetCupoDisponibleAsync(int clienteId) => throw new NotImplementedException();
        public Task<decimal> GetCreditoUtilizadoAsync(int clienteId) => throw new NotImplementedException();
        public Task<ScoringThresholdsViewModel> GetScoringThresholdsAsync() => throw new NotImplementedException();
        public Task UpdateScoringThresholdsAsync(ScoringThresholdsViewModel model) => throw new NotImplementedException();
    }

    private sealed class TasaCreditoPersonalConfigService : IConfiguracionPagoService
    {
        private readonly decimal? _tasa;

        public TasaCreditoPersonalConfigService(decimal? tasa)
        {
            _tasa = tasa;
        }

        public Task<decimal?> ObtenerTasaInteresMensualCreditoPersonalAsync() => Task.FromResult(_tasa);
        public Task<List<ConfiguracionPagoViewModel>> GetAllAsync() => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> GetByTipoPagoAsync(TipoPago tipoPago) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel> CreateAsync(ConfiguracionPagoViewModel viewModel) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> UpdateAsync(int id, ConfiguracionPagoViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<List<ConfiguracionTarjetaViewModel>> GetTarjetasActivasAsync() => throw new NotImplementedException();
        public Task<List<TarjetaActivaVentaResultado>> GetTarjetasActivasParaVentaAsync() => throw new NotImplementedException();
        public Task<ConfiguracionTarjetaViewModel?> GetTarjetaByIdAsync(int id) => throw new NotImplementedException();
        public Task<bool> ValidarDescuento(TipoPago tipoPago, decimal descuento) => throw new NotImplementedException();
        public Task<decimal> CalcularRecargo(TipoPago tipoPago, decimal monto) => throw new NotImplementedException();
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoAsync() => throw new NotImplementedException();
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoActivosAsync() => throw new NotImplementedException();
        public Task GuardarCreditoPersonalAsync(CreditoPersonalConfigViewModel config) => throw new NotImplementedException();
        public Task<ParametrosCreditoCliente> ObtenerParametrosCreditoClienteAsync(int clienteId, decimal tasaGlobal) => throw new NotImplementedException();
        public Task<(int Min, int Max, string Descripcion, string? PerfilNombre)> ResolverRangoCuotasAsync(
            MetodoCalculoCredito metodo,
            int? perfilId,
            int? clienteId) => throw new NotImplementedException();
        public Task<MaxCuotasSinInteresResultado?> ObtenerMaxCuotasSinInteresEfectivoAsync(
            int tarjetaId,
            IEnumerable<int> productoIds) => throw new NotImplementedException();
        public Task<List<MontoPorPuntajeCreditoViewModel>> GetMontosPorPuntajeAsync() => Task.FromResult(new List<MontoPorPuntajeCreditoViewModel>());
        public Task<(bool Ok, List<string> Errores)> GuardarMontosPorPuntajeAsync(List<MontoPorPuntajeCreditoViewModel> items, string usuario) => Task.FromResult((true, new List<string>()));
        // ML8: este doble ejercita la resolución vía escalar único global (sin tabla de planes por
        // cantidad) — igual que ConfiguracionPagoService cuando no hay ninguna cuota global
        // configurada. Antes emulaba una tabla de planes 1..24 con TasaMensual = _tasa ?? 0m, pero
        // eso no distinguía "tasa no configurada" (_tasa null) de "tasa 0%" (ambos daban 0m en el
        // plan). Los tests que sí prueban la tabla de planes por cantidad tienen su propio
        // PlanesCreditoPersonalResultado explícito vía StubVentaService + VentaConfiguracionPagoService.
        public Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalAsync() => Task.FromResult(new List<CuotaCreditoPersonalViewModel>());
        public Task<List<CuotaCreditoPersonalViewModel>> GetCuotasCreditoPersonalActivasAsync() => Task.FromResult(new List<CuotaCreditoPersonalViewModel>());
        public Task<PlanesCreditoPersonalResultado> ResolverPlanesCreditoPersonalAsync(IEnumerable<int> productoIds) =>
            Task.FromResult(PlanesCreditoPersonalResultado.SinTablaDePlanes());
        public Task<(bool Ok, List<string> Errores)> GuardarCuotasCreditoPersonalAsync(List<CuotaCreditoPersonalViewModel> items, string usuario) => Task.FromResult((true, new List<string>()));
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

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

        var controller = new CreditoController(
            creditoService: null!,
            evaluacionService: null!,
            financialService: financial,
            configuracionPagoService: null!,
            configuracionMoraService: null!,
            ventaService: null!,
            logger: NullLogger<CreditoController>.Instance,
            creditoDisponibleService: null!,
            currentUser: null!,
            viewBagBuilder: null!,
            contratoVentaCreditoService: null!,
            aptitudService: aptitud);

        var result = await controller.SimularPlanVenta(
            totalVenta: 10_000m,
            anticipo: 0m,
            cuotas: 10,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: "2026-01-01",
            tasaMensual: 0m);

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
        var controller = new CreditoController(
            creditoService: null!,
            evaluacionService: null!,
            financialService: new RecordingFinancialCalculationService(),
            configuracionPagoService: new TasaCreditoPersonalConfigService(null),
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
            cuotas: 10,
            gastosAdministrativos: 0m,
            fechaPrimeraCuota: "2026-01-01",
            tasaMensual: null);

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
        var controller = new CreditoController(
            creditoService: null!,
            evaluacionService: null!,
            financialService: new RecordingFinancialCalculationService(),
            configuracionPagoService: null!,
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
            anticipo: 1_000m,
            cuotas: 6,
            gastosAdministrativos: 250m,
            fechaPrimeraCuota: "2026-07-15",
            tasaMensual: 4.25m);

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
    }

    [Fact]
    public async Task SimularPlanVenta_FechaInvalida_UsaFallbackAlMesSiguiente()
    {
        var financial = new RecordingFinancialCalculationService();
        var antes = DateTime.Today.AddMonths(1).Date;
        var controller = new CreditoController(
            creditoService: null!,
            evaluacionService: null!,
            financialService: financial,
            configuracionPagoService: null!,
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
            fechaPrimeraCuota: "fecha-invalida",
            tasaMensual: 5m);
        var despues = DateTime.Today.AddMonths(1).Date;

        Assert.IsType<JsonResult>(result);
        Assert.NotNull(financial.ReceivedFechaPrimeraCuota);
        Assert.InRange(financial.ReceivedFechaPrimeraCuota!.Value.Date, antes, despues);
    }

    [Theory]
    [InlineData(-1, 0, 5, "anticipo no puede ser negativo")]
    [InlineData(0, -1, 5, "gastos administrativos no pueden ser negativos")]
    [InlineData(0, 0, -1, "tasa mensual no puede ser negativa")]
    public async Task SimularPlanVenta_RechazaValoresNegativos(decimal anticipo, decimal gastos, decimal tasa, string mensaje)
    {
        var controller = new CreditoController(
            creditoService: null!,
            evaluacionService: null!,
            financialService: new RecordingFinancialCalculationService(),
            configuracionPagoService: null!,
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
            anticipo: anticipo,
            cuotas: 6,
            gastosAdministrativos: gastos,
            fechaPrimeraCuota: "2026-07-15",
            tasaMensual: tasa);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = badRequest.Value!.GetType().GetProperty("error")?.GetValue(badRequest.Value)?.ToString();
        Assert.Contains(mensaje, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SimularPlanVenta_TasaRequestTienePrioridadSobreTasaGlobal()
    {
        var financial = new RecordingFinancialCalculationService();
        var controller = new CreditoController(
            creditoService: null!,
            evaluacionService: null!,
            financialService: financial,
            configuracionPagoService: new TasaCreditoPersonalConfigService(9m),
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
            tasaMensual: 3.5m);

        Assert.IsType<JsonResult>(result);
        Assert.Equal(3.5m, financial.ReceivedTasaMensual);
    }

    [Fact]
    public async Task SimularPlanVenta_SinTasaRequest_UsaTasaGlobal()
    {
        var financial = new RecordingFinancialCalculationService();
        var controller = new CreditoController(
            creditoService: null!,
            evaluacionService: null!,
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

        Assert.IsType<JsonResult>(result);
        Assert.Equal(8m, financial.ReceivedTasaMensual);
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
        public Task GuardarConfiguracionesModalAsync(IReadOnlyList<ConfiguracionPagoViewModel> configuraciones) => throw new NotImplementedException();
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
    }
}

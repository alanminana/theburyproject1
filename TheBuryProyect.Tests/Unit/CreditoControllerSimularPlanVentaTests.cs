using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
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

    private sealed class RecordingFinancialCalculationService : IFinancialCalculationService
    {
        public decimal? ReceivedVerdeMax { get; private set; }
        public decimal? ReceivedAmarilloMax { get; private set; }

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
}

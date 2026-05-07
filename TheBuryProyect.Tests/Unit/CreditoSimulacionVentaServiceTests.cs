using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Tests.Unit;

public sealed class CreditoSimulacionVentaServiceTests
{
    [Fact]
    public async Task Simular_FechaInvalida_UsaFallbackAlMesSiguiente()
    {
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial);
        var antes = DateTime.Today.AddMonths(1).Date;

        var result = await service.SimularAsync(Request(fechaPrimeraCuota: "fecha-invalida"));
        var despues = DateTime.Today.AddMonths(1).Date;

        Assert.True(result.EsValido);
        Assert.NotNull(financial.ReceivedFechaPrimeraCuota);
        Assert.InRange(financial.ReceivedFechaPrimeraCuota!.Value.Date, antes, despues);
    }

    [Theory]
    [InlineData(-1, 0, 5, "anticipo no puede ser negativo")]
    [InlineData(0, -1, 5, "gastos administrativos no pueden ser negativos")]
    [InlineData(0, 0, -1, "tasa mensual no puede ser negativa")]
    public async Task Simular_RechazaValoresNegativos(decimal anticipo, decimal gastos, decimal tasa, string mensaje)
    {
        var service = CrearService(new RecordingFinancialCalculationService());

        var result = await service.SimularAsync(Request(
            anticipo: anticipo,
            gastosAdministrativos: gastos,
            tasaMensual: tasa));

        Assert.False(result.EsValido);
        Assert.Contains(mensaje, result.Error!.error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Simular_TasaRequestTienePrioridadSobreTasaGlobal()
    {
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, new TasaCreditoPersonalConfigService(9m));

        var result = await service.SimularAsync(Request(tasaMensual: 3.5m));

        Assert.True(result.EsValido);
        Assert.Equal(3.5m, financial.ReceivedTasaMensual);
    }

    [Fact]
    public async Task Simular_SinTasaRequest_UsaTasaGlobal()
    {
        var financial = new RecordingFinancialCalculationService();
        var service = CrearService(financial, new TasaCreditoPersonalConfigService(8m));

        var result = await service.SimularAsync(Request(tasaMensual: null));

        Assert.True(result.EsValido);
        Assert.Equal(8m, financial.ReceivedTasaMensual);
    }

    [Fact]
    public async Task Simular_SinTasaRequestYSinConfiguracionGlobal_RetornaInvalido()
    {
        var service = CrearService(
            new RecordingFinancialCalculationService(),
            new TasaCreditoPersonalConfigService(null));

        var result = await service.SimularAsync(Request(tasaMensual: null));

        Assert.False(result.EsValido);
        Assert.Contains("tasa de inter", result.Error!.error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cr", result.Error.error);
    }

    [Fact]
    public async Task Simular_DevuelveCalculoFinancieroEsperado()
    {
        var service = CrearService(new RecordingFinancialCalculationService());

        var result = await service.SimularAsync(Request(
            totalVenta: 10_000m,
            anticipo: 1_000m,
            cuotas: 6,
            gastosAdministrativos: 250m,
            fechaPrimeraCuota: "2026-07-15",
            tasaMensual: 4.25m));

        Assert.True(result.EsValido);
        Assert.NotNull(result.Plan);
        Assert.Equal(9_000m, result.Plan!.montoFinanciado);
        Assert.Equal(1_000m, result.Plan.cuotaEstimada);
        Assert.Equal(4.25m, result.Plan.tasaAplicada);
        Assert.Equal(250m, result.Plan.gastosAdministrativos);
        Assert.Equal(10_250m, result.Plan.totalPlan);
        Assert.Equal("2026-07-15", result.Plan.fechaPrimerPago);
    }

    [Fact]
    public async Task Simular_DevuelveSemaforoConMismaEstructura()
    {
        var financial = new RecordingFinancialCalculationService();
        var aptitud = new SemaforoOnlyClienteAptitudService(new SemaforoFinancieroViewModel
        {
            RatioVerdeMax = 0.12m,
            RatioAmarilloMax = 0.20m
        });
        var service = CrearService(financial, aptitudService: aptitud);

        var result = await service.SimularAsync(Request());

        Assert.True(result.EsValido);
        Assert.Equal(0.12m, financial.ReceivedVerdeMax);
        Assert.Equal(0.20m, financial.ReceivedAmarilloMax);
        Assert.NotNull(result.Plan);
        Assert.Equal("verde", result.Plan!.semaforoEstado);
        Assert.Equal("Condiciones preliminares saludables.", result.Plan.semaforoMensaje);
        Assert.False(result.Plan.mostrarMsgIngreso);
        Assert.False(result.Plan.mostrarMsgAntiguedad);
    }

    private static CreditoSimulacionVentaService CrearService(
        RecordingFinancialCalculationService financial,
        IConfiguracionPagoService? configuracionPagoService = null,
        IClienteAptitudService? aptitudService = null) =>
        new(financial, configuracionPagoService, aptitudService);

    private static CreditoSimulacionVentaRequest Request(
        decimal totalVenta = 10_000m,
        decimal? anticipo = 0m,
        int cuotas = 6,
        decimal? gastosAdministrativos = 0m,
        string? fechaPrimeraCuota = "2026-07-15",
        decimal? tasaMensual = 5m) =>
        new()
        {
            TotalVenta = totalVenta,
            Anticipo = anticipo,
            Cuotas = cuotas,
            GastosAdministrativos = gastosAdministrativos,
            FechaPrimeraCuota = fechaPrimeraCuota,
            TasaMensual = tasaMensual
        };

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

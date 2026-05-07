using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services;

public sealed class CreditoSimulacionVentaService : ICreditoSimulacionVentaService
{
    private const string TasaGlobalNoConfigurada =
        "La tasa de inter\u00e9s de Cr\u00e9dito Personal no est\u00e1 configurada. " +
        "Configure el valor en Administraci\u00f3n \u2192 Tipos de Pago.";

    private readonly IFinancialCalculationService _financialService;
    private readonly IConfiguracionPagoService? _configuracionPagoService;
    private readonly IClienteAptitudService? _aptitudService;

    public CreditoSimulacionVentaService(
        IFinancialCalculationService financialService,
        IConfiguracionPagoService? configuracionPagoService,
        IClienteAptitudService? aptitudService = null)
    {
        _financialService = financialService;
        _configuracionPagoService = configuracionPagoService;
        _aptitudService = aptitudService;
    }

    public async Task<CreditoSimulacionVentaResultado> SimularAsync(
        CreditoSimulacionVentaRequest request,
        CancellationToken cancellationToken = default)
    {
        var anticipoVal = request.Anticipo ?? 0m;
        var gastosVal = request.GastosAdministrativos ?? 0m;

        decimal tasaVal;
        if (request.TasaMensual.HasValue)
        {
            tasaVal = request.TasaMensual.Value;
        }
        else
        {
            var tasaConfig = _configuracionPagoService is null
                ? null
                : await _configuracionPagoService.ObtenerTasaInteresMensualCreditoPersonalAsync();

            if (tasaConfig == null)
                return CreditoSimulacionVentaResultado.Invalido(TasaGlobalNoConfigurada);

            tasaVal = tasaConfig.Value;
        }

        if (request.TotalVenta <= 0)
            return CreditoSimulacionVentaResultado.Invalido("El monto total de la venta debe ser mayor a cero.");
        if (anticipoVal < 0)
            return CreditoSimulacionVentaResultado.Invalido("El anticipo no puede ser negativo.");
        if (request.Cuotas <= 0)
            return CreditoSimulacionVentaResultado.Invalido("Ingres\u00e1 una cantidad de cuotas mayor a cero.");
        if (tasaVal < 0)
            return CreditoSimulacionVentaResultado.Invalido("La tasa mensual no puede ser negativa.");
        if (gastosVal < 0)
            return CreditoSimulacionVentaResultado.Invalido("Los gastos administrativos no pueden ser negativos.");

        var fecha = DateTime.TryParse(request.FechaPrimeraCuota, out var parsed)
            ? parsed
            : DateTime.Today.AddMonths(1);

        cancellationToken.ThrowIfCancellationRequested();

        var semaforo = _aptitudService != null
            ? await _aptitudService.GetSemaforoFinancieroAsync()
            : new SemaforoFinancieroViewModel();

        var plan = _financialService.SimularPlanCredito(
            request.TotalVenta,
            anticipoVal,
            request.Cuotas,
            tasaVal,
            gastosVal,
            fecha,
            semaforo.RatioVerdeMax,
            semaforo.RatioAmarilloMax);

        return CreditoSimulacionVentaResultado.Valido(new CreditoSimulacionVentaJson
        {
            montoFinanciado       = plan.MontoFinanciado,
            cuotaEstimada         = plan.CuotaEstimada,
            tasaAplicada          = plan.TasaAplicada,
            interesTotal          = plan.InteresTotal,
            totalAPagar           = plan.TotalAPagar,
            gastosAdministrativos = plan.GastosAdministrativos,
            totalPlan             = plan.TotalPlan,
            fechaPrimerPago       = plan.FechaPrimerPago.ToString("yyyy-MM-dd"),
            semaforoEstado        = plan.SemaforoEstado,
            semaforoMensaje       = plan.SemaforoMensaje,
            mostrarMsgIngreso     = plan.MostrarMsgIngreso,
            mostrarMsgAntiguedad  = plan.MostrarMsgAntiguedad
        });
    }
}

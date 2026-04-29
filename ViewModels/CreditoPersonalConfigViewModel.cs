using System.ComponentModel.DataAnnotations;

namespace TheBuryProject.ViewModels;

/// <summary>
/// ViewModel para recibir configuración completa de crédito personal
/// </summary>
public class CreditoPersonalConfigViewModel
{
    public DefaultsGlobalesViewModel? DefaultsGlobales { get; set; }
    public List<PerfilCreditoViewModel>? Perfiles { get; set; }
    public ScoringThresholdsViewModel? ScoringThresholds { get; set; }
    public SemaforoFinancieroViewModel? SemaforoFinanciero { get; set; }
}

public class DefaultsGlobalesViewModel
{
    public decimal TasaMensual { get; set; }
    public decimal GastosAdministrativos { get; set; }
    public int MinCuotas { get; set; }
    public int MaxCuotas { get; set; }
}

/// <summary>
/// Umbrales del semáforo visual de simulación financiera.
/// Se calculan sobre cuota / monto financiado; no forman parte del scoring crediticio.
/// </summary>
public class SemaforoFinancieroViewModel
{
    [Range(0.01, 0.99, ErrorMessage = "Debe estar entre 0.01 y 0.99.")]
    public decimal RatioVerdeMax { get; set; } = 0.08m;

    [Range(0.01, 0.99, ErrorMessage = "Debe estar entre 0.01 y 0.99.")]
    public decimal RatioAmarilloMax { get; set; } = 0.15m;
}

/// <summary>
/// Umbrales de negocio del motor de scoring de evaluación crediticia.
/// Todos los valores se persisten en ConfiguracionCredito.
/// </summary>
public class ScoringThresholdsViewModel
{
    /// <summary>Puntaje mínimo de riesgo del cliente (escala 0–10). Por debajo → rechazo directo.</summary>
    [Range(0.1, 10, ErrorMessage = "Debe estar entre 0.1 y 10.")]
    public decimal PuntajeRiesgoMinimo { get; set; } = 3.0m;

    /// <summary>Puntaje de riesgo a partir del cual el resultado es "Bueno" (banda media, escala 0–10).</summary>
    [Range(0.1, 10, ErrorMessage = "Debe estar entre 0.1 y 10.")]
    public decimal PuntajeRiesgoMedio { get; set; } = 5.0m;

    /// <summary>Puntaje de riesgo a partir del cual el resultado es "Excelente" (banda alta, escala 0–10).</summary>
    [Range(0.1, 10, ErrorMessage = "Debe estar entre 0.1 y 10.")]
    public decimal PuntajeRiesgoExcelente { get; set; } = 7.0m;

    /// <summary>Relación cuota/ingreso máxima aceptable (0.01–0.99). Ej: 0.35 = 35 %.</summary>
    [Range(0.01, 0.99, ErrorMessage = "Debe estar entre 0.01 y 0.99.")]
    public decimal RelacionCuotaIngresoMax { get; set; } = 0.35m;

    /// <summary>Umbral cuota/ingreso por debajo del cual la capacidad de pago es "Excelente".</summary>
    [Range(0.01, 0.99, ErrorMessage = "Debe estar entre 0.01 y 0.99.")]
    public decimal UmbralCuotaIngresoBajo { get; set; } = 0.25m;

    /// <summary>Umbral cuota/ingreso por encima del cual la capacidad de pago es "Insuficiente".</summary>
    [Range(0.01, 0.99, ErrorMessage = "Debe estar entre 0.01 y 0.99.")]
    public decimal UmbralCuotaIngresoAlto { get; set; } = 0.45m;

    /// <summary>Monto solicitado a partir del cual se exige garante.</summary>
    [Range(1, 10_000_000, ErrorMessage = "Debe estar entre 1 y 10 000 000.")]
    public decimal MontoRequiereGarante { get; set; } = 500_000m;

    /// <summary>Puntaje mínimo (0–100) para resultado Aprobado. Debe ser mayor que el umbral de análisis.</summary>
    [Range(1, 100, ErrorMessage = "Debe estar entre 1 y 100.")]
    public decimal PuntajeMinimoParaAprobacion { get; set; } = 70m;

    /// <summary>Puntaje mínimo (0–100) para resultado Requiere Análisis. Por debajo → Rechazado.</summary>
    [Range(0, 99, ErrorMessage = "Debe estar entre 0 y 99.")]
    public decimal PuntajeMinimoParaAnalisis { get; set; } = 50m;
}

using System.ComponentModel.DataAnnotations;
using TheBuryProject.ViewModels.Punitorio;

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
    public List<ClienteCreditoLimiteItemViewModel> LimitesPorPuntaje { get; set; } = new();
    public List<MontoPorPuntajeCreditoViewModel> MontosPorPuntaje { get; set; } = new();
    public List<CuotaCreditoPersonalViewModel> CuotasCreditoPersonal { get; set; } = new();

    /// <summary>
    /// PUN-ML8: estado vigente/próximo/historial de <c>ConfiguracionPunitorio</c> y el form de
    /// alta de nueva versión. Nunca se postea junto con el resto de este ViewModel — la pestaña
    /// "Punitorios por mora" tiene su propio <c>&lt;form&gt;</c> y su propia acción de POST
    /// (<c>CrearVersionPunitorio</c>), con permisos distintos de <c>configuracion.update</c>.
    /// </summary>
    public ConfiguracionPunitorioPageViewModel? Punitorios { get; set; }
}

/// <summary>
/// Recargo TOTAL (no tasa mensual) y disponibilidad de Crédito Personal (fuente Global) por
/// cantidad de cuotas. El nombre de la propiedad (<c>TasaMensual</c>) es legacy y se conserva
/// por compatibilidad de binding; representa un porcentaje aplicado una sola vez sobre el
/// saldo financiado, nunca mensual ni compuesto. Si la lista queda vacía, Crédito Personal no
/// ofrece cuotas (sin fallback a rango).
/// </summary>
public class CuotaCreditoPersonalViewModel
{
    public int Id { get; set; }

    [Range(1, 120, ErrorMessage = "La cantidad de cuotas debe estar entre 1 y 120.")]
    public int CantidadCuotas { get; set; }

    /// <summary>
    /// Porcentaje de recargo TOTAL de esta cantidad de cuotas. Este ViewModel se reutiliza en dos
    /// contextos con semántica distinta:
    /// <list type="bullet">
    /// <item>Tabla global (<c>ConfiguracionCreditoPersonalCuota</c>, vía
    /// <c>ConfiguracionPagoController.CreditoPersonal</c>): campo editable y autoritativo — null
    /// NO hereda el recargo único legacy de <c>ConfiguracionPago</c> (dejó de ser fallback desde
    /// ML2.1), se persiste tal cual. 0 = sin recargo (0 % explícito, válido); X = recargo del plan.</item>
    /// <item>Planes de producto (<c>ProductoCreditoPersonalCuota</c>, vía
    /// <c>ProductoController</c> Create/Edit — ML5): SOLO LECTURA, informativo. Sale siempre del
    /// plan global vigente para esa cantidad (<c>ProductoCreditoPersonalConfigService.ObtenerAsync</c>),
    /// nunca del valor propio persistido en la entidad. La UI no expone ningún input editable para
    /// este caso y <c>GuardarAsync</c> ignora explícitamente cualquier valor entrante — un payload
    /// manipulado no puede reintroducir una tasa propia. null = sin plan global para esa cantidad
    /// (no disponible), nunca "hereda".</item>
    /// </list>
    /// </summary>
    [Range(0, 100, ErrorMessage = "El recargo debe estar entre 0 y 100.")]
    public decimal? TasaMensual { get; set; }

    public bool Activo { get; set; } = true;

    public int Orden { get; set; }
}

public class MontoPorPuntajeCreditoViewModel
{
    public int Id { get; set; }

    [Range(0, 10)]
    public int Puntaje { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "El monto no puede ser negativo.")]
    public decimal MontoMaximoFinanciable { get; set; }

    public bool RequiereAnalisis { get; set; }

    public bool Activo { get; set; } = true;

    public int Orden { get; set; }
}

public class DefaultsGlobalesViewModel
{
    /// <summary>
    /// LEGADO (ML4) — SIN autoridad financiera. El plan de cuotas (<see cref="CuotaCreditoPersonalViewModel"/>)
    /// activo es la única fuente del recargo aplicado en una venta; este valor nunca completa un
    /// plan sin porcentaje propio (dejó de ser fallback desde ML2.1/ML2). El nombre de la propiedad
    /// es legacy y se conserva por compatibilidad de binding.
    /// </summary>
    [Range(0, 100, ErrorMessage = "El recargo debe estar entre 0 y 100.")]
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

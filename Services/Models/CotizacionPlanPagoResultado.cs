namespace TheBuryProject.Services.Models;

public sealed class CotizacionPlanPagoResultado
{
    public string Plan { get; init; } = string.Empty;
    public int? CantidadCuotas { get; init; }
    public decimal? TasaMensual { get; init; }
    public decimal RecargoPorcentaje { get; init; }
    public decimal DescuentoPorcentaje { get; init; }
    public decimal InteresPorcentaje { get; init; }
    public decimal? CostoFinancieroTotal { get; init; }
    public string? TipoCalculo { get; init; }
    public decimal Total { get; init; }
    public decimal? ValorCuota { get; init; }
    public bool Recomendado { get; init; }
    public List<string> Advertencias { get; init; } = new();

    // Desglose exclusivo de Credito personal (recargo total, ML8): salen tal cual del resultado
    // de FinancialCalculationService.SimularPlanCredito via CreditoSimulacionVentaService. Null
    // para el resto de los medios de pago, que no financian en cuotas con recargo total.
    public decimal? Anticipo { get; init; }
    public decimal? SaldoAFinanciar { get; init; }
    public decimal? TotalFinanciado { get; init; }
    public decimal? UltimaCuota { get; init; }

    /// <summary>
    /// De donde salio <see cref="TasaMensual"/>. ML6.1 — contrato congelado: el plan de cuotas es la
    /// UNICA autoridad del porcentaje, asi que este campo siempre vale "Plan" para Credito personal.
    /// Nunca "Manual"/"Cliente"/"Producto"/"Global": esas etiquetas describian de donde salia la
    /// DISPONIBILIDAD de cantidades (ver <see cref="CotizacionMedioPagoResultado.FuenteTasaDescripcion"/>),
    /// no de donde salia el %. Ver <see cref="Services.CreditoSimulacionVentaService"/>.
    /// </summary>
    public string? FuentePorcentaje { get; init; }

    /// <summary>Vector exacto de cuotas (solo Credito personal). Solo la ultima absorbe el residuo.</summary>
    public IReadOnlyList<CotizacionPlanCuotaResultado> Cuotas { get; init; } = Array.Empty<CotizacionPlanCuotaResultado>();
}

public sealed class CotizacionPlanCuotaResultado
{
    public int NumeroCuota { get; init; }
    public decimal Capital { get; init; }
    public decimal Interes { get; init; }
    public decimal Total { get; init; }
}

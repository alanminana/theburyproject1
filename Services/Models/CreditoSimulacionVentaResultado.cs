namespace TheBuryProject.Services.Models;

public sealed class CreditoSimulacionVentaResultado
{
    private CreditoSimulacionVentaResultado(bool esValido, CreditoSimulacionVentaJson? plan, CreditoSimulacionVentaError? error)
    {
        EsValido = esValido;
        Plan = plan;
        Error = error;
    }

    public bool EsValido { get; }
    public CreditoSimulacionVentaJson? Plan { get; }
    public CreditoSimulacionVentaError? Error { get; }

    public static CreditoSimulacionVentaResultado Valido(CreditoSimulacionVentaJson plan) =>
        new(true, plan, null);

    public static CreditoSimulacionVentaResultado Invalido(string error) =>
        new(false, null, new CreditoSimulacionVentaError { error = error });
}

public sealed class CreditoSimulacionVentaError
{
    public string error { get; init; } = string.Empty;
}

public sealed class CreditoSimulacionVentaJson
{
    /// <summary>Total real de la venta resuelto en servidor (venta.Total cuando hay VentaId).</summary>
    public decimal totalVenta { get; init; }
    public decimal anticipo { get; init; }
    public decimal montoFinanciado { get; init; }
    public decimal cuotaEstimada { get; init; }
    public decimal tasaAplicada { get; init; }
    public decimal interesTotal { get; init; }
    public decimal totalAPagar { get; init; }
    public decimal gastosAdministrativos { get; init; }
    public decimal totalPlan { get; init; }
    public string fechaPrimerPago { get; init; } = string.Empty;

    /// <summary>
    /// De dónde salió <see cref="tasaAplicada"/>. ML6.1 — Contrato congelado: siempre "Plan" (el
    /// plan de cuotas es la única fuente del porcentaje). Nunca "Producto"/"Perfil"/"Cliente"/
    /// "Manual"/"Global": esas etiquetas describían la disponibilidad de cantidades, no la fuente
    /// financiera.
    /// </summary>
    public string fuentePorcentaje { get; init; } = string.Empty;

    /// <summary>Vector exacto de cuotas de FinancialCalculationService.SimularPlanCredito (ML3).</summary>
    public IReadOnlyList<CreditoSimulacionCuotaJson> cuotas { get; init; } = Array.Empty<CreditoSimulacionCuotaJson>();

    public string semaforoEstado { get; init; } = string.Empty;
    public string semaforoMensaje { get; init; } = string.Empty;
    public bool mostrarMsgIngreso { get; init; }
    public bool mostrarMsgAntiguedad { get; init; }
}

public sealed class CreditoSimulacionCuotaJson
{
    public int numeroCuota { get; init; }
    public decimal capital { get; init; }
    public decimal interes { get; init; }
    public decimal total { get; init; }
}

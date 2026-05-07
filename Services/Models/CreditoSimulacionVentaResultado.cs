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
    public decimal montoFinanciado { get; init; }
    public decimal cuotaEstimada { get; init; }
    public decimal tasaAplicada { get; init; }
    public decimal interesTotal { get; init; }
    public decimal totalAPagar { get; init; }
    public decimal gastosAdministrativos { get; init; }
    public decimal totalPlan { get; init; }
    public string fechaPrimerPago { get; init; } = string.Empty;
    public string semaforoEstado { get; init; } = string.Empty;
    public string semaforoMensaje { get; init; } = string.Empty;
    public bool mostrarMsgIngreso { get; init; }
    public bool mostrarMsgAntiguedad { get; init; }
}

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
}

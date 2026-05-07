namespace TheBuryProject.Services.Models;

public sealed class CreditoSimulacionVentaRequest
{
    public decimal TotalVenta { get; init; }
    public decimal? Anticipo { get; init; }
    public int Cuotas { get; init; }
    public decimal? GastosAdministrativos { get; init; }
    public string? FechaPrimeraCuota { get; init; }
    public decimal? TasaMensual { get; init; }
}

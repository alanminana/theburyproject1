using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services.Models;

public sealed class CreditoVentaResultado
{
    public int Id { get; init; }
    public string? Numero { get; init; }
    public decimal MontoAprobado { get; init; }
    public decimal SaldoPendiente { get; init; }
    public decimal TasaInteres { get; init; }
    public EstadoCredito Estado { get; init; }
    public bool Disponible { get; init; }
}

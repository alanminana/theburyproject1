namespace TheBuryProject.Services.Models;

/// <summary>Nota de crédito vigente de un cliente, ofrecida como medio de pago en la venta (H4-E2).</summary>
public sealed record NotaCreditoDisponibleDto(int Id, string Numero, decimal MontoDisponible, DateTime FechaEmision, DateTime? FechaVencimiento);
